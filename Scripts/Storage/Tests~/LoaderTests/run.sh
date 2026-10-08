#!/bin/sh
# Loader + explicit-download tests: the REAL RemoteAddressableVehicleLoader, VehicleDownloads and DasDialog compiled
# against Addressables/UI stand-ins (LoaderStubs.cs) that simulate slow downloads. Needs Mono (mcs, mono).
set -e
cd "$(dirname "$0")"
S=../../..
mcs -nologo -out:/tmp/das-loader-tests.exe -r:System.Core LoaderStubs.cs LoaderTests.cs $S/RemoteAddressableVehicleLoader.cs $S/Net/DasNet.cs $S/Storage/DiskSpace.cs $S/Storage/ByteFormat.cs 2>&1 | grep -v warning || true
mono /tmp/das-loader-tests.exe | grep -E "^(FAIL|Passed)"
mcs -nologo -out:/tmp/das-download-tests.exe -r:System.Core LoaderStubs.cs DownloadTests.cs $S/RemoteAddressableVehicleLoader.cs $S/Net/DasNet.cs $S/Storage/DiskSpace.cs $S/Storage/ByteFormat.cs $S/Storage/VehicleDownloads.cs $S/Core/DasDialog.cs 2>&1 | grep -v warning || true
mono /tmp/das-download-tests.exe | grep -E "^(FAIL|Passed)"
