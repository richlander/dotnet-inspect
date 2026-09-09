# Custom-attribute package corpus

This is evidence for
[D3](../../../docs/design/custom-attribute-value-decoding.md#pinned-package-fidelity-gate),
not another normative owner or the decompiler's method baseline.
`custom-attribute-d3.json` records two explicit `dotnet-inspect.any` snapshots:
eight managed assemblies from 0.14.0 and all 33 first-party managed assemblies
from 0.25.0. Each snapshot records its own producer, image identities, and
independent enum-width sources. The bundled Markout image is retained only as a
defining dependency.

## Producer association

### 0.14.0

The portable job in
[run 28294556178](https://github.com/richlander/dotnet-inspect/actions/runs/28294556178/job/83832135926)
checked out source commit
`8681f6eac3ff44b231925913c3e2b17c8be0ddd4`.
The release tag `v0.14.0` points elsewhere and is not used as the build identity.
The workflow at the actual build source is
[`release.yml`](https://github.com/richlander/dotnet-inspect/blob/8681f6eac3ff44b231925913c3e2b17c8be0ddd4/.github/workflows/release.yml).
It packages the checked-out source, uploads `package-any`, and publishes those
artifacts to NuGet and the GitHub release.

Relevant build-log excerpts, recorded on 2026-09-06:

```text
2026-06-27T16:10:12.4679910Z HEAD is now at 8681f6e Release 0.14.0: version bump and CLI usability fixes (#1690) (#1697)
2026-06-27T16:10:16.5699415Z dotnet-install: Installed version is 11.0.100-preview.5.26302.115
2026-06-27T16:11:00.8414980Z Run dotnet pack src/dotnet-inspect -c Release -r any -p:PublishAot=false -p:OfficialBuild=true
2026-06-27T16:11:03.0968435Z /usr/share/dotnet/sdk/11.0.100-preview.5.26302.115/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.RuntimeIdentifierInference.targets(383,5): message NETSDK1057: You are using a preview version of .NET.
2026-06-27T16:11:42.2984749Z Successfully created package '/home/runner/work/dotnet-inspect/dotnet-inspect/src/../artifacts/package/release/dotnet-inspect.any.0.14.0.nupkg'.
2026-06-27T16:11:44.0094600Z SHA256 digest of uploaded artifact is be5a1f6fb2ba338c8e1e56c4b64c0582756380c114bc79c856d6765ae45c83ec
2026-06-27T16:11:44.3553120Z Artifact package-any successfully finalized. Artifact ID 7926053949
```

The SDK targets path is evidence of the SDK used by the pack command, not
merely of an installed SDK. The original artifact's API record associates it
with the same run and source commit; it reported an expiration of
`2026-09-25T16:09:59Z`. Its downloaded archive matched the logged SHA-256.
The contained unsigned nupkg matched the
[GitHub release asset](https://github.com/richlander/dotnet-inspect/releases/tag/v0.14.0)
digest recorded in the JSON.

The NuGet archive has a different package digest and an additional
`.signature.p7s` entry. Every selected managed DLL matched byte-for-byte between
the original build artifact and NuGet; those per-image hashes are in the JSON.
The gate downloads through `NuGetClient` and verifies both the NuGet package
hash and each selected image hash before inspection.
This is recorded CI provenance, not a signed toolchain attestation.

### 0.25.0

The 0.25.0 package records source commit
`473d56a68e26338fc27aca9808c1e98dbf30b259` in its nuspec. That commit has
successful [main CI](https://github.com/richlander/dotnet-inspect/actions/runs/34135265105)
and [Deep Inspect](https://github.com/richlander/dotnet-inspect/actions/runs/34135337437)
runs. The later
[publish run](https://github.com/richlander/dotnet-inspect/actions/runs/34143458645)
was dispatched from `84ac15def955786eb4363db7cfa4e11c3fe76ce6`, but its
resolve job explicitly selected the certified `473d56a6` commit and every build
job checked out that resolved source.

The portable
[build job](https://github.com/richlander/dotnet-inspect/actions/runs/34143458645/job/101812529378)
used SDK `11.0.100-preview.7.26381.103` to produce `package-any`. The retained
artifact has ID `10026874093`, SHA-256
`0524bb6515edcad9d03489824bf2a2225bce66296eb27a8e972e73b41a01c728`,
and reported expiration `2026-12-06T16:28:56Z`. Its unsigned nupkg has SHA-256
`fe760810b4efaeb1d6e7c497cf7c0b2157b7f1594479093d0c4d13b8e2e806ba`.
The signed NuGet nupkg has SHA-256
`a1d0f8e6b5ec9903904f31349d4e871e8ee683f4dbf8c9408b5aadccbb96d00c`;
its additional archive entry is `.signature.p7s`. All 33 selected first-party
DLLs are byte-identical between the retained artifact and NuGet, and their
hashes are recorded in the JSON.

There is no `v0.25.0` GitHub release or tag, and neither is used as provenance.
NuGet reports publication at 16:36 UTC on 2026-09-07, while the recorded
workflow's upload attempts at 16:38 received duplicate conflicts. No adjacent
GitHub Actions run identifies the first uploader. The record therefore does not
claim who performed that upload; it associates the selected NuGet bytes with
the identical retained bytes produced from the exact source and SDK above.

## Independent enum evidence

The 0.14.0 and 0.25.0 snapshots use twelve and eighteen source-declared `Int32`
enum widths, respectively. Each entry names the definition separately from its
serialized input spellings and links the exact source revision. These
declarations are inputs to the test-owned SRM provider; the provider never asks
the product for an enum width.

- Package-owned decompiler enums use their snapshot's build-source commit. The
  0.25.0 snapshot adds `Forward`, `NameProvenance`, and `Oracle` from the
  `InverseArchitecture` namespace.
- Markout 0.14.0 and 0.36.0 use their nuspec-recorded commits
  `2d0f2c4bbdb2539b2ddd3d79e966da117ab93773` and
  `1cf1a84ebbfb5bf46b600695fe856031ff575a79`, respectively.
- Framework declarations use dotnet/runtime's `v10.0.0` source commit
  `60629d14374c56f1cb51819049ad1fa529307f8d`. The second snapshot adds
  `DllImportSearchPath`, `JsonNumberHandling`, and
  `JsonUnmappedMemberHandling`.
  The defining images come from the running framework, are separately
  identified in each report, and must expose the declared widths. This is not
  a reconstruction of either original runtime.

The product resolver is built separately for each snapshot over its retained
images. Every planned definition must resolve and expose the source-declared
underlying type before that corpus walk. Unknown/defaulted widths fail this
corpus; there is no missing-image exception hidden in its success count.

Because both packages have only `Int32` enum cases, each corpus invocation also
runs the existing four source-owned retained-image `long`/`byte` cases once.
`D3FixtureProducerSdk` records their actual compilation SDK in the test assembly.
The report distinguishes that SDK from the historical package SDK. The
companions do not retroactively become output from the package's compiler.

## Execution

From the repository root, after SDK preflight:

```sh
DOTNET_INSPECT_D3_REPORT="$PWD/artifacts/custom-attribute-d3/result.json" \
dotnet run --project tests/ILInspector.Metadata.Tests -c Release -- \
  --filter-class ILInspector.Metadata.Tests.CustomAttributeCorpusTests \
  ILInspector.Metadata.Tests.CustomAttributeFidelityTests --no-progress
```

Use `--filter-not-trait "Speed=Slow"` to run the small controls without the
networked package sweep. Normal PR CI and the Windows PR workflow use that
selection. Deep Inspect's full metadata runs keep the sweep and upload its
JSON result separately for Windows, macOS, and Linux. The workflow passes the
report path rooted at `github.workspace`; the MTP host does not use the
repository root as its current directory.

The schema-version-2 report groups observations by snapshot and binds them to
the package/assembly hashes, decoder and harness builds, actual oracle
SRM/runtime, and retained defining images.
The input's `net10.0` directory does not pin the oracle.
`CustomAttributeCorpusTests.PinnedPackages_AllAttributeRowsEqualIndependentOracle`
enforces complete equality for all 189,142 selected attribute rows, with no
refusals, oracle failures, differences, or defaulted widths.
Diagnostic examples are limited to the first twenty failures per image;
failure totals and row accounting are not capped.

This is a finite two-point package/fixture evidence set, not all output from
either SDK, an SDK interval, all bundled dependencies, all NuGet packages, or
exhaustive grammar coverage. Expanding it requires another explicit
input/provenance entry and an independent oracle for any newly encountered enum
types.
