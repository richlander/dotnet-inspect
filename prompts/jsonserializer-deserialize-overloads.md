# Prompt: JsonSerializer.Deserialize Overloads

Please tell me how many overloads there are for JsonSerializer.Deserialize in the System.Text.Json package. Show me the ones that are generic.

## Optimal Path (Expert, Markdown)

Use `-v:d` for detailed verbosity to see all signatures in a table:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json -m Deserialize -v:d
```

## Optimal Path (Expert, JSON)

```bash
# All Deserialize signatures
dotnet-inspect member JsonSerializer --package System.Text.Json -m Deserialize --json | jq '.members[] | .signature'

# Filter to generic overloads (return TValue)
dotnet-inspect member JsonSerializer --package System.Text.Json -m Deserialize --json | jq '.members[] | select(.signature | contains("TValue")) | .signature'

# Count total
dotnet-inspect member JsonSerializer --package System.Text.Json -m Deserialize --json | jq '.members | length'

# Or use dotted syntax
dotnet-inspect member -m JsonSerializer.Deserialize --package System.Text.Json --json | jq '.members | length'
```

## Discovery Path (Learning)

Doesn't know exact package name or wants to explore:

```bash
# Step 1: Find the package/type
dotnet-inspect find "JsonSerializer" --framework runtime

# Output shows: System.Text.Json.JsonSerializer in System.Text.Json.dll

# Step 2: See all members of the type (quick overview)
dotnet-inspect type JsonSerializer --platform System.Text.Json

# Step 3: Get full signatures with detailed verbosity
dotnet-inspect member JsonSerializer --platform System.Text.Json -m Deserialize -v:d
```

## Expected Output

Shows a table with all 40 Deserialize overloads. Generic overloads return `TValue`, non-generic return `object`.

## Key Learnings

- Use `-v:d` for detailed markdown output with full signature table
- Default verbosity (`-v:m`) summarizes as "40 overloads"
- Generic methods return `TValue`, non-generic return `object`
- Use `-m` (or `--member`) to filter to a specific member name
- Can use `--package` for NuGet or `--platform` for SDK assemblies
