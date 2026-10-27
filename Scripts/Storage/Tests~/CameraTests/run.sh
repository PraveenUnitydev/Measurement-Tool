#!/bin/sh
# 2D view (OrbitCameraController), manual measuring (ManualMeasureTool) and the cut section (SectionIndex) outside
# Unity. Folder ends in "~" so Unity ignores it.
set -e
cd "$(dirname "$0")"
rm -f /tmp/das-camera-tests.exe
if ! mcs -nologo -out:/tmp/das-camera-tests.exe -r:System.Core UnityStandIns.cs ../../../OrbitCameraController.cs ../../../ManualMeasureTool.cs ../../../SectionIndex.cs CameraTests.cs > /tmp/das-camera-build.log 2>&1; then
  grep -v "warning CS" /tmp/das-camera-build.log; exit 1
fi
mono /tmp/das-camera-tests.exe
