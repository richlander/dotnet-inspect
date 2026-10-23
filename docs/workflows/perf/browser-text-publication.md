---
id: browser-text-publication
description: Exact-build Browser/Wasm text publication through managed JSON and retained JavaScript results
commands: [inspect-web]
areas: [performance, browser, wasm, serialization, inert-text]
---

# Performance: Browser Text Publication

> Validate one published-runtime metadata-name population through managed
> execution, JSON serialization, Wasm interop, `JSON.parse`, Worker result
> validation, structured clone, and retained page-owned JavaScript identity.

The scenario uses the production Worker and generated metadata facade. It does
not add a benchmark-only managed export or change the product representation.
The pinned `Microsoft.Extensions.Primitives.StringSegment` Type overview is
loaded once, then measured repeatedly in one runtime while every returned
result remains retained until the final payload census.

The Browser does not expose a stable portable heap-size API at this boundary.
The report therefore records exact result cardinality, contained string
characters and UTF-8 bytes, serialized JSON size, and the aggregate payload
retained by all measured results. These are payload facts, not a claim about
Firefox object overhead or total process memory.

## Preconditions

Point `INSPECT_WEB_WORKFLOW_SITE` at the `wwwroot` directory from an exact
published Browser/Wasm product. Set its reported commit and product version.
Use Node 24 or later, install the checked-in frontend dependencies and Firefox,
and keep network access explicit because the pinned package warmup uses the
production NuGet path:

```bash
set -e -o pipefail
: "${INSPECT_WEB_WORKFLOW_SITE:?set the exact published wwwroot path}"
: "${INSPECT_WEB_WORKFLOW_COMMIT:?set the exact 40-character source commit}"
: "${INSPECT_WEB_WORKFLOW_VERSION:?set the expected product version}"
test -f "$INSPECT_WEB_WORKFLOW_SITE/index.html"
test -f "$INSPECT_WEB_WORKFLOW_SITE/inspect-web-metadata.js"
test -f "$INSPECT_WEB_WORKFLOW_SITE/_framework/dotnet.js"
test "$(node --version | sed 's/^v//' | cut -d. -f1)" -ge 24
test -x inspect-web/node_modules/.bin/playwright
export BROWSER_TEXT_PUBLICATION_WORK="$PWD/artifacts/workflows/browser-text-publication"
mkdir -p "$BROWSER_TEXT_PUBLICATION_WORK"
```

## 1. Preserve exact parsed result identity

> Goal: Prove repeated calls return one exact Type-overview result while the
> real product path crosses managed serialization, generated-facade parsing,
> Worker validation, structured clone, and page retention.

```bash
set -e -o pipefail
report="$BROWSER_TEXT_PUBLICATION_WORK/report.json"
server_log="$BROWSER_TEXT_PUBLICATION_WORK/server.log"
export INSPECT_WEB_PACKAGE_ADOPTION_SITE="$INSPECT_WEB_WORKFLOW_SITE"
export INSPECT_WEB_PACKAGE_ADOPTION_PORT=4193
cd inspect-web
node scripts/serve-package-adoption-gate.ts > "$server_log" 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT
for attempt in 1 2 3 4 5 6 7 8 9 10; do
  if curl --fail --silent --show-error \
    http://127.0.0.1:4193/index.html > /dev/null; then
    break
  fi
  sleep 1
done
curl --fail --silent --show-error \
  http://127.0.0.1:4193/index.html > /dev/null
npm run benchmark:text-publication -- \
  --site http://127.0.0.1:4193 \
  --expected-commit "$INSPECT_WEB_WORKFLOW_COMMIT" \
  --expected-version "$INSPECT_WEB_WORKFLOW_VERSION" \
  --samples 20 \
  --output "$report"
```

```expect
text-publication-ok
semantic-sha256=cb83c2acb4e09990016d7b77d1d340b4ba86dd8c9726f39bcea0b8793b6d81d3
groups=38
complete-members=55
retained-results=20
```

## 2. Record publication latency and retained payload

> Goal: Keep first-use and warm publication latency separate and preserve the
> exact retained-result payload boundary without presenting it as browser heap
> allocation.

```bash
set -e -o pipefail
report="$BROWSER_TEXT_PUBLICATION_WORK/report.json"
jq -e '
  .schema == 1
  and .harness.dirty == false
  and .harness.commit == .buildIdentity.commit
  and .configuration.samples == 20
  and .configuration.packageWarmup == "excluded"
  and .payload.retainedResults == 20
  and .payload.browserHeapBytes == null
  and .semanticSha256
    == "cb83c2acb4e09990016d7b77d1d340b4ba86dd8c9726f39bcea0b8793b6d81d3"
  and .resultIdentity.typeIdentity
    == "Microsoft.Extensions.Primitives.StringSegment"
  and .resultIdentity.moduleVersionId
    == "02251bb3-2215-4b77-b5a0-f1e67f7ba93c"
  and .resultIdentity.groups == 38
  and .resultIdentity.completeMembers == 55
  and .payload.perResult.strings == 209
  and .payload.perResult.utf16CodeUnits == 2011
  and .payload.perResult.utf8Bytes == 2011
  and .payload.perResult.serializedUtf8Bytes == 10463
  and .payload.retained.strings == 4180
  and .payload.retained.utf16CodeUnits == 40220
  and .payload.retained.utf8Bytes == 40220
  and .payload.retained.serializedUtf8Bytes == 209281
  and .latency.firstMilliseconds > 0
  and .latency.warmMilliseconds.count == 19
  and .latency.warmMilliseconds.median > 0
' "$report" > /dev/null
jq -r '
  "identity=\(.semanticSha256)",
  "first-ms=\(.latency.firstMilliseconds)",
  "warm-median-ms=\(.latency.warmMilliseconds.median)",
  "warm-p95-ms=\(.latency.warmMilliseconds.p95)",
  "groups=\(.resultIdentity.groups)",
  "complete-members=\(.resultIdentity.completeMembers)",
  "per-result-utf16=\(.payload.perResult.utf16CodeUnits)",
  "per-result-utf8=\(.payload.perResult.utf8Bytes)",
  "serialized-utf8=\(.payload.perResult.serializedUtf8Bytes)",
  "retained-results=\(.payload.retainedResults)",
  "retained-serialized-utf8=\(.payload.retained.serializedUtf8Bytes)"
' "$report"
```

```expect
identity=
first-ms=
warm-median-ms=
warm-p95-ms=
groups=
complete-members=
per-result-utf16=
per-result-utf8=
serialized-utf8=
retained-results=20
retained-serialized-utf8=
```
