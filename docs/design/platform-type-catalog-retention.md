# Platform type catalog retention

## Owner and claim

This document owns retained-state behavior for the current
`DotnetInspector.Services.PlatformTypeCatalog` compatibility implementation.

> Each lookup derives its catalog from that invocation's normalized reference
> path, supplied framework identity and version, and current readable assembly
> inventory. No catalog, provenance, failure, or filesystem observation is
> retained for an unrelated later lookup.

The catalog is a stateless reverse type-declaration lookup, not a cache owner,
resource owner, lease, or persistent-cache category.

## Current role

The implementation enumerates one Platform reference directory, asks Metadata
for each assembly's definitions and forwarders, and selects candidate assemblies
for implicit CLI routing. It preserves structured type names, declaration kind,
assembly descriptors, and deterministic ambiguity.

This is discovery rather than binding. A returned candidate does not establish
dependency closure, terminal forwarding resolution, implementation
correspondence, or Metadata's final binding decision.

The current `PlatformTypeLookupCandidate` and its
`ResolvedAssemblyReference` remain compatibility surfaces in this slice.
[#6843](https://github.com/richlander/dotnet-inspect/issues/6843) separately
develops the general model in which `find` locates detached typed coordinates
and Type, Member, and neighboring commands consume them.

Platform's static filename population remains the source for Spotlight display.
This metadata-derived type index does not replace that inventory.

## Lookup boundary

One call to `PlatformTypeCatalog.Lookup` is the complete current operation:

1. validate and normalize the requested type pattern;
2. normalize the supplied reference directory path;
3. enumerate its current `*.dll` population in deterministic path order;
4. construct assembly descriptors with the framework identity and version
   supplied by this invocation;
5. read definition and forwarding inventories through Metadata;
6. select a resolved, missing, ambiguous, or rejected outcome; and
7. release every catalog-only reference when the call returns.

A later call repeats those steps. It may observe a changed local directory,
another framework identity for the same path, or a corrected earlier failure.
It inherits no correspondence or currentness claim from the earlier call.

Local files may change freely between operations. The current implementation
does not claim a stable snapshot when files change during one catalog build.

## Selection and failure behavior

Removing retained state does not change source-selection policy:

- definitions remain preferred over forwarders;
- exact type-name matches remain preferred over partial matches;
- explicit generic notation preserves its current arity behavior;
- assembly-prefix disambiguation retains its existing ordering;
- candidate output remains ordered by assembly identity, type name,
  declaration kind, and path; and
- invalid patterns, unavailable catalogs, invalid assemblies, missing types,
  and ambiguity remain typed outcomes.

No failure is retained after the invocation. A later call always attempts the
ordinary cold path, so an unavailable directory, empty directory, or invalid
assembly may succeed after the source is corrected.

## Future caching

This compatibility implementation has no cache. Any future CoreCache,
Workspace, or PlatformHouse caching is outside this document and requires its
owning contract and evidence.

## Production composition

`PlatformResolver.LookupType`, `LookupTypeInFramework`, and
`LookupTypeAcrossFrameworks` are the current production callers. Their public
selection and typed-failure semantics remain unchanged for one stable input.
Repeated lookups intentionally gain freshness: they use their own supplied
provenance and may observe corrected failures or a changed assembly population.

Some higher CLI resolution paths may make more than one public lookup and
therefore rebuild a reference catalog more than once. That is an explicit
transitional tradeoff: the current catalog is migration evidence rather than a
future public query session, and this slice does not publish a temporary owner
or broaden into the #6843 locator adoption.

## Evidence

`PlatformTypeCatalogTests` in the Services Release suite gates:

- the same path under two framework/version identities in either request order;
- replacement of the assembly population behind one path;
- retry after an unavailable or empty directory;
- retry after an invalid assembly is replaced;
- release eligibility after the lookup result is released; and
- preservation of typed failure.

Existing CLI `PlatformResolverTests` retain real installed-reference-pack
evidence for deterministic definition selection, forwarding behavior,
cross-framework ambiguity, and framework-specific provenance.

## Non-goals

- No general reverse type-locator design or adoption; #6843 owns that work.
- No change to Find, Type, Member, Spotlight, or output rendering.
- No PlatformHouse or Workspace adoption.
- No CoreCache category or persistent derived index.
- No new weak cache, single-flight cache, timer, or public lookup session.
- No Platform resolution, pruning, target-currency, or binding redesign.
- No support for Windows Metadata.
- No defense against local files changing during one catalog build.
