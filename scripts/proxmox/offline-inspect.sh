#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Read-only offline inspection of a stopped, sysprepped tiny10 source VM.
# Usage: offline-inspect.sh <vmid> <outdir>      (<outdir> must not exist; its parent must)
# backup.sh expects the evidence at <dumpdir>/offline-evidence, i.e. run with <outdir> = <dumpdir>/offline-evidence.
# Evidence layout: inspect.exit, inspect.log, vmid.txt, scsi0-volume.txt (backup.sh ties both to the VM), guest/Tiny11Template/..., guest/Sysprep/{Sysprep_succeeded.tag,Panther/},
# guest/cloudbase/..., vioscsi-installed.sha256, evidence.sha256. The loop device is read-only and always detached.
# Success rule (lib.sh sysprep_evidence_ok): finalization-status.json is SysprepReturned or SysprepStarting, AND
# Sysprep_succeeded.tag is present, AND Sysprep_failed.tag is absent. Sysprep /shutdown can power the VM off before the
# guest writes SysprepReturned, yet Sysprep itself writes the succeeded tag; the tag is required always.
set -Eeuo pipefail
umask 077
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib.sh
. "$HERE/lib.sh"

[ $# -eq 2 ] || die "usage: offline-inspect.sh <vmid> <outdir>" 2
VMID=$1
E=$2
require_vmid_min "$VMID"
require_stopped "$VMID"
mkdir -m 700 "$E" # fails closed if the directory already exists

LOOP=''
MNT="$E/mnt"
exec 3>&1
exec >"$E/inspect.log" 2>&1
cleanup() {
  local rc=$?
  trap - EXIT
  if mountpoint -q "$MNT"; then umount "$MNT" || rc=90; fi
  if [ -n "$LOOP" ]; then losetup -d "$LOOP" || rc=91; fi
  printf '%s\n' "$rc" >"$E/inspect.exit"
  printf 'offline-inspect exit %s (evidence in %s)\n' "$rc" "$E" >&3
  exit "$rc"
}
trap cleanup EXIT

qm config "$VMID" >"$E/qm-before.conf"
printf '%s\n' "$VMID" >"$E/vmid.txt"
scsi0_volume "$VMID" "$E/qm-before.conf" >"$E/scsi0-volume.txt"
DEV=$(scsi0_device "$VMID" "$E/qm-before.conf")
[ -z "$(losetup -j "$DEV")" ] || die "$DEV already has a loop device attached" 1
sfdisk -d "$DEV" >"$E/partition-table.txt"
# Last/largest NTFS data partition from the real partition table; nothing is hard-coded.
read -r START SIZE <<<"$(find_ntfs_partition <"$E/partition-table.txt")"
[ -n "${START:-}" ] && [ -n "${SIZE:-}" ] || die "no NTFS data partition found in $DEV" 1
echo "NTFS partition: start=$START size=$SIZE sectors" >"$E/partition-chosen.txt"

LOOP=$(losetup --find --show --read-only --offset $((START * 512)) --sizelimit $((SIZE * 512)) "$DEV")
mkdir -m 700 "$MNT"
mount -t ntfs3 -o ro,nosuid,nodev,noexec "$LOOP" "$MNT"
findmnt -n -o SOURCE,FSTYPE,OPTIONS --target "$MNT" >"$E/mount-options.txt"
blockdev --getro "$LOOP" >"$E/loop-readonly.txt"
df -B1 "$MNT" >"$E/guest-space.txt"

list_files() { # list_files <dir> <maxdepth>: "relative-path bytes"
  [ -d "$1" ] || return 0
  find "$1" -maxdepth "$2" -type f | while IFS= read -r f; do
    printf '%s %s bytes\n' "${f#"$1"/}" "$(wc -c <"$f" | tr -d ' ')"
  done
}

TPL="$MNT/ProgramData/Tiny11Template"
[ -d "$TPL" ] || die "template state dir missing in guest" 1
list_files "$TPL" 4 >"$E/template-file-list.txt"
mkdir -m 700 "$E/guest"
cp -a "$TPL" "$E/guest/"

mkdir -m 700 "$E/guest/Sysprep" "$E/guest/Panther"
cp -a "$MNT/Windows/System32/Sysprep/Panther" "$E/guest/Sysprep/"
find "$MNT/Windows/System32/Sysprep" -maxdepth 1 -type f -iname '*tag*' | while IFS= read -r f; do cp -a "$f" "$E/guest/Sysprep/"; done
for f in setupact.log setuperr.log; do
  if [ -f "$MNT/Windows/Panther/$f" ]; then cp -a "$MNT/Windows/Panther/$f" "$E/guest/Panther/"; fi
done

CB="$MNT/Program Files/Cloudbase Solutions"
list_files "$CB" 5 | grep -E '\.(conf|py) ' >"$E/cloudbase-files.txt" || true
mkdir -m 700 "$E/guest/cloudbase"
if [ -d "$CB" ]; then
  find "$CB" -maxdepth 5 -type f \( -name 'cloudbase-init.conf' -o -name 'cloudbase-init-unattend.conf' -o -iname 'tiny10*.py' \) |
    while IFS= read -r f; do
      rel=${f#"$MNT"/}
      mkdir -p "$E/guest/cloudbase/$(dirname "$rel")"
      cp -a "$f" "$E/guest/cloudbase/$rel"
    done
fi

find "$MNT/Windows/System32/drivers" -maxdepth 1 -type f -iname 'vioscsi.sys' | while IFS= read -r f; do sha256sum "$f"; done >"$E/vioscsi-installed.sha256"

# Assertions: sysprep succeeded (see sysprep_evidence_ok in lib.sh); the driver is present.
sysprep_evidence_ok "$E/guest/Tiny11Template/finalization-status.json" "$E/guest/Sysprep" || die "Sysprep success not proven by the collected evidence" 1
[ -s "$E/vioscsi-installed.sha256" ] || die "vioscsi.sys not found in guest" 1

find "$E/guest" -type f -exec chmod 600 '{}' +
find "$E/guest" -type d -exec chmod 700 '{}' +
find "$E/guest" -type f | LC_ALL=C sort | while IFS= read -r f; do sha256sum "$f"; done >"$E/evidence.sha256"
require_stopped "$VMID"
echo OFFLINE_COLLECTION_COMPLETE
