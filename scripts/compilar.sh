#!/usr/bin/env bash
# Compila MojarraNav y su instalador desde Linux o WSL (necesita .NET 8 SDK y NSIS: apt install dotnet-sdk-8.0 nsis)
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/MojarraNav.csproj)
rm -rf dist/app
dotnet publish src/MojarraNav.csproj -c Release -o dist/app
makensis -V2 -DVERSION="$VERSION" -DSRC="$PWD/dist/app" -DOUTFILE="$PWD/dist/MojarraNav-Setup-$VERSION.exe" installer/MojarraNav.nsi
ls -lh "dist/MojarraNav-Setup-$VERSION.exe"
