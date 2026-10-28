"""Check the actual publication script's handling of official version repairs."""
import hashlib
import json
import pathlib
import subprocess
import tempfile
import unittest


class CatalogChecks(unittest.TestCase):
    def test_only_official_redirects_to_safe_published_targets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            payload = b'signed-payload-verification-is-a-separate-publication-gate'
            version = '294.0.0'
            (root / f'Robust.Client.Android-{version}.zip').write_bytes(payload)
            (root / 'bundled-manifest.json').write_text(json.dumps({version: {
                'insecure': False, 'platforms': {'android-arm64': {
                    'sha256': hashlib.sha256(payload).hexdigest(), 'sig': '00', 'url': 'asset://engine'}}}}))
            official = {version: {'insecure': False},
                        'old': {'insecure': True, 'redirect': version},
                        'chain': {'insecure': True, 'redirect': 'old'},
                        'unknown': {'redirect': 'not-published'},
                        'loop-a': {'redirect': 'loop-b'}, 'loop-b': {'redirect': 'loop-a'},
                        'unported': {'insecure': False}, 'unported-alias': {'redirect': 'unported'}}
            (root / 'official.json').write_text(json.dumps(official))
            command = ['python3', 'scripts/create-update-catalog.py', '--official', str(root / 'official.json'),
                       '--engines', str(root), '--release-base', 'https://example.test/release',
                       '--output', str(root / 'output.json')]
            subprocess.run(command, check=True, capture_output=True)
            output = json.loads((root / 'output.json').read_text())
            self.assertEqual(set(output), {version, 'old', 'chain'})
            for alias in ('old', 'chain'):
                self.assertEqual(output[alias], {'insecure': True, 'redirect': version, 'platforms': {}})
            self.assertEqual(output[version]['platforms']['android-arm64']['url'],
                             'https://example.test/release/Robust.Client.Android-294.0.0.zip')
            official[version]['insecure'] = True
            (root / 'official.json').write_text(json.dumps(official))
            rejected = subprocess.run(command, capture_output=True)
            self.assertNotEqual(rejected.returncode, 0)
            self.assertIn(b'unknown or revoked', rejected.stderr)


if __name__ == '__main__':
    unittest.main()
