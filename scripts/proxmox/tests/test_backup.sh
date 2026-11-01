#!/bin/bash
# shellcheck source-path=SCRIPTDIR
set -Eeuo pipefail
# shellcheck source=helpers.sh
. "$(dirname "$0")/helpers.sh"
S="$SCRIPTS/backup.sh"

new_case backup-vmid-9100
run bash "$S" 9100 "$WORK"
assert_rc 2; assert_not_logged '^vzdump'; assert_not_logged '^qm set'

new_case backup-running
make_evidence "$WORK"
STUB_VM_STATE=running run bash "$S" 9200 "$WORK"
assert_rc 3; assert_not_logged '^vzdump'; assert_not_logged '^qm set'

new_case backup-low-space
make_evidence "$WORK"
STUB_DF_AVAIL=5000000000 run bash "$S" 9200 "$WORK"
assert_rc 3; assert_not_logged '^vzdump'

new_case backup-no-evidence
run bash "$S" 9200 "$WORK"
assert_rc_nonzero; assert_not_logged '^vzdump'

new_case backup-bad-evidence
make_evidence "$WORK"; echo 1 >"$WORK/offline-evidence/inspect.exit"
run bash "$S" 9200 "$WORK"
assert_rc_nonzero; assert_not_logged '^vzdump'

new_case backup-no-sysprep-returned
make_evidence "$WORK"; echo '{"State":"Running"}' >"$WORK/offline-evidence/guest/Tiny11Template/finalization-status.json"
run bash "$S" 9200 "$WORK"
assert_rc_nonzero; assert_not_logged '^vzdump'

new_case backup-starting-with-tag
make_evidence "$WORK" starting
run bash "$S" 9200 "$WORK"
assert_rc 0; assert_logged '^vzdump'; assert_logged '^qm set'

for v in starting-notag starting-bothtags; do
  new_case "backup-$v"
  make_evidence "$WORK" "$v"
  run bash "$S" 9200 "$WORK"
  assert_rc_nonzero; assert_not_logged '^vzdump'; assert_not_logged '^qm set'
done

new_case backup-failed-stage-with-tag
make_evidence "$WORK"; echo '{"Stage":"Failed"}' >"$WORK/offline-evidence/guest/Tiny11Template/finalization-status.json"
run bash "$S" 9200 "$WORK"
assert_rc_nonzero; assert_not_logged '^vzdump'; assert_not_logged '^qm set'

for bad in 'cipassword: secret' 'template: 1' 'ciuser: Administrator' 'ipconfig0: ip=dhcp' 'cicustom: user=x' 'sshkeys: abc'; do
  new_case "backup-config-$bad"
  make_evidence "$WORK"
  { cat "$STUB_DIR/default.conf"; echo "$bad"; } >"$T/bad.conf"
  STUB_QM_CONFIG="$T/bad.conf" run bash "$S" 9200 "$WORK"
  assert_rc_nonzero; assert_not_logged '^vzdump'; assert_not_logged '^qm set'
done

new_case backup-happy
make_evidence "$WORK"
run bash "$S" 9200 "$WORK"
assert_rc 0
assert_eq "$(grep '^vzdump' "$STUB_LOG")" "vzdump 9200 --mode stop --compress zstd --zstd 1 --bwlimit 65536 --remove 0 --script /bin/true --dumpdir $WORK/dump"
assert_eq "$(grep -c '^vzdump' "$STUB_LOG")" 1
assert_eq "$(grep '^qm set' "$STUB_LOG")" "qm set 9200 --ide2 none,media=cdrom --ide0 local-lvm:cloudinit --citype configdrive2 --boot order=scsi0"
assert_eq "$(grep -c '^boot:' "$WORK"/backup-*/source.conf)" 1
assert_eq "$(grep '^boot:' "$WORK"/backup-*/source.conf)" "boot: order=scsi0"
assert_logged '^zstd -t '
assert_not_logged '^qm start'
assert_not_logged '^qm template'
assert_not_logged 'rclone'
R=''; for d in "$WORK"/backup-*; do R=$d; done
assert_eq "$(cat "$R/result.txt")" BACKUP_PASS
assert_file "$R/archive.sha256"; assert_file "$R/source.conf"; assert_file "$R/scsi-before.sha256"
assert_eq "$(cat "$R/exit")" 0

new_case backup-hash-mismatch
make_evidence "$WORK"
STUB_MUTATE=1 run bash "$S" 9200 "$WORK"
assert_rc_nonzero
assert_logged '^vzdump'
if ls "$WORK"/backup-*/result.txt >/dev/null 2>&1; then fail "result.txt written despite hash mismatch"; else _ok; fi

new_case backup-boot-not-applied
make_evidence "$WORK"
STUB_SET_NOOP_BOOT=1 run bash "$S" 9200 "$WORK"
assert_rc_nonzero; assert_not_logged '^vzdump'

new_case backup-boot-ide2-never-passes
make_evidence "$WORK"
STUB_SET_NOOP_BOOT=1 run bash "$S" 9200 "$WORK"
if ls "$WORK"/backup-*/result.txt >/dev/null 2>&1; then fail "BACKUP_PASS with ide2 boot order"; else _ok; fi

new_case backup-two-archives-or-none
make_evidence "$WORK"
STUB_NO_ARCHIVE=1 run bash "$S" 9200 "$WORK"
assert_rc_nonzero

new_case backup-zstd-fail
make_evidence "$WORK"
STUB_ZSTD_FAIL=1 run bash "$S" 9200 "$WORK"
assert_rc_nonzero
if ls "$WORK"/backup-*/result.txt >/dev/null 2>&1; then fail "result.txt written"; else _ok; fi

new_case backup-dump-preexists
make_evidence "$WORK"; mkdir "$WORK/dump"
run bash "$S" 9200 "$WORK"
assert_rc_nonzero; assert_not_logged '^vzdump'
summary test_backup
