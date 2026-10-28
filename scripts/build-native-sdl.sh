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
source_dir="$(pwd)/artifacts/native-src/SDL"
build_dir="$(pwd)/artifacts/native-build/SDL-$build_arch"
destination="$(pwd)/artifacts/native/$abi"
revision=f0e99e7c7f9aa90d5ce2e3b8a69f72c23faf257e
mkdir -p "$(dirname "$source_dir")" "$destination"
if [[ ! -d "$source_dir/.git" ]]; then
  git init "$source_dir"
  git -C "$source_dir" remote add origin https://github.com/libsdl-org/SDL.git
fi
git -C "$source_dir" fetch --depth 1 origin "$revision"
git -C "$source_dir" checkout --detach "$revision"
cmake -S "$source_dir" -B "$build_dir" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI="$abi" -DANDROID_PLATFORM=26 -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON \
  -DCMAKE_BUILD_TYPE=Release -DSDL_SHARED=ON -DSDL_STATIC=OFF \
  -DSDL_ANDROID_JAR=OFF -DSDL_TEST_LIBRARY=OFF
cmake --build "$build_dir" --parallel 4
cp "$build_dir/libSDL3.so" "$destination/libSDL3.so"
cp "$source_dir/LICENSE.txt" "$destination/SDL-LICENSE.txt"
