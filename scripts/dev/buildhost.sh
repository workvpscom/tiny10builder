#!/usr/bin/env bash
# Drive the Windows build host (Proxmox VM 9200) from the dev machine.
#   buildhost.sh push            copy the committed+working tree of this repo to C:\work\tiny10 on 9200
#   buildhost.sh run '<pwsh>'    run a PowerShell snippet on 9200 via QGA (cwd C:\work\tiny10), print output, exit with its code
# Uses only VMID 9200. Transfer is a throwaway ISO swapped into 9200's ide2 CD drive.
# WARNING: `push` ejects/replaces the VM's ide2 CD drive. Never run it while a build or an ISO copy is using ide2.
# NODE (the Proxmox node, e.g. NODE=root@pve.example) must be set in the environment; there is no default.
set -euo pipefail
NODE=${NODE:?set NODE to the Proxmox node, e.g. NODE=root@pve.example}
VMID=9200
TIMEOUT=${TIMEOUT:-900}
ROOT=$(git rev-parse --show-toplevel)

ssh_() { ssh -o BatchMode=yes "$NODE" "$@"; }

guest_ps() {  # $1 = powershell snippet
  local b64 out
  b64=$(printf '%s' "\$ProgressPreference='SilentlyContinue'; $1" | iconv -f UTF-8 -t UTF-16LE | base64 | tr -d '\n')
  out=$(ssh_ "qm guest exec $VMID --timeout $TIMEOUT -- powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $b64")
  printf '%s' "$out" | python3 -c '
import sys, json
d = json.load(sys.stdin)
sys.stdout.write(d.get("out-data", "").replace("\r", ""))
sys.stderr.write(d.get("err-data", "").replace("\r", ""))
sys.exit(d.get("exitcode", 1))'
}

case "${1:-}" in
  push)
    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    mkdir "$tmp/iso"
    # tracked files plus untracked-but-not-ignored files, current working tree contents
    (cd "$ROOT" && git ls-files -co --exclude-standard -z | xargs -0 zip -q -@ "$tmp/iso/tiny10.zip")
    scp -q -o BatchMode=yes -r "$tmp/iso" "$NODE:/root/tiny10-transfer-src"
    ssh_ "set -e; qm set $VMID --ide2 none >/dev/null; genisoimage -quiet -J -r -V TINY10XFER -o /var/lib/vz/template/iso/tiny10-transfer.iso /root/tiny10-transfer-src; rm -rf /root/tiny10-transfer-src; qm set $VMID --ide2 local:iso/tiny10-transfer.iso,media=cdrom >/dev/null; sleep 6"
    guest_ps '$d=(Get-Volume | ? FileSystemLabel -eq "TINY10XFER").DriveLetter; if(-not $d){throw "transfer CD not found"}; Remove-Item C:\work\tiny10 -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force C:\work\tiny10 | Out-Null; Expand-Archive "${d}:\tiny10.zip" C:\work\tiny10 -Force; "pushed " + (Get-ChildItem C:\work\tiny10 -Recurse -File | Measure-Object).Count + " files"'
    ssh_ "qm set $VMID --ide2 none >/dev/null; rm -f /var/lib/vz/template/iso/tiny10-transfer.iso"
    ;;
  run)
    shift
    guest_ps "New-Item -ItemType Directory -Force C:\\work\\tmp | Out-Null; \$env:TEMP='C:\\work\\tmp'; \$env:TMP='C:\\work\\tmp'; Set-Location C:\\work\\tiny10; \$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine'); $1; exit \$LASTEXITCODE"
    ;;
  *) echo "usage: $0 push | run '<powershell>'" >&2; exit 2;;
esac
