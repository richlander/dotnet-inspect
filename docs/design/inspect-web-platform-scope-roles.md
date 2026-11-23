# Inspect Web Platform scope roles

## Status and owner

**Status: contract; adoption in progress.** This document is the normative
owner of the roles of an Inspect Web Platform Library. Adoption is tracked by
[#11231](https://github.com/richlander/dotnet-inspect/issues/11231), whose
[census](https://github.com/richlander/dotnet-inspect/issues/11231#issuecomment-6906698786)
records which pack each Platform view read before adoption.
[Adoption](#adoption) records what is adopted and what remains.

The package compile and implementation roles this contract extends are
described in [Inspect Web](../../inspect-web/README.md#managed-layout).
[Platform Library Resolution](platform-assemblies.md) describes the CLI's use
of the same reference and runtime split.

## Roles

A Platform Library has the same two roles as a package Library.
- **Surface:** the matching assembly in its family's reference pack
  (`Microsoft.NETCore.App.Ref` or `Microsoft.AspNetCore.App.Ref`) for the
  selected platform version and target framework.
- **Implementation:** the same-named assembly in that family's runtime pack.

The surface answers what a developer compiles against. It owns:
- the Type list;
- Type and member declarations and documents, including API declarations;
- catalog counts;
- the memory-safety contracts of the members it declares;
- the navigation identity of Analysis rows.

The implementation answers what runs. It owns:
- method bodies and Source;
- Metadata images;
- Integrations, their Opportunities, and Performance;
- implementation profiles, heat, and leverage;
- body evidence for Unsafe, and the memory-safety contracts of members the
  surface omits;
- Resource Triage;
- Library metrics and structure;
- call graphs.

A view reads one role for each fact it shows, so a count always describes the
same assembly as the list beside it.

Memory-safety information shows why the surface is not interchangeable with
the implementation. A member's safety contract has two parts:

- **The caller-unsafe marker.** A member declared `unsafe` compiles to
  `RequiresUnsafeAttribute` under the updated memory-safety rules, which an
  assembly opts into with `MemorySafetyRulesAttribute`.
- **The safety prose.** This is the member's `<safety>` documentation, which
  ships in the reference pack's XML documentation beside the rest of its
  docs.

The published .NET 11 reference assemblies carry the markers. The .NET 11
runtime-pack assemblies, including `System.Private.CoreLib`, do not.
dotnet/runtime has begun enrolling implementation projects in the updated
rules, for example
[dotnet/runtime#136657](https://github.com/dotnet/runtime/pull/136657). Later
runtime packs may therefore carry markers on their own members, including
non-public members the reference assembly omits. The `<safety>` prose is not
yet written.

The role that declares a member supplies its safety contract:
- **Surface-declared members.** For a member the reference assembly declares,
  the surface is authoritative. A view that shows its marker or prose reads the
  reference assembly and its documentation, and never infers either from the
  implementation.
- **Members the surface omits.** For a non-public member, or any member the
  reference assembly omits, the implementation's own marker is the only
  contract.
- **Unsafe findings.** A finding combines body evidence from the
  implementation with the contract from the declaring role.

[Platform caller-unsafe contracts](platform-caller-unsafe-contracts.md)
owns marker consumption. These roles only fix which assembly supplies it.

Opportunities are computed with Integrations as their prerequisite, so they
read the implementation, as package Opportunities do.

Surface and implementation pair by assembly name within one family and
version, as a package pairs a `ref/` asset with its matching `lib/` asset.
Pairing does not require equivalent assembly identities. These edge cases
apply:

- **No reference counterpart.** An implementation assembly such as
  `System.Private.CoreLib` is its own surface, as for a package without `ref/`
  assets. The same holds when the configured source does not publish the
  family's reference pack. Transport, timeout, and content failures while
  acquiring the reference stay visible.
- **No runtime counterpart.** A reference assembly has a surface only.
  Implementation views report it unavailable rather than empty.
- **Facades.** A facade keeps its reference surface even when its runtime
  assembly forwards every Type elsewhere. For example, the reference
  `System.Runtime` lists the Types developers compile against.
  - A Type that the reference declares and the runtime assembly forwards has
    the same identity as its forwarder. It lists and counts once, as its
    forwarder, so it keeps the forwarder's Overview-only inspector: a Type
    forwarder has no members, Source, or analysis of its own to show. A
    forwarder that the reference omits lists the same way.
  - A forwarded Type reaches its defining assembly only through
    [Platform forwarded-Type activation](inspect-web-platform-forwarded-type-activation.md).
    That route stays on runtime-pack `ExportedType` evidence, because the
    defining assembly, such as `System.Private.Xml`, often has no reference
    counterpart.

Analysis rows computed from implementation evidence, such as Performance and
Unsafe, navigate through the surface.
- A member the reference assembly declares opens its listed Type.
- A non-public member, or one the reference assembly omits, opens as a
  transient Type, as Package rows do.

## Adoption

The exact package-backed Platform load path, which every Inspect Web Library
selection uses, acquires both roles.
- **Acquisition.** Each selected runtime-pack assembly is realized with its
  same-named reference-pack assembly into a separate surface group in the same
  workspace. Accumulation re-realizes both roles.
- **One image budget.** The workspace's retained-image budget covers both
  groups. The surface group retains exactly its reference images, and the
  implementation group keeps the remainder. Reference images are a small
  fraction of their implementations: the .NET 11 reference `System.Runtime`
  is under 1 MB, while the runtime `System.Private.CoreLib` is about 19 MB. So
  the even split that package roles use would cut the implementation budget
  that Platform accumulations already need.
- **Adopted surface views.** These read the surface:
  - the Type list;
  - Type overview and member population;
  - member groups, member documents, and member declarations;
  - API declarations;
  - member XML documentation, requested by the reference assembly's own
    identity, which may differ from the runtime identity.
- **Declarations.** A member declaration reads the surface when the reference
  assembly declares the member, and the implementation only when the reference
  omits it. A surface projection that exceeds its bounds fails visibly rather
  than falling back.
- **Implementation views.** These accept surface Type and member identities.
  Member metadata tokens are hints that the selector key validates, so a
  reference token never selects a different runtime member.
- **Facade Types.** The Type list projection marks each reference Type that
  the runtime assembly forwards, read from the runtime `ExportedType` table.
  A marked Type lists, counts, and selects only as its forwarder, including
  before the forwarder inventory that supplies its activation has loaded. The
  Type list, its filters and counts, and the Library's Navigate count show it
  once.

- **Analysis rows.** Performance and Unsafe run on the implementation and
  name their rows by the Library's surface participant. A row whose Type the
  surface lists opens that Type; any other opens as a transient Type built
  from the implementation's declaration. A member drill loads the
  implementation member population, so a non-public member opens.
- **Library document.** Type Names read the surface. Enablements and the
  Image and Description facts read the implementation. A names-only document
  reads and names the surface assembly, as a package names-only document does.
  A document that also requests Enablements or facts names the implementation
  and carries the surface's Type Names.

Remaining adoption:
- A transient Type's API lens. After its drill, switching that Type to the
  API lens reloads the ordinary member population, which reads the surface
  that does not declare the Type. This holds for a Package with `ref/` assets
  as well. The transient Type should keep the implementation population.
- The Platform Playwright fixtures and specs.

The Workspace context paths keep implementation-only scopes, in which each
assembly is its own surface. These are the home demo, complete-population
loads, and hosts without a package source client.
