---
id: library-performance-triage
description: Static artifact triage for leveraged methods with performance opportunity signals
commands: [library, member]
areas: [performance, calls, decompiler, source, il]
---

# Performance: Library Method Triage

> Rank performance-review candidates from shipped artifacts. This workflow
> produces evidence-backed hypotheses, not benchmark conclusions.

The goal is to find methods worth human performance review by combining two
signals:

- **Leverage**: public exposure, inbound callers, and central call-graph
  position.
- **Opportunity**: static evidence such as allocation, boxing, copying,
  reflection, async/iterator state machines, string construction, locks, or
  loops in a high-leverage path.

Always report the artifact boundary: package or platform library, version, TFM,
assembly, and any caller corpus scanned with `--bin`, `--project`, or
`--caller-package`. Without a caller corpus, inbound arcs are limited to the
selected assembly.

For local product binaries, adapt the same flow to a file boundary and keep the
caller corpus explicit:

```usage
dotnet-inspect library path/to/Product.dll -v:q
dotnet-inspect member TypeName --library path/to/Product.dll --all -m Method \
  -S "Callers,Calls,Facts" --bin path/to/output-dir --rows -n 20
```

## 1. Establish the artifact boundary

> Goal: name the exact platform/package/library artifact before interpreting any
> performance signal.

```prompt
What artifact am I inspecting for System.Text.Json performance triage?
```

```bash
dotnet-inspect library System.Text.Json -v:q
```

```expect
System.Text.Json.dll
Source: Platform
```

```query
grep -E 'System.Text.Json.dll|Source: Platform'
```

## 2. Find candidate methods

> Goal: start from a type/member family and get stable selectors for the methods
> to rank or drill into.

```prompt
Which JsonSerializer WriteString overloads are available for performance triage?
```

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json --all -m WriteString \
  -S "Member Index" --columns "Selector;Canonical Signature" --tsv -n 5
```

```expect
WriteString
M:System.Text.Json.JsonSerializer.WriteString
```

```query
grep 'WriteString'
```

Use `-m Name` for discovery. When drilling into a stable selector such as
`Method~digest`, pass the selector positionally and omit `-m`; the filter and
the selector are separate ways to choose the method.

## 3. Measure leverage from inbound callers

> Goal: identify whether a candidate is reached from public or otherwise
> important call sites.

```prompt
Which methods in System.Text.Json call JsonSerializer.WriteString?
```

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json --all WriteString \
  -S Callers --rows -n 8
```

```expect
## Callers
JsonSerializer.Serialize
```

```query
grep -c 'JsonSerializer.Serialize'
```

For cross-assembly leverage, repeat the same selected member query with a caller
corpus:

```bash
dotnet-inspect member TypeName --library MyLib.dll Method~digest -S Callers \
  --bin ./artifacts/bin/App/release --rows -n 20
```

## 4. Inspect outbound cost shape

> Goal: separate a high-leverage wrapper from the lower-level work it delegates
> to.

```prompt
What lower-level calls does JsonSerializer.WriteString delegate to?
```

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json --all WriteString \
  -S Calls --rows -n 12
```

```expect
RentWriterAndBuffer
Serialize
TranscodeHelper
ReturnWriterAndBuffer
```

```query
grep -E 'RentWriterAndBuffer|ReturnWriterAndBuffer|TranscodeHelper'
```

Interpretation: this is a leveraged method, but the candidate cost is mostly in
buffer rental, serialization, transcoding, and return paths. The next drill-in is
one of those callees, not a claim that `WriteString` itself is slow.

## 5. Capture allocation evidence

> Goal: cite exact source and IL evidence for an allocation or allocation-like
> path before writing a performance-review hypothesis.

```prompt
Show source and IL evidence for allocation in JsonEncodedText.Encode.
```

```bash
dotnet-inspect member JsonEncodedText --platform System.Text.Json Encode:1 \
  -S "Decompiled Source,Calls,IL" --rows -n 100
```

```expect
new JsonEncodedText(Array.Empty<byte>())
newobj
System.Array.Empty<byte>()
```

```query
grep -E 'newobj|Array.Empty|new JsonEncodedText'
```

Interpretation: the empty-input path constructs a `JsonEncodedText` value over
`Array.Empty<byte>()`. That is evidence of construction and an empty-array cache
use, not proof of a harmful allocation. A useful report says which path allocates
or constructs, how often the method is reached, and what measurement would
confirm or falsify the concern.

## Report template

Use this compact shape for handoffs and PR-review notes:

| Field | Required content |
| --- | --- |
| Artifact boundary | package/platform/library, version, TFM, assembly, caller corpus |
| Leverage | caller count or named high-value callers, with command receipts |
| Opportunity | allocation/copy/reflection/loop/async/etc. evidence with source or IL |
| Confidence | high/medium/low, based on whether the static evidence is on a likely path |
| Falsifier | benchmark/profile evidence, setup-only path, cache reuse, or no hot callers |
| Next proof | benchmark, trace, source audit, or narrower caller-corpus query |

Do not turn static evidence into a benchmark claim. The strongest conclusion is:
this method is worth measuring because artifact queries show both leverage and a
specific cost shape.
