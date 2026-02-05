# Prompt: API Diff Between Versions

What API changes happened in JsonSerializer between System.Text.Json 9.0 and 10.0?

## Optimal Path (Expert, Markdown)

```bash
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0
```

## Optimal Path (Expert, JSON)

```bash
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0 --json
```

## Discovery Path (Learning)

```bash
# Step 1: Find the type
dotnet-inspect find JsonSerializer --framework runtime

# Step 2: Compare versions
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.0
```

## Expected Output

```text
## API Diff: JsonSerializer

**9.0.0** → **10.0.0**

**Summary:** +9 added, -0 removed

### Added

+ `IAsyncEnumerable<TValue> DeserializeAsyncEnumerable(PipeReader utf8Json, ...)`
+ `ValueTask<TValue> DeserializeAsync(PipeReader utf8Json, ...)`
...
```

## Key Learnings

- Version range syntax: `Package@v1..v2`
- Shows added (+) and removed (-) members
- Works with both `--package` and `--platform`
- For platform: `--platform Assembly@v1..v2`
