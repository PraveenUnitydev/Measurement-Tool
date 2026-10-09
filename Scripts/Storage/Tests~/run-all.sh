#!/bin/sh
# Every outside-Unity check in one go (needs Mono + python3). Stops at the first compile error, and fails if any
# test reports a failure (the individual scripts print "FAIL ..." / "Failed N" rather than exiting non-zero).
set -e
cd "$(dirname "$0")"
LOG=$(mktemp)
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
