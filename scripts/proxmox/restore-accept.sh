#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Restore a tiny10 backup archive to a NEW VMID for acceptance testing.
# Usage: restore-accept.sh <archive.vma.zst> <newvmid> <name> [--storage local-lvm] [--start]
# Refuses newvmid < 9200, an existing VMID, an invalid/too long (> 15) computer name and a corrupt archive.
# Takes and stores no passwords: it prints the cloud-init commands for the operator to run, then (only with
# --start) starts the VM.
set -Eeuo pipefail
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib.sh
. "$HERE/lib.sh"

[ $# -ge 3 ] || die "usage: restore-accept.sh <archive> <newvmid> <name> [--storage local-lvm] [--start]" 2
ARCHIVE=$1
NEWVMID=$2
NAME=$3
shift 3
STORAGE=local-lvm
START=0
while [ $# -gt 0 ]; do
  case "$1" in
    --storage) [ $# -ge 2 ] || die "--storage needs a value" 2; STORAGE=$2; shift 2 ;;
    --start) START=1; shift ;;
    *) die "unknown argument: $1" 2 ;;
  esac
done

require_vmid_min "$NEWVMID"
valid_vm_name "$NAME" || die "invalid computer name '$NAME' (max 15 chars, letters/digits/hyphen, not all digits, no edge hyphen)" 2
[ -f "$ARCHIVE" ] || die "archive $ARCHIVE not found" 1
if vm_exists "$NEWVMID"; then die "VM $NEWVMID already exists" 1; fi
zstd -t "$ARCHIVE"

qmrestore "$ARCHIVE" "$NEWVMID" --storage "$STORAGE" --unique 1
if qm config "$NEWVMID" | grep -Eq '^template: 1'; then die "restored VM $NEWVMID is a template; refusing to continue" 1; fi
qm set "$NEWVMID" --name "$NAME"

cat <<NEXT
Restored VM $NEWVMID as '$NAME'. Next, as the operator (choose the password yourself; it is not stored here):
  qm set $NEWVMID --ciuser Administrator --cipassword '<password>' --ipconfig0 ip=<ip>/<prefix>,gw=<gw> --nameserver <dns>
  qm start $NEWVMID
Then run acceptance-body.ps1 through qm guest exec with \$AcceptancePassword set to that password.
NEXT
if [ "$START" -eq 1 ]; then qm start "$NEWVMID"; fi
