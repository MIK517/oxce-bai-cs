#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
    echo "Usage: $0 PUBLISH_DIRECTORY video|audio EXPECTED_DRIVER" >&2
    exit 2
fi

readonly publish_directory="$1"
readonly mode="$2"
readonly expected_driver="$3"
readonly application="$publish_directory/Oxce.App.dll"

if [[ ! -f "$application" ]]; then
    echo "Published application was not found at $application." >&2
    exit 1
fi

case "$mode" in
    video)
        output="$(dotnet "$application" --sdl-smoke)"
        echo "$output"
        grep -F "SDL video driver: $expected_driver" <<<"$output"
        frames="$(awk -F': ' '/^SDL presented frames:/ { print $2 }' <<<"$output")"
        ticks="$(awk -F': ' '/^SDL ticks:/ { print $2 }' <<<"$output")"
        renderer="$(awk -F': ' '/^SDL renderer:/ { print $2 }' <<<"$output")"
        suppressed="$(awk -F': ' '/^SDL suppressed presentations:/ { print $2 }' <<<"$output")"
        maximum_us="$(awk -F': ' '/^SDL maximum presentation us:/ { print $2 }' <<<"$output")"
        [[ "$frames" =~ ^[1-9][0-9]*$ ]]
        [[ "$ticks" =~ ^[1-9][0-9]*$ ]]
        [[ "$suppressed" =~ ^[1-9][0-9]*$ ]]
        [[ "$maximum_us" =~ ^[0-9]+([.][0-9]+)?$ ]]
        awk -v value="$maximum_us" 'BEGIN { exit !(value > 0) }'
        [[ -n "$renderer" && "$renderer" != "unknown" ]]
        ;;
    audio)
        output="$(dotnet "$application" --sdl-audio-smoke)"
        echo "$output"
        grep -F "SDL audio driver: $expected_driver" <<<"$output"
        ;;
    *)
        echo "Unsupported smoke mode '$mode'." >&2
        exit 2
        ;;
esac

grep -F "SDL version: 3.4.10" <<<"$output"
