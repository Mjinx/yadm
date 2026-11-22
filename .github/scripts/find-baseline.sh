#!/usr/bin/env bash
set -euo pipefail

current="${1%%[-+]*}"
if [[ ! "$current" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Invalid release version: $1" >&2
  exit 1
fi
major="${current%%.*}"

{ printf '%s\n' "$current"; cat; } |
  awk -v major="$major." 'index($0, major) == 1 && /^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/' |
  sort -V |
  awk -v current="$current" '$0 == current { found = 1 } !found { previous = $0 } END { print previous }'
