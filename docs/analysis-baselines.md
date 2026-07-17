# Performance Analysis baselines

`dotnet-inspect` ships a lot of low-level analysis tools (metadata, IL, decompile, caller graph,
allocation triage, leak triage). **Performance Analysis is the one-stop shop**: run one view over a
library and see whether there is something interesting worth a closer look — without first knowing
which low-level tool to reach for. This doc is the internal baseline of *what each analysis type
finds* over a fixed corpus, plus an **effectiveness rating** so we know which lanes are worthy of
leading the one-stop-shop view. Everything is **static** (nothing is executed) and every row has a
re-runnable receipt.

## Corpus

44 assemblies, all resolved from NuGet / the shared framework:

- **Apps**: `Aspire.Dashboard` 9.0.0.
- **Serialization / wire**: Newtonsoft.Json, MessagePack, protobuf-net, Google.Protobuf, YamlDotNet,
  CsvHelper, MongoDB.Bson.
- **Networking / data**: Npgsql, StackExchange.Redis, RabbitMQ.Client, Grpc.Net.Client,
  Confluent.Kafka, Pipelines.Sockets.Unofficial, MongoDB.Driver, RestSharp, Flurl.Http, Refit,
  AWSSDK.Core.
- **Text / parsing**: MimeKit, Markdig, AngleSharp, HtmlAgilityPack, Humanizer, Microsoft.CodeAnalysis(.CSharp).
- **Infra / util**: Serilog, NLog, AutoMapper, MediatR, Dapper, FluentValidation, Polly, NodaTime,
  Bogus, SharpCompress, K4os.LZ4, SixLabors.ImageSharp, prometheus-net, ZLinq, CliWrap.

The leak/MemoryPool lanes were also baselined over the .NET 9.0.14 shared framework (308 asm).
Resource Triage also has a pinned ArrayPool-heavy community corpus: the primary library from each
of nine package roots, acquired through dotnet-inspect's package services by
`eng/prepare-resource-triage-corpus.cs`.

## The one-stop-shop funnel (Performance Triage, all 44 assemblies)

| Cut | Rows | What it is |
| --- | ---- | ---------- |
| Raw `Performance Triage` | 13,598 | every allocation/scan site — too much to read |
| Ranked (`--loop --min-confidence medium`) | 943 | in a hot loop, non-trivial confidence |
| Sharp (`--loop --min-confidence high`) | 318 | the "read these first" set |
| Algorithmic shapes (scan/linq/string-build in loop) | 155 | quadratic / hot-loop hotspots — the headline |

The takeaway: **the raw dump is noise; the value is the ranking.** Root-Reach + Loop + Confidence +
Post-Dominance columns turn 13.6k rows into a few hundred worth a look, and the algorithmic shapes
are the cheapest "found something interesting" wins.

## Effectiveness matrix

| # | Analysis type | Command | Volume (44 asm) | Precision | Actionability | Verdict |
| - | ------------- | ------- | --------------- | --------- | ------------- | ------- |
| 1 | Algorithmic scan-in-loop | `Performance Triage` (`scan-method-in-loop-call`, `linq-scan-in-loop`, `string-build-in-loop`, `allocation-hotspot`) | 155 | High | **Very high** (quadratic) | **Headline** |
| 2 | Leak-after-exception | `Resource Triage` (harness `--leak-actionability` for the full census) | 4 current framework / 9 historical confirmed | **Very high** | High (`try/finally`) | **Headline** |
| 3 | Delegate allocation | `Performance Triage` (`instance-method-group-delegate`, `capturing-delegate`) | 3,327 raw / 128 hot | High (real) / High in loops | High in loops | **Worthy w/ ranking** |
| 4 | Enumerator allocation | `Performance Triage` (`enumerator-allocation`) | 121 / 117 hot | High | Med–High | **Worthy** |
| 5 | Reach / leverage | `Top Leverage` | ranking | — | Impact multiplier for 1,3,4 | **Worthy as a lens** |
| 6 | Boxing | `Performance Triage` (`box-value-type`) | 2,478 / 136 hot | High | Low–Med (mostly cold `ThrowHelper`) | **Only when loop + reach** |
| 7 | Small-array | `Performance Triage` (`small-array`) | 7,466 / 446 hot | High | Low (often non-escaping / cold) | **Defer** |
| 8 | MemoryPool lifecycle | harness `--memorypool-lifecycle` | 8 sites / 0 leaks | High (0 false) | Low so far | **Census, not a bug-finder** |

## Per-type baselines

### 1. Algorithmic scan-in-loop — the headline allocation-lane shape

Flags a **linear scan invoked on every iteration of a caller's loop** — the quadratic shape. 155
across the corpus, concentrated in text/serialization/reflection-heavy libs: Humanizer (20),
Microsoft.CodeAnalysis.CSharp (14), Newtonsoft.Json (10), AWSSDK.Core (9), MimeKit / AngleSharp (8),
MongoDB.Bson / HtmlAgilityPack / Confluent.Kafka / Aspire.Dashboard (7). Low volume, very high
impact, clean "build an index / hoist the scan" fix.

