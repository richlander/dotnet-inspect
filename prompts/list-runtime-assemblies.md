# Prompt: List Runtime Assemblies

What assemblies are included in the .NET runtime?

## Optimal Path (Expert, Markdown)

```bash
dotnet-inspect platform --framework runtime
```

## Optimal Path (Expert, JSON)

```bash
dotnet-inspect platform --framework runtime --json | jq '.assemblies[].name'
```

## Why This Command

Use `platform` to list **assemblies** (DLL files) in a framework.
Use `find` to search for **types** (classes, interfaces) by name pattern.

❌ Wrong: `dotnet-inspect find "*" --framework runtime` — searches for types, not assemblies
✓ Right: `dotnet-inspect platform --framework runtime` — lists all assembly files

## Discovery Path (Learning)

```bash
# Step 1: See what frameworks are installed
dotnet-inspect platform

# Output: Lists runtime and aspnetcore with versions and assembly counts

# Step 2: List assemblies in runtime
dotnet-inspect platform --framework runtime

# Step 3: (Optional) List ASP.NET Core assemblies
dotnet-inspect platform --framework aspnetcore
```

## Expected Output

```
## runtime (10.0.1)

| Assembly |
|----------|
| Microsoft.CSharp.dll |
| System.Collections.dll |
| System.Console.dll |
| System.IO.dll |
| System.Linq.dll |
| System.Net.Http.dll |
| System.Runtime.dll |
| System.Text.Json.dll |
...
```

## Key Learnings

- `platform` with no args shows installed frameworks
- `--framework runtime` lists all runtime assemblies
- `--framework aspnetcore` lists ASP.NET Core assemblies
- Use `--json` for machine-parseable output
