import paramiko

host = "82.223.9.98"
user = "root"
password = "$9%&zig$7N"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

try:
    client.connect(host, port=22, username=user, password=password, timeout=10)
    print("SSH OK")

    stdin, stdout, stderr = client.exec_command('sudo -u postgres psql -d FleetManagerDb -c "UPDATE accounts SET status = \'Stopped\' WHERE status = \'Stable\';"')
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    print("Out:", out)
    print("Err:", err)

except Exception as e:
    print(f"FAILED: {e}")
finally:
    client.close()
    print("DONE")
