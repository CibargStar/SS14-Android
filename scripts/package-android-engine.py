#!/usr/bin/env python3
"""Package the genuine Android engine build and sign it with an external key.

The private key is never included in the package or copied into the source tree.
"""
import argparse
import hashlib
import json
import pathlib
import subprocess
import zipfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--engine', type=pathlib.Path, default=pathlib.Path('engine/RobustToolbox'))
parser.add_argument('--output', type=pathlib.Path, default=pathlib.Path('artifacts/engines'))
parser.add_argument('--key', type=pathlib.Path, required=True)
parser.add_argument('--version', required=True)
args = parser.parse_args()
root = args.engine.resolve()
actual_version = ET.parse(root / 'MSBuild/Robust.Engine.Version.props').findtext('.//Version')
if args.version != actual_version:
    raise SystemExit(f'Requested version {args.version} differs from source version {actual_version}.')
args.output.mkdir(parents=True, exist_ok=True)
commit = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
metadata = dict(version=args.version, upstream=commit, runtime='net10.0', platform='Android', host_abi=1)
archive = args.output / f'Robust.Client.Android-{args.version}.zip'
files = [(p, p.name) for p in sorted((root / 'bin/Client').glob('*.dll'))]
if not any(name == 'Robust.Client.dll' for _, name in files):
    raise SystemExit('Build the Android client before packaging it.')
files += [(p, p.relative_to(root).as_posix()) for p in sorted((root / 'Resources').rglob('*')) if p.is_file()]
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for path, name in files:
        info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, path.read_bytes())
    z.writestr('android-engine.json', json.dumps(metadata, sort_keys=True))
signature = archive.with_suffix('.zip.sig')
subprocess.run(['openssl', 'pkeyutl', '-sign', '-rawin', '-inkey', str(args.key),
                '-in', str(archive), '-out', str(signature)], check=True)
public = args.output / 'android-engine-development.pub'
subprocess.run(['openssl', 'pkey', '-in', str(args.key), '-pubout', '-out', str(public)], check=True)
subprocess.run(['openssl', 'pkeyutl', '-verify', '-rawin', '-pubin', '-inkey', str(public),
                '-in', str(archive), '-sigfile', str(signature)], check=True)
entry = metadata | dict(sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),
                        sig=signature.read_bytes().hex(), url='asset://engines/' + archive.name)
catalog_path = args.output / 'bundled-manifest.json'
catalog = json.loads(catalog_path.read_text()) if catalog_path.exists() else {}
catalog[args.version] = dict(insecure=False, platforms={rid: entry for rid in ('android-arm64', 'android-x64')})
catalog_path.write_text(json.dumps(catalog, indent=2) + '\n')
print(f'{archive}: {archive.stat().st_size} bytes; upstream {commit}')
