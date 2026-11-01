#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Offline vzdump backup of a stopped, sysprepped tiny10 source VM.
# Usage: backup.sh <vmid> <dumpdir>       (<dumpdir> must exist, with > 12 GB free)
# Exit codes: 2 bad arguments, 3 VM running or too little free space, other non-zero = failed check.
# Evidence convention (produced by offline-inspect.sh <vmid> <dumpdir>/offline-evidence):
#   <dumpdir>/offline-evidence/inspect.exit                                 must contain 0
#   <dumpdir>/offline-evidence/guest/Tiny11Template/finalization-status.json must contain SysprepReturned
#   <dumpdir>/offline-evidence/guest/Sysprep/Sysprep_succeeded.tag           must exist
# Outputs: <dumpdir>/dump/*.vma.zst (new dir, must not pre-exist) and <dumpdir>/backup-<UTC>/ with job.log,
# source.conf, scsi-before/after.sha256, archive.sha256, archive.path, exit and result.txt (BACKUP_PASS).
# The node's /etc/vzdump.conf hook deletes/uploads archives, so `--script /bin/true` is mandatory below.
# Never starts the VM, converts it to a template, or touches files outside <dumpdir>.
set -Eeuo pipefail
umask 077
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib.sh
. "$HERE/lib.sh"

[ $# -eq 2 ] || die "usage: backup.sh <vmid> <dumpdir>" 2
VMID=$1
DUMPDIR=$2
require_vmid_min "$VMID"
[ -d "$DUMPDIR" ] || die "dumpdir $DUMPDIR does not exist" 2
require_stopped "$VMID"
require_free_gb "$DUMPDIR" 12

EV="$DUMPDIR/offline-evidence"
[ -f "$EV/inspect.exit" ] && [ "$(cat "$EV/inspect.exit")" = 0 ] || die "offline-inspect evidence missing or not clean ($EV/inspect.exit)" 1
grep -q SysprepReturned "$EV/guest/Tiny11Template/finalization-status.json" || die "evidence lacks SysprepReturned" 1
[ -f "$EV/guest/Sysprep/Sysprep_succeeded.tag" ] || die "evidence lacks Sysprep_succeeded.tag" 1
[ ! -e "$DUMPDIR/dump" ] || die "$DUMPDIR/dump already exists; refusing to mix archives" 1

R="$DUMPDIR/backup-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -m700 "$R"
exec 3>&1
exec >"$R/job.log" 2>&1
finish() {
  local rc=$?
  echo "$rc" >"$R/exit"
  echo "backup exit $rc (see $R)" >&3
}
trap finish EXIT

DEV=$(scsi0_device "$VMID")
[ -z "$(losetup -j "$DEV")" ] || die "$DEV still has a loop device attached" 1

qm set "$VMID" --ide2 none,media=cdrom --ide0 local-lvm:cloudinit --citype configdrive2
qm config "$VMID" >"$R/source.conf"
if grep -Eq '^(template: 1|ciuser:|cipassword:|ipconfig[0-9]+:|cicustom:|sshkeys:)' "$R/source.conf"; then
  die "source config carries template/cloud-init identity settings" 1
fi
BEFORE=$(sha256sum "$DEV")
printf '%s\n' "$BEFORE" >"$R/scsi-before.sha256"

mkdir "$DUMPDIR/dump"
vzdump "$VMID" --mode stop --compress zstd --zstd 1 --bwlimit 65536 --remove 0 --script /bin/true --dumpdir "$DUMPDIR/dump" >"$R/vzdump.log" 2>&1

ARCH=''
COUNT=0
while IFS= read -r f; do
  ARCH=$f
  COUNT=$((COUNT + 1))
done < <(find "$DUMPDIR/dump" -name '*.vma.zst' -type f)
[ "$COUNT" -eq 1 ] || die "expected exactly one *.vma.zst, found $COUNT" 1
zstd -t "$ARCH"
sha256sum "$ARCH" >"$R/archive.sha256"
echo "$ARCH" >"$R/archive.path"

AFTER=$(sha256sum "$DEV")
printf '%s\n' "$AFTER" >"$R/scsi-after.sha256"
[ "$BEFORE" = "$AFTER" ] || die "source disk hash changed during backup" 1
require_stopped "$VMID"
echo BACKUP_PASS >"$R/result.txt"
