import paramiko
import sys
import os
from script_env import (
    SSH_HOST as host,
    SSH_USERNAME as user,
    SSH_PASSWORD as password,
    optional_env,
    require_env,
    require_uuid_list,
    sql_string_list,
)

admin_password = require_env("FLEETMANAGER_API_PASSWORD")
purge_node_ids = require_uuid_list("FLEETMANAGER_PURGE_NODE_IDS")
purge_node_ids_sql = sql_string_list(purge_node_ids)
extra_node_id = optional_env("FLEETMANAGER_EXTRA_NODE_ID")

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

def run(cmd, label=""):
    if label:
        print(f"\n--- {label} ---")
    stdin, stdout, stderr = client.exec_command(cmd, timeout=120)
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out:
        print(out)
    if err and "NOTICE" not in err and "warning" not in err.lower():
        print(f"ERR: {err}")
    return out

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    # Step 1: Stop the API
    run("systemctl stop fleetmanager-api.service", "STOP API")

    # Step 2: Check current API dir
    run("ls -la /opt/fleetmanager-api/ | head -20", "CURRENT API FILES")

    # Step 3: Upload new API binary (the published one)
    publish_dir = os.path.join(os.path.dirname(__file__), "..", "publish", "api")
    publish_dir = os.path.abspath(publish_dir)
    
    print(f"\n--- UPLOAD NEW API ---")
    print(f"Source: {publish_dir}")
    
    sftp = client.open_sftp()
    
    # Create temp dir on server
    run("rm -rf /tmp/fleet-api-new && mkdir -p /tmp/fleet-api-new")
    
    # Upload all files
    uploaded = 0
    for fname in os.listdir(publish_dir):
        fpath = os.path.join(publish_dir, fname)
        if os.path.isfile(fpath):
            remote_path = f"/tmp/fleet-api-new/{fname}"
            sftp.put(fpath, remote_path)
            uploaded += 1
            if uploaded % 50 == 0:
                print(f"  uploaded {uploaded} files...")
    
    sftp.close()
    print(f"  Uploaded {uploaded} files total")

    # Step 4: Replace the API binary
    run("cp -r /opt/fleetmanager-api /opt/fleetmanager-api.bak", "BACKUP OLD API")
    run("cp -f /tmp/fleet-api-new/* /opt/fleetmanager-api/", "DEPLOY NEW API")
    run("chmod +x /opt/fleetmanager-api/FleetManager.Api", "SET PERMISSIONS")

    # Step 5: Purge demo data from DB BEFORE starting new API
    sql = f"""
DELETE FROM "AccountAlerts" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql}));
DELETE FROM "AccountWorkflowStages" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql}));
DELETE FROM "ProxyEntries" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql}));
DELETE FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql});
DELETE FROM "NodeCommands" WHERE "VpsNodeId" IN ({purge_node_ids_sql});
DELETE FROM "AgentInstallJobs" WHERE "VpsNodeId" IN ({purge_node_ids_sql});
DELETE FROM "VpsNodes" WHERE "Id" IN ({purge_node_ids_sql});
"""
    if extra_node_id:
        sql += f'DELETE FROM "VpsNodes" WHERE "Id" = \'{extra_node_id}\';\n'
    sftp2 = client.open_sftp()
    with sftp2.file("/tmp/purge_demo.sql", "w") as f:
        f.write(sql)
    sftp2.close()
    
    run("sudo -u postgres psql -d FleetManagerDb -f /tmp/purge_demo.sql", "PURGE ALL DEMO DATA")

    # Verify DB is clean
    run("""sudo -u postgres psql -d FleetManagerDb -c 'SELECT COUNT(*) as nodes FROM "VpsNodes"; SELECT COUNT(*) as accounts FROM "Accounts";'""", "VERIFY DB CLEAN")

    # Step 6: Start new API
    run("systemctl start fleetmanager-api.service", "START NEW API")
    
    import time
    time.sleep(4)
    
    run("systemctl is-active fleetmanager-api.service", "API STATUS")
    
    # Final verification via API
    run(f"""curl -s http://localhost:5000/api/auth/token -X POST -H 'Content-Type: application/json' -d '{{"Password":"{admin_password}"}}' | python3 -c 'import sys,json;t=json.load(sys.stdin)["token"];print("TOKEN OK")' """, "TEST TOKEN")

    run(f"""TOKEN=$(curl -s http://localhost:5000/api/auth/token -X POST -H 'Content-Type: application/json' -d '{{"Password":"{admin_password}"}}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])'); curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/nodes""", "API NODES")

    run(f"""TOKEN=$(curl -s http://localhost:5000/api/auth/token -X POST -H 'Content-Type: application/json' -d '{{"Password":"{admin_password}"}}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])'); curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/accounts""", "API ACCOUNTS")

except Exception as e:
    print(f"FAILED: {e}", file=sys.stderr)
    sys.exit(1)
finally:
    client.close()
    print("\nALL DONE")
