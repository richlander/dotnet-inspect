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
$ dotnet-inspect api JsonSerializer --package System.Text.Json -m Serialize

# System.Text.Json.JsonSerializer (System.Text.Json 10.0.2)

**Kind:** class
**Modifiers:** static, abstract, sealed

## Members

| Member | Kind | Signature |
|--------|------|-----------|
| Serialize | method | `string Serialize(object value, Type inputType, JsonSerializerOptions options)` |
| Serialize | method | `string Serialize(TValue value, JsonSerializerOptions options)` |
| Serialize | method | `void Serialize(Stream utf8Json, TValue value, JsonSerializerOptions options)` |
...
```

## Documentation

### Using the Tool

| Document | Description |
| -------- | ----------- |
| [Architecture](architecture.md) | Tool overview, commands, and design philosophy |
| [LLM Design](llm-design.md) | Why output is structured for AI-assisted development |
| [Platform Components](platform-components.md) | Accessing SDK libraries vs NuGet packages |
| [Audit](assembly-audit.md) | Understanding `audit` output and network scope flags |
| [PDB Acquisition](pdb-acquisition.md) | How symbols and SourceLink are resolved |
| [Sample References](sample-references.md) | Extracting code samples from XML docs |

### For Contributors

| Document | Description |
| -------- | ----------- |
| [Style Guide](design/style-guide.md) | Output formatting conventions |
| [Rendering Model](design/rendering-model.md) | Verbosity vs mode-switch flags: how output is controlled |
| [NuGet API](design/nuget.md) | NuGet API endpoints used by the tool |

## Getting Started

```bash
# Install and run with dnx (like npx)
dnx dotnet-inspect -y -- --help

# Or install globally
dotnet tool install -g dotnet-inspect
dotnet-inspect --help
```
