#!/usr/bin/env python3
"""Bundle a small set of engines from the pinned, externally signed channel."""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess
import tempfile
import urllib.parse
import urllib.request
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--catalog', required=True)
parser.add_argument('--key', type=pathlib.Path, required=True)
parser.add_argument('--bundle-version', action='append', required=True)
parser.add_argument('--output', type=pathlib.Path, default=pathlib.Path('artifacts/engines'))
args = parser.parse_args()

def download(url, limit):
    address = urllib.parse.urlsplit(url)
    if address.scheme != 'https' or address.username:
        raise ValueError('Channel downloads require HTTPS.')
    request = urllib.request.Request(url, headers={'User-Agent': 'SS14-Android-channel-build/1'})
    with urllib.request.urlopen(request, timeout=60) as response:
        if urllib.parse.urlsplit(response.url).scheme != 'https':
            raise ValueError('Channel redirect left HTTPS.')
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError('Channel download exceeds its size limit.')
    return data

catalog = json.loads(download(args.catalog, 4 * 1024 * 1024))
args.output.mkdir(parents=True, exist_ok=True)
bundled = {}
for version in args.bundle_version:
    record = catalog[version]
    if record.get('insecure') or record.get('redirect'):
        raise ValueError('Bundled versions must identify a secure concrete engine.')
    entry = record['platforms']['android-arm64']
    payload = download(entry['url'], 128 * 1024 * 1024)
    if hashlib.sha256(payload).hexdigest() != entry['sha256']:
        raise ValueError('Signed channel engine hash mismatch.')
    with tempfile.TemporaryDirectory() as temporary:
        temporary = pathlib.Path(temporary)
        (temporary / 'engine.zip').write_bytes(payload)
        (temporary / 'signature').write_bytes(bytes.fromhex(entry['sig']))
        subprocess.run(['openssl', 'pkeyutl', '-verify', '-pubin', '-rawin',
            '-inkey', str(args.key), '-in', str(temporary / 'engine.zip'),
            '-sigfile', str(temporary / 'signature')], check=True)
        with zipfile.ZipFile(temporary / 'engine.zip') as archive:
            if json.loads(archive.read('android-engine.json'))['version'] != version:
                raise ValueError('Signed channel engine version mismatch.')
    filename = f'Robust.Client.Android-{version}.zip'
    (args.output / filename).write_bytes(payload)
    # Keep patch metadata available even when its normal package is bundled.
    for platform in record['platforms'].values():
        platform['url'] = 'asset://engines/' + filename
    bundled[version] = record
shutil.copyfile(args.key, args.output / 'android-engine-development.pub')
(args.output / 'bundled-manifest.json').write_text(json.dumps(bundled, indent=2) + '\n')
print('Bundled externally signed engines: ' + ', '.join(bundled))
