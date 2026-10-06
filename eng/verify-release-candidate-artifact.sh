#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 5 ]; then
  echo "usage: $0 <candidate-root> <run-id> <attempt> <source-sha> <artifact-digest>" >&2
  exit 2
fi

root=$1
run_id=$2
attempt=$3
source_sha=$4
artifact_digest=$5
receipt="$root/release-candidate.json"
packages="$root/packages"
site="$root/site"

if [[ ! "$run_id" =~ ^[1-9][0-9]*$ ]] ||
   [[ ! "$attempt" =~ ^[1-9][0-9]*$ ]] ||
   [[ ! "$source_sha" =~ ^[0-9a-f]{40}$ ]] ||
   [[ ! "$artifact_digest" =~ ^sha256:[0-9a-f]{64}$ ]]; then
  echo "invalid expected release candidate identity." >&2
  exit 1
fi

test -f "$receipt"
test -f "$root/package-sha256.txt"
test -f "$root/site-sha256.txt"
test -d "$packages"
test -d "$site"

version=$(jq -er '.version | select(test("^[0-9]+\\.[0-9]+\\.[0-9]+$"))' "$receipt")
jq -e \
  --argjson run_id "$run_id" \
  --argjson attempt "$attempt" \
  --arg source_sha "$source_sha" \
  '
    .schema == 1
    and .sourceSha == $source_sha
    and .producer.runId == $run_id
    and .producer.runAttempt == $attempt
    and (.packageManifestSha256 | test("^[0-9a-f]{64}$"))
    and (.siteManifestSha256 | test("^[0-9a-f]{64}$"))
  ' "$receipt" >/dev/null

test "$(git rev-parse HEAD)" = "$source_sha"
project_version=$(
  sed -n \
    's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' \
    src/DotnetInspect.Cli/DotnetInspect.Cli.csproj
)
skill_version=$(sed -n 's/^version: //p' skills/dotnet-inspect/SKILL.md)
test "$project_version" = "$version"
test "$skill_version" = "$version"

package_manifest_sha=$(sha256sum "$root/package-sha256.txt" | awk '{print $1}')
site_manifest_sha=$(sha256sum "$root/site-sha256.txt" | awk '{print $1}')
test "$package_manifest_sha" = "$(jq -r .packageManifestSha256 "$receipt")"
test "$site_manifest_sha" = "$(jq -r .siteManifestSha256 "$receipt")"
(
  cd "$packages"
  sha256sum -c ../package-sha256.txt
)
(
  cd "$site"
  sha256sum -c ../site-sha256.txt
)

test "$(find "$packages" -maxdepth 1 -type f -name '*.nupkg' | wc -l)" -eq 7
for id in \
  dotnet-inspect \
  dotnet-inspect.any \
  dotnet-inspect.win-x64 \
  dotnet-inspect.win-arm64 \
  dotnet-inspect.osx-arm64 \
  dotnet-inspect.linux-x64 \
  dotnet-inspect.linux-arm64; do
  test "$(find "$packages" -maxdepth 1 -type f \
    -name "$id.$version.nupkg" | wc -l)" -eq 1
done

settings_path() {
  unzip -l "$1" \
    | grep -oE 'tools/[^[:space:]]*DotnetToolSettings.xml' \
    | head -1
}
test "$(settings_path "$packages/dotnet-inspect.any.$version.nupkg")" \
  = "tools/net10.0/any/DotnetToolSettings.xml"
test "$(settings_path "$packages/dotnet-inspect.$version.nupkg")" \
  = "tools/any/any/DotnetToolSettings.xml"

eng/verify-inspect-web-site-artifact.sh "$site"

{
  echo "## Verified release candidate"
  echo
  echo "- Run: \`$run_id\`"
  echo "- Attempt: \`$attempt\`"
  echo "- SHA: \`$source_sha\`"
  echo "- Version: \`$version\`"
  echo "- Artifact digest: \`$artifact_digest\`"
} >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
