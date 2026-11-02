#!/bin/bash
# Test helpers: stubbed Proxmox commands on PATH, plain-bash assertions. bash 3.2 compatible.
# shellcheck disable=SC2034  # variables are consumed by the sourcing test files
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
SCRIPTS=$(cd "$HERE/.." && pwd)
PASS=0
FAIL=0
CASE=''

# new_case <name>: fresh temp dir with stubs first on PATH.
new_case() {
  CASE=$1
  T=$(mktemp -d "${TMPDIR:-/tmp}/t10px.XXXXXX")
  STUB_DIR="$T/stubs"
  STUB_LOG="$T/calls.log"
  mkdir -p "$STUB_DIR" "$T/work"
  : >"$STUB_LOG"
  export STUB_DIR STUB_LOG
  unset STUB_VM_STATE STUB_QM_CONFIG STUB_DF_AVAIL STUB_ZSTD_FAIL STUB_MUTATE STUB_NO_ARCHIVE STUB_SFDISK STUB_FIXTURE STUB_SET_NOOP_BOOT STUB_TWO_ARCHIVES STUB_PVESM_RAW STUB_LVM_AVAIL STUB_LVM_STATUS EV_VMID EV_VOL
  make_stubs
  PATH="$STUB_DIR:$ORIG_PATH"
  WORK=$(cd "$T/work" && pwd -P)
}
ORIG_PATH=$PATH

