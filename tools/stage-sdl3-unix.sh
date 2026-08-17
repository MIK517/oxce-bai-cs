#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "Usage: $0 SDL_INSTALL_DIRECTORY PUBLISH_DIRECTORY" >&2
    exit 2
fi

readonly install_directory="$1"
readonly publish_directory="$2"

if [[ "$(uname -s)" == "Darwin" ]]; then
    library="$(find "$install_directory" -type f -name 'libSDL3*.dylib' -print -quit)"
    destination="$publish_directory/libSDL3.dylib"
else
    library="$(find "$install_directory" -type f -name 'libSDL3.so*' -print -quit)"
    destination="$publish_directory/libSDL3.so"
fi

if [[ -z "$library" ]]; then
    echo "Could not find the installed SDL3 shared library." >&2
    exit 1
fi

mkdir -p "$publish_directory"
cp -L "$library" "$destination"
echo "Staged $library as $destination"
