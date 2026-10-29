#!/usr/bin/env python3
"""Check that the launcher APK can bootstrap downloaded, signed engines."""
import argparse
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--apk', type=Path, required=True)
parser.add_argument('--abi', choices=['arm64-v8a', 'x86_64'], required=True)
parser.add_argument('--public-key', type=Path, default=Path(__file__).resolve().parent.parent / 'updates/android-engine-public.pub')
args = parser.parse_args()
with zipfile.ZipFile(args.apk) as archive:
    names = archive.namelist()
    assert len(names) == len(set(names)), 'Duplicate APK entries'
    assert not any(n.startswith('assets/engines/') or 'Robust.Client.Android-' in n for n in names), 'APK bundles an engine'
    assert archive.read('assets/android_signing_key') == args.public_key.read_bytes(), 'Wrong engine channel key'
    assert 'assets/update-sources.json' in names, 'Missing update sources'
    for library in ['libcoreclr.so', 'libSDL3.so', 'libopenal.so', 'libfreetype.so', 'libzstd.so', 'libsodium.so']:
        assert f'lib/{args.abi}/{library}' in names, f'Missing runtime library: {library}'
    assert any('ss14_logo' in n for n in names), 'Missing original logo'
print(f'Bootstrap APK verified: {args.abi}, no bundled engines, pinned key and runtime present.')
