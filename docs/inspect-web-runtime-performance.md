# Inspect Web runtime performance evidence

This document owns the method used to compare Inspect Web runtime
configurations. The claim is deliberately narrow: the harness produces
reproducible, semantically validated latency and throughput observations for
one pinned browser workload. It does not define a performance threshold or
select a runtime by itself.

[#6077](https://github.com/richlander/dotnet-inspect/issues/6077) is the
end-to-end tracker. The original five production-host slices established the
harness, nightly public evidence, the .NET 12 CoreCLR deployment, the rejected
ReadyToRun trial, and the accepted post-recovery comparison. The remaining
three-slice adoption path is:

1. establish a controlled nightly cohort and classify the existing public
   comparison as a production synthetic;
2. advance the checked-in .NET 12 cohort deliberately as new daily builds are
   selected; and
3. aggregate accepted receipts before artifact expiry and define the runtime
   decision policy from the resulting longitudinal evidence.

The controlled workflow is the production host for the primary test
infrastructure. The deployed Inspect Web sites remain the product consumers
for the secondary production synthetic. Browser-only scope is intentional and
explicitly user-approved; this harness does not create a shared CLI performance
substrate.

## Evidence lanes

The controlled cohort is the primary runtime comparison. One workflow run
builds all variants from one source commit and one frontend artifact, admits
them through a deterministic production-Worker package operation, and measures
the admitted artifacts over loopback on one fresh runner. It excludes TLS,
CDN, deployment skew, and permanent-site lifecycle from the runtime result.

The public nightly is a production synthetic. It measures promoted Mono and
CoreCLR sites through their real deployment, TLS, and CDN paths. It remains
valuable operational evidence, but host and network effects mean it is not the
primary runtime dataset. A permanent public ReadyToRun site is not required.

## Comparison contract

A comparative report requires all of the following:

- every sample completed successfully;
- every site kept one product commit for the entire run;
- all compared sites reported the same product commit;
- every measured operation returned the same semantic fingerprint; and
- the report records the harness revision, host, browser, scenario, individual
  samples, host load before and after the run, and summary statistics.

A controlled report additionally requires:

- every variant was built from the receipt's source commit and shared frontend
  manifest;
- CoreCLR IL and ReadyToRun used one exact SDK, runtime, workload, and VMR
  cohort;
- each measured variant passed the focused production-Worker package
  operation; and
- the report and trend point are hash-bound to the accepted cohort receipt.

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
| Framework bytes | Playwright network accounting for page- and Worker-initiated `/_framework/` requests through readiness | Encoded response-body and response-header transfer evidence associated with startup |
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
construct managed evidence. It opts into a narrow browser benchmark bridge
over the site's existing production `EngineClient`, so startup and every
measured operation use the same long-lived Worker runtime and generated
product-operation path as the deployed application. The pinned comparison
operation remains part of this matched workload even when it has no current UI
affordance. Ordinary site loads do not install the bridge.

Window Resource Timing does not include the dedicated Worker's framework
requests in Firefox. The harness therefore records framework transfers from
Playwright's page-level network events, which include requests initiated by
the page and its Worker. It reports encoded response bytes; decoded response
bytes are unavailable at that boundary and remain `null` in the raw report.
Observation starts before navigation. At managed readiness, the harness stops
accepting new framework requests and waits within the same startup deadline for
every request already observed to finish or fail; a failed or stalled request
rejects the sample visibly instead of producing partial byte accounting.

Promoted run `34545510641` established the migration failure that this boundary
replaces: all ten samples timed out waiting for uninitialized main-thread
generated facades, before build identity or timing evidence. The retained host
load was modest, so the run was rejected as a deterministic harness defect and
produced no trend point.

## Running the harness

Install the existing Inspect Web toolchain, including Firefox:

```bash
cd inspect-web
npm ci
npx playwright install firefox
```

Run a matched-head comparison:

```bash
npm run benchmark:published -- \
  --site mono=https://dotnet-inspect.net \
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

## Controlled nightly cohort

[`.github/workflows/inspect-web-runtime-cohort-nightly.yml`](../.github/workflows/inspect-web-runtime-cohort-nightly.yml)
runs daily at 00:47 UTC and is manually dispatchable. It builds these variants
from one exact source commit:

| Variant | Runtime configuration | Admission role |
| --- | --- | --- |
| `mono` | .NET 11 Mono, compiler async, no ReadyToRun | Required control |
| `coreclr-il` | Pinned .NET 12 CoreCLR, runtime async, IL | Required comparison |
| `coreclr-r2r` | Same .NET 12 cohort, runtime async, non-composite per-assembly ReadyToRun | Experimental candidate |

The frontend is built once and its manifest digest must match every variant
receipt. Build jobs may run in parallel because the receipts bind source,
frontend, runtime, workload, configuration, async-lowering evidence, and the
published-site file manifest. Timing still occurs sequentially on one fresh
runner after all artifacts are downloaded.

Admission uses the same focused package-adoption scenario as CoreCLR
deployment. Mono or CoreCLR IL rejection fails the cohort. A ReadyToRun
candidate may be recorded as a correctness rejection only when the browser
reached the product operation and emitted the retained
`INSPECT_WEB_PRODUCT_OPERATION_FAILURE:` marker.
The current fatal thunk text additionally links the rejection to
dotnet/runtime#129622 and dotnet/runtime#129857. Missing artifacts, fixture
failures, browser startup failures, or any other unevaluated candidate state
fail the cohort rather than being mislabeled as product evidence.

Only admitted variants are served on loopback and passed to the existing
benchmark. The current expected outcome is therefore a valid two-way
Mono/CoreCLR IL trend plus an explicit ReadyToRun correctness rejection. If a
future cohort admits ReadyToRun, the same run automatically becomes a
three-way comparison. No trend point claims timing for a rejected candidate.

The retained evidence includes the three publication receipts and file
manifests, admission logs and browser traces, the cohort receipt, raw benchmark
report, trend point, and a final receipt binding their SHA-256 digests. Build
artifacts exist only long enough to reach the measurement job; evidence is
retained for 90 days. The longitudinal aggregation slice must preserve accepted
and rejected receipts before the first controlled artifacts expire.

## Public production synthetic

[`.github/workflows/inspect-web-performance-nightly.yml`](../.github/workflows/inspect-web-performance-nightly.yml)
runs daily at 02:17 UTC and is also manually dispatchable. It measures Mono
and CoreCLR in one job on one fresh `ubuntu-26.04` runner, with alternating
site order, five samples per site, and ten distinct member operations per
sample. Manual dispatch may change the sample and member counts for diagnostic
runs without changing the scheduled defaults.

The Mono control is the promoted production site at
`https://dotnet-inspect.net`, not the continuously deployed staging site at
`https://dotnet-inspect.ca`. Production Mono and the isolated CoreCLR site
advance from the same promotion SHA, while staging advances on every successful
`main` deployment and cannot provide a stable cross-site commit pair.

The report records the runner's raw one-, five-, and fifteen-minute load
averages before and after the browser work, along with values normalized by
logical processor count. These measurements expose obvious runner contention;
they do not correct timings or make a rejected result acceptable.

Every run uploads the benchmark log and any raw report for 90 days. An accepted
run additionally uploads a compact trend point containing the product commit,
harness revision, environment, configuration, medians, and the raw report's
SHA-256. The workflow summary presents the same medians for quick comparison.
The retained trend-point artifacts are production-synthetic input; they are
not a threshold or regression verdict.

A failed operation, missing or changing product identity, cross-site commit
mismatch, or semantic divergence produces no trend point and fails the
workflow after uploading the available evidence. The nightly workflow is
evidence collection, not a pull-request gate, and it never retries around the
product's deadline.

For a short diagnostic run while deployments intentionally differ:

```bash
npm run benchmark:published -- \
  --site mono=https://dotnet-inspect.net \
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

Each .NET 12 deployment must use one exact, coherent SDK and workload cohort.
A floating daily or a stable SDK combined with separately overridden runtime
packages is not comparable evidence.

The CoreCLR deployment pins the runtime-main cohort:

- SDK `12.0.100-alpha.1.26459.112`;
- runtime and browser workload packs `12.0.0-alpha.1.26459.112`;
- dotnet/dotnet VMR source commit
  `7792b064d8573a30d8527944de8184b7e108837e`; and
- workload feed
  `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet12/nuget/v3/index.json`.

This cohort's browser workload describes `wasm-tools` for `net11.0`, so the
Inspect Web project graph retains that target framework while executing on the
.NET 12 CoreCLR runtime. The workflow sets `PublishReadyToRun=false`
explicitly. Both workload installation and application publication restore use
the pinned daily feed plus NuGet.org. Publication uses package-source mapping so
the installed workload supplies `Microsoft.NET.Sdk.WebAssembly.Pack`, the daily
feed supplies SDK-selected linker, NativeAOT compiler, and runtime packages,
and NuGet.org supplies ordinary project dependencies. The same mapped
configuration governs both publication and runtime-async verification. Its
artifact carries `dotnet --info`, `dotnet workload list`, and a machine-readable
receipt that binds the SDK, runtime, workload manifest and packs, feeds, target
framework, runtime-async lowering, and non-ReadyToRun configuration. It also
records the pinned CoreCLR pack's native JavaScript and Wasm hashes, which must
equal the published runtime assets. The same receipt is verified before
artifact upload and again before deployment. Before upload, the focused
package-adoption canary opens a deterministic local package through the
published production Worker and `QueryPackage` operation. Build identity and
the async-lowering canary are not sufficient deployment evidence by
themselves.

The earlier `12.0.100-alpha.1.26454.116` non-ReadyToRun cohort produced the
accepted baseline run `34439612493`. The later cohort remains pinned after the
ReadyToRun trial so the rejected optimization is the only configuration
removed.

### Rejected ReadyToRun trial

Promotion run `34559349236` deployed product commit
`e7572e46d66a8dd064131c6fa66d4230a8405b98` with non-composite application
ReadyToRun. Crossgen2 produced 71 of 74 managed assets, including all eight
application assets and `System.Private.CoreLib`. The artifact passed build
identity, runtime-async, asset-identity, and publication-shape checks.

The first one-sample production preflight rejected that configuration before
the five-sample budget was spent. Mono completed the pinned workload, while
CoreCLR reached Worker readiness and then failed the first package query with:

```text
Fatal error.
Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.
```

The same commit and cohort succeeded locally with
`PublishReadyToRun=false` and reproduced the fatal error with
`PublishReadyToRun=true`. Making only
`DotnetInspect.Web.Interop.Package` IL-only did not change the failure. A direct
generated-facade invocation exposed mismatched
`WasmDelayLoadHelper`/`WasmR2RToInterpreterThunk` dispatch beginning in
`NuGetFetch.PackageSourceOperation.CaptureAsync<T>`; interpreting `NuGetFetch`
revealed another failing generic async dispatch in
`BrowserPackageWorkspace.RunPackageOperationAsync<T>`. Selective exclusions
therefore do not provide a viable application configuration.

This is the same CoreCLR-Wasm R2R thunk/signature-mismatch family tracked by
[dotnet/runtime#129622](https://github.com/dotnet/runtime/issues/129622) and
[dotnet/runtime#129857](https://github.com/dotnet/runtime/issues/129857), but
the pinned later cohort still reproduces the product failure. The public
CoreCLR comparison consequently uses the later .NET 12 cohort without
ReadyToRun. No R2R performance trend point exists because correctness is a
precondition for measurement.

Any future ReadyToRun publication must additionally record non-composite
per-assembly output, prove that published assets are the Crossgen2 Wasm images,
record compressed and uncompressed `/_framework/` size, and pass the same
focused production-Worker package operation before deployment. The retained
publication verifier can produce that evidence, but it does not make R2R a
supported deployment configuration.

The earlier runtime-main cohort had a Linux path-casing defect: Crossgen2 wrote
`R2R/` while the browser packaging target probed `r2r/`.
[dotnet/runtime#133203](https://github.com/dotnet/runtime/pull/133203) carries
the fix. The pinned ReadyToRun cohort contains the synchronized fix and does
not depend on the private `_WasmPublishR2RDir` workaround used during the
investigation.
