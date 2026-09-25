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
infrastructure. The deployed CoreCLR sites are nightly diagnostic consumers of
the same accepted cohort artifacts. Browser-only scope is intentional and
explicitly user-approved; this harness does not create a shared CLI performance
substrate.

## Evidence lanes

The controlled cohort is the primary runtime comparison. One workflow run
builds all variants from one source commit and one frontend artifact, admits
them through a deterministic production-Worker package operation, and measures
the admitted artifacts over loopback on one fresh runner. It excludes TLS,
CDN, deployment skew, and permanent-site lifecycle from the runtime result.

The public CoreCLR sites are collaboration surfaces, not comparative
performance evidence. They expose the nightly IL and ReadyToRun artifacts
through their real deployment, TLS, and CDN paths so a product operation can be
shared and reproduced. They deliberately advance independently of promoted
Mono. A matched-head public synthetic can still be run manually, but the
controlled cohort owns runtime comparisons.

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

The
[runtime-site deployment workflow](../.github/workflows/deploy-inspect-web-runtime-sites.yml)
runs automatically after each completed nightly release candidate. It
validates that exact candidate run and attempt, accepts either green evidence
or completed qualification concerns, and passes the candidate SHA and identity
to the reusable and manually dispatchable
[controlled-cohort workflow](../.github/workflows/inspect-web-runtime-cohort-nightly.yml),
which builds these variants from that source commit:

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

The CoreCLR build jobs move the repository development `global.json` out of
their ephemeral checkouts before invoking `dotnet`. This keeps the independently
validated .NET 12 cohort pin authoritative without weakening the repository's
.NET 11 SDK selection for ordinary development and CI.

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

### Nightly public runtime sites

The scheduled workflow uses the exact accepted cohort to prepare two complete
Azure Static Web Apps artifacts:

| Site | Variant | Deployment admission |
| --- | --- | --- |
| <https://coreclr.dotnet-inspect.ca> | `coreclr-il` | Admitted |
| <https://coreclr-r2r.dotnet-inspect.ca> | `coreclr-r2r` | Admitted, or the exact retained dotnet/runtime#129622 and #129857 rejection |

Both artifacts use the cohort's shared frontend and one managed API built from
the same source commit. Preparation revalidates the publication receipt,
frontend manifest, site manifest, async-lowering receipt, runtime identity, and
cohort membership before assembling the deployment. The complete artifact has
its own SHA-256 manifest and publishes `runtime-site.json` with the source,
runtime configuration, and admission result.

The environment-scoped deployment jobs download only those prepared artifacts
and revalidate their manifests and variant-specific admission status. An
unfamiliar ReadyToRun correctness failure, a failed required variant, missing
evidence, or any preparation or infrastructure failure leaves the prior public
site deployed. Publishing the recognized rejection is intentional: the R2R
site exists to make that exact product failure reproducible through shareable
URLs. Its failure details retain the originating managed/Wasm operation
diagnostic and stack ahead of any later cleanup failure. This does not make R2R
supported or eligible for timing.

### Daily runtime-pin advancement

[`inspect-web/runtime-cohort-pin.json`](../inspect-web/runtime-cohort-pin.json)
is the sole checked-in owner of the .NET 12 SDK, runtime/workload, feed, target
framework, and VMR identity consumed by both the controlled cohort and the
CoreCLR deployment. A daily version change edits that file only; workflow
structure and admission policy are separate changes.

[`.github/workflows/inspect-web-runtime-pin-proposal.yml`](../.github/workflows/inspect-web-runtime-pin-proposal.yml)
runs daily at 05:17 UTC and is manually dispatchable from `main`. It rejects
any other source ref before discovery. The workflow fetches
`productCommit-linux-x64.json` once from the pin's authoritative .NET 12 daily
URL and treats that immutable response as the candidate snapshot. Resolution
requires all published components to name one VMR commit, the SDK and runtime
daily suffixes to match, and both versions to advance monotonically. A current
or older coherent snapshot is a successful no-op. Invalid, split, or partially
advanced metadata fails the run.

