# Inspect Web runtime performance evidence

This document owns the method used to compare deployed Inspect Web runtime
configurations. The claim is deliberately narrow: the harness produces
reproducible, semantically validated latency and throughput observations for
one pinned browser workload. It does not define a performance threshold or
select a runtime by itself.

[#6077](https://github.com/richlander/dotnet-inspect/issues/6077) is the
end-to-end tracker. It sequences five production-host slices: establish this
harness, establish a nightly evidence lane, move the isolated CoreCLR
deployment to a pinned .NET 12 runtime-main cohort without ReadyToRun, enable
application ReadyToRun, and compare all three configurations before choosing
the lasting deployment shape. The public Mono .NET 11 deployment remains the
control throughout.

The deployed Inspect Web sites are the product consumers. The benchmark script
is the production host for this test infrastructure. Browser-only scope is
intentional and explicitly user-approved; this harness does not create a
shared CLI performance substrate.

## Comparison contract

A comparative report requires all of the following:

- every sample completed successfully;
- every site kept one product commit for the entire run;
- all compared sites reported the same product commit;
- every measured operation returned the same semantic fingerprint; and
- the report records the harness revision, host, browser, scenario, individual
  samples, host load before and after the run, and summary statistics.

The harness refuses a non-comparable result by default. The
`--allow-mismatched-commits` option exists only for diagnostic runs that prove
the harness itself or characterize an operational problem. It records the
override and reasons while keeping `comparison.comparable` false.

No threshold is selected before the first matched-head baseline. A later
runtime decision must preserve the raw report rather than copying only a
headline ratio.

## Pinned scenario

The initial scenario uses
`Microsoft.Extensions.Primitives@10.0.0` targeting `net10.0`. The coordinate
exercises package acquisition, metadata projection, IL analysis, decompilation,
and diff production. The current unsupported .NET 11 CoreCLR deployment can
exceed its 30-second browser package-operation deadline even for this
coordinate. That timeout is product behavior: the harness must preserve it and
reject the run rather than increase the deadline or publish partial timings.

The method-body comparison uses
`Microsoft.Extensions.Primitives.StringSegment.Trim` and `TrimStart`. The
member-throughput batch sorts concrete method-body coordinates by typed
assembly, type, selector, and metadata-token identity, then selects evenly
spaced entries across that stable ordering. A separate final entry warms the
analysis path without caching any timed member. The harness does not infer
identity from display text.

Each sample uses a fresh Firefox browser context with service workers blocked.
The browser process is reused because process launch is outside the website
contract. Site order alternates by sample to avoid assigning every earlier or
later observation to one runtime.

## Measurements

| Measurement | Boundary | Interpretation |
| --- | --- | --- |
| Startup latency | Navigation start through callable managed build identity | User-visible cold site startup, including asset transfer and runtime initialization |
| Framework bytes | Browser resource timing for `/_framework/` through readiness | Transfer evidence associated with startup; zero transfer sizes make the observation unsuitable for byte comparison |
| Cold package inspection | First exact package query in the fresh context | Network-sensitive end-to-end user latency |
| Warm package inspection | Immediate repeat of the exact query | Process-local package reuse plus repeated managed projection |
| Package-performance latency | First and second whole-package performance scans | Expensive first-use and warm managed analysis |
| Member-analysis throughput | Fixed count of distinct method analyses after one excluded warmup method | Sustained work over varied IL bodies, reported as operations per second and individual latencies |
| Method-comparison latency | Target preparation plus first and repeated exact comparison | Expensive Research/decompiler/IL-diff first-use and warm behavior |

Cold package acquisition is intentionally retained because it is a real user
experience, but it is not evidence of isolated runtime CPU performance.
Network conditions, NuGet service behavior, and CDN state can dominate it.
Warm analysis and method throughput are the primary runtime comparisons.

The harness reports median, mean, minimum, maximum, and nearest-rank p95. Three
samples are the default smoke-quality comparison; consequential runtime claims
should use at least five matched-head samples from the same host and browser.

## Semantic oracle

Timing a failed, partial, or different result is not performance evidence. Each
sample therefore validates and fingerprints:

- package, framework, assembly, type, and member counts;
- package-performance opportunity and analyzed-member counts;
- every selected member identity and its fact-category counts; and
- method-comparison completion, producer count, and C#/IL row counts.

All successful samples and sites must produce one fingerprint. A timeout or
other failure is retained in the report with its stage and message, makes the
report non-comparable, and causes a nonzero exit.

The harness does not repair product output, bypass product acquisition, or
construct managed evidence. It invokes the published product facades exactly
as the site does.

## Running the harness

Install the existing Inspect Web toolchain, including Firefox:

```bash
cd prototypes/inspect-web
npm ci
npx playwright install firefox
```

Run a matched-head comparison:

```bash
npm run benchmark:published -- \
  --site mono=https://dotnet-inspect.ca \
  --site coreclr=https://coreclr.dotnet-inspect.ca \
  --samples 5 \
  --member-count 10 \
  --output ../../artifacts/inspect-web-runtime-performance.json \
  --trend-output ../../artifacts/inspect-web-runtime-trend-point.json
```

If any sample reaches a product deadline, preserve the rejected report and wait
for the next controlled run or runtime cohort. Retrying interactively on a busy
machine does not turn a partial run into comparative evidence.

`--trend-output` writes a compact median summary only when the report is
comparative. Before each run the harness removes any existing file at that
path, so a rejected run cannot leave a stale trend point.

## Nightly evidence lane

[`.github/workflows/inspect-web-performance-nightly.yml`](../.github/workflows/inspect-web-performance-nightly.yml)
runs daily at 02:17 UTC and is also manually dispatchable. It measures Mono
and CoreCLR in one job on one fresh `ubuntu-26.04` runner, with alternating
site order, five samples per site, and ten distinct member operations per
sample. Manual dispatch may change the sample and member counts for diagnostic
runs without changing the scheduled defaults.

The report records the runner's raw one-, five-, and fifteen-minute load
averages before and after the browser work, along with values normalized by
logical processor count. These measurements expose obvious runner contention;
they do not correct timings or make a rejected result acceptable.

Every run uploads the benchmark log and any raw report for 90 days. An accepted
run additionally uploads a compact trend point containing the product commit,
harness revision, environment, configuration, medians, and the raw report's
SHA-256. The workflow summary presents the same medians for quick comparison.
The retained trend-point artifacts are the longitudinal input; they are not a
threshold or regression verdict.

A failed operation, missing or changing product identity, cross-site commit
mismatch, or semantic divergence produces no trend point and fails the
workflow after uploading the available evidence. The nightly workflow is
evidence collection, not a pull-request gate, and it never retries around the
product's deadline.

For a short diagnostic run while deployments intentionally differ:

```bash
npm run benchmark:published -- \
  --site mono=https://dotnet-inspect.ca \
  --site coreclr=https://coreclr.dotnet-inspect.ca \
  --samples 1 \
  --member-count 3 \
  --allow-mismatched-commits \
  --output ../../artifacts/inspect-web-runtime-performance-diagnostic.json
```

Reports belong under ignored `artifacts/` unless a focused design or pull
request intentionally records one as durable evidence.

## Preliminary diagnostic

A one-sample hand-run on 2026-09-05 established that successful executions are
discriminating:

| Measurement | Mono .NET 11 | CoreCLR .NET 11 |
| --- | ---: | ---: |
| Ten distinct member-facts operations | 28.7 s | 179.2 s |
| Method-body comparison | 14.7 s | 27.4 s |
| Method-body result | 35 rows | 35 rows |

These numbers are not a baseline because the sites reported different product
commits. They justify the harness shape only.

## Runtime migration evidence

The .NET 12 non-ReadyToRun and ReadyToRun deployments must use one exact,
coherent SDK and workload cohort. A floating daily or a stable SDK combined
with separately overridden runtime packages is not comparable evidence.

The non-ReadyToRun CoreCLR deployment pins the runtime-main cohort:

- SDK `12.0.100-alpha.1.26454.116`;
- runtime and browser workload packs `12.0.0-alpha.1.26454.116`; and
- workload feed
  `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet12/nuget/v3/index.json`.

This cohort's browser workload describes `wasm-tools` for `net11.0`, so the
Inspect Web project graph retains that target framework while executing on the
.NET 12 CoreCLR runtime. The workflow sets `PublishReadyToRun=false`
explicitly. Its artifact carries `dotnet --info`, `dotnet workload list`, and a
machine-readable receipt that binds the SDK, runtime, workload manifest and
packs, feeds, target framework, runtime-async lowering, and non-ReadyToRun
configuration. It also records the pinned CoreCLR pack's native JavaScript and
Wasm hashes, which must equal the published runtime assets. The same receipt is
verified before artifact upload and again before deployment.

ReadyToRun publication must additionally record:

- `PublishReadyToRun=true`;
- non-composite per-assembly output;
- proof that published application and framework assets are the Crossgen2 Wasm
  images;
- compressed and uncompressed `/_framework/` size; and
- the same runtime-async deployment and browser correctness gates used by the
  non-ReadyToRun CoreCLR deployment.

The current runtime-main daily has a Linux path-casing defect: Crossgen2 writes
`R2R/` while the browser packaging target probes `r2r/`.
[dotnet/runtime#133203](https://github.com/dotnet/runtime/pull/133203) carries
the fix. The deployment should select a daily containing that fix rather than
commit a dependency on the private `_WasmPublishR2RDir` workaround used during
the investigation.
