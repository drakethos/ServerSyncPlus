#!/usr/bin/env bash
# Stages a fake "Valheim install" with compile-only reference assemblies, so CI (or a machine without
# Valheim) can build ServerSyncPlus. Same source as the other DrakeMods repos:
# the Pfhoenix.Valheim.ModProjectReferences NuGet package.
#
# Usage: .github/scripts/stage-game-refs.sh [target-dir]   (default: ci_valheim)
# Then:  dotnet build src/ServerSyncPlus.csproj -c Release -p:GamePath="$PWD/ci_valheim"
set -euo pipefail
PFHOENIX_VERSION="${PFHOENIX_VERSION:-1.0.23}"
TARGET="${1:-ci_valheim}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

curl -fsSL -o "$WORK/refs.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/pfhoenix.valheim.modprojectreferences/${PFHOENIX_VERSION}/pfhoenix.valheim.modprojectreferences.${PFHOENIX_VERSION}.nupkg"
unzip -q "$WORK/refs.nupkg" -d "$WORK/pkg"
LIB="$(ls -d "$WORK"/pkg/lib/net46 | head -1)"

mkdir -p "$TARGET/valheim_Data/Managed" "$TARGET/BepInEx/core"
cp "$LIB"/*.dll "$TARGET/valheim_Data/Managed/"
cp "$LIB/0Harmony.dll" "$TARGET/BepInEx/core/"
cp "$(ls "$LIB"/[Bb]ep[Ii]n[Ee]x.dll | head -1)" "$TARGET/BepInEx/core/BepInEx.dll"
echo "Staged Valheim reference assemblies (Pfhoenix ${PFHOENIX_VERSION}) in $TARGET"
