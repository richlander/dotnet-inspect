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
- No nuget.org implementation assembly observed on 2026-09-25 carries
  `[module: MemorySafetyRulesAttribute(2)]`. Nine reference assemblies in
  `Microsoft.NETCore.App.Ref` 11.0.0-rc.1.26425.128 do, which is real grounding
  for the reference-assembly rule below. Memory Safety v2 Enabled is grounded in
  repository fixtures built with `<MemorySafetyRules>updated</MemorySafetyRules>`
  until a real implementation ships. That synthetic-only grounding requires
  operator approval before the implementation slice lands.

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

Enablements describe how a Library's implementation was built. A reference
assembly, identified by the assembly-level `ReferenceAssemblyAttribute`,
describes a compile-time surface and does not reliably carry that evidence, so
every enablement reports Unavailable with the reason `ReferenceAssembly`.
This rule applies before the per-enablement rules below; reference assemblies
never report Enabled or Not enabled.

The real `Microsoft.NETCore.App.Ref` and `Microsoft.NETCore.App.Runtime`
11.0.0-rc.1.26425.128 packs show why one rule is needed:

- Implementation flags are stripped. `System.Net.Sockets.dll` and
  `System.Net.Http.dll` in the reference pack carry no `Async` MethodDef rows;
  the runtime pack carries 9 and 119.
- Surface markers can differ from the implementation. `System.Runtime`,
  `System.Memory`, `System.Security.Cryptography`, and six other reference
  assemblies carry `[module: MemorySafetyRulesAttribute(2)]` because their
  surface is annotated for v2 callers. No runtime-pack implementation carries
  it.

This owner judges the image it is given. When a Library carries both a
reference and an implementation assembly, the
[Library document](library-inspection-document.md#library-facts) asks about
the implementation, so the reference rule applies only when no implementation
is available.

### AOT

`AssemblyMetadataAttribute` rows on the assembly with key `IsAotCompatible`
decide the state:

- Enabled when at least one row exists and every row's value is `true`.
- Not enabled when no row exists, or every row's value is `false`.
- Unavailable with reason `UnrecognizedValue` when any value is neither,
  `ConflictingValues` when decoded values disagree, or `UndecodableMetadata`
  when any assembly `AssemblyMetadataAttribute` blob cannot be decoded,
  because the undecoded row might carry the key.

When several conditions hold, the reported reason is the first of
`UndecodableMetadata`, `UnrecognizedValue`, and `ConflictingValues`.

Values compare case-insensitively after trimming surrounding whitespace, the
grammar MSBuild's `True` and `False` spellings satisfy. `AssemblyMetadata` on
modules, Types, or members is not evidence.

The marker is the author's MSBuild declaration, which enables the trim, AOT,
and single-file analyzers at build. Enabled reports that declaration; it does
not claim the Library was exercised under Native AOT.

### Runtime Async

Enabled when at least one MethodDef row carries the `Async` implementation
flag, regardless of accessibility, declaring Type, or whether the declaring
Type is compiler-generated. Otherwise Not enabled.

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

Badges and chips show only Enabled. Not enabled and Unavailable are silent in
those presentations, so a badge is never a guess. Structured output and the
Signals rows retain every state and reason.

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
- `System.Net.Sockets.dll` and `System.Security.Cryptography.dll` from
  `Microsoft.NETCore.App.Ref` 11.0.0-rc.1.26425.128 report all three
  enablements Unavailable with reason `ReferenceAssembly`, although they carry
  `IsAotCompatible` and, for `System.Security.Cryptography`, a v2 module
  marker; the runtime-pack `System.Security.Cryptography.dll` is Memory Safety
  v2 Not enabled;
- an undecodable assembly `AssemblyMetadataAttribute` blob makes AOT
  Unavailable with reason `UndecodableMetadata`;
- `IsAotCompatible` values `True` and `False` together, and the value `yes`,
  are Unavailable with reasons `ConflictingValues` and `UnrecognizedValue`;
- conflicting and unsupported memory-safety markers are Unavailable with the
  memory-safety owner's state preserved.

## Non-claims

This owner does not define:

- package, Platform, or multi-Library aggregation of enablements;
- which image of a Library is judged, which the Library document owns;
- chip or badge visual design, color, or icons;
- whether an enablement is requested by default in any host;
- Native AOT execution, trimming, or warning-free build verification;
- the amount or location of unsafe code, P/Invoke, or runtime marshalling; or
- additional enablements beyond the three named here.
