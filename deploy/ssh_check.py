import paramiko
import json

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

    # Check API logs
    run("journalctl -u fleetmanager-api.service --no-pager -n 30", "API LOGS")

    # Check agent config
    run("cat /opt/fleetmanager-agent/appsettings.json 2>/dev/null || echo 'no config'", "AGENT CONFIG")
    
    # Check agent status
    run("systemctl is-active fleetmanager-agent.service", "AGENT STATUS")
    run("journalctl -u fleetmanager-agent.service --no-pager -n 10", "AGENT LOGS")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("\nDONE")
