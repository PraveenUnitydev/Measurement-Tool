#!/bin/sh
# Local vehicle index tests (needs Mono). Folder ends in "~" so Unity ignores it.
set -e
cd "$(dirname "$0")"
mcs -out:/tmp/picker-index-tests.exe -r:System.Core ../UnityApiStubs.cs ../UnityStub.cs IndexTests.cs ../../../Core/LocalVehicleIndex.cs
mono /tmp/picker-index-tests.exe
