# ArrayPool ownership retirement report

**Verdict:** the generic lifecycle and Research ownership paths are ready to replace the ArrayPool-specific implementations for the measured scope.

## Baselines

- Fully legacy product: `0.25.0+473d56a` (`473d56a68e26338fc27aca9808c1e98dbf30b259`).
- Shipped continuity product: `0.26.0+d236a7a` (`d236a7ad79bf4d76c63f0530cedfab0ede3cf3dd`).
- Generic comparison head: `d2ac4f244409aff7316c716afd5460f2a442ab6d`.
- At the comparison head, the in-tree `LeakTriage`,
  `ArrayPoolOwnershipFlow`, and `ArrayPoolOwnershipPathFindings` oracle files
  were byte-for-byte unchanged from the v0.26.0 source tag. The producer
  refused to run if that condition was false. Those oracle implementations
  were retired after this comparison reached zero classified defects.

## Method

- Public product continuity compares sorted `Resource Triage` JSONL from v0.25.0, v0.26.0, and the current CLI.
- Lifecycle compares typed whole-library result states first, then complete `ResourceTriageAssessment` values when both engines complete.
- Research compares acquisition roots, physical forwarding coordinates, terminal outcome and sink identity, operation limits, Finding identity, and path-local completeness over one shared call-graph projection.
- The JSONL ledger beside this report is the complete machine-readable classification. No display text is used as a typed comparison authority.

## Product boundary

The current public CLI succeeds on the framework input and emits the two findings below. It fails closed with explicit incomplete-analysis diagnostics on 9 of 12 comparison inputs: `fixture:arraypool-lookalikes`, `nuget:MessagePack@2.5.192`, `nuget:MimeKit@4.8.0`, `nuget:Npgsql@8.0.4`, `nuget:Pipelines.Sockets.Unofficial@2.2.8`, `nuget:prometheus-net@8.2.1`, `nuget:System.Text.Json@10.0.12`, `nuget:TouchSocket@3.1.5`, `nuget:ZLinq@1.4.9`.

The typed lifecycle and Research comparisons resolve the selected assemblies in process and therefore complete on those inputs. They certify replacement semantics for the measured engines; they do not establish that the public CLI can complete those same library inspections.

## Positive framework findings

The current generic lifecycle product reports these untrusted-input ArrayPool exception-path leaks:

- `System.IO.Compression.ZipLocalFileHeader.GetExtraFields` acquires at `IL_003B` before `System.IO.Stream::ReadExactly` at `IL_008A`.
- `System.IO.Compression.ZipArchive.ReadCentralDirectoryAsync` acquires at `IL_0011` before `System.IO.Stream::ReadAtLeastAsync` at `IL_0040`.

## Observed differences

- Lifecycle preserves every comparable legacy assessment and adds 77 owner-specified positive assessments. Finding ordinals are excluded because they are stream metadata; identity, payload, coordinates, boundaries, policy fields, descriptor, and detail remain exact.
- Research adds 145 focus roots, 166 acquisition coordinates, and 35 typed terminal paths that the narrower legacy producer omitted.
- v0.25.0 to v0.26.0 records 10 already-shipped public JSONL, diagnostic, or exit-status changes and 3 corresponding typed fail-closed transitions. Every v0.26.0-to-current comparison is exact.

## Input provenance

