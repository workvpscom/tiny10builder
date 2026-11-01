#!/bin/bash
# shellcheck source-path=SCRIPTDIR
set -Eeuo pipefail
# shellcheck source=helpers.sh
. "$(dirname "$0")/helpers.sh"
new_case lib
# shellcheck source=../lib.sh
. "$SCRIPTS/lib.sh"

for n in a tiny10-core WIN10 a-b 123a 12345678901234a; do
  run valid_vm_name "$n"; assert_rc 0
done
for n in '' 1234 -ab ab- 1234567890123456 tiny10_x 'a b' abcdefghijklmnop; do
  run valid_vm_name "$n"; assert_rc_nonzero
done

run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 9100"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min abc"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min ''"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 9200"; assert_rc 0
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 12345"; assert_rc 0
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 09300"; assert_rc 2

run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 99999999999999999999"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 9999999999"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 999999999"; assert_rc 0
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min +9200"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min ' 9200'"; assert_rc 2
run bash -c ". '$SCRIPTS/lib.sh'; require_vmid_min 9200abc"; assert_rc 2

# scsi0_volume / scsi0_device: no silent fallback
printf 'scsi0: local-lvm:vm-9210-disk-3,iothread=1,size=20G\n' >"$T/c1.conf"
run bash -c ". '$SCRIPTS/lib.sh'; scsi0_volume 9210 '$T/c1.conf'"; assert_rc 0; assert_eq "$OUT" local-lvm:vm-9210-disk-3
run bash -c ". '$SCRIPTS/lib.sh'; scsi0_device 9210 '$T/c1.conf'"; assert_rc 0; assert_eq "$OUT" /dev/pve/vm-9210-disk-3
run bash -c ". '$SCRIPTS/lib.sh'; scsi0_device 9211 '$T/c1.conf'"; assert_rc_nonzero; assert_out 'no .scsi0'
printf 'sata0: local-lvm:vm-9210-disk-1,size=20G\n' >"$T/c2.conf"
run bash -c ". '$SCRIPTS/lib.sh'; scsi0_device 9210 '$T/c2.conf'"; assert_rc_nonzero; assert_out 'no .scsi0'

# require_lvm_free_gb <storage> <gib> (pvesm reports KiB; 40 GiB = 41943040 KiB)
new_case lvm-free
lv() { run bash -c ". '$SCRIPTS/lib.sh'; require_lvm_free_gb local-lvm 40"; }
lv; assert_rc 0; assert_logged '^pvesm status --storage local-lvm'
STUB_LVM_AVAIL=41943040 lv; assert_rc 0
STUB_LVM_AVAIL=41943039 lv; assert_rc 3
STUB_LVM_AVAIL=31457280 lv; assert_rc 3; assert_out '30 GiB'
STUB_LVM_STATUS=inactive lv; assert_rc 3; assert_out 'not active'
STUB_PVESM_RAW='' lv; assert_rc 3
STUB_PVESM_RAW='garbage' lv; assert_rc 3
STUB_PVESM_RAW=$'Name Type Status\nlocal-lvm lvmthin active 1 2\n' lv; assert_rc 3
STUB_PVESM_RAW=$'Name Type Status Total Used Available %\nother lvmthin active 1 2 99999999999 1%\n' lv; assert_rc 3
STUB_PVESM_RAW=$'Name Type Status Total Used Available %\nlocal-lvm lvmthin active 1 2 12x4 1%\n' lv; assert_rc 3
STUB_PVESM_RAW=$'Name Type Status Total Used Available %\nlocal-lvm lvmthin active 1 2 -5 1%\n' lv; assert_rc 3

# partition picker: largest NTFS data partition wins, nothing hard-coded
cat >"$T/sfdisk.txt" <<'S'
label: gpt
device: /dev/pve/vm-9200-disk-1
unit: sectors

/dev/pve/vm-9200-disk-1-part1 : start=        2048, size=      202752, type=C12A7328-F81F-11D2-BA4B-00A0C93EC93B, uuid=A, name="EFI"
/dev/pve/vm-9200-disk-1-part2 : start=      204800, size=       32768, type=E3C9E316-0B5C-4DB8-817D-F92DF00215AE, uuid=B, name="MSR"
/dev/pve/vm-9200-disk-1-part3 : start=      237568, size=    40000000, type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7, uuid=C, name="Win"
/dev/pve/vm-9200-disk-1-part4 : start=    40237568, size=     1000000, type=DE94BBA4-06D1-4D40-A16A-BFD50179D6AC, uuid=D, name="Rec"
S
run bash -c ". '$SCRIPTS/lib.sh'; find_ntfs_partition <'$T/sfdisk.txt'"
assert_rc 0; assert_eq "$OUT" "237568 40000000"
cat >"$T/mbr.txt" <<'S'
label: dos
/dev/x1 : start=2048, size=1000, type=7, bootable
/dev/x2 : start=9999, size=5000, type=7
S
run bash -c ". '$SCRIPTS/lib.sh'; find_ntfs_partition <'$T/mbr.txt'"
assert_eq "$OUT" "9999 5000"
printf 'label: gpt\n' >"$T/none.txt"
run bash -c ". '$SCRIPTS/lib.sh'; find_ntfs_partition <'$T/none.txt'"; assert_rc_nonzero

# require_free_gb / require_stopped
export STUB_DF_AVAIL=5000000000
run bash -c ". '$SCRIPTS/lib.sh'; require_free_gb '$WORK' 12"; assert_rc 3
export STUB_DF_AVAIL=13000000000
run bash -c ". '$SCRIPTS/lib.sh'; require_free_gb '$WORK' 12"; assert_rc 0
STUB_VM_STATE=running run bash -c ". '$SCRIPTS/lib.sh'; require_stopped 9200"; assert_rc 3
run bash -c ". '$SCRIPTS/lib.sh'; require_stopped 9200"; assert_rc 0

# sysprep_evidence_ok <status.json> <sysprep-dir>
sy() { # sy <json-or-NONE> <tags: s|f|sf|none>
  rm -rf "$T/sy"; mkdir -p "$T/sy/ev"
  if [ "$1" != NONE ]; then printf '%s\n' "$1" >"$T/sy/status.json"; fi
  case "$2" in *s*) : >"$T/sy/ev/Sysprep_succeeded.tag" ;; esac
  case "$2" in *f*) : >"$T/sy/ev/Sysprep_failed.tag" ;; esac
  run bash -c ". '$SCRIPTS/lib.sh'; sysprep_evidence_ok '$T/sy/status.json' '$T/sy/ev'"
}
new_case sysprep-evidence
sy '{"Stage":"SysprepReturned"}' s; assert_rc 0
sy '{"Stage":"SysprepReturned"}' none; assert_rc 1; assert_out 'Sysprep_succeeded.tag'
sy '{"Stage":"SysprepReturned"}' sf; assert_rc 1; assert_out 'Sysprep_failed.tag'
sy '{"Stage":"Failed"}' sf; assert_rc 1
sy '{"Stage":"SysprepStarting"}' s; assert_rc 0
sy '{"Stage":"SysprepStarting"}' none; assert_rc 1; assert_out 'Sysprep_succeeded.tag'
sy '{"Stage":"SysprepStarting"}' sf; assert_rc 1; assert_out 'Sysprep_failed.tag'
sy '{"Stage":"Failed"}' s; assert_rc 1
sy '{"Stage":"Running"}' s; assert_rc 1
sy NONE s; assert_rc 1; assert_out 'missing'
sy '' s; assert_rc 1
sy 'not json {{' s; assert_rc 1
summary test_lib
