# Repository dependency policy

## Ownership and purpose

`eng/DependencyPolicy` owns repository dependency-graph acquisition, strict
interpretation of `eng/dependency-policy.json`, and deterministic diagnostics.
The architecture documents cited by each rule remain authoritative for the
component boundary; neither the tool nor the JSON file redefines those
components.

This design implements
[#5569](https://github.com/richlander/dotnet-inspect/issues/5569). The concrete
consumer is the Release CI build. This is contributor tooling, not a product
capability, so CLI/browser host enablement and product rendering do not apply.
The tool writes one diagnostic per dependency violation and a final summary;
it does not use Markout or add a reusable presentation domain.

The complexity basis is correctness: project references and compiled assembly
references are different evidence. An unused or imported project reference can
violate the repository composition even when it emits no `AssemblyRef`, while a
binary reference can enter the compiled graph without an ordinary repository
project edge. The gate must inspect both to cover the stated boundaries.

## Evidence model

The tool generates the evaluated Release restore graph for
`dotnet-inspect.slnx`. That graph supplies project inventory and target
frameworks. For every project selected by a project-graph rule, the tool asks
MSBuild for its evaluated `ProjectReference` items once per target framework.
Conditional, imported, and build-only references such as
`ReferenceOutputAssembly="false"` are therefore visible even when NuGet's
restore graph omits them.

For compiled evidence, it asks MSBuild for `TargetPath` for every project
selected by an assembly rule, opens that exact Release output through
`ILInspector.Metadata.AssemblyInspectionSession`, and reads
`AssemblyIdentityNames`. It does the same for the evaluated project-reference
closure of those targets so `$repository` is complete for their ordinary
repository dependencies. This covers projects both inside and outside `src/`,
custom output layouts, and an overridden `AssemblyName`. A rule over the
assembly graph fails when its target or project closure has no built output or
when any assembly-reference row cannot be decoded. Platform assemblies are
identities from the current
`Microsoft.NETCore.App` runtime directory; identities produced by projects
selected by any assembly rule form the governed repository set. Every other
compiled identity is external. The broad external-dependency rule selects every
product library, so every product-to-product edge has a governed identity. A
governed repository identity that collides with a platform simple name is
rejected as ambiguous rather than classified silently.

The dependency-policy tool owns its MSBuild invocation separately from CI
change detection. The latter runs as an early bootstrap before the product
graph is built; sharing a library would make that classifier acquire the
Metadata product closure it decides whether CI should build. Both consumers use
MSBuild's restore-graph contract, but keep their process and failure ownership
separate.

Rules inspect direct edges. A forbidden transitive composition must contain a
direct edge at some governed boundary, and the policy governs every product
library participating in the broad IL and external-dependency claims.

## JSON contract

The top-level document contains:

- `schemaVersion`: currently `1`;
- `solution`: a repository-relative solution path;
- `configuration`: the evaluated and inspected build configuration; and
- `rules`: a non-empty ordered array.

Every rule contains a unique `id`, an architectural `source`, one or both
`graphs` (`project` or `assembly`), and non-vacuous `targets`. Target and
dependency patterns use case-sensitive simple-expression matching with `*` and
`?`. `projectPaths` limits selection by canonical repository-relative project
path; `excludeTargets` and `excludeProjectPaths` remove matched projects.

A rule chooses exactly one mode:

- `allowOnly` rejects every dependency not matched by its list.
- `deny` rejects matching dependencies except those matched by `except`.

Assembly `allowOnly` rules may use `$platform` for the runtime platform set and
`$repository` for assemblies produced by the evaluated solution. Those tokens
match nothing while the same rule evaluates the project graph, so a combined
rule must still name every permitted repository project explicitly. Tokens are
rejected in targets, exclusions, deny lists, and exceptions. Unknown
properties, enum values, tokens, duplicate rule IDs, missing build evidence,
and each individual target pattern that selects no project are configuration
errors rather than successful empty results.

## External dependencies

Product libraries admit platform and repository assemblies by default. The
ruleset names external assemblies only at the owner that intentionally consumes
them. Broad wildcard targets cover new `ILInspector.*` and
`DotnetInspector.*` product libraries automatically; explicit exclusions keep
tests, fixtures, temporary apps, and the separately governed external owners
out of that default rule.

## Initial coverage

The checked-in rules provide full gate coverage for these dependency claims:

1. The documented contract floors remain free of repository and external
   dependencies.
2. IL and other engine libraries do not acquire tool-layer
   `DotnetInspector.*` dependencies. `ILInspector.Metadata` retains only the
   documented source-neutral `DotnetInspector.Artifacts` exception.
3. Product libraries use only repository and platform assemblies unless an
   owner-specific rule names an external assembly. Markout is admitted only at
   the metadata-rendering boundary; package, query, service, and NuGet
   acquisition libraries retain their explicit narrow exceptions.
4. The direct project and compiled dependencies of the CSharp, Metadata,
   Instructions, Analysis, ILDiff, Decompiler, SourceLink, Research,
   JavaScript-export, and TypeScript-generation components stay within their
   explicit allowed sets.

Claims not represented by a JSON rule remain unverified by this gate. Changing
an allowed set requires changing the rule's cited owner contract or showing
that the existing contract already permits the edge; the ruleset is not an
exception ledger detached from architecture.

## Gate

Build the solution before running the focused gate:

```bash
dotnet build dotnet-inspect.slnx -c Release
dotnet run --project eng/DependencyPolicy.Tests -c Release --no-build
dotnet run --project eng/DependencyPolicy -c Release --no-build
```

The test executable covers strict schema handling, platform/repository/external
classification, allow-only and deny rules, exceptions, target exclusions,
non-vacuity, deterministic DP0001/DP0002 outcomes, and a real evaluated
`ReferenceOutputAssembly="false"` edge. The final command evaluates the real
Release project and assembly graphs.

## Non-claims

The tool does not inspect source text, public API ownership beyond assembly
edges, package vulnerabilities, runtime dynamic loading, native imports,
test/fixture layering, or projects outside the evaluated solution. It does not
repair a violation or decide that a new dependency is architecturally valid.

Dependency identities are compared by case-sensitive simple assembly name.
Package provenance, version, public-key token, and binding resolution are
outside this composition gate.
