---
id: cli-byte-publication
description: Exact-build latency, allocation, and output identity for direct CLI UTF-8 publication
commands: [type]
areas: [performance, presentation, utf8, nativeaot]
---

# Performance: CLI Byte Publication

> Validate one complete command-input-to-UTF-8-standard-output path without
> adding a product-only path marker.

The direct command and its neighboring fallback select the same `Type Info`
document. Adding a Member selector makes the direct document ineligible and
therefore forces the established rich renderer. Byte-for-byte output parity
proves the comparison preserves the user result; the exact-source allocation
probe distinguishes the implementations through their observable cost.

## Preconditions

Point `INSPECT` at the exact published NativeAOT product apphost. Point
`ALLOCATION_PROBE` at `eng/measure-cli-byte-publication.cs` published as
NativeAOT from the same revision. Pin one real `System.Linq.dll` by path and
SHA-256:

```bash
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_BINARY:?set the exact published apphost path}"
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
: "${DOTNET_INSPECT_WORKFLOW_ALLOCATION_BINARY:?set the exact allocation probe path}"
: "${DOTNET_INSPECT_WORKFLOW_ASSEMBLY:?set the pinned System.Linq.dll path}"
: "${DOTNET_INSPECT_WORKFLOW_ASSEMBLY_SHA256:?set its expected SHA-256}"
export INSPECT="$DOTNET_INSPECT_WORKFLOW_BINARY"
export ALLOCATION_PROBE="$DOTNET_INSPECT_WORKFLOW_ALLOCATION_BINARY"
export ASSEMBLY="$DOTNET_INSPECT_WORKFLOW_ASSEMBLY"
test -x "$INSPECT"
test -x "$ALLOCATION_PROBE"
test -f "$ASSEMBLY"
test "$("$INSPECT" --version)" = "$DOTNET_INSPECT_WORKFLOW_VERSION"
test "$(command -v dotnet-inspect)" = "$INSPECT"
"$INSPECT" --flavor | grep -q '^NativeAOT;'
test "$("$ALLOCATION_PROBE" --identity)" = \
  "$DOTNET_INSPECT_WORKFLOW_VERSION;NativeAOT"
test "$(sha256sum "$ASSEMBLY" | awk '{print $1}')" = \
  "$DOTNET_INSPECT_WORKFLOW_ASSEMBLY_SHA256"
export DOTNET_INSPECT_ISOLATED=cli-byte-publication
export BYTE_PUBLICATION_WORK="$PWD/artifacts/workflows/cli-byte-publication"
mkdir -p "$BYTE_PUBLICATION_WORK"
```

Warm both routes:

```bash
"$INSPECT" type System.Linq.Enumerable \
  --library "$ASSEMBLY" -S "Type Info" --tsv > /dev/null
"$INSPECT" type System.Linq.Enumerable \
  --library "$ASSEMBLY" -m Any -S "Type Info" --tsv > /dev/null
```

## 1. Preserve exact output across the direct and fallback routes

> Goal: Prove that the byte-native sink changes representation and work, not
> the selected user document.

```bash
direct="$BYTE_PUBLICATION_WORK/direct.tsv"
fallback="$BYTE_PUBLICATION_WORK/fallback.tsv"
"$INSPECT" type System.Linq.Enumerable \
  --library "$ASSEMBLY" -S "Type Info" --tsv > "$direct"
"$INSPECT" type System.Linq.Enumerable \
  --library "$ASSEMBLY" -m Any -S "Type Info" --tsv > "$fallback"
cmp "$direct" "$fallback"
normalized_sha256=$(
  sed $'s#^Library\\t.*#Library\\t<assembly>#' "$direct" \
    | sha256sum \
    | awk '{print $1}'
)
test "$normalized_sha256" = \
  "83128eba37c9b8735192dcb6180b87075280861e511dcd613c8a0ec7bc1cf445"
printf 'output-equal\nnormalized-sha256=%s\n' "$normalized_sha256"
cat "$direct"
```

```expect
output-equal
normalized-sha256=83128eba37c9b8735192dcb6180b87075280861e511dcd613c8a0ec7bc1cf445
System.Linq.Enumerable
Kind
class
Library
Source
```

## 2. Keep the complete direct command within its latency budget

> Goal: Measure command input, exact inspection, containment, Presentation,
> and UTF-8 standard-output publication together.

```bash
for iteration in 1 2 3 4 5; do
  "$INSPECT" type System.Linq.Enumerable \
    --library "$ASSEMBLY" -S "Type Info" --tsv > /dev/null
done
```

```perf
max_ms: 500
```

## 3. Retain the direct route's allocation advantage

> Goal: Measure managed allocation from public command construction and
> parsing through inspection, Presentation, and output, using the same
> semantic direct/fallback pair as the external process scenario.

The probe writes command output to its inherited standard output and metrics
to standard error. Redirecting standard output to `/dev/null` preserves the
real CLI sink while keeping the metrics machine-readable.

```bash
metrics="$BYTE_PUBLICATION_WORK/allocation.tsv"
"$ALLOCATION_PROBE" \
  "$ASSEMBLY" \
  System.Linq.Enumerable \
  Any \
  --samples 20 \
  > /dev/null \
  2> "$metrics"
cat "$metrics"
awk -F '\t' '
  $1 == "direct" { direct = $3 }
  $1 == "fallback" { fallback = $3 }
  END {
    if (direct > 0 && direct <= 4000000 && direct * 10 < fallback)
      print "allocation-budget-ok"
  }
' "$metrics"
```

```expect
identity
median_allocated_b
direct
fallback
allocation-budget-ok
```
