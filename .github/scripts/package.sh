#!/usr/bin/env bash
# Builds the Hexium/Thunderstore zip from a Release build.
# Usage: .github/scripts/package.sh <version> <build-output-dir>
# Output: DrakeMods-ServerSyncPlus-<version>.zip in the repo root.
set -euo pipefail
VERSION="$1"; OUT="$2"
NAME="DrakeMods-ServerSyncPlus-${VERSION}"
rm -rf "$NAME" "$NAME.zip"
mkdir -p "$NAME/plugins"
# Gale/r2modman keep folder structure only under plugins/; docs live at the zip root.
cp "$OUT/ServerSyncPlus.dll" "$OUT/ServerSyncPlus.xml" "$NAME/plugins/"
cp package/manifest.json package/icon.png README.md CHANGELOG.md "$NAME/"
(cd "$NAME" && zip -qr "../$NAME.zip" .)
python3 .github/scripts/check-gale-layout.py "$NAME.zip" "DrakeMods-ServerSyncPlus" --expect ServerSyncPlus.dll --expect ServerSyncPlus.xml
echo "$NAME.zip"
