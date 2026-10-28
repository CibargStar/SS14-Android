#!/usr/bin/env python3
"""Create a signed deterministic delta from an official ZIP to a ported ZIP.

Only byte-copy and literal operations are used. The Android client produces the
exact already-reviewed target archive, including its existing output signature.
This is distribution of a prepared port, not an automatic source-code port.
"""
import argparse
import hashlib
import json
import pathlib
import struct
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--source', type=pathlib.Path, required=True)
parser.add_argument('--target', type=pathlib.Path, required=True)
parser.add_argument('--version', required=True)
parser.add_argument('--output', type=pathlib.Path, required=True)
parser.add_argument('--key', type=pathlib.Path, required=True)
args = parser.parse_args()
source = args.source.read_bytes()
target = args.target.read_bytes()
with zipfile.ZipFile(args.target) as archive:
    if json.loads(archive.read('android-engine.json'))['version'] != args.version:
        raise SystemExit('Target archive is not the requested Android engine version.')

def data_range(data, entry):
    offset = entry.header_offset
    if data[offset:offset + 4] != b'PK\x03\x04':
        raise ValueError('Invalid ZIP local header')
    name, extra = struct.unpack_from('<HH', data, offset + 26)
    start = offset + 30 + name + extra
    return start, entry.compress_size

source_ranges = {}
with zipfile.ZipFile(args.source) as archive:
    for entry in archive.infolist():
        offset, length = data_range(source, entry)
        if length >= 64:
            source_ranges[hashlib.sha256(source[offset:offset + length]).digest()] = (offset, length)

copies = []
with zipfile.ZipFile(args.target) as archive:
    for entry in sorted(archive.infolist(), key=lambda e: e.header_offset):
        offset, length = data_range(target, entry)
        match = source_ranges.get(hashlib.sha256(target[offset:offset + length]).digest())
        if match and match[1] == length and source[match[0]:match[0] + length] == target[offset:offset + length]:
            copies.append((offset, *match))

header = json.dumps(dict(format=1, version=args.version,
    sourceSha256=hashlib.sha256(source).hexdigest(),
    targetSha256=hashlib.sha256(target).hexdigest(), targetLength=len(target)),
    separators=(',', ':')).encode()
args.output.parent.mkdir(parents=True, exist_ok=True)
with args.output.open('wb') as output:
    output.write(b'SS14DP01' + struct.pack('<i', len(header)) + header)
    cursor = 0
    for target_offset, source_offset, length in copies:
        if target_offset > cursor:
            literal = target[cursor:target_offset]
            output.write(b'\x01' + struct.pack('<q', len(literal)) + literal)
        output.write(b'\x00' + struct.pack('<qq', source_offset, length))
        cursor = target_offset + length
    if cursor < len(target):
        literal = target[cursor:]
        output.write(b'\x01' + struct.pack('<q', len(literal)) + literal)
    output.write(b'\xff')
signature = args.output.with_suffix(args.output.suffix + '.sig')
subprocess.run(['openssl', 'pkeyutl', '-sign', '-rawin', '-inkey', str(args.key),
                '-in', str(args.output), '-out', str(signature)], check=True)
print(json.dumps(dict(bytes=args.output.stat().st_size,
    copied_bytes=sum(length for _, _, length in copies),
    sha256=hashlib.sha256(args.output.read_bytes()).hexdigest(),
    signature=signature.read_bytes().hex())))
