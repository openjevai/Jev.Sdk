#!/usr/bin/env bash
# pack-world.sh — build a shippable world package from a world document.
#
#   ./pack-world.sh worlds/escape-the-room.md        -> escape-the-room.zip
#
# A package is the world document named 'world.md', plus anything beside it. The player detects the
# zip, unpacks it to a temporary folder, and plays from there.
#
# Usage from the repository root:
#   bash samples/World/pack-world.sh samples/World/worlds/escape-the-room.md

set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: $(basename "$0") <world.md|world.json> [output.zip]"
  exit 1
fi

source_doc="$1"

if [ ! -f "$source_doc" ]; then
  echo "no such world document: $source_doc" >&2
  exit 1
fi

# The package is named after the document, and the world inside is always 'world.md' or 'world.json'
# so the player finds it without being told which.
name="$(basename "${source_doc%.*}")"
extension="${source_doc##*.}"
output="$(basename "${2:-${name}.zip}")"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

cp "$source_doc" "$staging/world.$extension"

# Anything else in the document's own folder that looks like material for a reader travels with it.
source_dir="$(dirname "$source_doc")"
for extra in "$source_dir/$name"*.md; do
  [ -f "$extra" ] || continue
  [ "$extra" = "$source_doc" ] && continue
  cp "$extra" "$staging/"
done

( cd "$staging" && zip -qr "$output" . )
mv "$staging/$output" "./$output"

echo "wrote $output"
unzip -l "$output"
