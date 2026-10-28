#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME}"
abi="${SS14_ANDROID_ABI:-arm64-v8a}"
case "$abi" in
  arm64-v8a) build_arch=arm64; triple=aarch64-linux-android ;;
  x86_64) build_arch=x64; triple=x86_64-linux-android ;;
  *) echo "Unsupported Android ABI: $abi" >&2; exit 1 ;;
esac
root="$(pwd)"
archive="$root/artifacts/libsodium-1.0.20.tar.gz"
source_dir="$root/artifacts/native-src/libsodium-release"
build_dir="$root/artifacts/native-build/libsodium-$build_arch"
destination="$root/artifacts/native/$abi"
mkdir -p "$source_dir" "$build_dir" "$destination"
if [[ ! -f "$archive" ]]; then
  curl -fsSL https://download.libsodium.org/libsodium/releases/libsodium-1.0.20.tar.gz -o "$archive"
fi
python3 - "$archive" <<'PY'
import hashlib, pathlib, sys
assert hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest() == 'ebb65ef6ca439333c2bb41a0c1990587288da07f6c7fd07cb3a18cc18d30ce19'
PY
tar -xzf "$archive" -C "$source_dir" --strip-components=1
toolchain="$ANDROID_NDK_HOME/toolchains/llvm/prebuilt/linux-x86_64/bin"
cd "$build_dir"
CC="$toolchain/${triple}26-clang" AR="$toolchain/llvm-ar" \
RANLIB="$toolchain/llvm-ranlib" STRIP="$toolchain/llvm-strip" \
CFLAGS='-O2 -fPIC' LDFLAGS='-Wl,-z,max-page-size=16384' \
  "$source_dir/configure" --host="$triple" --enable-shared --disable-static \
    --prefix="$root/artifacts/native-install/libsodium-$build_arch"
make -j4
make install
cp "$root/artifacts/native-install/libsodium-$build_arch/lib/libsodium.so" "$destination/"
cp "$source_dir/LICENSE" "$destination/libsodium-LICENSE.txt"
