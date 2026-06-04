# Auto-generated DocumentSchema

The discovery system (`-D`, `--fields`, `--columns`) uses `DocumentSchema` to answer
questions about what sections and items a view model contains. Previously, each command
maintained a hand-written `CreateSchemaMap()` method that duplicated information already
captured by Markout's source generator. This doc describes the replacement: a bridge
method that derives `DocumentSchema` automatically from the source-generated schema.

## Problem

Adding or renaming a section required touching two places:

1. The view model class (Markout attributes like `[MarkoutSection]`)
2. The manual `CreateSchemaMap()` in the section descriptors file

These could drift. The Markout source generator already walks the same attributes to
emit serialization code and `MarkoutSchemaInfo` metadata. The schema map was a redundant
second source of truth.

## Solution: `MarkoutSchemaInfo.ToDocumentSchema()`

A single method on `MarkoutSchemaInfo` that converts the source-generated rendering
metadata into a `DocumentSchema`:

```csharp
var schema = MarkoutContext.Default
    .GetSchemaInfo<LibraryInspectionView>()!
    .ToDocumentSchema();
```

This replaces all four `CreateSchemaMap()` methods and their 10 call sites.

### Rendering string → DocumentSchema mapping

The source generator produces rendering strings like `H2 Section "Dependencies" (table)`.
`ToDocumentSchema()` parses the section name and parenthetical content type, then maps
to the appropriate `DocumentSchema.Add()` call:

| Content type | ItemKind | Items source |
| ------------ | -------- | ------------ |
| `(table)` | `column` | Children with `Column` rendering → DisplayName |
| `(subsections)` | `column` | Children with `Column` rendering → DisplayName |
| `(field)` | `field` | The property's own DisplayName |
| `(fields)` | `field` | Nested children with `Field` rendering (recursive) |
| `(tree)` | `tree` | Children with `Column` rendering → DisplayName |
| `(field table)` | — | Section only (dynamic FieldCollection) |
| `(code block)` | — | Section only (no queryable items) |
| `(bar chart)` | — | Section only |
| `(distribution)` | — | Section only |
| no parens | — | Section only (e.g. bullet list) |

### Duplicate section merging

Multiple properties can share the same section name. For example, `TypeView` has 7+
properties all mapped to "Constructors" (table, docs table, overloads, code sections).
When a section name appears again, items are merged (union preserving order, deduplicated
case-insensitively). If an earlier property was section-only (e.g. code block) and a later
one has items (e.g. table with columns), the item kind is upgraded.

### Known limitations vs manual schemas

| Aspect | Manual | Auto-generated |
| ------ | ------ | -------------- |
| Field table sections (Summary, Package) | Explicit field names | Section-only (dynamic content) |
| Bullet list sections (Files, Non-normalized Paths) | Explicit item name | Section-only (no column metadata) |
| Tree sections (Dependencies, Library Refs Transitive) | Explicit item name | Section-only (TreeNode has no columns) |
| New sections/fields added to view models | Manual update needed | Automatic |
| Removed sections/fields | Manual cleanup needed | Automatic |

The "section-only" entries still appear in `-D` discovery output. The only loss is
item-level drill-down for these sections, which was manually fabricated anyway
(e.g. the manual schema listed "Path" as an item for Files, but that name didn't
correspond to any queryable property).

### Improvements over manual schemas

The auto-generated schemas are more accurate in several places:

- **Symbols** section now includes Warning and Recommendation fields
- **Vulnerabilities** correctly shows column names from PackageVulnerability
  (Severity, Cve Id, Summary, Advisory Url, Ghsa Id) instead of a fake list item
- **TypeView** sections include Select, Description, Overloads, and other columns
  that were missing from the manual schema
- **Type Forwarders** section correctly appears in CliApiSurface
- **SourceLink Missing Files** section now discoverable (was absent from manual map)

## Files changed

### Markout (upstream library)

- `src/Markout/MarkoutSchemaInfo.cs` — Added `ToDocumentSchema()` and private helpers
- `tests/Markout.Tests/ProjectionTests.cs` — 9 tests covering all content type mappings,
  section merging, and edge cases

### dotnet-inspect

**Replaced** (10 call sites across 5 files):

- `Commands/PackageCommand.cs` — 3 sites
- `Commands/AssemblyCommand.cs` — 2 sites
- `Commands/ApiCommand.cs` — 1 site (type + member schemas)
- `CommandLine/Commands/RouterCommandDefinition.cs` — 1 site
- `CommandLine/Commands/ApiCommandDefinitions.cs` — 2 sites

**Deleted** (4 methods across 3 files):

- `Sections/LibrarySections.CreateSchemaMap()`
- `Sections/PackageSectionDescriptors.CreateSchemaMap()`
- `Sections/ApiTypeSectionDescriptors.CreateSchemaMap()`
- `Sections/ApiMemberSectionDescriptors.CreateSchemaMap()`

## View model → schema mapping

| Call site pattern | View type |
| ----------------- | --------- |
| `MarkoutContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema()` | Library command |
| `MarkoutContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema()` | Package command |
| `MarkoutContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema()` | API type-list |
| `MarkoutContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema()` | API type-detail |

## Relationship to schema-query design doc

The [schema-query](schema-query.md) doc described a five-step implementation path.
This change completes step 4 (source-generated schema) by implementing
`ToDocumentSchema()` as a runtime bridge rather than extending the source generator
itself. The generator already emits the rendering metadata; the bridge method converts
it at runtime. This avoids generator changes while achieving the same goal: one source
of truth for both rendering and querying.
