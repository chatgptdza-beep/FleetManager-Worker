param(
    [Parameter(Mandatory = $true)]
    [string]$VpsIp,

    [Parameter(Mandatory = $true)]
    [string]$RootPassword,

    [Parameter(Mandatory = $true)]
    [string]$LocalExtensionPath,

    [string]$RemoteExtensionPath = '/opt/fleetmanager-agent/extensions/quickreserve-loader',

    [string]$HostKey = $(if ($env:FLEETMANAGER_SSH_HOSTKEY) { $env:FLEETMANAGER_SSH_HOSTKEY } else { 'ssh-ed25519 255 SHA256:Cxy9EIVH+I9PxBjh3UP4UTulElvJ9+vIGNESBG7sRq4' }),

    [int[]]$LauncherPorts = @(45321, 65430, 65475),

    [switch]$StartLauncherTunnel
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$plinkPath = 'C:\Program Files\PuTTY\plink.exe'
$pscpPath = 'C:\Program Files\PuTTY\pscp.exe'

if (-not (Test-Path $plinkPath)) { throw "plink not found: $plinkPath" }
if (-not (Test-Path $pscpPath)) { throw "pscp not found: $pscpPath" }

$resolvedLocalExtensionPath = [string](Resolve-Path $LocalExtensionPath)
$localManifest = Get-ChildItem -Path $resolvedLocalExtensionPath -Recurse -File -Filter 'manifest.json' | Select-Object -First 1
if (-not $localManifest) {
    throw "No manifest.json found under local extension path: $resolvedLocalExtensionPath"
}

function Invoke-Remote {
    param([string]$Command)

    & $plinkPath -batch -ssh -hostkey $HostKey -l root -pw $RootPassword $VpsIp $Command 2>&1
}

function Upload-And-InstallExtension {
    param(
        [string]$LocalPath,
        [string]$RemotePath
    )

    Write-Output ('EXTENSION_UPLOAD_LOCAL=' + $LocalPath)
    Write-Output ('EXTENSION_INSTALL_REMOTE=' + $RemotePath)

    Invoke-Remote "rm -rf /tmp/fm-extension-upload; mkdir -p /tmp/fm-extension-upload" | Out-Null

    & $pscpPath -batch -hostkey $HostKey -pw $RootPassword -r $LocalPath ("root@{0}:/tmp/fm-extension-upload/" -f $VpsIp)
    if ($LASTEXITCODE -ne 0) {
        throw "PSCP upload failed with code $LASTEXITCODE"
    }

        $escapedRemotePath = $RemotePath.Replace("'", "'\\''")
        $installCmd = @'
set -e
TARGET='__REMOTE_PATH__'
UPLOAD='/tmp/fm-extension-upload'
src="$(find "$UPLOAD" -mindepth 1 -maxdepth 1 -type d | head -n 1 || true)"
if [ -z "$src" ]; then
  echo EXT_UPLOAD_EMPTY
  exit 1
fi
manifest=''
if [ -f "$src/manifest.json" ]; then
    manifest="$src/manifest.json"
else
    manifest="$(find "$src" -mindepth 2 -maxdepth 2 -type f -name manifest.json | head -n 1 || true)"
fi
if [ -z "$manifest" ]; then
  echo EXT_MANIFEST_MISSING
  exit 1
fi
root="$(dirname "$manifest")"
mkdir -p "$(dirname "$TARGET")"
rm -rf "$TARGET"
cp -a "$root" "$TARGET"
chown -R fleetmgr:fleetmgr "$TARGET" || true
find "$TARGET" -type d -exec chmod 755 {} \;
find "$TARGET" -type f -exec chmod 644 {} \;
test -f "$TARGET/manifest.json" && echo EXTENSION_READY="$TARGET" || (echo EXT_MANIFEST_FINAL_MISSING; exit 1)
rm -rf "$UPLOAD"
'@
        $installCmd = $installCmd.Replace('__REMOTE_PATH__', $escapedRemotePath)

    $installOutput = Invoke-Remote ("bash -lc " + [char]39 + $installCmd + [char]39)
    $installText = ($installOutput | Out-String)
    Write-Output $installText.Trim()

    if ($installText -notmatch 'EXTENSION_READY=') {
        throw 'Extension installation did not produce EXTENSION_READY marker.'
    }
}

function Configure-AgentExtension {
    param([string]$RemotePath)

    $escapedRemotePath = $RemotePath.Replace("'", "'\\''")
    $configureCmd = @'
set -e
python3 -c "import json; p='/opt/fleetmanager-agent/appsettings.json'; d=json.load(open(p)); d.setdefault('Agent', {})['BrowserExtensions']=['__REMOTE_PATH__']; open(p,'w').write(json.dumps(d, indent=2)); print('APPSETTINGS_BROWSER_EXTENSIONS_OK')"
mkdir -p /etc/systemd/system/fleetmanager-agent.service.d
printf '[Service]\nEnvironment=FM_BROWSER_EXTENSIONS=%s\n' '__REMOTE_PATH__' > /etc/systemd/system/fleetmanager-agent.service.d/10-browser-extensions.conf
systemctl daemon-reload
systemctl restart fleetmanager-agent
echo AGENT_STATUS=$(systemctl is-active fleetmanager-agent 2>/dev/null || true)
systemctl show fleetmanager-agent --property=Environment --no-pager | sed 's/^/AGENT_ENV=/'
grep -n BrowserExtensions /opt/fleetmanager-agent/appsettings.json || true
test -f '__REMOTE_PATH__/manifest.json' && echo REMOTE_MANIFEST_OK || echo REMOTE_MANIFEST_MISSING
'@
    $configureCmd = $configureCmd.Replace('__REMOTE_PATH__', $escapedRemotePath)

    $configureOutput = Invoke-Remote ("bash -lc " + [char]39 + $configureCmd + [char]39)
    $configureText = ($configureOutput | Out-String)
    Write-Output $configureText.Trim()

    if ($configureText -notmatch 'AGENT_STATUS=active') {
        throw 'fleetmanager-agent is not active after extension configuration.'
    }

    if ($configureText -notmatch 'REMOTE_MANIFEST_OK') {
        throw 'Remote extension manifest verification failed.'
    }
}

function Start-LauncherBridgeTunnel {
    param(
        [string]$TargetIp,
        [string]$Password,
        [string]$ExpectedHostKey,
        [int[]]$Ports
    )

    if ($Ports.Count -eq 0) {
        Write-Output 'TUNNEL_STATUS=SKIPPED_NO_PORTS'
        return
    }

    $forwardNeedles = @($Ports | ForEach-Object { "-R 127.0.0.1:{0}:127.0.0.1:{0}" -f $_ })

    $existing = Get-CimInstance Win32_Process -Filter "Name = 'plink.exe'" |
        Where-Object {
            $cmd = $_.CommandLine
            if (-not $cmd) { return $false }
            if ($cmd -notlike "* $TargetIp*") { return $false }
            foreach ($needle in $forwardNeedles) {
                if ($cmd -notlike "*$needle*") { return $false }
            }
            return $true
        }

    foreach ($proc in $existing) {
        try {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
            Write-Output ('STOPPED_PREVIOUS_TUNNEL_PID=' + $proc.ProcessId)
        }
        catch {
            Write-Output ('STOP_PREVIOUS_TUNNEL_WARNING=' + $_.Exception.Message)
        }
    }

    $plinkArgs = @('-batch', '-ssh', '-hostkey', $ExpectedHostKey, '-l', 'root', '-pw', $Password, '-N')
    foreach ($port in $Ports) {
        $plinkArgs += @('-R', ("127.0.0.1:{0}:127.0.0.1:{0}" -f $port))
    }
    $plinkArgs += $TargetIp

    $proc = Start-Process -FilePath $plinkPath -ArgumentList $plinkArgs -WindowStyle Hidden -PassThru
    Write-Output ('TUNNEL_PROCESS_PID=' + $proc.Id)

    $portRegex = ($Ports | ForEach-Object { ":{0}" -f $_ }) -join '|'
    $verifyOutput = Invoke-Remote ("ss -lnt | awk 'NR==1 || /$portRegex/'")
    Write-Output (($verifyOutput | Out-String).Trim())
}

Write-Output ('TARGET_VPS=' + $VpsIp)
Write-Output ('REMOTE_EXTENSION_PATH=' + $RemoteExtensionPath)
Write-Output ('LAUNCHER_PORTS=' + ($LauncherPorts -join ','))

$sshProbe = (Invoke-Remote "echo SSH_OK; hostname; date") | Out-String
if ($sshProbe -notmatch 'SSH_OK') {
    throw 'SSH probe failed. Verify VPS IP, root password, or host key fingerprint.'
}
Write-Output 'SSH_STATUS=OK'

Upload-And-InstallExtension -LocalPath $resolvedLocalExtensionPath -RemotePath $RemoteExtensionPath
Configure-AgentExtension -RemotePath $RemoteExtensionPath

if ($StartLauncherTunnel) {
    Start-LauncherBridgeTunnel -TargetIp $VpsIp -Password $RootPassword -ExpectedHostKey $HostKey -Ports $LauncherPorts
}
else {
    Write-Output 'TUNNEL_STATUS=SKIPPED'
}

Write-Output 'RESULT=SUCCESS'
