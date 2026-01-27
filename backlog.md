# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

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

## Parameter Names in Signatures

Current output shows types but omits parameter names:

```
| Serialize | method | `void Serialize(Stream, TValue, JsonSerializerOptions)` |
```

For LLMs generating code, parameter names are crucial for readability and understanding intent:

```
| Serialize | method | `void Serialize(Stream utf8Json, TValue value, JsonSerializerOptions? options = null)` |
```

This would help LLMs generate more idiomatic code without guessing parameter names or consulting documentation.

## Type Hierarchy Display

When inspecting a type, showing its inheritance chain and implemented interfaces inline would help LLMs understand type relationships without a separate query.

Current:
```
## Spectre.Console.ProgressContext
*sealed class*
```

Enhanced:
```
## Spectre.Console.ProgressContext
*sealed class : IDisposable*
*inherits: Object*
```

The `--interfaces` flag exists but requires a separate invocation. Consider making this the default or adding a `--hierarchy` flag for full inheritance chain.

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
