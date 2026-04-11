import paramiko
import sys

host = "82.223.9.98"
user = "root"
password = "$9%&zig$7N"

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
    sql = """
DELETE FROM "AccountAlerts" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf'));
DELETE FROM "AccountWorkflowStages" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf'));
DELETE FROM "ProxyEntries" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf'));
DELETE FROM "ProxyRotationLogs" WHERE "ProxyEntryId" IN (SELECT "Id" FROM "ProxyEntries" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf')));
DELETE FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "NodeCommands" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "AgentInstallJobs" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "VpsNodes" WHERE "Id" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
"""
    
    # Upload SQL file
    sftp = client.open_sftp()
    with sftp.file("/tmp/purge_demo.sql", "w") as f:
        f.write(sql)
    sftp.close()
    
    run("sudo -u postgres psql -d FleetManagerDb -f /tmp/purge_demo.sql", "PURGE DEMO DATA")

    # Also delete the extra VPS-NEW-01 if it exists
    sftp = client.open_sftp()
    with sftp.file("/tmp/purge_extra.sql", "w") as f:
        f.write("""DELETE FROM "VpsNodes" WHERE "Id" = 'c7b2206d-1ca4-43a3-8bb3-7d10bfc28d6a';\n""")
    sftp.close()
    run("sudo -u postgres psql -d FleetManagerDb -f /tmp/purge_extra.sql", "PURGE VPS-NEW-01")

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
