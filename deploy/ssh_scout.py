import paramiko
import sys

host = "82.223.9.98"
user = "root"
password = "$9%&zig$7N"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH_CONNECTED")
    
    # Find the API process and database
    commands = [
        "echo '=== RUNNING PROCESSES ===' && ps aux | grep -i fleet | grep -v grep",
        "echo '=== FIND DB FILES ===' && find / -name '*.db' -path '*fleet*' 2>/dev/null | head -10",
        "echo '=== FIND API BINARY ===' && find / -name 'FleetManager.Api' -type f 2>/dev/null | head -5",
        "echo '=== FIND API DIR ===' && find / -type d -name '*fleet*' 2>/dev/null | head -10",
        "echo '=== SYSTEMD SERVICES ===' && systemctl list-units --type=service 2>/dev/null | grep -i fleet || echo 'no fleet services'",
        "echo '=== DOCKER CONTAINERS ===' && docker ps -a 2>/dev/null | grep -i fleet || echo 'no docker'",
    ]
    
    for cmd in commands:
        stdin, stdout, stderr = client.exec_command(cmd, timeout=15)
        out = stdout.read().decode()
        err = stderr.read().decode()
        if out.strip():
            print(out.strip())
        if err.strip():
            print(f"STDERR: {err.strip()}")
        print()

except Exception as e:
    print(f"FAILED: {e}", file=sys.stderr)
    sys.exit(1)
finally:
    client.close()
