#!/usr/bin/env bash
# Re-fetches every third-party subresource pinned in prototypes/inspect-web/index.html and
# checks the bytes still hash to the `integrity` value committed beside them.
#
# `require-sri` in .htmlvalidate.json enforces that a cross-origin subresource *carries* a
# digest. That is a source-text property, and it is the whole of what a linter can see. It
# says nothing about whether the digest still describes what the CDN serves today, because
# that fact lives on the network and changes without any commit to this repository.
#
# The failure this closes is narrow and worth naming. A stale pin does not silently load
# the wrong script -- the browser enforces SRI and refuses it -- so the risk is not
# execution of unexpected bytes. It is that the site quietly loses the subresource, which
# on this site means syntax highlighting stops working, with nothing in CI to say why. The
# same check also surfaces a CDN that has begun serving different bytes under a pinned
# immutable URL, which is a supply-chain signal worth an issue even though SRI already
# blocked it.
#
# Run from the repository root. Network access is required, which is why this is a
# scheduled job rather than a PR gate: jsDelivr being unreachable is not a defect in a
# pull request, and failing every PR on it would be a flake, not a protection.
set -euo pipefail

index="prototypes/inspect-web/index.html"
test -f "$index"

# The pins are read out of the document rather than restated here. A second copy of the
# URL/digest pairs would be one more thing to drift, and the drift would be silent in
# exactly the direction that matters -- a pin updated in the markup and not here would
# leave this script verifying a resource the site no longer loads.
pins="$(node --input-type=module -e '
  import { readFile } from "node:fs/promises";
  const raw = await readFile(process.argv[1], "utf8");

  // A commented-out tag is not loaded, so tracking it would mean this check fails when a
  // CDN drops a resource the site stopped requesting.
  const html = raw.replace(/<!--[\s\S]*?-->/gu, "");

  // HTML allows double-quoted, single-quoted and unquoted attribute values, and tag and
  // attribute names are case-insensitive. html-validate accepts all of those -- verified,
  // rather than assumed -- so a pattern that only understands lowercase double-quoted
  // markup would drop real subresources out of this check and still report success.
  //
  // \x27 is an apostrophe. It is spelled that way because this whole program is inside a
  // single-quoted shell string, so a literal apostrophe would end it.
  const attribute = (tag, name) => {
    const pattern = new RegExp(
      String.raw`\b${name}\s*=\s*(?:"([^"]*)"|\x27([^\x27]*)\x27|([^\s"\x27>]+))`,
      "iu",
    );
    const match = pattern.exec(tag);
    return match === null ? undefined : (match[1] ?? match[2] ?? match[3]);
  };

  // Subresource Integrity is defined for `script` and `link` only, so those two elements
  // are the complete set here rather than a sample of it.
  const tags = html.match(/<(?:script|link)\b[^>]*>/giu) ?? [];
  for (const tag of tags) {
    const url = attribute(tag, "src") ?? attribute(tag, "href");
    // Protocol-relative URLs are cross-origin too, and need a digest just as much.
    if (url === undefined || !/^(?:https?:)?\/\//iu.test(url)) continue;
    // curl rejects a protocol-relative URL outright ("No host part in the URL"), so one
    // has to be resolved here or the pin would report as unfetchable rather than being
    // checked. The site is served over HTTPS, which is the scheme a browser would pick.
    const absolute = url.startsWith("//") ? `https:${url}` : url;
    console.log(`${absolute}\t${attribute(tag, "integrity") ?? ""}`);
  }
' "$index")"

if [ -z "$pins" ]; then
  echo "no third-party subresources found in $index" >&2
  echo "this script exists to check them, so finding none means the markup shape changed" >&2
  exit 1
fi

status=0
# `while read` over a here-string rather than `mapfile`, which bash 3.2 -- still the
# system bash on macOS -- does not have. A here-string keeps the loop in this shell, so
# `status` set inside it survives.
while IFS=$'\t' read -r url integrity; do
  [ -n "$url" ] || continue
  if [ -z "$integrity" ]; then
    echo "MISSING  $url"
    echo "         loaded from another origin with no integrity attribute"
    status=1
    continue
  fi

  algorithm="${integrity%%-*}"
  expected="${integrity#*-}"
  case "$algorithm" in
    sha256|sha384|sha512) ;;
    *)
      echo "UNKNOWN  $url"
      echo "         integrity algorithm '$algorithm' is not one SRI defines"
      status=1
      continue
      ;;
  esac

  # Hashing straight out of the pipe rather than through a shell variable. Command
  # substitution strips every trailing newline (and drops NUL bytes), so buffering the
  # body first would hash something other than what the CDN served and report DRIFT on a
  # perfectly good pin. The three resources pinned today happen to end in `;`, which is
  # the only reason that bug was invisible. `pipefail` is set, so a curl failure still
  # fails the pipeline rather than hashing an empty stream.
  if ! actual="$(curl -sSfL --retry 3 --retry-delay 5 --max-time 60 "$url" \
      | openssl dgst "-$algorithm" -binary \
      | openssl base64 -A)"; then
    echo "FETCH    $url"
    echo "         could not be retrieved, or could not be hashed"
    status=1
    continue
  fi

  if [ "$actual" = "$expected" ]; then
    echo "OK       $url"
  else
    echo "DRIFT    $url"
    echo "         pinned $algorithm-$expected"
    echo "         served $algorithm-$actual"
    status=1
  fi
done <<< "$pins"

exit "$status"
