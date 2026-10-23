#!/bin/sh
# 2D view (OrbitCameraController) and manual measuring (ManualMeasureTool) outside Unity. Folder ends in "~" so Unity ignores it.
set -e
cd "$(dirname "$0")"
mcs -nologo -out:/tmp/das-camera-tests.exe -r:System.Core UnityStandIns.cs ../../../OrbitCameraController.cs ../../../ManualMeasureTool.cs CameraTests.cs 2>&1 | grep -v "warning CS" || true
mono /tmp/das-camera-tests.exe
