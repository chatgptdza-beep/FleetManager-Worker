import paramiko
import json
import time

host = "82.223.9.98"
user = "root"  
password = "$9%&zig$7N"

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

    # The agent is connected to a7e1c2e1... - keep that one
    keep_id = "a7e1c2e1-a546-4ab0-9c46-4d977b0520c1"
    delete_ids = ["278db7ed-52dc-4a21-b1b6-f861418ce963", "9d62da09-5fa0-4a47-8a4e-0f425beb1bf9"]

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
