# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

## Document Generic Type Syntax in llms.txt

Querying generic types requires the CLR-style backtick notation:

```bash
dotnet-inspect api 'Argument`1' --package System.CommandLine
dotnet-inspect api 'Option`1' --package System.CommandLine
dotnet-inspect api 'Dictionary`2' --package System.Collections
```

This is non-obvious and easy to get wrong. The llms.txt file should explicitly document:
1. The backtick-arity syntax (`Argument\`1` for `Argument<T>`)
2. That quotes are needed to escape the backtick in most shells
3. Examples for common generic types

Alternatively, consider supporting C#-style syntax as an alias:
```bash
dotnet-inspect api 'Argument<T>' --package System.CommandLine  # More intuitive
```

## Property Mutability Indicator

When showing properties, indicate whether they are settable:

```
| Property | Kind | Signature |
|----------|------|-----------|
| Description | property | `string Description { get; set; }` |
| Name | property | `string Name { get; }` |
```

Currently properties just show the type (`string Description`), but knowing if it's settable is critical for understanding initialization patterns. This caused confusion during a System.CommandLine conversion—I didn't realize `Description` was a settable property rather than a constructor parameter.

## Inherited Members Option

A `--inherited` flag to show members from base classes inline:

```bash
dotnet-inspect api Command --package System.CommandLine --inherited
```

Currently you have to separately query base classes (`Symbol`) to find inherited members like `Description` and `Name`. Flattening the inheritance chain would give a complete picture in one query.

## Constructor Emphasis Mode

When using `-m .ctor`, show constructors with parameter descriptions and common instantiation patterns:

```bash
dotnet-inspect api Command --package System.CommandLine -m .ctor --verbose
```

Could show:
- All constructor overloads prominently
- Which parameters are required vs optional
- A minimal instantiation example

This is the most common "how do I create this?" query pattern.

## Built-in Diff Command

Currently, comparing API surfaces between versions requires shell gymnastics:

```bash
diff <(dotnet-inspect api JsonSerializer --package System.Text.Json@9.0.0) \
     <(dotnet-inspect api JsonSerializer --package System.Text.Json@10.0.2)
```

A built-in diff command would be more ergonomic:

```bash
dotnet-inspect diff api JsonSerializer --package System.Text.Json@9.0.0..10.0.2
```

This could also provide richer output than plain diff—highlighting added/removed/changed members with semantic awareness rather than line-by-line comparison.

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
