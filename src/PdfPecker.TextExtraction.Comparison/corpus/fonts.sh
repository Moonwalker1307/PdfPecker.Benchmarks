#!/usr/bin/env bash
# Downloads the fonts the corpus pages use (all SIL OFL 1.1) into corpus/fonts, from the @expo-google-fonts packages on
# npm, which carry Google Fonts' TrueType files. They are not checked in: Noto Sans SC and JP alone are 30 MB.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$here/fonts"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
cd "$work"
for family in noto-sans-sc noto-sans-jp noto-sans-hebrew noto-naskh-arabic eb-garamond; do
  tar xzf "$(npm pack --silent "@expo-google-fonts/$family")"
  rm -f ./*.tgz
done
find "$work" \( -name '*_400Regular*.ttf' -o -name '*_700Bold.ttf' \) -exec cp {} "$here/fonts/" \;
ls "$here/fonts"
