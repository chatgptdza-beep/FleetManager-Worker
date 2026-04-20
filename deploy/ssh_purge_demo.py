import paramiko
import sys
from script_env import (
    SSH_HOST as host,
    SSH_USERNAME as user,
    SSH_PASSWORD as password,
    optional_env,
    require_uuid_list,
    sql_string_list,
)

purge_node_ids = require_uuid_list("FLEETMANAGER_PURGE_NODE_IDS")
purge_node_ids_sql = sql_string_list(purge_node_ids)
extra_node_id = optional_env("FLEETMANAGER_EXTRA_NODE_ID")

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

def run(cmd, label=""):
    if label:
        print(f"\n--- {label} ---")
    stdin, stdout, stderr = client.exec_command(cmd, timeout=60)
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out:
        print(out)
    if err and "NOTICE" not in err:
        print(f"ERR: {err}")
    return out

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    # Step 1: Stop the API
    run("systemctl stop fleetmanager-api.service", "STOP API")

    # Step 2: Write a SQL file on the server, then execute it
    sql = f"""
DELETE FROM "AccountAlerts" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql}));
DELETE FROM "AccountWorkflowStages" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql}));
DELETE FROM "ProxyEntries" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql}));
DELETE FROM "ProxyRotationLogs" WHERE "ProxyEntryId" IN (SELECT "Id" FROM "ProxyEntries" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql})));
DELETE FROM "Accounts" WHERE "VpsNodeId" IN ({purge_node_ids_sql});
DELETE FROM "NodeCommands" WHERE "VpsNodeId" IN ({purge_node_ids_sql});
DELETE FROM "AgentInstallJobs" WHERE "VpsNodeId" IN ({purge_node_ids_sql});
DELETE FROM "VpsNodes" WHERE "Id" IN ({purge_node_ids_sql});
"""
    
    # Upload SQL file
    sftp = client.open_sftp()
    with sftp.file("/tmp/purge_demo.sql", "w") as f:
        f.write(sql)
    sftp.close()
    
    run("sudo -u postgres psql -d FleetManagerDb -f /tmp/purge_demo.sql", "PURGE DEMO DATA")

    if extra_node_id:
        sftp = client.open_sftp()
        with sftp.file("/tmp/purge_extra.sql", "w") as f:
            f.write(f'DELETE FROM "VpsNodes" WHERE "Id" = \'{extra_node_id}\';\n')
        sftp.close()
        run("sudo -u postgres psql -d FleetManagerDb -f /tmp/purge_extra.sql", "PURGE EXTRA NODE")

    # Verify
    run("""sudo -u postgres psql -d FleetManagerDb -c 'SELECT "Id","Name","IpAddress" FROM "VpsNodes";'""", "REMAINING NODES")
    run("""sudo -u postgres psql -d FleetManagerDb -c 'SELECT "Id","Email","VpsNodeId" FROM "Accounts";'""", "REMAINING ACCOUNTS")

    # Restart API
    run("systemctl start fleetmanager-api.service", "START API")
    
    import time
    time.sleep(3)
    run("systemctl is-active fleetmanager-api.service", "API STATUS")

except Exception as e:
    print(f"FAILED: {e}", file=sys.stderr)
    sys.exit(1)
finally:
    client.close()
    print("\nDONE")
