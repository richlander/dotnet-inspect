# dotnet-inspect Output Style Guide

This document defines the output format conventions for `dotnet-inspect`. The primary goal is to produce output that is easily consumable by both humans and LLMs.

## Links

All links in output should be **raw URLs**, not markdown-formatted links. This ensures:
- LLMs can directly fetch and process the content
- Users can copy/paste URLs without extraction
- No ambiguity about what the link points to

### GitHub Link Formats

| Format | Example | Commit-Specific | Content | Redirect |
|--------|---------|-----------------|---------|----------|
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
```
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

### Opting into Browsable URLs

Use `--browsable-urls` to switch from `/raw/` to `/blob/` URLs for browser viewing:

```bash
# Default: /raw/ URLs (LLM-friendly, returns raw content via 302 redirect)
dotnet-inspect api TreeNode --package Markout --docs

# With --browsable-urls: /blob/ URLs (browser-friendly, returns HTML page)
dotnet-inspect api TreeNode --package Markout --docs --browsable-urls
```
