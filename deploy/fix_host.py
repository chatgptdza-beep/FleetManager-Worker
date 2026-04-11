import paramiko

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
        return stdout.read().decode().strip(), stderr.read().decode().strip()

    # Update common.sh to use the VPS IP address instead of 127.0.0.1 for the Viewer Host
    out, err = run(f"sed -i 's/FM_VIEWER_HOST:-127.0.0.1/FM_VIEWER_HOST:-{host}/g' /opt/fleetmanager-agent/commands/common.sh")
    
    # Check if we need to update any existing viewer.env files
    run(f"find /var/lib/fleetmanager/sessions/ -name viewer.env -exec sed -i 's/FM_VIEWER_HOST=.*/FM_VIEWER_HOST={host}/g' {{}} \\;")

    out, err = run("grep FM_VIEWER_HOST /opt/fleetmanager-agent/commands/common.sh")
    print(out)

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("DONE")
