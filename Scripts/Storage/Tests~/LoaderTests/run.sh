#!/bin/sh
# Loader + explicit-download tests: the REAL RemoteAddressableVehicleLoader, VehicleDownloads and DasDialog compiled
# against Addressables/UI stand-ins (LoaderStubs.cs) that simulate slow downloads. Needs Mono (mcs, mono).
# A compile error stops the run (old test programs are deleted first, so a stale build can never "pass").
set -e
cd "$(dirname "$0")"
S=../../..
COMMON="$S/Core/DasKeys.cs $S/Core/VehicleIdentity.cs $S/RemoteAddressableVehicleLoader.cs $S/Net/DasNet.cs $S/Storage/DiskSpace.cs $S/Storage/ByteFormat.cs $S/Core/VehicleThumbnailStore.cs"
rm -f /tmp/das-loader-tests.exe /tmp/das-download-tests.exe
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-loader-tests.exe -r:System.Core LoaderStubs.cs LoaderTests.cs $COMMON
mono /tmp/das-loader-tests.exe | grep -E "^(FAIL|Passed)"
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-download-tests.exe -r:System.Core LoaderStubs.cs DownloadTests.cs $COMMON $S/Storage/VehicleDownloads.cs $S/Core/DasDialog.cs
mono /tmp/das-download-tests.exe | grep -E "^(FAIL|Passed)"
rm -f /tmp/das-thumb-tests.exe
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-thumb-tests.exe -r:System.Core LoaderStubs.cs ThumbnailTests.cs $COMMON
mono /tmp/das-thumb-tests.exe | grep -E "^(FAIL|Passed)"
rm -f /tmp/das-batch-tests.exe
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-batch-tests.exe -r:System.Core LoaderStubs.cs BatchTests.cs $COMMON $S/Storage/VehicleDownloads.cs $S/Storage/BatchDownloads.cs $S/Storage/VehicleLibrary.cs $S/Storage/StorageLabels.cs $S/Storage/VehicleStatusEvaluator.cs $S/Storage/VehicleStorageModel.cs
mono /tmp/das-batch-tests.exe | grep -E "^(FAIL|Passed)"
rm -f /tmp/das-cat-tests.exe
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-cat-tests.exe -r:System.Core LoaderStubs.cs ContentCatalogTests.cs $COMMON
mono /tmp/das-cat-tests.exe | grep -E "^(FAIL|Passed)"
rm -f /tmp/das-key-tests.exe
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-key-tests.exe -r:System.Core LoaderStubs.cs KeyTests.cs $COMMON
mono /tmp/das-key-tests.exe | grep -E "^(FAIL|Passed)"
rm -f /tmp/das-mig-tests.exe
mcs -nologo -nowarn:414,169,649,219,108,618,67 -out:/tmp/das-mig-tests.exe -r:System.Core LoaderStubs.cs MeasurementStubs.cs MigrationTests.cs $COMMON $S/MeasurementKeyMigration.cs $S/VehicleMeasurementStorage.cs
mono /tmp/das-mig-tests.exe | grep -E "^(FAIL|Passed)"
