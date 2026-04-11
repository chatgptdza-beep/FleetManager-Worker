import paramiko

host = "82.223.9.98"
user = "root"
password = "$9%&zig$7N"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    stdin, stdout, stderr = client.exec_command("grep -n FM_VIEWER_HOST /opt/fleetmanager-agent/commands/*")
    out = stdout.read().decode().strip()
    print("--- FM_VIEWER_HOST in commands ---")
    print(out)
except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
