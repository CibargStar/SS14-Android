#!/usr/bin/env python3
"""Expose signed Android builds and only upstream-authorized version redirects."""
import argparse
import hashlib
import json
import pathlib
import urllib.parse

parser = argparse.ArgumentParser()
parser.add_argument('--official', type=pathlib.Path, required=True)
parser.add_argument('--engines', type=pathlib.Path, default=pathlib.Path('artifacts/engines'))
parser.add_argument('--release-base', required=True)
parser.add_argument('--output', type=pathlib.Path, required=True)
args = parser.parse_args()
if urllib.parse.urlsplit(args.release_base).scheme != 'https':
    raise SystemExit('Engine release URLs must use HTTPS.')
official = json.loads(args.official.read_text())
catalog = json.loads((args.engines / 'bundled-manifest.json').read_text())
for version, record in catalog.items():
    if version not in official or official[version].get('insecure'):
        raise SystemExit('Upstream engine is unknown or revoked: ' + version)
    archive_name = f'Robust.Client.Android-{version}.zip'
    payload = (args.engines / archive_name).read_bytes()
    for entry in record['platforms'].values():
        if entry['sha256'] != hashlib.sha256(payload).hexdigest():
            raise SystemExit('Local signed package changed: ' + version)
        entry['url'] = args.release_base.rstrip('/') + '/' + archive_name
        delta_name = f'Robust.Client.Android-{version}.ss14patch'
        delta = args.engines / delta_name
        if delta.exists():
            source = official[version]['platforms']['linux-arm64']
            entry['patch'] = dict(sourceUrl=source['url'], sourceSha256=source['sha256'],
                sourceSignature=source['sig'], url=args.release_base.rstrip('/') + '/' + delta_name,
                sha256=hashlib.sha256(delta.read_bytes()).hexdigest(),
                signature=delta.with_suffix('.ss14patch.sig').read_bytes().hex())
concrete = set(catalog)
for alias, info in official.items():
    target = alias
    seen = set()
    while target in official and official[target].get('redirect'):
        if target in seen: break
        seen.add(target)
        target = official[target]['redirect']
    if alias != target and target in concrete and not info.get('insecure'):
        catalog[alias] = dict(insecure=False, redirect=target, platforms={})
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(catalog, indent=2) + '\n')
print(f'Published catalog: {len(concrete)} exact engines, {len(catalog) - len(concrete)} upstream redirects.')
