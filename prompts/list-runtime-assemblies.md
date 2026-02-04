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

## Discovery Path (Learning)

```bash
# Step 1: See what frameworks are installed
dotnet-inspect platform

# Output: Lists runtime, aspnetcore, netstandard with versions

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
- `--framework netstandard` lists .NET Standard assemblies
- Use `--json` for machine-parseable output
