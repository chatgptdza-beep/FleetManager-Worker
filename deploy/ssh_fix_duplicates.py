import paramiko
import json
import time
from script_env import SSH_HOST as host, SSH_USERNAME as user, SSH_PASSWORD as password, require_env, require_uuid_list

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

def run(cmd, label=""):
    if label:
        print(f"\n--- {label} ---")
    stdin, stdout, stderr = client.exec_command(cmd, timeout=30)
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out:
        print(out)
    if err:
        print(f"ERR: {err}")
    return out

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    keep_id = require_env("FLEETMANAGER_KEEP_NODE_ID")
    delete_ids = require_uuid_list("FLEETMANAGER_DELETE_NODE_IDS")

    # Write SQL file for clean execution
    sql = ""
    for did in delete_ids:
        sql += f"""
DELETE FROM "AccountAlerts" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" = '{did}');
DELETE FROM "AccountWorkflowStages" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" = '{did}');
DELETE FROM "Accounts" WHERE "VpsNodeId" = '{did}';
DELETE FROM "NodeCommands" WHERE "VpsNodeId" = '{did}';
DELETE FROM "AgentInstallJobs" WHERE "VpsNodeId" = '{did}';
DELETE FROM "VpsNodes" WHERE "Id" = '{did}';
"""
    # Rename the kept one  
    sql += f"""UPDATE "VpsNodes" SET "Name" = 'EU-SERVER-01' WHERE "Id" = '{keep_id}';\n"""

    sftp = client.open_sftp()
    with sftp.file("/tmp/fix_nodes.sql", "w") as f:
        f.write(sql)
    sftp.close()

    run("systemctl stop fleetmanager-api.service", "STOP API")
    run("sudo -u postgres psql -d FleetManagerDb -f /tmp/fix_nodes.sql", "DELETE DUPLICATES & RENAME")
    run("""sudo -u postgres psql -d FleetManagerDb -c 'SELECT "Id","Name","IpAddress","Status" FROM "VpsNodes";'""", "VERIFY")

    run("systemctl start fleetmanager-api.service", "START API")
    time.sleep(3)
    run("systemctl is-active fleetmanager-api.service", "API STATUS")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("\nDONE")
