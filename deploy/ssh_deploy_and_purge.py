import paramiko
import sys
import os

host = "82.223.9.98"
user = "root"
password = "$9%&zig$7N"

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
    sql = """
DELETE FROM "AccountAlerts" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf'));
DELETE FROM "AccountWorkflowStages" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf'));
DELETE FROM "ProxyEntries" WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf'));
DELETE FROM "Accounts" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "NodeCommands" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "AgentInstallJobs" WHERE "VpsNodeId" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "VpsNodes" WHERE "Id" IN ('3a5ff57d-e3d8-4d04-858e-fcef5b4997bf','df8ec7ab-4bd8-43c8-bd6d-4b5ebf901437','70c8c145-a615-42eb-82bf-b93112f0fe12','2f4a72af-4f7b-4c51-a3cb-b0ad6e3b3ecf');
DELETE FROM "VpsNodes" WHERE "Id" = 'c7b2206d-1ca4-43a3-8bb3-7d10bfc28d6a';
"""
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
    run("""curl -s http://localhost:5000/api/auth/token -X POST -H 'Content-Type: application/json' -d '{"Password":"Admin@FleetMgr2026!"}' | python3 -c 'import sys,json;t=json.load(sys.stdin)["token"];print("TOKEN OK")' """, "TEST TOKEN")

    run("""TOKEN=$(curl -s http://localhost:5000/api/auth/token -X POST -H 'Content-Type: application/json' -d '{"Password":"Admin@FleetMgr2026!"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])'); curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/nodes""", "API NODES")

    run("""TOKEN=$(curl -s http://localhost:5000/api/auth/token -X POST -H 'Content-Type: application/json' -d '{"Password":"Admin@FleetMgr2026!"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])'); curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/accounts""", "API ACCOUNTS")

except Exception as e:
    print(f"FAILED: {e}", file=sys.stderr)
    sys.exit(1)
finally:
    client.close()
    print("\nALL DONE")
