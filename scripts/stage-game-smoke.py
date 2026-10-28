#!/usr/bin/env python3
"""Run the genuine signed engine and separately packaged content in a debug emulator.

Requires a rooted userdebug emulator and a debuggable APK already installed.
The private GameActivity remains unexported; this does not add a product backdoor.
"""
import argparse
import json
import pathlib
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--content', type=pathlib.Path, required=True)
parser.add_argument('--engine-version', default='294.0.1')
parser.add_argument('--engines', type=pathlib.Path, default=pathlib.Path('artifacts/engines'),
                    help='Directory containing the signed engine, public key and bundled-manifest.json')
parser.add_argument('--adb', default='adb')
parser.add_argument('--max-fps', type=int, default=10, help='Limit rendering load in software emulators')
parser.add_argument('--connect', help='Optional address of an actual compatible test server')
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parent.parent
private = '/data/user/0/io.ss14.android/files'
adb = [args.adb]

def run(*arguments, **kwargs):
    return subprocess.run(adb + list(arguments), check=True, **kwargs)

run('root')
run('wait-for-device')
if subprocess.check_output(adb + ['shell', 'id', '-u'], text=True).strip() != '0':
    raise SystemExit('This smoke check requires a rooted emulator, not a production device.')
run('shell', 'am', 'force-stop', 'io.ss14.android')
run('shell', 'run-as', 'io.ss14.android', 'mkdir', '-p', 'files/integration', 'files/launches')
engine_dir = args.engines if args.engines.is_absolute() else root / args.engines
engine = engine_dir / f'Robust.Client.Android-{args.engine_version}.zip'
public = engine_dir / 'android-engine-development.pub'
manifest = json.loads((engine_dir / 'bundled-manifest.json').read_text())
signature = manifest[args.engine_version]['platforms']['android-x64']['sig']

def stage(source, name):
    run('push', str(source), '/data/local/tmp/ss14-stage')
    run('shell', 'chmod', '644', '/data/local/tmp/ss14-stage')
    # Fixed private paths, no untrusted filename is interpolated into a shell.
    run('shell', 'run-as', 'io.ss14.android', 'sh', '-c',
        f"'cat /data/local/tmp/ss14-stage > files/integration/{name}'")
    return private + '/integration/' + name

engine_path = stage(engine, 'engine.zip')
key_path = stage(public, 'engine.pub')
content_path = stage(args.content, 'content.zip')
engine_args = ['--mount-zip', content_path, '--cvar', f'display.max_fps={args.max_fps}']
if args.connect:
    engine_args += ['--connect', '--connect-address', args.connect, '--username', 'AndroidPortTest']
request = {'Arguments': [engine_path, signature, key_path] + engine_args,
           'Environment': {}}
request_file = root / 'artifacts/game-smoke-request.json'
request_file.write_text(json.dumps(request))
stage(request_file, 'request.json')
run('shell', 'run-as', 'io.ss14.android', 'cp', 'files/integration/request.json', 'files/launches/smoke.json')
run('shell', 'am', 'start', '-n', 'io.ss14.android/io.ss14.android.GameActivity',
    '--es', 'launch_request', private + '/launches/smoke.json')
print('Started the original loader. Inspect files/client/client.log, crash.log and adb logcat.')
