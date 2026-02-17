# Prompt: Find Exception Types

Find all exception types in System.Runtime.

## Optimal Path (Expert, Markdown)

```bash
dotnet-inspect type --platform System.Runtime -t "*Exception*"
```

## Optimal Path (Expert, JSON)

```bash
dotnet-inspect type --platform System.Runtime -t "*Exception*" --json | jq '.types[].name'
```

## Discovery Path (Learning)

```bash
# Step 1: Search across runtime for Exception types
dotnet-inspect find "*Exception*" --framework runtime -n 20

# Shows exceptions from many assemblies

# Step 2: Focus on System.Runtime assembly
dotnet-inspect type --platform System.Runtime -t "*Exception*"

# Lists all exception types in System.Runtime
```

## Expected Output

```text
# System.Runtime

**Types:** 94  

| Type | Kind | Members |
|------|------|---------|
| System.AccessViolationException | class | 3 |
| System.ArgumentException | class | 9 |
| System.ArgumentNullException | class | 6 |
| System.ArgumentOutOfRangeException | class | 16 |
| System.Exception | class | 14 |
| System.InvalidOperationException | class | 3 |
| System.NullReferenceException | class | 3 |
...
```

## Key Learnings

- `-t` uses glob patterns for type names in `type` command
- Combine `--platform` with `-t` to search within one assembly
- `find` searches across all assemblies in a framework
- Use `-n` to limit results when exploring
