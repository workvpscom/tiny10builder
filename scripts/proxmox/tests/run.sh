#!/bin/bash
# Runs every test_*.sh in this directory; exits non-zero if any fails.
set -Eeuo pipefail
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
rc=0
for t in "$HERE"/test_*.sh; do
  echo "== $(basename "$t")"
  bash "$t" || rc=1
done
if [ "$rc" -eq 0 ]; then echo ALL_PASS; else echo SOME_FAILED; fi
exit "$rc"
