#!/bin/sh
# Compile check of Editor/Publish against stand-ins with the real Unity 6 / Addressables 2.7.6 signatures.
set -e
cd "$(dirname "$0")"
mcs -nologo -target:library -out:/tmp/das-editor-check.dll -r:System.Core EditorStubs.cs ../../../Editor/Publish/*.cs ../../../Editor/AppUpdate/*.cs ../../../AppUpdate/AppReleaseCore.cs ../../../AppUpdate/DasAppUpdateKey.cs
echo "Editor/Publish and Editor/AppUpdate compile against the stand-ins: OK"
mcs -nologo -out:/tmp/das-codesign-tests.exe -r:System.Core -r:/tmp/das-editor-check.dll CodeSigningTests.cs
MONO_PATH=/tmp mono /tmp/das-codesign-tests.exe
