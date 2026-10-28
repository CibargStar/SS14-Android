#!/usr/bin/env python3
"""Replace only APK engine assets with externally signed release packages.

The result is unsigned: zipalign and apksigner must run afterwards. This allows
the host/native build and the exact engine matrix to run concurrently without
shipping stale or ephemeral engine keys. Managed/native host code is unchanged.
"""
import argparse
import copy
import hashlib
import json
import pathlib
import re
import subprocess
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--apk', required=True, type=pathlib.Path)
parser.add_argument('--engines', required=True, type=pathlib.Path)
parser.add_argument('--public-key', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
parser.add_argument('--version', action='append', required=True)
args = parser.parse_args()
if args.apk.resolve() == args.output.resolve():
    raise SystemExit('Input and output APK must differ.')
manifest = json.loads((args.engines / 'bundled-manifest.json').read_text())
bundled = {}
payloads = {}
for version in dict.fromkeys(args.version):
    if not re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}(?:-[A-Za-z0-9._-]+)?', version):
        raise SystemExit('Invalid engine version.')
    record = copy.deepcopy(manifest[version])
    if record.get('insecure') or record.get('redirect'):
        raise SystemExit('APK bundle needs a safe concrete engine.')
    archive = args.engines / f'Robust.Client.Android-{version}.zip'
    data = archive.read_bytes()
    signature = archive.with_suffix('.zip.sig')
    subprocess.run(['openssl', 'pkeyutl', '-verify', '-rawin', '-pubin', '-inkey', str(args.public_key),
                    '-in', str(archive), '-sigfile', str(signature)], check=True)
    with zipfile.ZipFile(archive) as engine:
        metadata = json.loads(engine.read('android-engine.json'))
    if (metadata['version'] != version or metadata['platform'] != 'Android' or metadata['host_abi'] != 1
            or metadata.get('runtime') != 'net10.0'
            or set(record['platforms']) != {'android-arm64', 'android-x64'}):
        raise SystemExit('Incompatible engine metadata.')
    for entry in record['platforms'].values():
        if entry['sha256'] != hashlib.sha256(data).hexdigest() or entry['sig'] != signature.read_bytes().hex():
            raise SystemExit('Engine differs from its signed manifest.')
        entry['url'] = 'asset://engines/' + archive.name
    bundled[version] = record
    payloads['assets/engines/' + archive.name] = data

args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.apk) as source:
    if source.read('assets/android_signing_key') != args.public_key.read_bytes():
        raise SystemExit('Host does not pin the established engine key.')
    if len(set(source.namelist())) != len(source.namelist()):
        raise SystemExit('Duplicate APK entries are not accepted.')
    with zipfile.ZipFile(args.output, 'w') as target:
        for entry in source.infolist():
            name = entry.filename
            if name.startswith('assets/engines/') or re.fullmatch(r'META-INF/(MANIFEST\.MF|[^/]+\.(SF|RSA|DSA|EC))', name, re.I):
                continue
            target.writestr(copy.copy(entry), source.read(entry))
        payloads['assets/engines/manifest.json'] = (json.dumps(bundled, indent=2) + '\n').encode()
        for name, data in payloads.items():
            entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_STORED
            target.writestr(entry, data)
print('Unsigned APK prepared with verified engines: ' + ', '.join(bundled))