make_stubs() {
  cat >"$STUB_DIR/default.conf" <<'CONF'
agent: enabled=1
bios: ovmf
boot: order=ide2;scsi0
efidisk0: local-lvm:vm-9200-disk-0,efitype=4m,pre-enrolled-keys=1,size=4M
ide0: local-lvm:vm-9200-cloudinit,media=cdrom
machine: pc-q35-9.0
name: tiny10-src
scsi0: local-lvm:vm-9200-disk-1,iothread=1,size=20G
CONF
  echo 1111aaaa >"$STUB_DIR/diskhash"
  cat >"$STUB_DIR/qm" <<'EOF2'
#!/bin/bash
echo "qm $*" >>"$STUB_LOG"
live="$STUB_DIR/live.conf"
[ -f "$live" ] || cp "${STUB_QM_CONFIG:-$STUB_DIR/default.conf}" "$live"
case "$1" in
  status)
    if [ "${STUB_VM_STATE:-stopped}" = none ]; then echo "Configuration file does not exist" >&2; exit 2; fi
    echo "status: ${STUB_VM_STATE:-stopped}"
    ;;
  config) cat "$live" ;;
  set)
    # Model the one effect backup.sh relies on: --boot rewrites the boot line (unless STUB_SET_NOOP_BOOT).
    while [ $# -gt 0 ]; do
      if [ "$1" = --boot ] && [ -z "${STUB_SET_NOOP_BOOT:-}" ]; then
        grep -v '^boot:' "$live" >"$live.new"; echo "boot: $2" >>"$live.new"; mv "$live.new" "$live"
      fi
      shift
    done
    ;;
esac
exit 0
EOF2
  cat >"$STUB_DIR/df" <<'EOF2'
#!/bin/bash
echo "df $*" >>"$STUB_LOG"
printf 'Avail\n%s\n' "${STUB_DF_AVAIL:-50000000000}"
EOF2
  cat >"$STUB_DIR/vzdump" <<'EOF2'
#!/bin/bash
echo "vzdump $*" >>"$STUB_LOG"
d=''
while [ $# -gt 0 ]; do if [ "$1" = --dumpdir ]; then d=$2; fi; shift; done
if [ -z "${STUB_NO_ARCHIVE:-}" ]; then echo archive >"$d/vzdump-qemu-9200-2026_11_01-00_00_00.vma.zst"; fi
if [ -n "${STUB_TWO_ARCHIVES:-}" ]; then echo archive2 >"$d/vzdump-qemu-9200-2026_11_01-00_00_01.vma.zst"; fi
if [ -n "${STUB_MUTATE:-}" ]; then echo 2222bbbb >"$STUB_DIR/diskhash"; fi
exit 0
EOF2
  cat >"$STUB_DIR/pvesm" <<'EOF2'
#!/bin/bash
echo "pvesm $*" >>"$STUB_LOG"
if [ -n "${STUB_PVESM_RAW+x}" ]; then printf '%s' "$STUB_PVESM_RAW"; exit 0; fi
printf 'Name             Type     Status           Total            Used       Available        %%\n'
printf '%s     lvmthin     %s       354275328        65966066       %s   18.62%%\n' "$3" "${STUB_LVM_STATUS:-active}" "${STUB_LVM_AVAIL:-288309261}"
exit 0
EOF2
  cat >"$STUB_DIR/zstd" <<'EOF2'
#!/bin/bash
echo "zstd $*" >>"$STUB_LOG"
if [ -n "${STUB_ZSTD_FAIL:-}" ]; then exit 1; fi
exit 0
EOF2
  cat >"$STUB_DIR/sha256sum" <<'EOF2'
#!/bin/bash
for f in "$@"; do
  case "$f" in
    /dev/*) echo "$(cat "$STUB_DIR/diskhash")  $f" ;;
    *) shasum -a 256 "$f" ;;
  esac
done
EOF2
  cat >"$STUB_DIR/losetup" <<'EOF2'
#!/bin/bash
echo "losetup $*" >>"$STUB_LOG"
case "$1" in
  -j) ;;
  -d) ;;
  --find) echo /dev/loop77 ;;
esac
exit 0
EOF2
  cat >"$STUB_DIR/qmrestore" <<'EOF2'
#!/bin/bash
echo "qmrestore $*" >>"$STUB_LOG"
exit 0
EOF2
  cat >"$STUB_DIR/sfdisk" <<'EOF2'
#!/bin/bash
echo "sfdisk $*" >>"$STUB_LOG"
cat "$STUB_SFDISK"
EOF2
  cat >"$STUB_DIR/mount" <<'EOF2'
#!/bin/bash
echo "mount $*" >>"$STUB_LOG"
for a in "$@"; do m=$a; done
cp -R "$STUB_FIXTURE/." "$m/"
touch "$m/.stub_mounted"
EOF2
  cat >"$STUB_DIR/umount" <<'EOF2'
#!/bin/bash
echo "umount $*" >>"$STUB_LOG"
rm -f "$1/.stub_mounted"
EOF2
  cat >"$STUB_DIR/mountpoint" <<'EOF2'
#!/bin/bash
for a in "$@"; do m=$a; done
[ -f "$m/.stub_mounted" ]
EOF2
  for c in findmnt blockdev; do
    printf "#!/bin/bash\necho \"%s \$*\" >>\"\$STUB_LOG\"\necho stub\n" "$c" >"$STUB_DIR/$c"
  done
  chmod +x "$STUB_DIR"/*
}

# run <cmd...>: captures RC and OUT (stdout+stderr).
run() {
  set +e
  OUT=$("$@" 2>&1)
  RC=$?
  set -e
}

fail() { OUT=''; _bad "$1"; }
_ok() { PASS=$((PASS + 1)); }
_bad() { FAIL=$((FAIL + 1)); printf 'FAIL [%s] %s\n' "$CASE" "$1"; printf '%s\n' "--- output ---" "$OUT" "--- calls ---"; cat "$STUB_LOG"; echo ---; }

assert_rc() { if [ "$RC" -eq "$1" ]; then _ok; else _bad "expected rc $1, got $RC"; fi; }
assert_rc_nonzero() { if [ "$RC" -ne 0 ]; then _ok; else _bad "expected non-zero rc"; fi; }
assert_logged() { if grep -q -- "$1" "$STUB_LOG"; then _ok; else _bad "expected call matching: $1"; fi; }
assert_not_logged() { if grep -q -- "$1" "$STUB_LOG"; then _bad "unexpected call matching: $1"; else _ok; fi; }
assert_out() { if printf '%s' "$OUT" | grep -q -- "$1"; then _ok; else _bad "expected output matching: $1"; fi; }
assert_file() { if [ -e "$1" ]; then _ok; else _bad "expected file: $1"; fi; }
assert_no_file() { if [ -e "$1" ]; then _bad "unexpected file: $1"; else _ok; fi; }
assert_eq() { if [ "$1" = "$2" ]; then _ok; else _bad "expected [$2], got [$1]"; fi; }

# make_evidence <dumpdir> [returned|returned-notag|returned-failedtag|starting|starting-notag|starting-bothtags]: offline-inspect evidence in the backup.sh convention.
make_evidence() {
  local e=$1/offline-evidence
  mkdir -p "$e/guest/Tiny11Template" "$e/guest/Sysprep"
  echo 0 >"$e/inspect.exit"
  echo "${EV_VMID:-9200}" >"$e/vmid.txt"
  echo "${EV_VOL:-local-lvm:vm-9200-disk-1}" >"$e/scsi0-volume.txt"
  echo '{"State":"SysprepReturned"}' >"$e/guest/Tiny11Template/finalization-status.json"
  : >"$e/guest/Sysprep/Sysprep_succeeded.tag"
  case "${2:-returned}" in
    starting | starting-notag | starting-bothtags) echo '{"Stage":"SysprepStarting"}' >"$e/guest/Tiny11Template/finalization-status.json" ;;
  esac
  { [ "${2:-}" = starting-notag ] || [ "${2:-}" = returned-notag ]; } && rm -f "$e/guest/Sysprep/Sysprep_succeeded.tag"
  { [ "${2:-}" = starting-bothtags ] || [ "${2:-}" = returned-failedtag ]; } && : >"$e/guest/Sysprep/Sysprep_failed.tag"
  return 0
}

summary() { printf '%s: %d passed, %d failed\n' "$1" "$PASS" "$FAIL"; [ "$FAIL" -eq 0 ]; }
