import paramiko
from script_env import SSH_HOST as host, SSH_USERNAME as user, SSH_PASSWORD as password

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    stdin, stdout, stderr = client.exec_command("cat /opt/fleetmanager-agent/commands/StartBrowser.sh")
    out = stdout.read().decode().strip()
    print("--- StartBrowser.sh ---")
    print(out)
except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
