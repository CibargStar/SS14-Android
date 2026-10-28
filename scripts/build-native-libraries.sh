#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME to an Android NDK installation}"
abi="${SS14_ANDROID_ABI:-arm64-v8a}"
case "$abi" in
  arm64-v8a) build_arch=arm64; triple=aarch64-linux-android ;;
  x86_64) build_arch=x64; triple=x86_64-linux-android ;;
  *) echo "Unsupported Android ABI: $abi" >&2; exit 1 ;;
esac
root="$(pwd)"
destination="$root/artifacts/native/$abi"
mkdir -p "$destination"

checkout() {
  local name="$1" url="$2" revision="$3"
  if [[ ! -d "$root/artifacts/native-src/$name/.git" ]]; then
    git init "$root/artifacts/native-src/$name"
    git -C "$root/artifacts/native-src/$name" remote add origin "$url"
  fi
  git -C "$root/artifacts/native-src/$name" fetch --depth 1 origin "$revision"
  git -C "$root/artifacts/native-src/$name" checkout --detach FETCH_HEAD
}

build() {
  local name="$1" source="$2"
  shift 2
  cmake -S "$source" -B "$root/artifacts/native-build/$name-$build_arch" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI="$abi" -DANDROID_PLATFORM=26 -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5 "$@"
  cmake --build "$root/artifacts/native-build/$name-$build_arch" --parallel 4
}

checkout FreeType https://github.com/freetype/freetype.git 42608f77f20749dd6ddc9e0536788eaad70ea4b5
build FreeType "$root/artifacts/native-src/FreeType" -DBUILD_SHARED_LIBS=ON
cp "$root/artifacts/native-build/FreeType-$build_arch/libfreetype.so" "$destination/"
cp "$root/artifacts/native-src/FreeType/docs/FTL.TXT" "$destination/FreeType-LICENSE.txt"

checkout OpenAL https://github.com/kcat/openal-soft.git dc7d7054a5b4f3bec1dc23a42fd616a0847af948
build OpenAL "$root/artifacts/native-src/OpenAL" -DLIBTYPE=SHARED \
  -DALSOFT_EXAMPLES=OFF -DALSOFT_TESTS=OFF -DALSOFT_UTILS=OFF -DALSOFT_EMBED_HRTF_DATA=YES
cp "$root/artifacts/native-build/OpenAL-$build_arch/libopenal.so" "$destination/"
cp "$root/artifacts/native-src/OpenAL/COPYING" "$destination/OpenAL-LICENSE.txt"

checkout Zstd https://github.com/facebook/zstd.git f8745da6ff1ad1e7bab384bd1f9d742439278e99
build Zstd "$root/artifacts/native-src/Zstd/build/cmake" \
  -DZSTD_BUILD_PROGRAMS=OFF -DZSTD_BUILD_STATIC=OFF -DZSTD_BUILD_SHARED=ON
cp "$root/artifacts/native-build/Zstd-$build_arch/lib/libzstd.so" "$destination/"
cp "$root/artifacts/native-src/Zstd/LICENSE" "$destination/Zstd-LICENSE.txt"

python3 - "$root/artifacts/native-src" "$destination" <<'PY'
import hashlib, json, pathlib, subprocess, sys
sources, destination = map(pathlib.Path, sys.argv[1:])
result = []
for name, library in [('FreeType', 'libfreetype.so'), ('OpenAL', 'libopenal.so'), ('Zstd', 'libzstd.so')]:
    revision = subprocess.check_output(['git', '-C', str(sources/name), 'rev-parse', 'HEAD'], text=True).strip()
    result.append({'name': name, 'revision': revision, 'file': library,
        'sha256': hashlib.sha256((destination/library).read_bytes()).hexdigest()})
(destination/'libraries.json').write_text(json.dumps(result, indent=2) + '\n')
PY
