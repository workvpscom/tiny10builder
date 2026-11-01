#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Offline vzdump backup of a stopped, sysprepped tiny10 source VM.
# Usage: backup.sh <vmid> <dumpdir>       (<dumpdir>: absolute path of an existing directory with > 12 GB free;
#                                         "/" and the node-global /var/lib/vz/dump are refused)
# Exit codes: 2 bad arguments, 3 VM running or too little free space, other non-zero = failed check.
# Evidence convention (produced by offline-inspect.sh <vmid> <dumpdir>/offline-evidence):
#   <dumpdir>/offline-evidence/inspect.exit                                 must contain 0
#   <dumpdir>/offline-evidence/vmid.txt                                     must equal <vmid>
#   <dumpdir>/offline-evidence/scsi0-volume.txt                             must equal the live scsi0 volume of the VM
#   <dumpdir>/offline-evidence/guest/Tiny11Template/finalization-status.json + guest/Sysprep/ must satisfy lib.sh
#   sysprep_evidence_ok: stage SysprepReturned or SysprepStarting, AND Sysprep_succeeded.tag present, AND no
#   Sysprep_failed.tag (Sysprep /shutdown can power the VM off before the guest writes SysprepReturned).
# Outputs: <dumpdir>/dump/*.vma.zst (new dir, must not pre-exist) and <dumpdir>/backup-<UTC>/ with job.log,
# source-before.conf, source.conf (after qm set), scsi-before/after.sha256, archive.sha256, archive.path, exit and result.txt (BACKUP_PASS).
# The node's /etc/vzdump.conf hook deletes/uploads archives, so `--script /bin/true` is mandatory below.
# The config is checked before any change (qm set only runs on a clean config) and again after it, which must show
# `boot: order=scsi0`. Never starts the VM, converts it to a template, or touches files outside <dumpdir>.
set -Eeuo pipefail
umask 077
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib.sh
. "$HERE/lib.sh"

[ $# -eq 2 ] || die "usage: backup.sh <vmid> <dumpdir>" 2
VMID=$1
DUMPDIR=$2
require_vmid_min "$VMID"
case "$DUMPDIR" in /*) ;; *) die "dumpdir must be an absolute path ($DUMPDIR)" 2 ;; esac
refuse_dumpdir() { # refuse the filesystem root and the node-global dump directory scanned by the vzdump hook
  local d=${1%/}
  case "$d" in '' | /var/lib/vz/dump) die "refusing dumpdir '$1': it is the filesystem root or the node-global dump directory" 2 ;; esac
}
refuse_dumpdir "$DUMPDIR"
[ -d "$DUMPDIR" ] || die "dumpdir $DUMPDIR does not exist" 2
DUMPDIR=$(cd "$DUMPDIR" && pwd -P)
refuse_dumpdir "$DUMPDIR"
require_stopped "$VMID"
require_free_gb "$DUMPDIR" 12

EV="$DUMPDIR/offline-evidence"
[ -f "$EV/inspect.exit" ] && [ "$(cat "$EV/inspect.exit")" = 0 ] || die "offline-inspect evidence missing or not clean ($EV/inspect.exit)" 1
sysprep_evidence_ok "$EV/guest/Tiny11Template/finalization-status.json" "$EV/guest/Sysprep" || die "evidence does not prove Sysprep success" 1
# Tie the evidence to this VM and to its current system disk before anything is changed.
[ -f "$EV/vmid.txt" ] && [ "$(tr -d ' \r\n' <"$EV/vmid.txt")" = "$VMID" ] || die "evidence does not belong to VM $VMID ($EV/vmid.txt missing or different)" 1
[ -f "$EV/scsi0-volume.txt" ] || die "evidence has no scsi0-volume.txt" 1
LIVE_VOL=$(scsi0_volume "$VMID") || exit $?
[ "$LIVE_VOL" = "$(tr -d ' \r\n' <"$EV/scsi0-volume.txt")" ] || die "live scsi0 volume $LIVE_VOL differs from the inspected volume in the evidence" 1
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

IDENTITY_RE='^(template: 1|ciuser:|cipassword:|ipconfig[0-9]+:|cicustom:|sshkeys:)'
# Pre-check the current config: a bad config is rejected before any mutation.
qm config "$VMID" >"$R/source-before.conf"
if grep -Eq "$IDENTITY_RE" "$R/source-before.conf"; then
  die "source config carries template/cloud-init identity settings (nothing was changed)" 1
fi
qm set "$VMID" --ide2 none,media=cdrom --ide0 local-lvm:cloudinit --citype configdrive2 --boot order=scsi0
# Re-capture and re-check the final config, including the boot order.
qm config "$VMID" >"$R/source.conf"
if grep -Eq "$IDENTITY_RE" "$R/source.conf"; then
  die "source config carries template/cloud-init identity settings" 1
fi
grep -qx 'boot: order=scsi0' "$R/source.conf" || die "source config boot order is not exactly 'order=scsi0'" 1
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
