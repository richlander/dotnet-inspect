# Library enablements

## Status

Focused design for a small closed vocabulary of Library enablements: binary
facts that tell a consumer, with certainty, that a Library was built to
participate in a modern .NET capability. Library document adoption is owned by
[Library inspection documents](library-inspection-document.md#library-facts).
Chip and badge presentation belong to each host.

## Authority and exact claim

`ILInspector.Metadata` owns this claim:

> Given one admitted managed image, report every named enablement as exactly
> one of Enabled, Not enabled, or Unavailable with a typed reason, using only
> evidence carried by that image's metadata.

An enablement is certain because it reads what the compiler or SDK wrote into
the binary. It never infers intent from names, dependencies, target framework,
package metadata, or source.

This owner composes existing contracts without redefining them:

- [Memory-safety models and evidence](memory-safety-models.md) owns module
  `MemorySafetyRulesAttribute` decoding and the normalized
  `MemorySafetyRulesResult` states.
- [Library inspection documents](library-inspection-document.md) owns when
  enablements are requested, carried, and returned.
- [Untrusted data threat model](untrusted-data-threat-model.md) owns
  containment for attribute text read from internet-origin images.

## Product question

A consumer choosing a dependency for a Native AOT tool, a runtime-async
application, or a memory-safety-v2 codebase asks:

> Was this exact Library built for that capability?

Today the answer is scattered across Signals rows with raw evidence
(`IsAotCompatible`, `Async Kind`, `Memory safety model`). Enablements answer the
question directly, once per Library, and give every host the same stable
identity for chips now and visual badges later.

Real scenarios:

- `ModelContextProtocol.Core` 2.2.0 and `System.Text.Json` carry
  `AssemblyMetadata("IsAotCompatible", "True")`; `NuGet.Versioning`, shipped
  inside `dotnet-inspect.any` 0.26.0, does not.
- `System.Net.Sockets.dll` in
  `Microsoft.NETCore.App.Runtime.linux-x64@11.0.0-rc.1.26425.128` is compiled
  with runtime async: 9 MethodDef rows carry the `Async` flag, only one of
  them public. `System.Net.Http.dll` from the same pack has 119 such rows (29
  public) alongside a pooling-builder state machine; it is still runtime-async
  enabled. `System.Text.Json.dll` and `System.IO.Pipelines.dll` from the same
  pack have none.
- No nuget.org Library observed on 2026-09-25 carries
  `[module: MemorySafetyRulesAttribute(2)]`. Memory Safety v2 is grounded in
  repository fixtures built with `<MemorySafetyRules>updated</MemorySafetyRules>`
  until a real package ships. That synthetic-only grounding requires operator
  approval before the implementation slice lands.

## Vocabulary

The vocabulary is closed. Each enablement has a stable identifier, which
structured output, badges, and search key on, and a short host-neutral label.

| Identifier | Label | Evidence |
| --- | --- | --- |
| `aot-compatible` | AOT | assembly `AssemblyMetadataAttribute("IsAotCompatible", value)` |
| `runtime-async` | Runtime Async | MethodDef rows whose implementation flags carry `Async` (`0x2000`) |
| `memory-safety-v2` | Memory Safety v2 | module `MemorySafetyRulesResult` |

Adding an enablement requires the same certainty: a single binary marker that
the build writes deliberately and that this owner can decode without analysis.
Aggregations such as "AI ready" are presentations over enablements, not
enablements.

## States

Every requested enablement has one state:

- **Enabled** — the image carries the evidence that the capability was built
  in.
- **Not enabled** — the image was read completely and does not carry that
  evidence.
- **Unavailable** — the evidence cannot be read, cannot support a unique
  judgment, or is absent by construction for this kind of image. The reason is
  typed and preserved; it never collapses to Not enabled.

### Reference assemblies

A reference assembly, identified by the assembly-level
`ReferenceAssemblyAttribute`, keeps declarations and strips implementation
details. The declarations enablements read survive: `IsAotCompatible` is
present in `Microsoft.NETCore.App.Ref` 11.0.0-rc.1.26425.128, and the
reference assembly the SDK produces for the memory-safety-updated fixture still
carries `MemorySafetyRulesAttribute(2)`. AOT and Memory Safety v2 are therefore
decided normally for reference assemblies.

Method implementation flags do not survive. `System.Net.Sockets.dll` and
`System.Net.Http.dll` in that reference pack carry no `Async` MethodDef rows,
while the same Libraries in the runtime pack carry 9 and 119. A reference
assembly reports Runtime Async as Unavailable with the reason
`ReferenceAssembly`, never Not enabled.

### AOT

`AssemblyMetadataAttribute` rows on the assembly with key `IsAotCompatible`
decide the state:

- Enabled when at least one row exists and every row's value is `true`.
- Not enabled when no row exists, or every row's value is `false`.
- Unavailable when any value is neither, decoded values disagree, or any
  assembly `AssemblyMetadataAttribute` blob cannot be decoded, because the
  undecoded row might carry the key.

Values compare case-insensitively after trimming surrounding whitespace, the
grammar MSBuild's `True` and `False` spellings satisfy. `AssemblyMetadata` on
modules, Types, or members is not evidence.

The marker is the author's MSBuild declaration, which enables the trim, AOT,
and single-file analyzers at build. Enabled reports that declaration; it does
not claim the Library was exercised under Native AOT.

### Runtime Async

Enabled when at least one MethodDef row carries the `Async` implementation
flag, regardless of accessibility, declaring Type, or whether the declaring
Type is compiler-generated. Otherwise Not enabled, except for reference
assemblies as described above.

Runtime-async compilation still emits state machines where the language
requires them, such as async iterators and methods using a custom async method
builder. Their presence does not reduce the state. A Library with no async
methods reports Not enabled; that is not a claim that it was compiled without
runtime async, only that the image carries no evidence of it.

The existing public-surface async classification is a different question and
does not decide this state. It skips non-public methods and compiler-generated
Types, which hold most runtime-async methods in real Libraries.

### Memory Safety v2

The normalized `MemorySafetyRulesResult` decides the state:

- Enabled for `Updated` (every decoded module marker is version `2`).
- Not enabled for `Legacy` (no module marker).
- Unavailable for `Unsupported`, `Malformed`, `Conflicting`, and metadata
  `Unavailable`, each carried as the typed reason.

Memory Safety v2 enablement does not describe how much `unsafe` code the
Library contains or exposes. Those remain separate memory-safety facts.

## Source execution and cost

Enablements read assembly custom attributes, module custom attributes, and
MethodDef implementation flags. They do not read method bodies, construct a
`LibraryBodyIndex`, resolve references, open companion PDB or documentation
content, or use the network. The work is bounded by the image's existing
Metadata admission.

## Consumers

Every consumer reads enablements from this owner. The Signals audit rows for
`IsAotCompatible`, async kind, and memory-safety model keep their raw evidence
columns, but where they answer an enablement question they consume this
derivation. The current Signals AOT reading, which also scans module, Type, and
member attributes and lets the last value win, retires in the CLI adoption
slice.

## Evidence

Release gates in `ILInspector.Metadata` tests, using the real assets above:

- `System.Net.Sockets` and `System.Net.Http` from the pinned runtime pack are
  runtime-async Enabled; `System.Text.Json` from the same pack is Not enabled.
- `ModelContextProtocol.Core` 2.2.0 is AOT Enabled; `NuGet.Versioning` from
  `dotnet-inspect.any` 0.26.0 is AOT Not enabled.
- A memory-safety-updated fixture is Enabled; the legacy fixture is Not
  enabled.

Pathological fixtures:

- an image whose only runtime-async method is internal and declared on a
  compiler-generated Type is Enabled;
- a Library with no async methods is Not enabled;
- `System.Net.Sockets.dll` from `Microsoft.NETCore.App.Ref`
  11.0.0-rc.1.26425.128 is AOT Enabled and Runtime Async Unavailable with
  reason `ReferenceAssembly`, and the memory-safety fixture's SDK-produced
  reference assembly is Memory Safety v2 Enabled;
- an undecodable assembly `AssemblyMetadataAttribute` blob makes AOT
  Unavailable;
- `IsAotCompatible` values `True` and `False` together, and the value `yes`,
  are each Unavailable with distinct reasons;
- conflicting and unsupported memory-safety markers are Unavailable with the
  memory-safety owner's state preserved.

## Non-claims

This owner does not define:

- package, Platform, or multi-Library aggregation of enablements;
- choosing an implementation assembly to answer for a reference assembly;
- chip, badge, color, or icon presentation;
- whether an enablement is requested by default in any host;
- Native AOT execution, trimming, or warning-free build verification;
- the amount or location of unsafe code, P/Invoke, or runtime marshalling; or
- additional enablements beyond the three named here.
