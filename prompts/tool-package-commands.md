# Prompt: Tool Package Commands

What commands does the dotnet-ef tool provide?

## Optimal Path (Expert, Markdown)

```bash
dotnet-inspect dotnet-ef -v:d
```

Look for "Tool Commands" in the Metadata section.

## Optimal Path (Expert, JSON)

```bash
dotnet-inspect dotnet-ef --json | jq '.toolCommands'
```

## Discovery Path (Learning)

```bash
# Step 1: Basic package info
dotnet-inspect dotnet-ef

# Output shows: "Type: Tool" in compact line

# Step 2: Get detailed info including commands
dotnet-inspect dotnet-ef -v:d

# Shows Tool Commands in metadata table
```

## Expected Output

The Metadata table includes:

```text
| Tool Commands | dotnet-ef |
| Framework Dependent | yes |
```

And the Files section shows:

```text
- tools/net8.0/any/dotnet-ef.dll
```

## Key Learnings

- Tool packages show "Type: Tool" in output
- `-v:d` shows tool commands and framework dependency
- JSON has `toolCommands` array and `isToolPackage` boolean
- Tool packages have `tools/` directory instead of `lib/`
