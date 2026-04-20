import paramiko
import json
import time
from script_env import SSH_HOST as host, SSH_USERNAME as user, SSH_PASSWORD as password, require_env

node_id = require_env("FLEETMANAGER_NODE_ID")
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

    # Fix BOM issue and update config
    update_script = f"""
import json
with open('/opt/fleetmanager-agent/appsettings.json', 'rb') as f:
    raw = f.read()
# Strip BOM if present
if raw.startswith(b'\\xef\\xbb\\xbf'):
    raw = raw[3:]
config = json.loads(raw.decode('utf-8'))
config.setdefault('Agent', {{}})
config['Agent']['NodeId'] = '{node_id}'
config['Agent']['BackendBaseUrl'] = 'http://localhost:5000'
with open('/opt/fleetmanager-agent/appsettings.json', 'w', encoding='utf-8') as f:
    json.dump(config, f, indent=2)
    f.write('\\n')
print(json.dumps(config, indent=2))
"""
    
    sftp = client.open_sftp()
    with sftp.file("/tmp/fix_agent_config.py", "w") as f:
        f.write(update_script)
    sftp.close()
    
    run("python3 /tmp/fix_agent_config.py", "FIX & UPDATE AGENT CONFIG")

    # Restart agent
    run("systemctl restart fleetmanager-agent.service", "RESTART AGENT")
    time.sleep(5)
    run("systemctl is-active fleetmanager-agent.service", "AGENT STATUS")
    run("journalctl -u fleetmanager-agent.service --no-pager -n 5", "AGENT LOGS")

    # Verify node data
    time.sleep(5)
    token_resp = run(f"curl -s -X POST http://localhost:5000/api/auth/token -H 'Content-Type: application/json' -d '{{\"Password\":\"{admin_password}\"}}'")
    token = json.loads(token_resp)["token"]
    
    result = run(f"curl -s -H 'Authorization: Bearer {token}' http://localhost:5000/api/nodes/{node_id}", "NODE DATA AFTER AGENT RESTART")
    node_data = json.loads(result)
    print(f"\nCPU: {node_data.get('cpuPercent')}%")
    print(f"RAM: {node_data.get('ramPercent')}%")
    print(f"Disk: {node_data.get('diskPercent')}%")
    print(f"Ping: {node_data.get('pingMs')}ms")
    print(f"Status: {node_data.get('status')}")
    print(f"Heartbeat: {node_data.get('lastHeartbeatAtUtc')}")
    print(f"Agent: {node_data.get('agentVersion')}")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("\nDONE")
