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

    # Check accounts in DB
    run("""sudo -u postgres psql -d FleetManagerDb -c 'SELECT "Id","Email","Status","VpsNodeId" FROM "Accounts";'""", "ACCOUNTS IN DB")

    # Check pending commands
    run("""sudo -u postgres psql -d FleetManagerDb -c 'SELECT "Id","CommandType","Status","ResultMessage" FROM "NodeCommands" ORDER BY "CreatedAtUtc" DESC LIMIT 10;'""", "RECENT COMMANDS")

    # Check agent logs for command execution
    run("journalctl -u fleetmanager-agent.service --no-pager -n 20", "AGENT LOGS")

    # Check if noVNC is running
    run("ps aux | grep -E 'novnc|vnc|websockify' | grep -v grep", "VNC PROCESSES")

    # Check if Chrome is running
    run("ps aux | grep chrome | grep -v grep | head -3", "CHROME PROCESSES")

    # Check Xvfb displays
    run("ps aux | grep Xvfb | grep -v grep", "XVFB DISPLAYS")

    # Check noVNC ports
    run("ss -tlnp | grep -E '6080|6081|5900|5901'", "VNC PORTS")

    # Check agent command scripts
    run("ls -la /opt/fleetmanager-agent/commands/ 2>/dev/null || echo 'No commands dir'", "AGENT COMMAND SCRIPTS")

    # Check fleetmanager sessions
    run("ls -la /var/lib/fleetmanager/sessions/ 2>/dev/null || echo 'No sessions'", "SESSIONS DIR")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("\nDONE")
