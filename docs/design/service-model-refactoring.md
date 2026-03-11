# Service Model Refactoring

> Design notes for future work on pushing view-compatible shapes into the service layer.
> Delete this document once the refactoring is complete.

## Context

v0.6.0 made oneline the default output and unified the find command's dual views into a single `FindResultView` that serves both `OneLineWriter` and `MarkdownWriter`. The command→view→writer pipeline is now clean, but the service→command boundary still has unnecessary transformation steps.

The spec's goal: "The commands should request data from the services. Those OMs should be crafted to be oneline-compatible such that there isn't much transformation required."

## Current State

### Find: TypeSearchResult → TypeFindResult

The find command orchestrates three data structures to produce results:

```text
Service:  TypeSearchResult[]  (raw types from metadata reflection)
Command:  Dict<pattern, List<TypeSearchResult>>  +  Dict<pattern, List<TypeSearchResult>>  +  List<string>
          (exact matches)                           (partial matches)                         (not found)
     ↓ ConvertToRawData() — 60 lines of dict-walking, match classification, similarity lookup
Model:    TypeFindResult[]  (flat, enriched with Match/Similarity/Pattern)
     ↓ FindOutputFormatter.BuildView()
View:     FindResultView → FindRow[]
```

`TypeSearchResult` is the raw service model — it knows about types but not about search context (which pattern matched, how well it matched). `TypeFindResult` in `Models/RawData.cs` adds `Pattern`, `Match` (exact/glob/partial/notfound), and `Similarity`. The `ConvertToRawData` step in FindCommand does ~60 lines of dictionary-walking to build this.

### Package: InspectionResult → InspectionResultView

```text
Service:  InspectionResult  (40+ raw properties — version, size, TFMs, deps, files, ...)
     ↓ InspectionResultView(result) — manual field construction
View:     InspectionResultView  (curated fields + sections for markdown)
     ↓ OR OutputFormatter.BuildPackageOneLineView()
View:     PackageOneLineView  (Property/Value rows from GetMetadataFields)
```

`GetMetadataFields()` does selective, formatted extraction: `ByteSizeFormatter.Format()`, date formatting, null filtering, collection joining. This is inherently view-level work — formatting raw bytes as "2.1 MB" is a presentation concern.

### Type/Member: ApiSurface/ApiType → ApiTypeView/ApiTypeOneLineView

```text
Service:  ApiSurface { Types: List<ApiType> }  where ApiType has List<ApiMember>
     ↓ ApiOutputFormatter.BuildTypeOneLineView() — groups by kind, extracts ReturnType/Detail
View:     ApiTypeOneLineView { Rows: List<ApiOneLineRow> }
     ↓ OR ApiOutputFormatter.BuildFullApiView() — builds 50+ property view
View:     ApiTypeView  (rich document with per-kind sections)
```

## Proposed Changes

### Find: Move match classification into the service

The service should own the full pipeline: collect types → match against patterns → score similarities → return flat results.

```csharp
// Today: service returns raw types, command classifies matches
var results = await TypeSearchService.CollectTypesAsync(options, pattern, ...);
// ... 80 lines of pattern matching, partial matching, similarity scoring in FindCommand ...
var rawResults = ConvertToRawData(resultsByPattern, partialMatchesByPattern, ...);

// Proposed: service returns classified results directly
var results = await TypeSearchService.FindTypesAsync(options, patterns, ...);
// results is already List<TypeFindResult> with Match/Similarity populated
var view = FindOutputFormatter.BuildView(results, title);
```

This would:
- Delete ~100 lines from FindCommand (`ExecuteMultiPatternAsync`, `ExecuteSinglePatternAsync`, `ConvertToRawData`)
- Make FindCommand look like other commands: parse options → call service → build view → pick writer
- Move `TypeMatcher.MatchesTypeFilter` and `TypeMatcher.FindClosest` calls into the service where they belong
- The `TypeFindResult` model in `Models/RawData.cs` stays — it becomes the service return type

### Package: Consider Markout attributes on the service model

If `InspectionResult` had Markout attributes directly — `[MarkoutSkipDefault]` on nullable properties, `[MarkoutPropertyName("Size")]` with custom formatters — the view layer could thin out for the oneline case. The service model IS the view, following the GitHubRepo demo pattern where `RepoView` has domain data with rendering attributes.

**Blocker:** `InspectionResult` lives in `DotnetInspector.Services` which doesn't reference Markout. Options:
1. Add Markout reference to Services (couples service to rendering)
2. Use a source-generated adapter in the dotnet-inspect project
3. Keep the current `InspectionResultView` wrapper (it's thin and works)

Option 3 is pragmatic. The `GetMetadataFields()` transformation involves formatting logic (byte sizes, date formatting, null coalescing) that belongs in the view layer regardless. The dual PackageOneLineView/InspectionResultView structure isn't a problem — `BuildPackageOneLineView` is 8 lines.

### Type: The structural gap is permanent

The type command has a genuine structural mismatch:
- **Oneline:** One flat table with Kind column, all member kinds merged, one row per unique name
- **Markdown:** Multiple sections per member kind (Properties, Methods, Events...), overloads expanded, docs inline

This isn't a data model problem — it's a presentation structure difference. The `ApiOutputFormatter` methods that build each view are doing real work (grouping, truncation, signature extraction). Collapsing them would add complexity, not remove it.

## Priority

1. **Find service refactor** — Highest impact. Deletes the most command-level code, makes FindCommand match the pattern of other commands.
2. **Package** — Low priority. Current architecture is clean. The view wrapper is thin.
3. **Type** — No change needed. Structural mismatch is inherent to the different output modes.

## Relationship to -S Unification

The deferred `-S`/`-s` unification (pushing section filters upstream for perf) intersects with this work. If the service returns `TypeFindResult[]` directly, the `-S` projection can be applied at the view level without the service knowing about it — Markout's `MarkoutProjection` handles column filtering at serialization time. The `-s` section filtering (which skips entire computation paths) remains a service-level concern and can coexist.
