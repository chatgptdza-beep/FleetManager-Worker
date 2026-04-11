import paramiko
import time

host = "82.223.9.98"
user = "root"
password = "$9%&zig$7N"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    def run(cmd):
        stdin, stdout, stderr = client.exec_command(cmd)
        out = stdout.read().decode().strip()
        err = stderr.read().decode().strip()
        return out, err

    # Fix common.sh websockify binding
    out, err = run("sed -i 's/\"$FM_WEB_PORT\" \"127.0.0.1:$FM_VNC_PORT\"/\"0.0.0.0:$FM_WEB_PORT\" \"127.0.0.1:$FM_VNC_PORT\"/g' /opt/fleetmanager-agent/commands/common.sh")
    
    # Verify
    out, err = run("grep websockify /opt/fleetmanager-agent/commands/common.sh")
    print("common.sh websockify lines:")
    print(out)

    # Open ports 6911-6950 in firewall
    run("ufw allow 6911:6950/tcp 2>/dev/null || iptables -A INPUT -p tcp --match multiport --dports 6911:6950 -j ACCEPT 2>/dev/null")

    # Kill background instances of websockify
    run("killall websockify 2>/dev/null")

    # Kill background instances of x11vnc and chrome to reset state cleanly
    run("pkill -f x11vnc 2>/dev/null")
    run("pkill -f chrome 2>/dev/null")

    # Restart agent
    run("systemctl restart fleetmanager-agent")
    print("Agent restarted.")

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("Server update complete.")