| Input | Selected asset | Assembly SHA-256 | Package SHA-256 |
| --- | --- | --- | --- |
| `fixture:arraypool-lookalikes` | `artifacts/bin/ILInspector.Analysis.LookalikeFixtures/release/ILInspector.Analysis.LookalikeFixtures.dll` | `827a707da5d7284f57c3ed041896fd09e6ec44569f4d5fb3ba0625d598aac1a7` | n/a |
| `fixture:ownership-flow` | `artifacts/bin/ILInspector.Analysis.OwnershipFlowFixtures/release/ILInspector.Analysis.OwnershipFlowFixtures.dll` | `882e85e2211395d9b8133bebad52ab21e2778dc6d737b9a13ae16171530e307f` | n/a |
| `nuget:MessagePack@2.5.192` | `lib/net6.0/MessagePack.dll` | `b2c1cc3fc4c262a0f7cfa14f668afc61c1f8b8b460b51d894d6331b63acc14b2` | `33d41410021e4a84a03b7c215643a8bb417c91b282349ec71c0bd3660cad90f5` |
| `nuget:MimeKit@4.8.0` | `lib/net8.0/MimeKit.dll` | `34ecbae9877337e3bcd67038ef22c1548594239475a974f424ce1edaf6046916` | `e04079e24b415eeab9411203f62f04ec5cd4ed8653138c301fedb2afb8af8bf4` |
| `nuget:Npgsql@8.0.4` | `lib/net8.0/Npgsql.dll` | `1323d6e67a66323309096c93eb874e0f6ec2154152c9924a5f2d3a25d0370d33` | `3b73ac4a9f870635650437ace6688c9822e31ac2e1d98f5f5dd72dd7bbed45d2` |
| `nuget:Pipelines.Sockets.Unofficial@2.2.8` | `lib/net5.0/Pipelines.Sockets.Unofficial.dll` | `fa2cdb1d5ffbb2b06512c92ce8bd18918a1a996171d0a72dfc73035bc586a711` | `e302a5830335e0893a5b207f3010cf2a17f1f461de643d91db438bc91cf6b05d` |
| `nuget:QuanTAlib@0.1.0` | `lib/net8.0/QuanTAlib.dll` | `ea2067b8fbc1420a37ceb798ee65bb0b3d977cf5b2ce5b7d98d31c9c0e674be1` | `472315cc981c0b97b812d5a08db54424ae620c08d0fe1f865949afe11339394c` |
| `nuget:System.Text.Json@10.0.12` | `lib/net10.0/System.Text.Json.dll` | `65b370a1f35da8a016bd4f79ac6b567599d969bf2275a820dfa26a66ac9f8eea` | `29da5e0ec03835a6af50af533d15d8070530626d2e2ee4f1ef1dd799e94a4207` |
| `nuget:TouchSocket@3.1.5` | `lib/net9.0/TouchSocket.dll` | `a36509cd0b6b029c4551d5f90dc42bf39b8872e88724f2585592b87f996db186` | `80a110e854ecf3685032bca7d748c6ff3479c6043c95ae68a7b3c60612e4aa9f` |
| `nuget:ZLinq@1.4.9` | `lib/net9.0/ZLinq.dll` | `353c3e4ee91a8f970beb52617e48b99d0a220dd603968d4d4fe26b29a07a53dd` | `9177ebad27e3687286cf3bdb216865ba97956908ed2159ebb7561a43e55cf150` |
| `nuget:prometheus-net@8.2.1` | `lib/net7.0/Prometheus.NetStandard.dll` | `87f007a4e95ded1e9a8b0b0cd0dca43e89822c926b8d93c15ec69a3e520f3620` | `3711de5dde1fc3073830cb13ea6adf5fcd5cce31e7ab618c4ad59369b671303c` |
| `platform:Microsoft.NETCore.App@11.0.0-rc.1.26425.128/System.IO.Compression` | `shared/Microsoft.NETCore.App/11.0.0-rc.1.26425.128/System.IO.Compression.dll` | `e27247f13b3deb4fa30f732cc30ac4dfba9a5e91751f38f1495b255af9a84600` | n/a |

## Population

| Input | Lifecycle findings | Research roots | Research paths | Intentional improvements | Defects |
| --- | ---: | ---: | ---: | ---: | ---: |
| `fixture:arraypool-lookalikes` | 0 | 1 | 1 | 2 | 0 |
| `fixture:ownership-flow` | 27 | 48 | 43 | 93 | 0 |
| `nuget:MessagePack@2.5.192` | 4 | 9 | 8 | 38 | 0 |
| `nuget:MimeKit@4.8.0` | 4 | 19 | 13 | 74 | 0 |
| `nuget:Npgsql@8.0.4` | 8 | 19 | 2 | 54 | 0 |
| `nuget:Pipelines.Sockets.Unofficial@2.2.8` | 5 | 8 | 3 | 22 | 0 |
| `nuget:QuanTAlib@0.1.0` | 0 | 0 | 0 | 0 | 0 |
| `nuget:System.Text.Json@10.0.12` | 69 | 84 | 27 | 275 | 0 |
| `nuget:TouchSocket@3.1.5` | 0 | 1 | 0 | 2 | 0 |
| `nuget:ZLinq@1.4.9` | 3 | 12 | 8 | 42 | 0 |
| `nuget:prometheus-net@8.2.1` | 0 | 20 | 10 | 67 | 0 |
| `platform:Microsoft.NETCore.App@11.0.0-rc.1.26425.128/System.IO.Compression` | 7 | 15 | 13 | 59 | 0 |

## Classification

The ledger contains 1556 rows: 815 `Parity`, 728 `IntentionalImprovement`, 13 `AcceptedCompatibilityChange`, and 0 `Defect`.

`IntentionalImprovement` is limited to owner-specified generic behavior: additional typed Resource Occurrence roots and root-local lifecycle outcomes, typed resource-aware Finding identity, and path-local and operation-level completeness. An omitted legacy acquisition or assessment, changed shared coordinate or sink, or other unowned difference is a `Defect`.

## Historical provenance

The report and adjacent JSONL ledger were generated at comparison head
`d2ac4f244409aff7316c716afd5460f2a442ab6d` with the package, framework,
fixture, and tool hashes recorded above. The comparison producer and both
ArrayPool-specific semantic implementations were then removed as the final
retirement step. The ledger remains the durable review artifact; reproducing
the retired head-to-head requires checking out that exact historical commit
and using its pinned corpus preparation and producer sources.
