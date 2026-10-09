#!/bin/sh
# Every outside-Unity check in one go (needs Mono + python3). Stops at the first compile error, and fails if any
# test reports a failure (the individual scripts print "FAIL ..." / "Failed N" rather than exiting non-zero).
set -e
cd "$(dirname "$0")"
LOG=$(mktemp)
# Syntax check of every script (catches broken braces etc. in files no harness compiles). Mono's parser stops at
# C# 7; the few scripts using newer syntax are listed in parse-skip.txt.
BAD=0
for f in $(find ../.. -name "*.cs" -not -path "*/Tests~/*"); do
  grep -qxF "$f" parse-skip.txt && continue
  if ! mcs --parse "$f" >/dev/null 2>&1; then echo "SYNTAX ERROR in $f"; mcs --parse "$f" 2>&1 | head -3; BAD=1; fi
done
[ "$BAD" = "0" ] || { echo "SOME CHECKS FAILED: syntax"; exit 1; }
echo "All scripts parse: OK"
for s in run-tests.sh LoaderTests/run.sh PickerTests/run.sh ClipTests/run.sh EditorTests/run.sh; do
  sh "$s" 2>&1 | tee -a "$LOG"
  # a failing script inside the pipe must still stop the run
  if [ "$(tail -n 1 "$LOG" | grep -c 'error CS')" != "0" ]; then echo "COMPILE ERROR in $s"; exit 1; fi
done
if grep -Eq '^FAIL|Failed[: ]+[1-9]|failed [1-9]' "$LOG"; then
  echo "SOME CHECKS FAILED:"; grep -E '^FAIL|Failed[: ]+[1-9]|failed [1-9]' "$LOG"; rm -f "$LOG"; exit 1
fi
if grep -q 'error CS' "$LOG"; then echo "COMPILE ERRORS"; grep 'error CS' "$LOG"; rm -f "$LOG"; exit 1; fi
rm -f "$LOG"
echo "ALL CHECKS PASSED"