```bash
dotnet-inspect library <asm> -S "Performance Triage" --triage-shape scan-method-in-loop-call,linq-scan-in-loop
```

Example (Aspire): `OtlpTrace.AddSpan` → `Enumerable.Any` invoked inside a loop by
`TelemetryRepository::AddTracesCore`; and the gist's `CalculateMaxDepth` (`Spans.Max(CalculateDepth)`
where each `CalculateDepth` step re-scans all spans) — the O(N·D·N) waterfall rebuild.

### 2. Leak-after-exception — highest-precision lane

Pooled-buffer churn on the exception path (see catalog issue #2572 and #2439).
`ResourceLifecycleAnalysis` records exact def-use-attributed `ArrayPool` boundaries, and
`ResourceTriageAnalysis` assesses whether they consume external input; `Resource Triage` exposes
the untrusted-actionable candidates.

- Historical broadened-package sensor (44 asm): 12 exception-path candidates →
  **5 untrusted-actionable, all confirmed**
  (MessagePack `ReadStringSlow`, MimeKit `Rfc2047.DecodePhrase`/`DecodeText`, Npgsql
  `TextConverter.GetChars`, Pipelines.Sockets `AsyncPipeStream.ReadByte`).
- Historical framework sensor (308 asm): 4 untrusted-actionable, all confirmed.
- **9/9 confirmed exception-path pool-retention defects**, IL-verified. Lowest volume,
  highest precision, clean `try/finally` fix.
- Current exact product contract (.NET 11 daily, 314 assemblies): 57 lifecycle
  observations → 4 untrusted-actionable (`MessagePackReader.ReadStringSlow`,
  `EncodingExtensions.GetString`, `TypeMapLazyDictionary.ConvertUtf8ToUtf16`,
  and `BinaryReader.FillBuffer`), 31 trusted, and 22 unknown.
- Current pinned community contract (9 assemblies): 19 lifecycle observations →
  3 untrusted-actionable (`MessagePackReader.ReadStringSlow`, Npgsql
  `TextConverter.GetChars`, and Pipelines.Sockets `AsyncPipeStream.ReadByte`)
  plus 11 trusted and 5 unknown. The Npgsql and Pipelines consumers are now
  reached through typed `Span<T>`/`Memory<T>` wrapper propagation. Eleven
  System.Text.Json rows identify trusted `Span<T>.Slice` work rather than the
  implicit conversion. MimeKit's two
  `TokenDecoder` constructor boundaries remain unknown because the later
  tokenizer calls are protected by cleanup.

### 3–4. Delegate & enumerator allocation

`instance-method-group-delegate` / `capturing-delegate`: a `Func`/closure allocated per call, hot when
in a loop or reached by many roots. `enumerator-allocation`: `foreach` over an interface-typed
sequence allocates a reference-type enumerator per pass. The two hand-found Aspire.Dashboard gist
finds reproduce on current `main`: `OtlpSpan.GetParentSpan()`/`GetChildSpans()` and
`ColorGenerator.GetColorIndex` all surface as `instance-method-group-delegate`. Worthy **with**
Root-Reach/Loop/Confidence ranking — the unfiltered list is dominated by cold straight-line delegates.

### 5. Reach / leverage — `Top Leverage`

Ranks members by `Callers`, `Root Reach`, `Fanout`, `Depth`, `Loop Calls`, `Visibility`, `Stable`. Not
a finding on its own — it is the **multiplier** that decides whether an allocation or scan matters (a
micro-alloc reached by 92 roots vs one on a cold path). It gates lanes 1, 3, 4.

### 6–7. Boxing & small-array — high volume, low actionability

`box-value-type` (2,478) and `small-array` (7,466) dominate the raw count but are mostly `low` weight:
boxing in cold `ThrowHelper`s post-dominated by `return`, or small non-escaping arrays. Only worth
surfacing when `--loop` + high Root-Reach. Defer small-array; gate boxing.

### 8. MemoryPool lifecycle — census

Every `MemoryPool<T>.Rent` → `IMemoryOwner<T>` site across framework + packages is
`ownership-transfer` (segments / pools / async state-machine fields): **0 leak candidates, 0 false
positives.** Precision-first census holds but has found no real leaks. Keep as a census / regression
guard; do not lead with it.

## Recommendation for the one-stop-shop view

Lead with high precision + high impact, gated by the reach lens:

1. **Leak-after-exception** and **algorithmic scan-in-loop** — headline finds; low volume, high
   confidence, clean fixes.
2. **Delegate / enumerator allocation in loops** — worthy with ranking.
3. **Top Leverage** — the impact lens that ranks 1–2.

De-emphasize small-array and cold boxing (surface only under loop + high reach), and treat MemoryPool
as a census.

Follow-ups: add a "confirmed vs candidate" verification step to the allocation lanes (as the leak lane
has); carry the caller-graph receipt into the algorithmic finding row; keep widening the app corpus.
