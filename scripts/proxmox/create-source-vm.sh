#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Create the tiny10 source VM (Q35 9.0, OVMF without TPM, virtio-scsi-single, 20G scsi0, e1000, QGA, ISO on ide2).
# Usage: create-source-vm.sh <vmid> <iso-volid> [--storage local-lvm] [--bridge vmbr1]
# Refuses VMID < 9200 and an existing VMID. Never starts the VM.
set -Eeuo pipefail
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=lib.sh
. "$HERE/lib.sh"

[ $# -ge 2 ] || die "usage: create-source-vm.sh <vmid> <iso-volid> [--storage local-lvm] [--bridge vmbr1]" 2
VMID=$1
ISO=$2
shift 2
STORAGE=local-lvm
BRIDGE=vmbr1
while [ $# -gt 0 ]; do
  case "$1" in
    --storage) [ $# -ge 2 ] || die "--storage needs a value" 2; STORAGE=$2; shift 2 ;;
    --bridge) [ $# -ge 2 ] || die "--bridge needs a value" 2; BRIDGE=$2; shift 2 ;;
    *) die "unknown argument: $1" 2 ;;
  esac
done

require_vmid_min "$VMID"
if vm_exists "$VMID"; then die "VM $VMID already exists; refusing to touch it" 2; fi

qm create "$VMID" \
  --name tiny10-src \
  --machine pc-q35-9.0 \
  --bios ovmf \
  --efidisk0 "$STORAGE:1,efitype=4m,pre-enrolled-keys=1" \
  --ostype win10 \
  --cpu host \
  --cores 2 \
  --memory 4096 \
  --balloon 0 \
  --scsihw virtio-scsi-single \
  --scsi0 "$STORAGE:20" \
  --net0 "e1000,bridge=$BRIDGE" \
  --agent enabled=1 \
  --ide2 "$ISO,media=cdrom" \
  --boot 'order=ide2;scsi0'
echo "VM $VMID created (not started)."
