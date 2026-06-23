# dotnet-inspect Output Style Guide

This document defines the output format conventions for `dotnet-inspect`. The primary goal is to produce output that is easily consumable by both humans and LLMs.

## Document Structure

All markdown output follows a consistent four-part structure:

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

### 1. H1 Title

Every output starts with a single H1 heading that names the **subject — and only the subject**:

- **Type view:** `# Namespace.TypeName`
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

### 2. Description Paragraph

An optional plain-text paragraph immediately after the H1. This is where documentation summaries appear. No special formatting (not a blockquote, not italicized).

```markdown
# System.Text.Json.JsonSerializer

Provides functionality to serialize objects or value types to JSON and deserialize JSON into objects or value types.
```

### 3. Key-Value Fields

Structured metadata as `**Label:** value` pairs, one per line. These fields form the stable "header" that `--fields-only` preserves.

**Line break handling:** Each field line ends with two trailing spaces to force a `<br>` in rendered markdown. Without the double space, consecutive field lines would collapse into a single paragraph.

**When to use fields vs tables:**

- Use **fields** for top-level metadata about the subject (type, library, package)
- Use **tables** within named H2 sections for collections of related items (members, files, audit results)

Fields describe "what this thing is"; tables list "what this thing contains".

**Standard fields for type output:**

Provenance first — these answer "where did this metadata come from?" and carry what the title
parenthetical used to (plus the version, TFM, and asset that a single parenthetical could not):

- `**Package:** System.Text.Json 10.0.0` (package name and resolved version; omitted for local-file sources)
- `**TFM:** net10.0` (the target framework the asset was selected for)
- `**Library:** lib/net10.0/System.Text.Json.dll` (the exact assembly asset the metadata came from — the in-package path for package sources, or the file path for a local library)

Then the subject's own facts:

- `**Kind:** class` / `interface` / `struct` / `enum`
- `**Modifiers:** static, sealed` (only if modifiers exist)
- `**Source:** https://...`
- `**Samples:** N available` (only with `--docs`, indicates samples exist)

```markdown
**Package:** System.Text.Json 10.0.0
**TFM:** net10.0
**Library:** lib/net10.0/System.Text.Json.dll
**Kind:** class
**Modifiers:** sealed
**Source:** https://github.com/.../JsonSerializer.cs
**Samples:** 2 available
```

Fields always appear in a consistent order. Empty/null fields are omitted.

The `**Samples:**` field is a count indicator. Sample URL tables belong in a
dedicated `Samples` section when package/docs metadata can provide trustworthy
links.

### 4. H2 Sections

Tables and structured content appear under H2 headings. Section visibility is controlled by verbosity level.

```markdown
## Members

| Member | Kind | Signature |
|--------|------|-----------|
| Parse  | method | `JsonDocument Parse(string json)` |
```

When there is only a single table, the H2 heading may be omitted for brevity.

## Verbosity Levels

Verbosity controls which H2 sections appear, not which fields appear:

| Level | Description | Sections |
| ----- | ----------- | -------- |
| Quiet | Title and fields only | None |
| Minimal | Compact section view (default) | Summary sections only |
| Normal | Full output | All standard sections |
| Detailed | Extended output | All sections including audit details |

Note: The dotnet CLI uses minimal as the default verbosity level, not normal. This tool follows that convention.

The H1, description, and fields are always present regardless of verbosity.

### Verbosity-independent views

Some views use the same layout at all verbosity levels because differentiation would not be meaningful. The **types listing** (full-library view) is verbosity-independent: it always shows per-kind sections (`## Classes`, `## Structs`, etc.) with `Type | Members` columns. The only variation is `--docs` adding a Description column. Verbosity differentiation is reserved for the **member view** (per-type), where quiet groups by name, minimal abbreviates signatures, and normal/detailed show full signatures.

## Table Formatting

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

- Member tables: `| Member | Kind | Signature |` (+ `| Description |` with `--docs`)
- Metadata tables: `| Property | Value |`
- Audit tables: `| File | Deterministic | SourceLink |`

## Code Formatting

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

### GitHub Link Formats

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

### Preferred Format

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

### Output Format

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

### Opting into Blob URLs

Use `--blob` to switch from raw URLs to `/blob/` URLs for browser viewing:

```bash
# Default: /raw/ URLs (LLM-friendly, returns raw content via 302 redirect)
dotnet-inspect type TreeNode --package Markout -S "Source Files"

# With --blob: /blob/ URLs (browser-friendly, returns HTML page)
dotnet-inspect type TreeNode --package Markout -S "Source Files" --blob
```
