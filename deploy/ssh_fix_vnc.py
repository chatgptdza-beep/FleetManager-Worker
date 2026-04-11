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

    # Check current websockify binding
    run("ss -tlnp | grep 6911", "CURRENT WEBSOCKIFY PORT")

    # Kill current websockify and restart with 0.0.0.0 binding
    run("kill $(pgrep -f 'websockify.*6911') 2>/dev/null; sleep 1", "KILL OLD WEBSOCKIFY")
    
    # Restart websockify bound to 0.0.0.0
    run("nohup /usr/bin/python3 /usr/bin/websockify --web /usr/share/novnc 0.0.0.0:6911 127.0.0.1:5911 > /tmp/novnc.log 2>&1 &", "START WEBSOCKIFY ON 0.0.0.0:6911")
    
    import time
    time.sleep(2)
    
    run("ss -tlnp | grep 6911", "VERIFY PORT BINDING")

    # Also fix the x11vnc to listen on correct port
    run("ss -tlnp | grep 5911", "X11VNC PORT")

    # Check firewall
    run("ufw status 2>/dev/null || iptables -L -n | head -20", "FIREWALL")

    # Open port in firewall
    run("ufw allow 6911/tcp 2>/dev/null || iptables -A INPUT -p tcp --dport 6911 -j ACCEPT 2>/dev/null", "OPEN PORT 6911")

    # Fix the OpenAssignedSession script to return the correct external URL
    run("cat /opt/fleetmanager-agent/commands/OpenAssignedSession.sh", "CURRENT VIEWER SCRIPT")

    print(f"\n{'='*50}")
    print(f"VIEWER URL: http://{host}:6911/vnc.html?autoconnect=true")
    print(f"{'='*50}")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("\nDONE")
