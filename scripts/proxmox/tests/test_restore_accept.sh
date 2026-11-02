#!/bin/bash
# shellcheck source-path=SCRIPTDIR
set -Eeuo pipefail
# shellcheck source=helpers.sh
. "$(dirname "$0")/helpers.sh"
S="$SCRIPTS/restore-accept.sh"

new_case restore-vmid-9100
echo x >"$WORK/a.vma.zst"
run bash "$S" "$WORK/a.vma.zst" 9100 tiny10-acc
assert_rc 2; assert_not_logged '^qmrestore'

new_case restore-16-char-name
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none run bash "$S" "$WORK/a.vma.zst" 9201 abcdefghijklmnop
assert_rc 2; assert_not_logged '^qmrestore'

new_case restore-missing-archive
STUB_VM_STATE=none run bash "$S" "$WORK/nope.vma.zst" 9201 tiny10-acc
assert_rc_nonzero; assert_not_logged '^qmrestore'

new_case restore-bad-zstd
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none STUB_ZSTD_FAIL=1 run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc
assert_rc_nonzero; assert_not_logged '^qmrestore'

new_case restore-existing-vmid
echo x >"$WORK/a.vma.zst"
run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc
assert_rc_nonzero; assert_not_logged '^qmrestore'

new_case restore-15-char-name
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none run bash "$S" "$WORK/a.vma.zst" 9201 abcdefghijklmno
assert_rc 0
assert_logged '^qmrestore '"$WORK"'/a.vma.zst 9201 --storage local-lvm --unique 1'
assert_logged '^qm set 9201 --name abcdefghijklmno'
assert_logged '^pvesm status --storage local-lvm'
assert_out 'BEFORE the first start'
assert_not_logged '^qm start'
assert_out '--ciuser Administrator --cipassword'
assert_out '--ipconfig0 ip=<ip>/<prefix>,gw=<gw>'
assert_out '--nameserver <dns>'

new_case restore-template-flag
echo x >"$WORK/a.vma.zst"
{ cat "$STUB_DIR/default.conf"; echo 'template: 1'; } >"$T/t.conf"
STUB_VM_STATE=none STUB_QM_CONFIG="$T/t.conf" run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc
assert_rc_nonzero; assert_not_logged '^qm set 9201 --name'

new_case restore-start-removed
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc --start
assert_rc 2; assert_not_logged '^qmrestore'; assert_not_logged '^qm start'

new_case restore-other-storage
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc --storage foo
assert_rc 2; assert_not_logged '^qmrestore'

new_case restore-low-lvm-free
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none STUB_LVM_AVAIL=31457280 run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc
assert_rc 3; assert_not_logged '^qmrestore'; assert_not_logged '^qm set'

new_case restore-garbled-pvesm
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none STUB_PVESM_RAW='nonsense' run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc
assert_rc 3; assert_not_logged '^qmrestore'

new_case restore-inactive-storage
echo x >"$WORK/a.vma.zst"
STUB_VM_STATE=none STUB_LVM_STATUS=inactive run bash "$S" "$WORK/a.vma.zst" 9201 tiny10-acc
assert_rc 3; assert_not_logged '^qmrestore'
summary test_restore_accept
