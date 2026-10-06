#!/bin/sh
# Runs the storage-logic tests outside Unity (needs Mono: `mcs` and `mono`).
# This folder ends in "~" so Unity ignores it. UnityStub.cs stands in for UnityEngine.JsonUtility.
set -e
cd "$(dirname "$0")"
mcs -out:/tmp/storage-tests.exe -r:System.Core UnityStub.cs Tests.cs ../*.cs
mono /tmp/storage-tests.exe
