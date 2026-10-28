#!/usr/bin/env python3
"""Prepare an exact upstream engine tag with the reviewed Android patch.

Never substitute a different engine version when a port does not apply.
"""
import argparse
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
already_applied = subprocess.run(['git', '-C', str(destination), 'apply', '--reverse',
                                  '--check', str(patch)], capture_output=True).returncode == 0
if not already_applied:
    git(destination, 'apply', '--check', str(patch))
    git(destination, 'apply', str(patch))
print(destination)
