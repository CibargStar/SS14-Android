#!/usr/bin/env python3
"""Inspect the official engine catalog. This is not a server compatibility test."""
import argparse
import datetime
import json
import pathlib
import urllib.request

DEFAULT_URL = "https://robust-builds.cdn.spacestation14.com/manifest.json"


def summarize(catalog):
    if not isinstance(catalog, dict):
        raise ValueError("Expected a version-to-build dictionary")
    platforms = sorted({rid for item in catalog.values() for rid in item.get("platforms", {})})
    return {
        "version_entries": len(catalog),
        "platforms": platforms,
        "android_version_entries": [
            version for version, item in catalog.items()
            if any(rid.startswith("android-") for rid in item.get("platforms", {}))
        ],
        "scope": "Engine catalog only; not the set of versions in use by live servers.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=pathlib.Path, help="Use an already downloaded manifest")
    parser.add_argument("--url", default=DEFAULT_URL)
    parser.add_argument("--output", type=pathlib.Path, default=pathlib.Path("artifacts/engine-catalog-summary.json"))
    args = parser.parse_args()
    if args.input:
        catalog = json.loads(args.input.read_text())
    else:
        with urllib.request.urlopen(args.url, timeout=30) as response:
            catalog = json.load(response)
    result = summarize(catalog)
    result["observed_at"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    result["source"] = str(args.input) if args.input else args.url
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
