#!/usr/bin/env bash
set -euo pipefail

export KPGV_TEST_DB="$PWD/integration-fixture.kdbx"
mkdir -p .integration-home
export HOME="$PWD/.integration-home"

for phase in setup verify; do
  export KPGV_TEST_PHASE="$phase"
  export KPGV_TEST_RESULT="$PWD/integration-$phase.txt"
  rm -f "$KPGV_TEST_RESULT"
  echo "Running KeePass under Mono/Xvfb: $phase"
  if ! timeout --signal=KILL 90s xvfb-run -a mono KeePass/KeePass.exe >"mono-$phase.log" 2>&1; then
    cat "mono-$phase.log"
    if [[ -f "$KPGV_TEST_RESULT" ]]; then cat "$KPGV_TEST_RESULT"; fi
    echo "KeePass exited unsuccessfully in phase '$phase'." >&2
    exit 1
  fi
  cat "$KPGV_TEST_RESULT"
  if [[ "$(head -n1 "$KPGV_TEST_RESULT")" != "PASS" ]]; then
    cat "mono-$phase.log"
    echo "KeePass integration phase '$phase' failed." >&2
    exit 1
  fi
done
echo 'KeePass/Mono UI integration tests passed.'