For a newer snapshot, the workflow installs the exact SDK into an isolated
directory and installs `wasm-tools` without refreshing manifests. The installed
manifest version must equal the SDK version, its direct pack inventory must
equal the checked-in policy, and every selected pack must use the candidate
runtime version. The installed `Microsoft.NET.Sdk.WebAssembly.Pack` nuspec must
name the expected dotnet/dotnet repository and the candidate VMR commit. The
installed runtime list must then expose exactly the candidate runtime version.
Human-readable installer output is not an identity source.

The validated identity is passed as a complete override set to the reusable
controlled cohort. That run builds and admits Mono, CoreCLR IL, and CoreCLR
ReadyToRun from one product commit and performs the same sequential loopback
measurement as the scheduled cohort. Required-runtime failure, unknown
ReadyToRun failure, publication failure, or rejected comparative measurement
blocks advancement. The already-defined, explicitly evidenced ReadyToRun
correctness rejection remains non-blocking, so a daily runtime fix can change
the cohort automatically from two admitted variants to three.
The proposal job consumes the exact cohort receipt and requires ReadyToRun to
be admitted or to identify that retained issue; an unfamiliar marked product
failure remains observational cohort evidence but cannot advance the pin.

Only an accepted candidate cohort may create a version-specific
`automation/inspect-web-runtime-pin-*` branch. The proposal job asserts that
the branch changes only the shared pin, then comments on #6077 with the exact
identity, run evidence, and normal PR creation link. Repository Actions
credentials cannot create pull requests, so the workflow intentionally stops
at that branch-and-tracker boundary. The main ruleset still requires the usual
PR, CI, and review. One outstanding proposal branch blocks later discovery;
merge-time branch deletion or explicit rejection and deletion reopens the
lane. Automation never updates `main` directly.

## Manual public production diagnostic

[`.github/workflows/inspect-web-performance-nightly.yml`](../.github/workflows/inspect-web-performance-nightly.yml)
runs only by manual dispatch. It measures Mono and CoreCLR in one job on one
fresh `ubuntu-26.04` runner, with alternating site order. Dispatch may change
the default five samples and ten distinct member operations per sample.

The Mono control is the promoted production site at
`https://dotnet-inspect.net`, not the continuously deployed staging site at
`https://dotnet-inspect.ca`. Production Mono advances only through deliberate
promotion, while the CoreCLR sites advance nightly. The manual workflow
therefore produces comparative evidence only when the selected sites happen to
report the same product commit; otherwise use
`--allow-mismatched-commits` for an explicitly non-comparable diagnostic.

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
workflow after uploading the available evidence. The workflow never retries
around the product's deadline.

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

The shared runtime pin records the exact SDK, runtime and browser workload
version, dotnet/dotnet VMR source commit, and workload feeds. Workflows load
those values only after checking out the exact product source commit they will
build.

This cohort's browser workload describes `wasm-tools` for `net11.0`, so the
Inspect Web project graph retains that target framework while executing on the
.NET 12 CoreCLR runtime. The IL variant sets `PublishReadyToRun=false`; the R2R
variant sets `PublishReadyToRun=true` and
`PublishReadyToRunComposite=false`. Both workload installation and application
publication restore use
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
accepted baseline run `34439612493`. Daily advancement preserves the
non-ReadyToRun production configuration until ReadyToRun itself passes the
same product-operation admission.

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
the pinned later cohort still reproduces the product failure. The public IL
site consequently uses the later .NET 12 cohort without ReadyToRun. The
separate public R2R diagnostic site may publish only this exact recognized
rejection so contributors can reproduce it; no R2R performance trend point
exists because correctness is a precondition for measurement.

Any future ReadyToRun publication must additionally record non-composite
per-assembly output, prove that published assets are the Crossgen2 Wasm images,
record compressed and uncompressed `/_framework/` size, and pass the same
focused production-Worker package operation before deployment. The retained
publication verifier can produce that evidence, but a diagnostic deployment
does not make R2R a supported product configuration.

The earlier runtime-main cohort had a Linux path-casing defect: Crossgen2 wrote
`R2R/` while the browser packaging target probed `r2r/`.
[dotnet/runtime#133203](https://github.com/dotnet/runtime/pull/133203) carries
the fix. The pinned ReadyToRun cohort contains the synchronized fix and does
not depend on the private `_WasmPublishR2RDir` workaround used during the
investigation.
