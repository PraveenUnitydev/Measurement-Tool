#!/bin/sh
# Runs every storage check outside Unity (needs Mono: `mcs` and `mono`). This folder ends in "~", so Unity ignores it.
#  1. Logic tests: status rules, deletion planner, registry persistence, old-tracker migration.
#  2. Report test: runs the diagnostics report against a fake cache folder with known contents.
#  3. Reconcile + label tests: what Home and the Storage screen are told, from what is really on disk.
#  5. Location test: choosing / moving the download folder, disk-space checks (real temp folders).
#  4. Service test: runs the real VehicleStorageService against a fake Addressables catalog and a fake cache.
# UnityStub.cs / UnityApiStubs.cs are APPROXIMATE stand-ins for Unity and Addressables: a pass here means the
# logic is right and the Unity-facing code is consistent with how the API is believed to look. The real
# check of the Unity API is Unity's own compile and the in-editor report (press F9, see StorageDiagnostics).
set -e
cd "$(dirname "$0")"
# All storage scripts plus the real DownloadedVehiclesTracker (the service calls into it)
SRC="../*.cs ../../DownloadedVehiclesTracker.cs"
mcs -out:/tmp/storage-logic-tests.exe -r:System.Core UnityApiStubs.cs UnityStub.cs Tests.cs $SRC
mono /tmp/storage-logic-tests.exe | grep -E "^(❌|Passed)"
mcs -out:/tmp/storage-report-test.exe -r:System.Core UnityApiStubs.cs UnityStub.cs ReportRuntimeTest.cs $SRC
mono /tmp/storage-report-test.exe | grep -E "^(FAIL|Passed)"
mcs -out:/tmp/storage-reconcile-test.exe -r:System.Core UnityApiStubs.cs UnityStub.cs ReconcileTests.cs $SRC
mono /tmp/storage-reconcile-test.exe | grep -E "^(FAIL|Passed)"
mcs -out:/tmp/storage-service-test.exe -r:System.Core UnityApiStubs.cs UnityStub.cs ServiceTests.cs $SRC
mono /tmp/storage-service-test.exe | grep -E "^(FAIL|Passed)"
mcs -out:/tmp/storage-location-test.exe -r:System.Core UnityApiStubs.cs UnityStub.cs LocationTests.cs $SRC
mono /tmp/storage-location-test.exe | grep -E "^(FAIL|Passed)"
mcs -target:library -out:/tmp/storage-unity-check.dll -r:System.Core UnityApiStubs.cs UnityStub.cs $SRC
echo "Unity-facing scripts + real tracker compile against the stand-ins: OK"
