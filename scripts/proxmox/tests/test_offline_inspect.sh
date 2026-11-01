#!/bin/bash
# shellcheck source-path=SCRIPTDIR
set -Eeuo pipefail
# shellcheck source=helpers.sh
. "$(dirname "$0")/helpers.sh"
S="$SCRIPTS/offline-inspect.sh"

make_fixture() { # make_fixture <dir> [good|nosysprep|notag|starting|starting-notag|starting-failed]
  local f=$1
  mkdir -p "$f/ProgramData/Tiny11Template/day2-state" "$f/Windows/System32/Sysprep/Panther" "$f/Windows/System32/drivers" \
    "$f/Windows/Panther" "$f/Program Files/Cloudbase Solutions/Cloudbase-Init/conf" "$f/Program Files/Cloudbase Solutions/Cloudbase-Init/Python/Lib/site-packages"
  echo '{"State":"SysprepReturned"}' >"$f/ProgramData/Tiny11Template/finalization-status.json"
  [ "${2:-good}" = nosysprep ] && echo '{"State":"Running"}' >"$f/ProgramData/Tiny11Template/finalization-status.json"
  case "${2:-good}" in starting*) echo '{"Stage":"SysprepStarting"}' >"$f/ProgramData/Tiny11Template/finalization-status.json" ;; esac
  [ "${2:-good}" = starting-failed ] && : >"$f/Windows/System32/Sysprep/Sysprep_failed.tag"
  echo log >"$f/Windows/System32/Sysprep/Panther/setupact.log"
  [ "${2:-good}" = notag ] || [ "${2:-good}" = starting-notag ] || : >"$f/Windows/System32/Sysprep/Sysprep_succeeded.tag"
  echo drv >"$f/Windows/System32/drivers/vioscsi.sys"
  echo conf >"$f/Program Files/Cloudbase Solutions/Cloudbase-Init/conf/cloudbase-init.conf"
  echo py >"$f/Program Files/Cloudbase Solutions/Cloudbase-Init/Python/Lib/site-packages/tiny10_cloudbase.py"
  echo s >"$f/Windows/Panther/setupact.log"
}
setup_sfdisk() {
  cat >"$T/sfdisk.txt" <<'S'
label: gpt
/dev/pve/vm-9200-disk-1-part1 : start=        2048, size=      202752, type=C12A7328-F81F-11D2-BA4B-00A0C93EC93B
/dev/pve/vm-9200-disk-1-part3 : start=      237568, size=    40000000, type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7
S
  export STUB_SFDISK="$T/sfdisk.txt"
}

new_case inspect-running
setup_sfdisk
STUB_VM_STATE=running run bash "$S" 9200 "$WORK/out"
assert_rc 3; assert_not_logged '^losetup --find'; assert_no_file "$WORK/out"

new_case inspect-vmid
run bash "$S" 9100 "$WORK/out"
assert_rc 2

new_case inspect-outdir-exists
setup_sfdisk; mkdir "$WORK/out"
run bash "$S" 9200 "$WORK/out"
assert_rc_nonzero; assert_not_logged '^losetup --find'

new_case inspect-happy
setup_sfdisk; make_fixture "$T/fix"; export STUB_FIXTURE="$T/fix"
run bash "$S" 9200 "$WORK/out"
assert_rc 0
assert_eq "$(cat "$WORK/out/inspect.exit")" 0
assert_logged '^losetup --find --show --read-only --offset 121634816 --sizelimit 20480000000 /dev/pve/vm-9200-disk-1'
assert_logged '^mount -t ntfs3 -o ro,nosuid,nodev,noexec /dev/loop77 '
assert_logged '^losetup -d /dev/loop77'
assert_logged '^umount '
assert_file "$WORK/out/guest/Tiny11Template/finalization-status.json"
assert_file "$WORK/out/guest/Sysprep/Sysprep_succeeded.tag"
assert_file "$WORK/out/guest/Sysprep/Panther/setupact.log"
assert_file "$WORK/out/vioscsi-installed.sha256"
assert_file "$WORK/out/guest/cloudbase/Program Files/Cloudbase Solutions/Cloudbase-Init/Python/Lib/site-packages/tiny10_cloudbase.py"
assert_file "$WORK/out/evidence.sha256"
assert_eq "$(cat "$WORK/out/vmid.txt")" 9200
assert_eq "$(cat "$WORK/out/scsi0-volume.txt")" local-lvm:vm-9200-disk-1
if grep -rqi chrome "$S"; then fail "script mentions chrome"; else _ok; fi

new_case inspect-starting-with-tag
setup_sfdisk; make_fixture "$T/fix" starting; export STUB_FIXTURE="$T/fix"
run bash "$S" 9200 "$WORK/out"
assert_rc 0
assert_eq "$(cat "$WORK/out/inspect.exit")" 0

for variant in nosysprep notag starting-notag starting-failed; do
  new_case "inspect-$variant"
  setup_sfdisk; make_fixture "$T/fix" "$variant"; export STUB_FIXTURE="$T/fix"
  run bash "$S" 9200 "$WORK/out"
  assert_rc_nonzero
  assert_logged '^losetup -d /dev/loop77'
  assert_logged '^umount '
  if [ "$(cat "$WORK/out/inspect.exit")" != 0 ]; then _ok; else fail "inspect.exit is 0 on failure"; fi
done
summary test_offline_inspect
