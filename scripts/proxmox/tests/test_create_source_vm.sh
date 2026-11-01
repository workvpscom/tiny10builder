#!/bin/bash
# shellcheck source-path=SCRIPTDIR
set -Eeuo pipefail
# shellcheck source=helpers.sh
. "$(dirname "$0")/helpers.sh"
S="$SCRIPTS/create-source-vm.sh"

new_case create-vmid-9100
run bash "$S" 9100 local:iso/tiny10.iso
assert_rc 2; assert_not_logged '^qm create'

new_case create-happy
STUB_VM_STATE=none run bash "$S" 9200 local:iso/tiny10.iso
assert_rc 0
assert_logged '^qm create 9200 '
c=$(grep '^qm create' "$STUB_LOG")
for want in '--machine pc-q35-9.0' '--bios ovmf' '--ostype win10' '--cpu host' '--cores 2' '--memory 4096' '--balloon 0' \
  '--scsihw virtio-scsi-single' '--scsi0 local-lvm:20' '--efidisk0 local-lvm:1,efitype=4m,pre-enrolled-keys=1' \
  '--net0 e1000,bridge=vmbr1' '--agent enabled=1' '--ide2 local:iso/tiny10.iso,media=cdrom' '--boot order=ide2;scsi0'; do
  case "$c" in *"$want"*) _ok ;; *) fail "create args missing: $want" ;; esac
done
case "$c" in *tpmstate*) fail "tpmstate present" ;; *) _ok ;; esac
assert_not_logged '^qm start'

new_case create-bridge-storage
STUB_VM_STATE=none run bash "$S" 9201 local:iso/x.iso --storage other --bridge vmbr9
assert_rc 0
assert_logged '--scsi0 other:20'
assert_logged '--efidisk0 other:1,efitype=4m,pre-enrolled-keys=1'
assert_logged 'bridge=vmbr9'

new_case create-existing
STUB_VM_STATE=stopped run bash "$S" 9200 local:iso/tiny10.iso
assert_rc_nonzero; assert_not_logged '^qm create'

new_case create-usage
run bash "$S" 9200
assert_rc 2; assert_not_logged '^qm create'
summary test_create_source_vm
