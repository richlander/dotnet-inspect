# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

## NuGet.config Support

Add support for reading `NuGet.config` files when resolving packages. Currently package resolution only uses the default NuGet cache and nuget.org. This would enable:

- Using local package sources for development/testing
- Using private feeds (Azure Artifacts, GitHub Packages, etc.)
- Respecting repository-specific package configurations

## Inherited Members Option

A `--inherited` flag to show members from base classes inline:

```bash
dotnet-inspect api Command --package System.CommandLine --inherited
```

Currently you have to separately query base classes (`Symbol`) to find inherited members like `Description` and `Name`. Flattening the inheritance chain would give a complete picture in one query.

## Example Snippets

A `--examples` flag could show basic usage patterns for types and methods:

```bash
dotnet-inspect api JsonSerializer --package System.Text.Json --examples
```

This could pull from XML documentation `<example>` tags if present, or generate minimal usage patterns from constructor/method signatures. Would significantly accelerate LLM code generation by providing working starting points.

## Constructor Parameter Context

When viewing a class, it would help to see what the constructor parameters represent—especially for dependency injection scenarios. Understanding "what do I need to construct this?" is a common LLM task.

Could show required vs optional parameters, and flag parameters that are typically injected vs provided directly.

## Related Types Discovery

When inspecting a type like `JsonSerializer`, suggest related types that are commonly used together:

- `JsonSerializerOptions`
- `JsonTypeInfo<T>`
- `JsonSerializerContext`

This would help LLMs understand the ecosystem around a type without multiple exploratory queries.

## Output to File

An `--out` flag to write results directly to a file:

```bash
dotnet-inspect api --package System.Text.Json --out api-surface.md
dotnet-inspect package System.Text.Json --json --out package-info.json
```

Useful for:
- Saving API surfaces for later reference or diffing
- Generating documentation artifacts
- Caching expensive queries locally
- Piping to other tools that prefer file input over stdin
