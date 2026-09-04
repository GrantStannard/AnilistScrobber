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

# Jellyfin fetches a plugin repository anonymously, so the download has to be reachable without
# credentials. The GitHub mirror carries the releases for that reason; the Forgejo repository
# itself stays private.
source_url="https://github.com/GrantStannard/AnilistScrobber/releases/download/v${version}/${zip_name}"

python3 - "$root/manifest.json" "$version" "$checksum" "$source_url" <<'PY'
import json, pathlib, sys
from datetime import datetime, timezone

manifest_path = pathlib.Path(sys.argv[1])
version, checksum, source_url = sys.argv[2:5]

manifest = json.loads(manifest_path.read_text())
versions = manifest[0]["versions"]
number = f"{version}.0"

# Rebuilding a version replaces its entry rather than adding a second one with the same number,
# and keeps whatever changelog was written for it by hand.
previous = next((v for v in versions if v["version"] == number), None)
entry = {
    "version": number,
    "changelog": previous["changelog"] if previous else "See the repository releases.",
    "targetAbi": "10.11.0.0",
    "sourceUrl": source_url,
    "checksum": checksum,
    "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}

# Jellyfin offers the first entry it finds, so the newest build goes at the front.
manifest[0]["versions"] = [entry] + [v for v in versions if v["version"] != number]
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
PY

echo "Packaged $out/$zip_name (md5 $checksum)"
echo "Updated manifest.json for ${version}.0"
