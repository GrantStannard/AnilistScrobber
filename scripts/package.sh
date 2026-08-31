#!/usr/bin/env bash
# Builds the plugin and packages it the way Jellyfin expects: a flat zip of the plugin's
# own assemblies, plus the checksum and metadata a plugin repository needs.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/Jellyfin.Plugin.AniListScrobbler/Jellyfin.Plugin.AniListScrobbler.csproj"
version="$(grep -oPm1 '(?<=<Version>)[^<]+' "$project")"
out="$root/artifacts"
stage="$out/stage"
zip_name="jellyfin-plugin-anilist-scrobbler_${version}.zip"

rm -rf "$out"
mkdir -p "$stage"

dotnet publish "$project" --configuration Release --output "$stage/publish"

# Jellyfin already ships every dependency this plugin uses, so only its own assembly is
# packaged. Shipping the framework copies would shadow the server's and break loading.
cp "$stage/publish/Jellyfin.Plugin.AniListScrobbler.dll" "$stage/"
rm -rf "$stage/publish"

if command -v zip >/dev/null 2>&1; then
  (cd "$stage" && zip -r "../$zip_name" ./*)
else
  # Minimal images and dev boxes often lack zip; python3 is a safe fallback.
  python3 - "$stage" "$out/$zip_name" <<'PY'
import pathlib, sys, zipfile

stage, target = pathlib.Path(sys.argv[1]), sys.argv[2]
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(stage.rglob("*")):
        if path.is_file():
            archive.write(path, path.relative_to(stage))
PY
fi
rm -rf "$stage"

checksum="$(md5sum "$out/$zip_name" | cut -d' ' -f1)"

cat > "$out/manifest-fragment.json" <<JSON
{
  "version": "${version}.0",
  "changelog": "See the repository releases.",
  "targetAbi": "10.11.0.0",
  "sourceUrl": "https://git.grantstannard.com/gstannard/anilist-scrobber/releases/download/v${version}/${zip_name}",
  "checksum": "${checksum}",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSON

echo "Packaged $out/$zip_name (md5 $checksum)"
