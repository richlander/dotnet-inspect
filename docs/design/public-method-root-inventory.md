# Public MethodDef root inventory

This document owns Metadata's exhaustive exact inventory of public local
MethodDefs. The focused implementation is tracked by
[#7391](https://github.com/richlander/dotnet-inspect/issues/7391); the first
composition consumer is
[#7390](https://github.com/richlander/dotnet-inspect/issues/7390).

## Claim and owner

`ILInspector.Metadata` owns one bounded operation: given one admitted ECMA-335
metadata reader and explicit type, method, and retained-root limits, return
every exact public MethodDef declared by an externally visible local TypeDef.

The result is detached from the reader and retains the module MVID, exact
`MetadataMethodAddress` roots in MethodDef-token order, bounded-work counts,
and any limit that stopped the inventory.

The operation first classifies TypeDefs in TypeDef-token order. Only after that
phase completes does it visit MethodDefs in MethodDef-token order. This phase
boundary lets valid uncompressed metadata use a reordered `MethodPtr` table
without changing root order or bounded prefixes.

This is declaration membership, not an API presentation surface. It does not
apply name, attribute, compiler-generated, `EditorBrowsable`, body-presence,
feature, package, or host filtering.

## Motivation

The direct-use cluster and local root-path owners can identify exact private
package-use sites and traverse to caller-supplied roots, but neither owns the
root set. The first production composition needs an exhaustive Metadata-issued
set so an empty path result can mean no selected public MethodDef reaches the
use site within the separately reported Analysis boundaries.

`ApiSurfaceExtractionScope.Public` cannot supply that absence claim. It is the
default consumer presentation surface and deliberately suppresses declarations
such as `EditorBrowsable(Never)`. Reusing it would make presentation policy
silently narrow graph roots.

The motivating real package is `Serilog.Sinks.Console` 6.0.0. Its two public
configuration entrypoints reach private `ThemedValueFormatter..ctor`, which
directly calls `Serilog` 4.0.0. The root inventory supplies those exact public
MethodDefs without knowing the provider, cluster, or local path.

## Basis

ECMA-335 visibility is represented by the
[`MethodAttributes`](https://learn.microsoft.com/dotnet/api/system.reflection.methodattributes)
member-access mask and
[`TypeAttributes`](https://learn.microsoft.com/dotnet/api/system.reflection.typeattributes)
visibility mask. The existing
`AssemblyTypeDeclarationInventory.IsPublicDefinition` already treats a local
type as externally visible only when its complete enclosing chain is public.
This owner applies the same type rule and the exact `MethodAttributes.Public`
member rule to MethodDefs.

The .NET linker also uses explicit rooted identities to begin reachability, but
its deployment closure and reflection policy are not transferred here. This
inventory supplies only exact declaration roots for another owner to traverse.

## Exact membership

A MethodDef is a root exactly when:

- its member-access mask is `MethodAttributes.Public`;
- its declaring top-level TypeDef is `TypeAttributes.Public`, or its declaring
  nested TypeDef and every enclosing TypeDef are public; and
- both rows belong to the input reader.

The inventory includes constructors, operators, property and event accessors,
special-name methods, compiler-generated public methods, methods carrying
`EditorBrowsable(Never)`, and bodiless declarations when they satisfy those
rules. It excludes protected, internal, private, and private-scope MethodDefs,
and every method whose declaring-type chain contains a non-public TypeDef.

Forwarders and exported types contribute no roots because they declare no
MethodDefs in the local module. The `<Module>` definition is non-public and
therefore contributes none without a name special case.

Roots are ordered by MethodDef token. No display name or decoded signature is
needed for membership.

## Bounds and completion

The caller supplies independent positive limits for:

- visited TypeDefs;
- visited MethodDefs; and
- retained roots.

One visited TypeDef is charged before its visibility is evaluated. One visited
MethodDef is charged before its declaring type and access are evaluated. One
root is retained only after both visibility conditions succeed. A TypeDef
boundary ends the first phase before any MethodDef root is observed.

Exactly filling a limit is complete. A typed boundary appears only when the
operation observes one additional type, method, or root. The operation stops
at the first boundary and retains every exact positive root already observed.
Its receipt reports visited types, visited methods, and retained roots.

Malformed visibility or ownership rows encountered by the bounded scan remain
visible metadata-read failures; a limit is never used as a success-shaped
substitute for an observed invalid row. Windows Metadata remains unsupported
under the repository-wide admission contract.

The Release gates are:

- `Read_ReturnsEveryExactPublicMethodDef`;
- `Read_RequiresPublicDeclaringTypeChain`;
- `Read_DoesNotApplyPresentationFilters`; and
- `Read_ReportsIndependentExactCapacityBounds`;
- `Read_ReorderedMethodPtrPreservesMethodDefTokenOrder`; and
- `Read_OrphanedNestedPublicTypeFailsVisibly`.

## Consumer handoff

The inventory issues only `MetadataMethodAddress` values. A consumer joins them
to another owner by module MVID and MethodDef token; acquisition registration
remains the higher-layer participant identity.

Issue #7390 will compose this inventory with one selected direct-use cluster
and `LibraryBodyRootPathAnalysis`. Metadata does not validate pair membership,
choose destinations, traverse method bodies, or construct CLI/Browser output.

## Non-claims

This owner does not:

- define a user-facing API or documentation surface;
- claim that public methods are usable, supported, recommended, or source
  compatible;
- include protected members or infer derived-type reachability;
- infer reflection, delegate, dynamic-dispatch, or runtime virtual roots;
- inspect method bodies or call relationships;
- identify direct-use clusters, features, packages, or ecosystems; or
- recommend package removal or source inlining.
