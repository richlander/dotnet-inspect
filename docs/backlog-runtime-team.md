# Backlog: dotnet/runtime Team Scenarios

How the dotnet/runtime team might use dotnet-inspect for investigations and daily work — and what capabilities are missing.

> **Context:** The dotnet/runtime team owns the .NET runtime, BCL, JIT, GC, and libraries. They maintain multiple active release branches (8.0 servicing, 9.0, 10.0, 11.0-preview), run formal API review twice weekly, and constantly investigate performance regressions, breaking changes, and customer-filed bugs against shipped assemblies.

## IL Diff Across Versions

**Priority: High** — This is the single highest-impact addition for the runtime team.

Regression investigation is a daily workflow. Performance bots auto-file issues when benchmarks regress (e.g., [#123124](https://github.com/dotnet/runtime/issues/123124) "Severe performance regression in .NET 10 vs .NET 9"). Breaking change triage ([#123372](https://github.com/dotnet/runtime/issues/123372) "Breaking change in JsonNodeConverter") requires understanding what changed in the implementation, not just the signature.

Today `diff` compares API surfaces. The team also needs IL-level comparison:

```bash
# Compare method IL between platform versions
dotnet-inspect diff JsonSerializer --platform System.Text.Json --framework runtime@9.0..10.0 --il

# Compare method IL between package versions
dotnet-inspect diff JsonNode --package System.Text.Json@9.0.0..10.0.0 --il
```

Output could show methods whose IL bodies changed, with a summary of what changed (code size delta, new/removed call targets, added allocations). Full IL diff for individual methods when filtered with `-m`.

The existing `diff` infrastructure (version range parsing, two-version resolution, API extraction) provides the foundation. The `show` command's IL reading provides the other half.

### Subfeatures

- **Code size summary**: Table of methods with code size changes, sorted by delta
- **Call target diff**: New/removed `call`/`callvirt` targets in a method
- **Allocation diff**: New/removed `newobj` instructions — the most common perf regression signal
- **Filter to changed methods only**: `--changed` flag to suppress unchanged methods

## Platform-to-Platform Diff

The team works across multiple .NET versions simultaneously. PRs like [#122558](https://github.com/dotnet/runtime/pulls/122558) ("[release/8.0] Update dependencies") and servicing updates show the multi-version reality.

Currently `diff` requires package versions. The team needs to compare platform assemblies directly:

```bash
# Compare an assembly across installed SDK versions
dotnet-inspect diff System.Runtime --platform runtime@9.0..10.0

# Compare aspnetcore framework assemblies
dotnet-inspect diff Microsoft.AspNetCore.Http --platform aspnetcore@9.0..10.0
```

This avoids the indirection of finding the right NuGet package for a platform assembly. The `PlatformResolver` already supports version selection — this extends `diff` to accept platform sources.

## Internal Member Visibility

The runtime team owns `System.Private.CoreLib` and other internal assemblies where most code is `internal`. The current `api` command focuses on public API surfaces, which is correct for consumers but not for the team that owns the code.

```bash
# Show all members, not just public
dotnet-inspect api String --platform System.Private.CoreLib --internal

# Show IL for internal methods
dotnet-inspect show String --platform System.Private.CoreLib -m TrimHelper --internal
```

Scenarios:
- Investigating internal helper methods during bug triage
- Understanding the internal implementation surface of a type
- Reviewing internal API consistency across the codebase

## IL Pattern Search

The JIT team investigates classes of optimization opportunities across the entire framework. PRs like [#122679](https://github.com/dotnet/runtime/issues/122679) "Devirtualize calls dominated by a type assertion" and [#120607](https://github.com/dotnet/runtime/issues/120607) "Slicing a span with a list pattern produces unnecessary bounds check" require finding patterns across many methods.

```bash
# Find methods containing boxing instructions
dotnet-inspect find --platform System.Private.CoreLib --il-pattern "box"

# Find methods with specific call patterns
dotnet-inspect find --platform System.Runtime --il-pattern "newobj.*Exception"

# Find methods above a code size threshold
dotnet-inspect find --platform System.Private.CoreLib --min-code-size 1000
```

This is the IL equivalent of `grep` — searching across method bodies rather than API signatures. Could be scoped to a single assembly or sweep across all platform assemblies.

### Use Cases

- **Boxing audit**: Find remaining boxing in hot paths
- **Allocation analysis**: Identify methods with `newobj` for heap allocation reduction
- **Pattern detection**: Find methods using specific instruction sequences (e.g., `ldsfld` + `brfalse` lazy initialization pattern)
- **Code size outliers**: Find unusually large methods that may benefit from refactoring

## Attribute Diff

Many breaking changes in the runtime manifest as attribute changes rather than signature changes. Adding `[Obsolete]`, changing `[EditorBrowsable]`, or adding `[SupportedOSPlatform]` constraints are all breaking in different ways.

```bash
# Show attribute changes between versions
dotnet-inspect diff System.Drawing.Common --platform runtime@9.0..10.0 --attributes
```

The existing `diff` command tracks added/removed/modified members. Extending it to also track attribute changes would catch:
- New `[Obsolete]` annotations (common in deprecation waves)
- `[SupportedOSPlatform]` / `[UnsupportedOSPlatform]` changes
- `[RequiresUnreferencedCode]` additions (trimming compatibility)
- `[Experimental]` markers on new APIs

## Batch Audit with Summary

The team ships packages containing dozens of assemblies. CI pipelines need to verify provenance across all of them efficiently.

```bash
# Audit all assemblies in a package with pass/fail summary
dotnet-inspect library Microsoft.NETCore.App.Runtime.linux-x64@10.0.0 -S Signals --batch

# Output:
# | Assembly | SourceLink | Deterministic | Signed |
# |----------|------------|---------------|--------|
# | System.Runtime.dll | ✓ | ✓ | ✓ |
# | System.Text.Json.dll | ✓ | ✓ | ✓ |
# | ... | | | |
# 147/147 passed
```

Signals currently work per-assembly. A batch mode with a summary table and exit code would integrate into CI pipelines for release validation.

## ReadyToRun Assembly Inspection

The runtime team ships ReadyToRun (R2R) assemblies containing both IL and precompiled native code. Crossgen2 PRs like [#123643](https://github.com/dotnet/runtime/pulls/123643) "Add support for encoding Continuation types with specific layouts in ReadyToRun" show active R2R development.

```bash
# Show R2R status and native code regions
dotnet-inspect assembly System.Private.CoreLib --platform runtime --r2r

# Verify IL is preserved in R2R assemblies
dotnet-inspect show String --platform runtime -m Concat --r2r
```

R2R assemblies embed a full copy of the IL alongside native code. The tool could surface:
- Whether an assembly is R2R compiled
- Which methods have precompiled native code vs IL-only
- R2R header information and version

## Cross-Version Type Evolution

Track how a type evolves across .NET releases — what members were added, removed, or changed per version:

```bash
# Show the evolution of JsonSerializer across .NET versions
dotnet-inspect history JsonSerializer --platform System.Text.Json --versions 8.0,9.0,10.0
```

This would produce a timeline view showing when each member was introduced or modified. Useful for:
- API review preparation (understanding the trajectory of a type)
- Documentation (release notes, migration guides)
- Customer support (explaining when a feature became available)

## Assembly Metadata Comparison

Beyond API surfaces, the team cares about assembly-level metadata changes:

```bash
dotnet-inspect diff System.Runtime --platform runtime@9.0..10.0 --metadata
```

Would compare:
- Assembly version and informational version
- Type forwarding changes (types moved between assemblies)
- Module-level attributes
- Embedded resources
- Assembly references

Type forwarding changes in particular are a common source of subtle breaks when assemblies are reorganized.

## Ref Assembly Inspection

The team produces and ships reference assemblies in `packs/`. These are metadata-only (RVA=0 for all methods) and are used for compilation. The team needs to verify them:

```bash
# Inspect a ref assembly specifically (not the runtime assembly)
dotnet-inspect api JsonSerializer --platform System.Text.Json --ref

# Verify ref assembly matches runtime assembly's public surface
dotnet-inspect diff System.Text.Json --platform ref..runtime
```

Scenarios:
- Verify that ref assemblies contain the correct API surface
- Check that new APIs are properly reflected in ref packs after builds
- Diff ref vs runtime to confirm they agree on the public surface
