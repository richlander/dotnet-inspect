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

### Using the Tool

| Document | Description |
| -------- | ----------- |
| [Overview](overview.md) | Minimum system and architecture context |
| [Architecture](architecture.md) | Tool overview, commands, and design philosophy |
| [LLM Design](llm-design.md) | Why output is structured for AI-assisted development |
| [Platform Components](platform-components.md) | Accessing SDK libraries vs NuGet packages |
| [Signals](assembly-audit.md) | Understanding Signals output and network scope flags |
| [PDB Acquisition](pdb-acquisition.md) | How symbols and SourceLink are resolved |
| [Sample References](sample-references.md) | Extracting code samples from XML docs |

### For Contributors

| Document | Description |
| -------- | ----------- |
| [Style Guide](design/style-guide.md) | Output formatting conventions |
| [Rendering Model](design/rendering-model.md) | Verbosity vs mode-switch flags: how output is controlled |
| [NuGet API](design/nuget.md) | NuGet API endpoints used by the tool |
| [Skill Guidance Taste](../taste/skill-guidance.md) | Good and bad examples for maintaining the embedded skill |

## Getting Started

```bash
# Install and run with dnx (like npx)
dnx dotnet-inspect -y -- --help

# Or install globally
dotnet tool install -g dotnet-inspect
dotnet-inspect --help
```
