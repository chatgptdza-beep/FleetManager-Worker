import paramiko
import json
import time
from script_env import SSH_HOST as host, SSH_USERNAME as user, SSH_PASSWORD as password, require_env

admin_password = require_env("FLEETMANAGER_API_PASSWORD")

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
    if err and "WARNING" not in err:
        print(f"ERR: {err}")
    return out

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    # Step 1: Create node via API from localhost (avoids auth issues)
    result = run(f"""curl -s -X POST http://localhost:5000/api/auth/token -H 'Content-Type: application/json' -d '{{"Password":"{admin_password}"}}' """)
    token = json.loads(result)["token"]
    print(f"Token: {token[:20]}...")

    create_body = json.dumps({
        "Name": "VPS-PROD-01",
        "IpAddress": host,
        "SshPort": 22,
        "ControlPort": 9001,
        "SshUsername": "root",
        "SshPassword": "",
        "AuthType": "Password",
        "OsType": "Ubuntu",
        "Region": "EU"
    })
    
    result = run(f"""curl -s -X POST http://localhost:5000/api/nodes -H 'Content-Type: application/json' -H 'Authorization: Bearer {token}' -d '{create_body}'""", "CREATE NODE")
    
    try:
        node = json.loads(result)
        node_id = node["id"]
        print(f"Node ID: {node_id}")
    except:
        print("Failed to parse node response, trying raw creation via DB...")
        # Fallback: create via psql
        run(f"""sudo -u postgres psql -d FleetManagerDb -c "INSERT INTO \\"VpsNodes\\" (\\"Id\\",\\"Name\\",\\"IpAddress\\",\\"SshPort\\",\\"ControlPort\\",\\"SshUsername\\",\\"SshPassword\\",\\"AuthType\\",\\"OsType\\",\\"Region\\",\\"Status\\",\\"ConnectionState\\",\\"ConnectionTimeoutSeconds\\",\\"CpuPercent\\",\\"RamPercent\\",\\"DiskPercent\\",\\"RamUsedGb\\",\\"StorageUsedGb\\",\\"PingMs\\",\\"ActiveSessions\\",\\"CreatedAtUtc\\") VALUES (gen_random_uuid(),'VPS-PROD-01','{host}',22,9001,'root','','Password','Ubuntu','EU','Pending','Connected',5,0,0,0,0,0,0,0,NOW());" """, "DB INSERT")
        result = run("""sudo -u postgres psql -d FleetManagerDb -t -c 'SELECT "Id" FROM "VpsNodes" LIMIT 1;'""")
        node_id = result.strip()
        print(f"Node ID from DB: {node_id}")

    # Step 2: Read current agent config
    run("cat /opt/fleetmanager-agent/appsettings.json | python3 -c 'import sys,json; d=json.load(sys.stdin); print(json.dumps(d,indent=2))'", "CURRENT AGENT CONFIG")

    # Step 3: Update agent config with new node ID
    update_script = f"""
import json
with open('/opt/fleetmanager-agent/appsettings.json', 'r') as f:
    config = json.load(f)
config.setdefault('Agent', {{}})
config['Agent']['NodeId'] = '{node_id}'
config['Agent']['BackendBaseUrl'] = 'http://localhost:5000'
with open('/opt/fleetmanager-agent/appsettings.json', 'w') as f:
    json.dump(config, f, indent=2)
    f.write('\\n')
print('Config updated')
"""
    
    sftp = client.open_sftp()
    with sftp.file("/tmp/update_agent_config.py", "w") as f:
        f.write(update_script)
    sftp.close()
    
    run("python3 /tmp/update_agent_config.py", "UPDATE AGENT CONFIG")
    run("cat /opt/fleetmanager-agent/appsettings.json | python3 -c 'import sys,json; d=json.load(sys.stdin); print(json.dumps(d,indent=2))'", "NEW AGENT CONFIG")

    # Step 4: Restart agent
    run("systemctl restart fleetmanager-agent.service", "RESTART AGENT")
    time.sleep(3)
    run("systemctl is-active fleetmanager-agent.service", "AGENT STATUS")

    # Step 5: Wait for heartbeat and verify
    time.sleep(5)
    result = run(f"""curl -s -H 'Authorization: Bearer {token}' http://localhost:5000/api/nodes/{node_id}""", "VERIFY NODE DATA")
    try:
        node_data = json.loads(result)
        print(f"CPU: {node_data.get('cpuPercent')}%")
        print(f"RAM: {node_data.get('ramPercent')}%")
        print(f"Ping: {node_data.get('pingMs')}ms")
        print(f"Status: {node_data.get('status')}")
    except:
        print("Could not parse node data yet")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("\nALL DONE")
