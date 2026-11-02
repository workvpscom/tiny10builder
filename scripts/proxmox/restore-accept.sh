#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Restore a tiny10 backup archive to a NEW VMID for acceptance testing.
# Usage: restore-accept.sh <archive.vma.zst> <newvmid> <name> [--storage local-lvm]
# Refuses newvmid < 9200, an existing VMID, an invalid/too long (> 15) computer name, a corrupt archive, any storage
# other than local-lvm and less than 40 GiB free on it (exit 3).
# Takes and stores no passwords and never starts the VM: it prints the cloud-init commands (set them BEFORE the
# first start) and the start command for the operator to run.
set -Eeuo pipefail
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib.sh
. "$HERE/lib.sh"

[ $# -ge 3 ] || die "usage: restore-accept.sh <archive> <newvmid> <name> [--storage local-lvm]" 2
ARCHIVE=$1
NEWVMID=$2
NAME=$3
shift 3
STORAGE=local-lvm
while [ $# -gt 0 ]; do
  case "$1" in
    --storage) [ $# -ge 2 ] || die "--storage needs a value" 2; STORAGE=$2; shift 2 ;;
    *) die "unknown argument: $1" 2 ;;
  esac
done

require_vmid_min "$NEWVMID"
[ "$STORAGE" = local-lvm ] || die "only --storage local-lvm is allowed (got '$STORAGE')" 2
valid_vm_name "$NAME" || die "invalid computer name '$NAME' (max 15 chars, letters/digits/hyphen, not all digits, no edge hyphen)" 2
[ -f "$ARCHIVE" ] || die "archive $ARCHIVE not found" 1
if vm_exists "$NEWVMID"; then die "VM $NEWVMID already exists" 1; fi
zstd -t "$ARCHIVE"
require_lvm_free_gb "$STORAGE" 40

qmrestore "$ARCHIVE" "$NEWVMID" --storage "$STORAGE" --unique 1
if qm config "$NEWVMID" | grep -Eq '^template: 1'; then die "restored VM $NEWVMID is a template; refusing to continue" 1; fi
qm set "$NEWVMID" --name "$NAME"

cat <<NEXT
Restored VM $NEWVMID as '$NAME'. Next, as the operator (set the cloud-init options BEFORE the first start; choose the password yourself, it is not stored here):
  qm set $NEWVMID --ciuser Administrator --cipassword '<password>' --ipconfig0 ip=<ip>/<prefix>,gw=<gw> --nameserver <dns>
  qm start $NEWVMID
Then run acceptance-body.ps1 through qm guest exec with \$AcceptancePassword set to that password.
NEXT
