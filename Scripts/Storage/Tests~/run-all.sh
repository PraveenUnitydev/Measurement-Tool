#!/bin/sh
# Every outside-Unity check in one go (needs Mono + python3). Stops at the first failure.
set -e
cd "$(dirname "$0")"
sh run-tests.sh
sh LoaderTests/run.sh
sh PickerTests/run.sh
sh ClipTests/run.sh
echo "ALL CHECKS PASSED"
