#!/usr/bin/env python3
"""Verify a downloaded CI artifact and sign its exact engine with the channel key."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import tempfile
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('artifact', type=pathlib.Path)
parser.add_argument('--artifact-sha256', required=True)
parser.add_argument('--version', required=True)
parser.add_argument('--upstream', required=True)
parser.add_argument('--key', type=pathlib.Path, required=True)
parser.add_argument('--output', type=pathlib.Path, default=pathlib.Path('artifacts/engines'))
args = parser.parse_args()
if hashlib.sha256(args.artifact.read_bytes()).hexdigest() != args.artifact_sha256:
    raise SystemExit('Downloaded CI artifact does not match its GitHub digest.')
with tempfile.TemporaryDirectory() as temporary:
    temporary = pathlib.Path(temporary)
    name = f'Robust.Client.Android-{args.version}.zip'
    with zipfile.ZipFile(args.artifact) as archive:
        payload = archive.read(name)
        catalog = json.loads(archive.read('bundled-manifest.json'))
        entry = catalog[args.version]['platforms']['android-arm64']
        if hashlib.sha256(payload).hexdigest() != entry['sha256']:
            raise SystemExit('CI engine payload digest mismatch.')
        (temporary / 'engine.pub').write_bytes(archive.read('android-engine-development.pub'))
        (temporary / 'engine.zip').write_bytes(payload)
        (temporary / 'signature').write_bytes(bytes.fromhex(entry['sig']))
    subprocess.run(['openssl', 'pkeyutl', '-verify', '-rawin', '-pubin', '-inkey',
        str(temporary / 'engine.pub'), '-in', str(temporary / 'engine.zip'),
        '-sigfile', str(temporary / 'signature')], check=True)
    with zipfile.ZipFile(temporary / 'engine.zip') as engine:
        metadata = json.loads(engine.read('android-engine.json'))
    if metadata['version'] != args.version or metadata['upstream'] != args.upstream:
        raise SystemExit('CI engine source provenance mismatch.')
    args.output.mkdir(parents=True, exist_ok=True)
    target = args.output / name
    target.write_bytes(payload)
    signature = target.with_suffix('.zip.sig')
    subprocess.run(['openssl', 'pkeyutl', '-sign', '-rawin', '-inkey', str(args.key),
        '-in', str(target), '-out', str(signature)], check=True)
    public = temporary / 'channel.pub'
    subprocess.run(['openssl', 'pkey', '-in', str(args.key), '-pubout', '-out', str(public)], check=True)
    pinned = args.output / 'android-engine-development.pub'
    if pinned.exists() and pinned.read_bytes() != public.read_bytes():
        raise SystemExit('Refusing to replace the existing channel public key.')
    pinned.write_bytes(public.read_bytes())
    subprocess.run(['openssl', 'pkeyutl', '-verify', '-rawin', '-pubin', '-inkey',
        str(pinned), '-in', str(target), '-sigfile', str(signature)], check=True)
    entry['sig'] = signature.read_bytes().hex()
    manifest = args.output / 'bundled-manifest.json'
    combined = json.loads(manifest.read_text()) if manifest.exists() else {}
    combined[args.version] = dict(insecure=False, platforms={rid: entry for rid in ('android-arm64', 'android-x64')})
    manifest.write_text(json.dumps(combined, indent=2) + '\n')
    print(f'Verified and signed exact engine {args.version} from {args.upstream}.')
