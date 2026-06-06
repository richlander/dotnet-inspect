# dotnet-inspect Documentation

dotnet-inspect is a CLI tool for exploring .NET libraries and NuGet packages. It's designed for both humans and LLMs—the structured markdown output is easy to read and easy to parse.

The tool answers questions like:

- What methods does `JsonSerializer` have?
- What changed between v9 and v10 of a package?
- Where does this type come from?
- Was this library built by Microsoft or rebuilt by my distro?

Unlike decompilers, dotnet-inspect focuses on the **public API surface**—the contracts you code against, not implementation details. It pulls from multiple sources (libraries, PDBs, symbol servers, NuGet metadata) to give you a complete picture.

## Quick Example

```bash
$ dotnet-inspect type JsonSerializer --package System.Text.Json --shape

# System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)

System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)
   ├─ string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
   ├─ string Serialize(object? value, Type inputType, JsonSerializerOptions? options = null)
   ├─ void Serialize<TValue>(Stream utf8Json, TValue value, JsonSerializerOptions? options = null)
   └─ ...
```

## Documentation

### Current system docs

| Document | Need served |
| -------- | ----------- |
| [Overview](overview.md) | Minimum system and architecture context for humans and agents. |
| [Architecture](architecture.md) | Current command and metadata architecture. |
| [LLM Design](llm-design.md) | Current agent-facing output and workflow design. |
| [Progressive Disclosure](design/progressive-disclosure.md) | Current model for verbosity, `-D`/`-S`, opt-in sections, `-S All`, counts, and row limits. |
| [Platform Components](platform-components.md) | Accessing SDK libraries vs NuGet packages. |
| [Signals](assembly-audit.md) | Understanding Signals output and network scope flags. |
| [PDB Acquisition](pdb-acquisition.md) | How symbols and SourceLink are resolved. |
| [Sample References](sample-references.md) | Extracting code samples from XML docs. |

### Contributor docs

| Document | Need served |
| -------- | ----------- |
| [Style Guide](design/style-guide.md) | Output formatting conventions. |
| [Rendering Model](design/rendering-model.md) | Historical/current rendering model notes; prefer [Progressive Disclosure](design/progressive-disclosure.md) for current agent-facing behavior. |
| [Section Model](design/section-model.md) | Section selection design notes; use with [Progressive Disclosure](design/progressive-disclosure.md). |
| [Schema Query](design/schema-query.md) | `-D`/`-S` schema/query implementation notes. |
| [NuGet API](design/nuget.md) | NuGet API endpoints used by the tool. |
| [Version Resolution](design/version-resolution.md) | Package/platform version and cache behavior. |
| [Skill Guidance Taste](../taste/skill-guidance.md) | Good and bad examples for maintaining the embedded skill. |

### Design history and backlog

Some files under `docs/design/` and `docs/backlog*.md` were written during ideation. They are useful design history, but may not describe current CLI behavior. When current behavior matters, start with Overview, Architecture, Progressive Disclosure, the embedded skill, and tests.

## Getting Started

```bash
# Install and run with dnx (like npx)
dnx dotnet-inspect -y -- --help

# Or install globally
dotnet tool install -g dotnet-inspect
dotnet-inspect --help
```
