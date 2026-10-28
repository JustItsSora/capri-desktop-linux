#!/usr/bin/env bash
# Builds a single-file Capri for Linux, with the .NET runtime inside.
#
#   ./build-linux.sh            -> out/linux/Capri
#
# Needs the .NET 10 SDK. On Debian/Ubuntu: https://dotnet.microsoft.com/download
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:-$here/out/linux}"
dotnet="${DOTNET:-dotnet}"

echo "Building Capri for linux-x64 into $out"
"$dotnet" publish "$here/cross/Capri.Cross.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$out"

chmod +x "$out/Capri"
echo "Done: $out/Capri ($(du -h "$out/Capri" | cut -f1))"
echo
echo "Run it with: $out/Capri"
echo "Her brain lives in ~/.config/Capri. For a cloud brain, right-click her and paste an API key."
