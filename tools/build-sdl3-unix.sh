#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 WORK_DIRECTORY" >&2
    exit 2
fi

readonly sdl_version="3.4.10"
readonly archive_sha256="12b34280415ec8418c864408b93d008a20a6530687ee613d60bfbd20411f2785"
readonly work_directory="$1"
readonly archive="$work_directory/SDL3-$sdl_version.tar.gz"
readonly source_directory="$work_directory/SDL3-$sdl_version"
readonly build_directory="$work_directory/build"
readonly install_directory="$work_directory/install"

mkdir -p "$work_directory"
curl --fail --location --retry 3 \
    "https://github.com/libsdl-org/SDL/releases/download/release-$sdl_version/SDL3-$sdl_version.tar.gz" \
    --output "$archive"

if command -v sha256sum >/dev/null 2>&1; then
    echo "$archive_sha256  $archive" | sha256sum --check --status
else
    [[ "$(shasum -a 256 "$archive" | awk '{ print $1 }')" == "$archive_sha256" ]]
fi

tar -xzf "$archive" -C "$work_directory"
cmake -S "$source_directory" -B "$build_directory" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$install_directory" \
    -DSDL_SHARED=ON \
    -DSDL_STATIC=OFF \
    -DSDL_TEST_LIBRARY=OFF \
    -DSDL_TESTS=OFF
cmake --build "$build_directory" --config Release --parallel 2
cmake --install "$build_directory" --config Release

echo "SDL $sdl_version installed to $install_directory"
