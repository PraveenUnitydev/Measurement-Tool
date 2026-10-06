#!/bin/sh
# Runs every storage check outside Unity (needs Mono: `mcs` and `mono`). This folder ends in "~", so Unity ignores it.
#  1. Logic tests: status rules, deletion planner, registry persistence, old-tracker migration.
#  2. Report test: runs the diagnostics report against a fake cache folder with known contents.
# UnityStub.cs / UnityApiStubs.cs are APPROXIMATE stand-ins for Unity and Addressables: a pass here means the
# logic is right and the Unity-facing code is consistent with how the API is believed to look. The real
# check of the Unity API is Unity's own compile and the in-editor report (press F9, see StorageDiagnostics).
set -e
cd "$(dirname "$0")"
mcs -out:/tmp/storage-logic-tests.exe -r:System.Core UnityApiStubs.cs UnityStub.cs Tests.cs ../*.cs
mono /tmp/storage-logic-tests.exe | grep -E "^(❌|Passed)"
mcs -out:/tmp/storage-report-test.exe -r:System.Core UnityApiStubs.cs UnityStub.cs ReportRuntimeTest.cs ../*.cs
mono /tmp/storage-report-test.exe | grep -E "^(FAIL|Passed)"
