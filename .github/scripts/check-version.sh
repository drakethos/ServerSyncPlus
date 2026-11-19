#!/usr/bin/env bash
# Fails unless every version declaration agrees (and matches the tag, if given).
# Usage: .github/scripts/check-version.sh [expected-version]
set -euo pipefail
CSPROJ=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/ServerSyncPlus.csproj | head -1)
PLUGIN=$(sed -n 's:.*public const string Version = "\(.*\)";.*:\1:p' src/Plus/Plugin.cs | head -1)
MANIFEST=$(python3 -c 'import json;print(json.load(open("package/manifest.json"))["version_number"])')
CHANGELOG=$(grep -m1 -oE '^## \[?[0-9]+\.[0-9]+\.[0-9]+' CHANGELOG.md | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')
echo "csproj=$CSPROJ plugin=$PLUGIN manifest=$MANIFEST changelog=$CHANGELOG expected=${1:-<none>}"
for v in "$PLUGIN" "$MANIFEST" "$CHANGELOG" "${1:-$CSPROJ}"; do
  if [ "$v" != "$CSPROJ" ]; then echo "::error::Version mismatch (see line above)"; exit 1; fi
done
