#!/usr/bin/env python3
"""Prepare an exact upstream engine tag with the reviewed Android patch.

Never substitute a different engine version when a port does not apply.
"""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('version')
args = parser.parse_args()
if not re.fullmatch(r'[0-9]+(?:\.[0-9]+){2,3}(?:-[A-Za-z0-9._-]+)?', args.version):
    raise SystemExit('Expected an upstream engine version, not a path or arbitrary ref.')
root = pathlib.Path(__file__).resolve().parent.parent
repository = root / 'engine/RobustToolbox'
destination = root / 'engine/versions' / args.version
patch = root / 'patches/robust-android-platform.patch'

def git(directory, *arguments, **options):
    return subprocess.run(['git', '-C', str(directory), *arguments], check=True, **options)

if not destination.exists():
    git(repository, 'fetch', '--depth', '1', 'origin', f'refs/tags/v{args.version}')
    git(repository, 'worktree', 'add', '--detach', str(destination), 'FETCH_HEAD')
git(destination, 'submodule', 'update', '--init', '--recursive')
marker = destination / '.android-platform-patch.json'
def state():
    diff = subprocess.check_output(['git', '-C', str(destination), 'diff', '--binary'])
    commit = subprocess.check_output(['git', '-C', str(destination), 'rev-parse', 'HEAD'], text=True).strip()
    return dict(commit=commit, patch=hashlib.sha256(patch.read_bytes()).hexdigest(),
                changes=hashlib.sha256(diff).hexdigest())
if marker.exists():
    if json.loads(marker.read_text()) != state():
        raise SystemExit('Prepared engine changed; review it before applying a new patch.')
    print(destination)
    raise SystemExit(0)
already_applied = subprocess.run(['git', '-C', str(destination), 'apply', '--reverse',
                                  '--check', str(patch)], capture_output=True).returncode == 0
if not already_applied:
    git(destination, 'apply', '--check', str(patch))
    git(destination, 'apply', str(patch))
# Before the upstream AltGr input addition, the original event constructor has
# eight arguments. Preserve that API on those exact versions; do not transplant
# a newer input event type into their network/content API.
events = (destination / 'Robust.Client/Input/Events.cs').read_text()
if 'public bool AltGr' not in events:
    input_path = destination / 'Robust.Client/Input/InputManager.cs'
    original = input_path.read_text()
    needle = ', args.RawCode, args.AltGr)'
    if original.count(needle) != 2:
        raise SystemExit('Unrecognized legacy input API; manual port review is required.')
    input_path.write_text(original.replace(needle, ', args.RawCode)'))
git(destination, 'add', '-N', '--', 'Robust.Client/Input/TouchFingerEventArgs.cs',
    'Robust.Client/UserInterface/Mobile')
marker.write_text(json.dumps(state(), indent=2) + '\n')
print(destination)
