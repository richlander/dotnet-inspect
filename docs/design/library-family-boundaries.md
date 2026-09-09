# Library family and host namespace boundaries

This document owns the meaning of the repository's project and namespace
families. A name starts with what a library processes, inspects, or acts on.
Dependency depth and component role are separate decisions.

Namespace families do not encode the L1/L2/L3 consumer layers directly, but
project dependencies must remain consistent with the family direction. A
production `ILInspector.*` project must not reference a
`DotnetInspector.*` project. A temporary violation must be documented as an
exception with a named retirement path.

The end-to-end adoption tracker is
[#6315](https://github.com/richlander/dotnet-inspect/issues/6315). It tracks
this contract, the two production-host migrations, and the focused library
adoption trackers named below.

## Claim

A reusable project or namespace is named for the subject it inspects or acts
on. A processor that supplies mechanics without owning inspection semantics may
instead use the name of the format, protocol, or language it processes. A host
is named for the product surface it implements.

Placement considers four questions:

| Question | Meaning | Examples |
| --- | --- | --- |
| What does it do? | Process, inspect, or act on a subject. | SRM processes metadata; Metadata inspects it; the Decompiler acts on IL. |
| What is the subject? | The thing whose meaning the component exposes or changes. | IL and PDB evidence, NuGet packages, C# text |
| Is it a host? | Commands, options, interaction, and host-native presentation name the product surface. | `DotnetInspect.Cli`, `DotnetInspect.Web` |
| What is its role? | A role suffix describes reach or operation only after the subject is clear. | Fetch, Service, House, query, renderer |

Dependency depth alone does not select a family, and a role such as Service
says nothing about the subject. `ILInspector.SourceLink` is a relatively high
composer that still inspects PDB-associated program evidence.
The current `DotnetInspector.Artifacts` project is a low, source-neutral
contract floor and therefore targets `Inspector.Artifacts`.
`House` describes reach and role; it does not select either family.

## Process, inspect, or act on

**Process** means supplying mechanics for reading, parsing, normalizing, or
transporting a representation without owning the product's inspection claim.
`System.Reflection.Metadata` processes PE, metadata, and PDB structures. It is
a dependency, not this repository's Metadata inspector. `CSharpText` processes
C# and XML-documentation grammar so Metadata and other consumers can express
.NET declarations with C# knowledge.

**Inspect** means deriving named facts, evidence, Findings, or relationships
about a subject. `ILInspector.Metadata` inspects the structures exposed by SRM.
`ILInspector.Analysis` inspects method bodies. `DotnetInspector.Packages`
inspects NuGet package contents and relationships.

**Act on** means transforming, comparing, or composing an inspected subject
while preserving its identities and semantics. `ILInspector.Decompiler` acts
on IL and metadata to produce C#. `ILInspector.ILDiff` acts on decoded method
bodies to compare implementations. `DotnetInspector.Queries` acts on
producer-issued evidence and ecosystem contexts to execute product queries.

A component may do more than one of these. Its family follows the primary
subject of the public contract, not the lowest-level operation in its
implementation.

## `ILInspector`: IL and its associated program evidence

`ILInspector.*` libraries inspect IL-bearing .NET programs or act on evidence
from those programs. Their inputs and identities include:

- PE and CLI metadata
- portable PDB records and their assembly association
- IL method bodies and exception regions
- typed or textual projections of those representations
- correlations and Findings whose identity remains anchored to those
  representations

`ILInspector` does not mean `ILProcessor`. SRM supplies the low-level processing
mechanics. The repository's libraries inspect the resulting metadata and IL or
act on them. Metadata inspection, PDB correlation, control flow, C# projection,
decompilation, and implementation comparison are all operations on the
compiled program even when only the instruction decoder reads opcodes directly.

An `ILInspector.*` component does not own:

- NuGet feed or package policy
- SDK, platform, or restored-project acquisition
- host authorization, command syntax, or navigation
- ambient filesystem discovery
- network fetching, credential policy, or persistent cache policy

Those concerns may supply content to the compiled-program family, but they do
not become compiled-program semantics.

## `DotnetInspector`: the broader .NET ecosystem

`DotnetInspector.*` libraries inspect or act on .NET ecosystem subjects beyond
one compiled program. Their subjects include:

- product workspaces and ecosystem artifact composition
- NuGet packages and package relationships
- platforms, SDK assets, and restored-project evidence
- source-selection and acquisition services
- cross-host queries, sections, row selection, and presentation
- product-owned catalogs shared by more than one host

The family also contains reusable product composition over those subjects and
the `ILInspector.*` results they contain. A query may inspect only one assembly
and still belong here when its public contract is product query selection,
capability, cost, prerequisite, or execution rather than a new fact about IL.
The family is not the namespace of the `dotnet-inspect` command-line
application.

This distinction allows the CLI and browser to consume the same
`DotnetInspector.*` libraries without making either host the architectural
center. A reusable component may be product-specific and still belong here; it
need not be a general-purpose .NET library.

## `Inspector`: subject-neutral inspection substrate

`Inspector.*` libraries provide contracts and mechanics intrinsic to
inspection but independent of both IL-bearing programs and the broader .NET
ecosystem:

- `Inspector.Artifacts` owns source-neutral artifact identity, acquisition,
  authorization, and lifetime contracts.
- `Inspector.Findings` owns the domain-neutral Finding algebra.
- `Inspector.Text` owns generic text Findings and deterministic text
  construction used by inspection producers.

This is a deliberately narrow shared family, not a general-purpose utility
layer. It does not admit `Core`, `Services`, networking, serialization, or
other infrastructure merely because several projects use it. It remains
host-neutral and Markout-free.

## Independent domain roots

A project may use an independent root when its contract is coherent outside
both inspection families and the shorter name is established by the subject:

- `NuGetFetch` owns NuGet protocol access and transport behavior.
- Target `SourceFetch` owns bounded, host-authorized source-byte retrieval,
  redirect and origin enforcement, content-store integration, and typed
  transport outcomes.
- `CSharpText` owns model-free C# and XML-documentation text grammars.
- `InertText` owns construction-time containment of untrusted text.
- Target `NetworkAccess` owns network-destination admission shared by
  otherwise independent transport owners.
- Target `UntrustedDocuments` owns hardened JSON and XML parsing entry points.

An independent root is not an escape from ownership. It must name a focused
domain, expose a contract that does not depend on the `Inspector`,
`ILInspector`, or `DotnetInspector` families, and have credible reuse at more
than one composition point. A one-caller implementation detail normally
belongs with its caller unless a separate boundary has another concrete reason
to exist.

## Host namespaces

Hosts name themselves rather than claiming a reusable library family.

| Host | Namespace boundary |
| --- | --- |
| `dotnet-inspect` CLI | Target `DotnetInspect.Cli` |
| Inspect Web | Target `DotnetInspect.Web` |
| Inspect Web JavaScript export boundary | Target `DotnetInspect.Web.Interop` |
| Focused tools and harnesses | Tool- or harness-specific root |

The CLI currently uses `DotnetInspector` and nested namespaces such as
`DotnetInspector.Commands`. That makes application-owned commands, options,
views, and mutable compatibility models appear to be peers of reusable
`DotnetInspector.Queries`, `DotnetInspector.Packages`, and
`DotnetInspector.Presentation`. The CLI migration in #6315 removes that
collision. It does not rename the executable, tool package, or reusable
libraries.

The web host currently uses `InspectWeb.Engine` and facet-specific
`InspectWeb.Engine.*Facade` namespaces. `DotnetInspect.Web` names the product
host rather than its current Wasm runtime or managed-engine implementation.
`DotnetInspect.Web.Interop` is its one architectural child: it owns JavaScript
export contracts, wire projections, and exported entry points. Domain suffixes
such as Source or Packages may organize those exports beneath `Interop` without
becoming new architectural layers.

The CLI's current suffixes, including Commands, Options, Output, Views,
Sections, Inspectors, Services, and Planning, may move mechanically beneath
`DotnetInspect.Cli`. They remain code-organization names rather than
architecture defined by this document. The same rule applies to test
namespaces.

## Carrier does not decide ownership

The format that carries a fact is evidence for placement, not an automatic
answer. Ownership follows the stable identity and semantics exposed to
consumers.

SourceLink demonstrates the rule. SourceLink is a source-provenance protocol,
not IL semantics:

1. A portable PDB carries the SourceLink document map.
2. `ILInspector.Metadata` owns extraction of the custom-debug-information
   record and PDB document, checksum, sequence-point, and row identities.
3. `ILInspector.SourceLink` interprets the map and correlates those identities
   with type, member, and IL-offset source evidence.
4. Target `SourceFetch` retrieves source bytes over authorized network
   transports without owning SourceLink or PDB semantics.
5. `PdbSourceHouse` composes local, repository, and remote candidate ordering,
   invokes the source fetcher, verifies PDB checksums, decodes source, and
   settles visible PDB-source outcomes.
6. `AssemblyContextSourceQuery` composes verified PDB source with the distinct
   decompiler fallback.

`ILInspector.SourceLink` therefore remains in the IL inspection family
because its primary contract is PDB document, type, member, and IL-offset
correlation. Not every result carries assembly identity: standalone map audit
and repository provenance are narrower subordinate contracts. They remain
colocated because the project has no independent generic SourceLink consumer,
not because all SourceLink semantics are inherently IL-related.

Conversely, moving network acquisition or host policy into that project would
violate the family boundary even though the acquired bytes are C# source named
by SourceLink. A future general-purpose SourceLink implementation with multiple
non-PDB consumers would deserve a neutral owner rather than automatic placement
under `ILInspector`.

The standalone SourceLink map utility had no independent second composition
point and is retired by #6312. Its map grammar and provenance logic remain
implementation details of `ILInspector.SourceLink`.

That retired `SourceLinkFetch` project was not the peer of `NuGetFetch`: it
parsed SourceLink maps and provenance but did not fetch source bytes. The
existing `SourceFetch` implementation is the transport peer. Its contract is
useful at multiple source-acquisition composition points and is independent of
the PDB-specific ordering and checksum policy owned by `PdbSourceHouse`.

## Placement test

Classify a project or namespace in this order:

1. **Name the verb.** Does the component process, inspect, or act on its input?
2. **Name the subject.** Is the public contract about IL and its associated
   program evidence; packages, platforms, projects, or workspaces; or an
   independent protocol or text grammar?
3. **Separate helpers from the owner.** A parser or formatter used by an
   inspector does not become the inspected subject. Keep a focused helper
   neutral when it has an independent contract and consumers.
4. **Identify policy dependencies.** Network authorization, package-source
   choice, ambient host state, and command behavior keep a component above the
   IL inspection family.
5. **Distinguish inspection from product composition.** A product query or
   presentation contract belongs to `DotnetInspector` even when its immediate
   input is one assembly. A producer belongs to `ILInspector` when its public
   result remains evidence about that program.
6. **Identify the host boundary.** Command routing, options, interactive state,
   and host-native rendering belong to a host-specific namespace.
7. **Then name the role.** Apply `Fetch`, `Service`, `House`, query, rendering,
   or another role only after the family is settled.

If the answers disagree, split the responsibilities before choosing a name.
A project that combines compiled-image interpretation with network policy is
not evidence that either family should absorb both.

## Classification

The following examples anchor the contract; they are not an exhaustive project
inventory. Names described as targets are accepted classifications whose
implementation belongs to separately tracked owner-scoped work.

| Classification | Representative projects |
| --- | --- |
| IL program inspection and action | `ILInspector.Metadata`, `ILInspector.SourceLink`, `ILInspector.Instructions`, `ILInspector.Analysis`, `ILInspector.Decompiler`, `ILInspector.ILDiff`, `ILInspector.Research` |
| Ecosystem and reusable product composition | `DotnetInspector.Packages`, `DotnetInspector.Queries`, `DotnetInspector.PackageQueries`, `DotnetInspector.SourceSelection`, `DotnetInspector.Sections`, `DotnetInspector.Presentation`, `DotnetInspector.MetadataRendering` |
| Subject-neutral inspection substrate | Target `Inspector.Artifacts`, `Inspector.Findings`, and `Inspector.Text` |
| Independent domain roots | `NuGetFetch`, `CSharpText`, `InertText`; target `SourceFetch`, `NetworkAccess`, and `UntrustedDocuments` |
| Product hosts and host boundary | `DotnetInspect.Cli`, `DotnetInspect.Web`; child `DotnetInspect.Web.Interop` |

The following dispositions close the existing ambiguous names:

| Current name | Disposition | Basis |
| --- | --- | --- |
| `DotnetInspector.MetadataRendering` | Keep. | It is a focused Markout-based product presentation adapter over Metadata projections, shared by `mdi` and the CLI. No production `ILInspector.*` library references it. Keeping it above Metadata preserves the Markout-free IL inspection layer. |
| `CSharpText.MemberSlicing` | Keep as adopted under [#6332](https://github.com/richlander/dotnet-inspect/issues/6332). | It consumes only the public `CSharpText` contract and operates on C# source structure. Its separate assembly preserves the enforced boundary that prevents access to lexer internals. “Member” is more accurate than “Body” because the result includes the complete declaration. |
| `DotnetInspector.Artifacts*` | Target `Inspector.Artifacts`, `Inspector.Artifacts.Workspaces`, and `Inspector.Artifacts.Local` under [#6333](https://github.com/richlander/dotnet-inspect/issues/6333). | The family is source-neutral and the base is a dependency-free artifact contract floor. `ILInspector.Metadata` legitimately consumes its scoped content and identities to construct artifact-to-assembly correspondence, but the current prefix creates the sole production `ILInspector.*` to `DotnetInspector.*` exception. |
| `DotnetInspector.Core` | Retire without a replacement assembly under [#6334](https://github.com/richlander/dotnet-inspect/issues/6334). | It groups unrelated cache, networking, untrusted-document, CLI telemetry, and single-consumer helpers by dependency depth instead of subject. |
| `Inspector.Findings` | Keep as adopted under [#6333](https://github.com/richlander/dotnet-inspect/issues/6333). | Its observation, census, matching, transition, comparison, diff, and correlation contracts are a coherent domain-neutral semantic model shared by both inspection families. They do not belong in metadata primitives, which owns mechanical ECMA/SRM operations rather than semantic models. |
| `ILInspector.Text` | Target `Inspector.Text` under [#6333](https://github.com/richlander/dotnet-inspect/issues/6333). | Generic text Findings and deterministic LF text construction are host-neutral and Markout-free, but not inherently IL- or C#-specific. The project continues to depend on `Inspector.Findings`. |
| `DotnetInspector.Services` | Retire without a replacement assembly under [#6335](https://github.com/richlander/dotnet-inspect/issues/6335). | It groups unrelated package, platform, source, assembly-resolution, parser, and corpus components by role. Targeted `*Service` names remain valid, while Houses and helpers move to their subject owners. |

`DotnetInspector.Core` decomposes by subject: shared cache behavior targets
`DotnetInspector.Cache`; HTTP composition and product network telemetry target
`DotnetInspector.Networking`; the destination-admission primitive shared with
`NuGetFetch` targets the independent `NetworkAccess` root; hardened JSON and
XML entry points target the independent `UntrustedDocuments` root; CLI
measurement moves to `DotnetInspect.Cli`; and single-consumer helpers move
beside their consumers.

`DotnetInspector.Services` likewise has no aggregate successor. Package
components move to the package owner, platform components to the
`PlatformHouse` owner, source-byte transport to the independent `SourceFetch`
root, PDB-specific source composition to the `PdbSourceHouse` owner, and
assembly-set or dependency-resolution components to their workspace or
assembly-resolution owner. `House` remains reserved for the accepted
clearing-house scenarios and does not become an assembly bucket.

The accepted library migrations are tracked by
[#6332](https://github.com/richlander/dotnet-inspect/issues/6332),
[#6333](https://github.com/richlander/dotnet-inspect/issues/6333),
[#6334](https://github.com/richlander/dotnet-inspect/issues/6334), and
[#6335](https://github.com/richlander/dotnet-inspect/issues/6335). Each
implementation step remains an owner-scoped PR. This document does not
authorize a repository-wide rename or alter any component's runtime contract.

## Adoption and evidence

The seven adoption tracks recorded by #6315 are:

1. Land this naming contract.
2. Move the CLI host to `DotnetInspect.Cli`.
3. Move the web host to `DotnetInspect.Web`, with JavaScript export contracts,
   wire projections, and entry points under `DotnetInspect.Web.Interop`.
4. Rename the C# member-slicing library under #6332.
5. Adopt the shared `Inspector.*` family under #6333.
6. Retire `DotnetInspector.Core` by subject under #6334.
7. Retire `DotnetInspector.Services` by subject under #6335.

Together, the host migrations prove that reusable `DotnetInspector.*`
libraries and both product hosts can be named distinctly without changing
behavior. `Web` names the product surface; neither its current Wasm runtime nor
its JavaScript export mechanism defines the host.

The library trackers prove the same boundary from below: neutral inspection
substrate does not masquerade as either subject-specific family, focused text
processing does not claim a C# inspector, and role-based buckets disappear
rather than receiving broader replacement names.

Future moves must preserve dependency direction and public behavior. Their
focused gates are compilation, existing owner tests, dependency-policy
validation when project edges change, and host tests when a consumed namespace
changes. This naming contract adds no runtime safety or correctness claim.

## Non-claims

- Project prefixes do not encode the L1/L2/L3 consumer layers, but production
  `ILInspector.*` projects must not reference `DotnetInspector.*`. The current
  Metadata-to-Artifact edge is a documented temporary exception retired by the
  `Inspector.Artifacts` rename.
- A lower dependency does not automatically belong to `ILInspector`.
- PDB carriage does not make acquisition policy an `ILInspector` concern.
- `DotnetInspector.*` does not mean CLI-only or necessarily high-level.
- `Web` does not promise a particular runtime, and `Interop` does not name the
  whole web host.
- Independent roots are not preferred over an existing coherent family.
- The accepted migrations are not authorization to combine their
  independently owned implementation slices.
