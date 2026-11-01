#!/bin/bash
# shellcheck source-path=SCRIPTDIR
# Shared helpers for the tiny10 Proxmox scripts. Source this file; do not execute it.
# Exit codes used by the scripts: 2 = bad usage/arguments/refused target, 3 = precondition not met
# (VM running, not enough free space).
# bash 3.2 compatible; every Proxmox command is called by plain name so tests can stub it via PATH.

TINY10_MIN_VMID=9200

die() { # die <message> [exit-code]
  printf '%s: %s\n' "${0##*/}" "$1" >&2
  exit "${2:-1}"
}

# require_vmid_min <vmid>: numeric, no leading zero, >= 9200. Refuses with exit 2.
require_vmid_min() {
  local v=${1:-}
  case "$v" in
    '' | *[!0-9]* | 0*) die "VMID '$v' is not a plain positive number" 2 ;;
  esac
  if [ "$v" -lt "$TINY10_MIN_VMID" ]; then
    die "refusing VMID $v: tiny10 scripts only touch VMIDs >= $TINY10_MIN_VMID" 2
  fi
}

# vm_exists <vmid>: true when qm status succeeds.
vm_exists() { qm status "$1" >/dev/null 2>&1; }

# require_stopped <vmid>: exit 3 unless `qm status` prints exactly "status: stopped".
require_stopped() {
  local s
  s=$(qm status "$1") || die "cannot read status of VM $1" 3
  [ "$s" = 'status: stopped' ] || die "VM $1 is not stopped ($s)" 3
}

# require_free_gb <dir> <gb>: exit 3 unless more than <gb> * 10^9 bytes are available on <dir>.
require_free_gb() {
  local free
  free=$(df -B1 --output=avail "$1" | tail -n 1 | tr -d ' ')
  case "$free" in '' | *[!0-9]*) die "cannot determine free space on $1" 3 ;; esac
  if [ "$free" -le $(($2 * 1000000000)) ]; then
    die "only $free bytes free on $1, need more than $2 GB" 3
  fi
}

# valid_vm_name <name>: NetBIOS-safe computer name, <= 15 chars, letters/digits/hyphen,
# no leading/trailing hyphen, not all digits.
valid_vm_name() {
  local n=${1:-}
  [ -n "$n" ] || return 1
  [ "${#n}" -le 15 ] || return 1
  case "$n" in
    *[!A-Za-z0-9-]* | -* | *-) return 1 ;;
    *[!0-9]*) return 0 ;;
    *) return 1 ;;
  esac
}

# scsi0_device <vmid> [qm-config-file]: block device of scsi0 on LVM-thin storage, derived from `qm config`.
# Falls back to /dev/<vg>/vm-<vmid>-disk-1.
scsi0_device() {
  local vmid=$1 cfg=${2:-} line vol
  if [ -n "$cfg" ]; then line=$(grep '^scsi0:' "$cfg" || true); else line=$(qm config "$vmid" | grep '^scsi0:' || true); fi
  vol=${line#scsi0: }
  vol=${vol%%,*}
  vol=${vol#*:}
  case "$vol" in vm-"$vmid"-disk-*) ;; *) vol="vm-$vmid-disk-1" ;; esac
  printf '/dev/%s/%s\n' "${TINY10_VG:-pve}" "$vol"
}

# find_ntfs_partition: reads `sfdisk -d` text on stdin, prints "<start> <size>" (sectors) of the largest
# NTFS data partition (GPT basic-data GUID or MBR type 7); on ties the last one wins.
find_ntfs_partition() {
  awk '
    /^\/dev\// {
      l = $0; gsub(/,/, " ", l); gsub(/=[ ]+/, "=", l)
      n = split(l, f, " "); s = ""; z = ""; t = ""
      for (i = 1; i <= n; i++) {
        if (f[i] ~ /^start=/) s = substr(f[i], 7)
        if (f[i] ~ /^size=/) z = substr(f[i], 6)
        if (f[i] ~ /^type=/) t = toupper(substr(f[i], 6))
      }
      if ((t == "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7" || t == "7") && z + 0 >= best + 0) { best = z; bs = s; found = 1 }
    }
    END { if (!found) exit 1; print bs, best }
  '
}

# sysprep_evidence_ok <finalization-status.json> <sysprep-evidence-dir>: returns 0 when the collected evidence
# shows a successful Sysprep, else 1 with a one-line reason on stderr. Rule:
#   (Stage == SysprepReturned OR Stage == SysprepStarting) AND Sysprep_succeeded.tag exists
#   AND Sysprep_failed.tag does not exist.
# Why SysprepStarting is accepted: Sysprep /shutdown can power the VM off before Finalize-Template.ps1 writes
# SysprepReturned, but Sysprep itself creates Sysprep_succeeded.tag at the end of a successful generalize.
# The succeeded tag is required in every accepted case; every other state (Failed, missing/garbled file,
# missing tag, a failed tag, any other stage) fails closed.
sysprep_evidence_ok() {
  local status=${1:-} dir=${2:-} stage
  if [ ! -f "$status" ]; then
    echo "finalization-status.json missing ($status)" >&2
    return 1
  fi
  stage=$(tr -d '\r\n' <"$status" | sed -En 's/.*"(Stage|State)"[[:space:]]*:[[:space:]]*"([^"]*)".*/\2/p')
  case "$stage" in
    SysprepReturned | SysprepStarting) ;;
    *)
      echo "finalization-status.json stage '${stage:-unreadable}' is neither SysprepReturned nor SysprepStarting" >&2
      return 1
      ;;
  esac
  if [ -n "$(find "$dir" -maxdepth 1 -iname 'Sysprep_failed.tag' 2>/dev/null)" ]; then
    echo "Sysprep_failed.tag present" >&2
    return 1
  fi
  if [ ! -f "$dir/Sysprep_succeeded.tag" ]; then
    echo "stage $stage without Sysprep_succeeded.tag" >&2
    return 1
  fi
  return 0
}
