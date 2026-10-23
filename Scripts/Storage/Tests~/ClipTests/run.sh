#!/bin/sh
# Clip section material tests (needs Mono + python3). Folder ends in "~" so Unity ignores it.
set -e
cd "$(dirname "$0")"
python3 gen.py ../../../..
mcs -out:/tmp/clip-tests.exe -r:System.Core ClipStubs.cs GraphMaterials.g.cs ClipTests.cs ../../../Core/ClipMaterialMapping.cs ../../../Core/ClipMaterials.cs
mono /tmp/clip-tests.exe
mcs -target:library -out:/tmp/clipmode-check.dll -r:System.Core ClipStubs.cs UiStubs.cs ../../../Core/ClipMaterialMapping.cs ../../../Core/ClipMaterials.cs ../../../ClipSectionMode.cs
echo "ClipSectionMode compiles against the stand-ins: OK"
