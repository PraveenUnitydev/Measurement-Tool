#!/bin/sh
# Compile check of Editor/Publish against stand-ins with the real Unity 6 / Addressables 2.7.6 signatures.
set -e
cd "$(dirname "$0")"
mcs -nologo -target:library -out:/tmp/das-editor-check.dll -r:System.Core EditorStubs.cs ../../../Editor/Publish/*.cs
echo "Editor/Publish compiles against the stand-ins: OK"
