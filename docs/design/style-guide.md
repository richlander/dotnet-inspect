# dotnet-inspect output style guide

This document defines the output format conventions for `dotnet-inspect`. The primary goal is to produce output that is easily consumable by both humans and LLMs.

## Document structure

Document-form Markdown uses a subject heading, optional description, available
context fields, and content sections. Focused projections may omit the header
under [progressive disclosure](progressive-disclosure.md#section-selection).

```markdown
# Title

Optional description paragraph.

**Field1:** value
**Field2:** value

## Section Name

| Column | Column |
|--------|--------|
| data   | data   |
```

### 1. H1 title

When a document header is present, its single H1 names the
**subject — and only the subject**:

- **Type view:** `# Namespace.TypeName`
- **Selected member:** `# Namespace.TypeName.MemberName`
- **Package view:** `# PackageName`
- **Library view:** `# AssemblyName.dll`

The title says *what* you are looking at. Everything you'd want to know *about* it —
package, version, target framework, the exact assembly asset, kind, modifiers — belongs in
the compact field list directly below the title (see [Key-Value Fields](#3-key-value-fields)),
**not** in a title parenthetical.

Do not append a `(Package Version)` parenthetical. There is always more than one fact worth
knowing about provenance (you want the package *and* its version *and* the TFM *and* the asset
that produced the metadata), and a field list scales to that and stays machine-extractable,
where a parenthetical can legibly hold only one item and is awkward to parse. The package view
already follows this: `# System.Text.Json` with the rest in fields.

### 2. Description paragraph

An optional description immediately after the H1. Tool-authored documentation
summaries are plain paragraphs with no special formatting.

Package manifest descriptions are the exception: they are untrusted,
package-authored prose, so every line renders inside a Markdown blockquote. The
quotation is a security boundary rather than emphasis; it keeps headings,
tables, and other block syntax visibly inside package content instead of
letting them impersonate peer structures emitted by the tool.

```markdown
# System.Text.Json.JsonSerializer

Provides functionality to serialize objects or value types to JSON and deserialize JSON into objects or value types.
```

### 3. Key-value fields

Context is structured metadata with named fields, selectable with `--fields`.
Markout controls inline versus stacked layout; type/member document headers
use a compact `Label: value | Label: value` line.

Header presence follows the existing view and section-selection policy.
Default type and member-group documents retain acquisition context outside the
title in both Markdown and plaintext; an explicitly focused inventory does not
gain a context row. Selected members retain their existing Summary context.
The default type tree is a declaration-oriented shape, not a document header.

**When to use fields vs tables:**

- Use **fields** for top-level metadata about the subject (type, library, package)
- Use **tables** within named H2 sections for collections of related items (members, files, audit results)

Fields describe "what this thing is"; tables list "what this thing contains".

**Standard fields for type output:**

Provenance first — these answer "where did this metadata come from?" and carry what the title
parenthetical used to (plus the version, TFM, and asset that a single parenthetical could not):

- `Package: System.Text.Json` (available package identity, not an assembly name)
- `Version: 10.0.0` (available package or framework selection version)
- `TFM: net10.0` (the available target framework selection)
- `Library: lib/net10.0/System.Text.Json.dll` (the selected API input asset, package-relative when its acquisition root is retained; otherwise its acquired filesystem path)
- `Source: NuGet` (acquisition context such as NuGet, Project, Platform, or Library; not a source-code URL)

The fields describe the API input selected for the query, including a package
resolved through project assets. For a forwarded type, `Library` remains the
selected contract/facade asset; it is not silently replaced with the defining
assembly. Source and implementation acquisition continue to use the retained
defining-assembly descriptor under their existing owners. A header therefore
does not claim that every displayed definition or IL body is physically in
`Library`. [Platform assemblies](platform-assemblies.md#type-forwarders)
explains this distinction. These are display projections, not new resolution
authority.

Then the subject's own facts:

- `Kind: class` / `interface` / `struct` / `enum`
- `Modifiers: static, sealed` (only if modifiers exist)
- `Samples: N available` (when the view has sample-reference metadata)

```markdown
Package: System.Text.Json | Version: 10.0.0 | TFM: net10.0 | Library: lib/net10.0/System.Text.Json.dll | Source: NuGet | Kind: class | Modifiers: static
```

Fields always appear in a consistent order. Empty/null fields are omitted.

`ApiHeaderProvenanceTests` gates subject-only headings, retained context,
focused-inventory behavior, and acquired asset paths.
`Type_SingleType_MarkdownQuiet_RendersCompactSectionView`,
`Type_SingleType_PlaintextIncludesAcquisitionContext`, and
`Router_FullyQualifiedGenericPlatformType_PreservesContractSource` gate CLI
header and contract-asset disclosure.
`ApiServices_RetainsSelectedForwarderDescriptor` gates the separation from
defining-assembly acquisition. Typed API JSON and declaration trees retain their separate
output contracts; header presentation does not redefine them.

The `**Samples:**` field is a count indicator, not a guarantee that a target
exposes a `Samples` section. Discover the current section catalog before
requesting sample output; do not invent URLs from the count.

### 4. H2 sections

Tables and structured content appear under H2 headings. Section visibility is
controlled by the command's verbosity preset and explicit section selection.

```markdown
## Members

| Member | Kind | Signature |
|--------|------|-----------|
| Parse  | method | `JsonDocument Parse(string json)` |
```

When there is only a single table, the H2 heading may be omitted for brevity.

## Verbosity levels

The [progressive disclosure model](progressive-disclosure.md#verbosity) owns
verbosity presets and explicit section selection. Verbosity can also change
signature detail and documentation columns within a command's view:

| Level | Description | Sections |
| ----- | ----------- | -------- |
| Quiet | Compact identity/context, where supported | No automatic content sections |
| Minimal | Compact view (default) | One high-value base section |
| Normal | More detail about the same subject | Multiple base sections |
| Detailed | Extended base output | All applicable base sections, not every domain |

Note: The dotnet CLI uses minimal as the default verbosity level, not normal. This tool follows that convention.

Descriptions and optional fields depend on the view and available evidence.
Focused section output may omit the identity header. For single-type section
output, use `--markdown`; the default type tree supports `-v:m`, `-v:n`, and
`-v:d`, not `-v:q`.

### Documentation columns

Type listings, single-type views, member groups, and selected signatures have
different schemas; they are not one verbosity-independent table. A selected
member's `Signature` can include a `Description` from available XML
documentation. Single-type Markdown views enable available XML summaries at
normal verbosity and above. Columns follow the selected view and its available
documentation, not a separate documentation switch.

Use `-D` to discover section names and `-D <section>` to inspect a section's
schema rather than assuming a standalone `Documentation` section.
[Platform components](../platform-components.md#documentation-access) shows the
package and platform invocations.

## Table formatting

Tables use pipe-delimited markdown:

```markdown
| Member | Kind | Signature |
|--------|------|-----------|
| Parse  | method | `JsonDocument Parse(string json)` |
```

**Conventions:**

- Header row with column names
- Separator row with dashes
- Pipes in cell content escaped as `\|`
- Boolean values: `✓` (yes) and `✗` (no)

**Common table formats:**

- Member tables: names/selectors and signatures, with `Description` where the
  selected view includes available documentation
- Metadata tables: `| Property | Value |`
- Audit tables: `| File | Deterministic | SourceLink |`

## Code formatting

Signatures use inline backticks within tables:

```markdown
| Method | Signature |
|--------|-----------|
| Parse  | `JsonDocument Parse(string json)` |
```

Triple-backtick code blocks are not used for signatures. This keeps output compact and works well in table cells.

## Links

All links in output should be **raw URLs**, not markdown-formatted links. This ensures:

- LLMs can directly fetch and process the content
- Users can copy/paste URLs without extraction
- No ambiguity about what the link points to

### GitHub link formats

| Format | Example | Commit-Specific | Content | Redirect |
| ------ | ------- | --------------- | ------- | -------- |
| HTML blob (branch) | `github.com/.../blob/main/file.cs` | ❌ No | HTML page | None |
| HTML blob (commit) | `github.com/.../blob/{sha}/file.cs` | ✅ Yes | HTML page | None |
| raw.githubusercontent | `raw.githubusercontent.com/.../{sha}/file.cs` | ✅ Yes | Raw file | None |
| GitHub raw | `github.com/.../raw/{sha}/file.cs` | ✅ Yes | Raw file | 302 → raw.githubusercontent |

#### Comparison

**HTML blob (branch)** - `https://github.com/richlander/markout/blob/main/src/Markout/TreeNode.cs`

- ❌ Not commit-specific (may drift from package version)
- ❌ Returns HTML page, not raw content
- ❌ Useless for programmatic access (`curl` returns HTML/CSS/JS garbage)

**HTML blob (commit)** - `https://github.com/richlander/markout/blob/{sha}/src/Markout/TreeNode.cs`

- ✅ Commit-specific (matches package exactly)
- ❌ Returns HTML page, not raw content
- ❌ Useless for programmatic access (`curl` returns HTML/CSS/JS garbage)

**raw.githubusercontent** - `https://raw.githubusercontent.com/richlander/markout/{sha}/src/Markout/TreeNode.cs`

- ✅ Commit-specific
- ✅ Returns raw file content
- ✅ Direct link (no redirect)
- ❌ Harder to convert to browsable URL (different domain)

**GitHub raw (Preferred)** - `https://github.com/richlander/markout/raw/{sha}/src/Markout/TreeNode.cs`

- ✅ Commit-specific
- ✅ Returns raw file content (via 302 redirect to raw.githubusercontent.com)
- ⚠️ 302 (temporary) redirect - not 301 (permanent), so caching behavior is appropriate
- ✅ Trivial to convert to browsable URL: `raw` → `blob`

### Preferred format

Use `github.com/.../raw/{sha}/path` format for all source links.

**Rationale:**

1. Returns raw content (via redirect), suitable for LLMs and `curl`
2. Commit-specific, ensuring exact match with package version
3. Easy conversion to browsable URL by replacing `raw` with `blob`
4. Stays on github.com domain for consistency

**Example transformation to browsable link:**

```text
https://github.com/richlander/markout/raw/4bfea7c.../TreeNode.cs
                                      ^^^
                                       ↓
https://github.com/richlander/markout/blob/4bfea7c.../TreeNode.cs
```

### Output format

Links should appear as raw URLs, not markdown links. Line numbers are omitted for type-level source URLs since they point to arbitrary members rather than the type declaration:

```markdown
**Source:** https://github.com/richlander/markout/raw/4bfea7c.../TreeNode.cs

**Samples:**
- Tree rendering: https://github.com/richlander/markout/raw/4bfea7c.../WriterUsage.cs (region: `WriteTree`)
```

Not:

```markdown
**Source:** [TreeNode.cs:15](https://github.com/...)

**Samples:**
- [Tree rendering](https://github.com/...)
```

### Opting into blob URLs

Use `--blob` to switch from raw URLs to `/blob/` URLs for browser viewing:

```bash
# Default: /raw/ URLs (LLM-friendly, returns raw content via 302 redirect)
dotnet-inspect type TreeNode --package Markout -S "Source Files"

# With --blob: /blob/ URLs (browser-friendly, returns HTML page)
dotnet-inspect type TreeNode --package Markout -S "Source Files" --blob
```
