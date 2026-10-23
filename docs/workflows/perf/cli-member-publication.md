---
id: cli-member-publication
description: Exact-build latency, allocation, and output identity for one exact CLI Member document
commands: [member]
areas: [performance, presentation, metadata, nativeaot]
---

# Performance: CLI Member Publication

> Validate one complete exact-Member command through subject resolution,
> Metadata declaration evidence, the host-neutral `MemberDocument`, final
> documentation composition, Markdown presentation, and standard output.

The scenario establishes a production baseline before broader byte-primary
signature-name adoption. It uses the document owner's pinned
`Newtonsoft.Json@13.0.3` asset and one exact `SerializeObject` overload. It
does not add a product-only route marker or claim that the final Markout sink
is byte-native.

## Preconditions

Point `INSPECT` at the exact published NativeAOT product apphost. Point
`ALLOCATION_PROBE` at `eng/measure-cli-member-publication.cs` published as
NativeAOT from the same revision. Pin the `Newtonsoft.Json@13.0.3` `net6.0`
assembly by path and SHA-256:

```bash
set -e -o pipefail
: "${DOTNET_INSPECT_WORKFLOW_BINARY:?set the exact published apphost path}"
: "${DOTNET_INSPECT_WORKFLOW_VERSION:?set the expected --version output}"
: "${DOTNET_INSPECT_WORKFLOW_ALLOCATION_BINARY:?set the exact allocation probe path}"
: "${DOTNET_INSPECT_WORKFLOW_ASSEMBLY:?set the pinned Newtonsoft.Json.dll path}"
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
export DOTNET_INSPECT_ISOLATED=cli-member-publication
export MEMBER_PUBLICATION_WORK="$PWD/artifacts/workflows/cli-member-publication"
mkdir -p "$MEMBER_PUBLICATION_WORK/input"
export SCENARIO_ASSEMBLY="$MEMBER_PUBLICATION_WORK/input/Newtonsoft.Json.dll"
cp "$ASSEMBLY" "$SCENARIO_ASSEMBLY"
rm -f "$MEMBER_PUBLICATION_WORK/input/Newtonsoft.Json.xml"
test "$(sha256sum "$SCENARIO_ASSEMBLY" | awk '{print $1}')" = \
  "$DOTNET_INSPECT_WORKFLOW_ASSEMBLY_SHA256"
```

Copying only the assembly makes the exact-Member document's optional compiled
documentation outcome deterministic. Warm the ordinary product command:

```bash
set -e -o pipefail
"$INSPECT" member Newtonsoft.Json.JsonConvert SerializeObject:1 \
  --library "$SCENARIO_ASSEMBLY" > /dev/null
```

## 1. Preserve the exact Member document

> Goal: Pin the owner-resolved exact subject, complete declaration, and visible
> documentation outcome before measuring their production publication cost.

```bash
set -e -o pipefail
output="$MEMBER_PUBLICATION_WORK/member.md"
"$INSPECT" member Newtonsoft.Json.JsonConvert SerializeObject:1 \
  --library "$SCENARIO_ASSEMBLY" > "$output"
output_sha256=$(sha256sum "$output" | awk '{print $1}')
test "$output_sha256" = \
  "f3f16d856d7eede9776954d11b00b4796da13e3111bab199f5ab860439574c94"
printf 'output-sha256=%s\n' "$output_sha256"
cat "$output"
```

```expect
output-sha256=f3f16d856d7eede9776954d11b00b4796da13e3111bab199f5ab860439574c94
## Signature
public static string SerializeObject(object value)
730f40d101
M:Newtonsoft.Json.JsonConvert.SerializeObject(System.Object)
## Documentation
Compiled documentation is unavailable.
```

## 2. Keep exact Member publication within its latency budget

> Goal: Measure command input, exact subject and declaration resolution,
> document composition, presentation, and standard-output publication
> together.

```bash
set -e -o pipefail
for iteration in 1 2 3 4 5; do
  "$INSPECT" member Newtonsoft.Json.JsonConvert SerializeObject:1 \
    --library "$SCENARIO_ASSEMBLY" > /dev/null
done
```

```perf
max_ms: 2500
```

## 3. Bound warmed exact-command allocation

> Goal: Measure managed allocation from public command construction and
> parsing through exact Member inspection, document composition, presentation,
> and output. The probe warms process-static state before sampling; this is a
> recurring regression budget, not a retained-memory claim.

The probe writes command output to inherited standard output and metrics to
standard error. Redirecting standard output to `/dev/null` preserves the real
CLI sink while keeping metrics machine-readable.

```bash
set -e -o pipefail
metrics="$MEMBER_PUBLICATION_WORK/allocation.tsv"
"$ALLOCATION_PROBE" \
  "$SCENARIO_ASSEMBLY" \
  Newtonsoft.Json.JsonConvert \
  SerializeObject:1 \
  --samples 20 \
  > /dev/null \
  2> "$metrics"
cat "$metrics"
awk -F '\t' '
  $1 == "member" {
    median = $3
    p95 = $4
  }
  END {
    if (median > 0 && median <= 75000000
        && p95 >= median && p95 <= 75000000)
      print "allocation-budget-ok"
  }
' "$metrics"
```

```expect
identity
scenario
median_allocated_b
p95_allocated_b
member
allocation-budget-ok
```
