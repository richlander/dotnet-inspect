---
id: json-output
description: Machine-readable JSON output for tooling and agent integration
commands: [--json, --compact]
areas: [json, output, agents, scripting, integration]
---

# JSON Output

> The `--json` flag produces structured JSON output for programmatic consumption. Combined with `--compact` for minified output, or piped through `jq` for extraction. Available on most commands — `package`, `type`, `member`, `find`, `extensions`, `implements`.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=json-output
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

## 1. Package metadata as JSON

> Goal: Get structured package information for tooling.

### 1a. Pretty-printed JSON

```prompt
Get System.CommandLine package info as JSON.
```

```bash
dotnet-inspect package System.CommandLine -v:q --json -n 10
```

```expect
{
"package_name"
"version"
"source"
```

### 1b. Compact JSON (single line)

```bash
dotnet-inspect find 'Command*' --package System.CommandLine --json --compact
```

```expect
[{"type":"Command"
```

```query
python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d))"
```

## 2. Type information as JSON

> Goal: Get type details in a structured format for agent consumption.

```prompt
Get the JsonSerializer type info as structured JSON.
```

```bash
dotnet-inspect type System.Text.Json JsonSerializer --json -v:q -n 15
```

```expect
{
"namespace": "System.Text.Json"
"name": "JsonSerializer"
"kind": "class"
```

## 3. Find results as JSON

> Goal: Search results as structured data, useful for feeding into other tools.

```bash
dotnet-inspect find 'JsonSer*' --json --compact
```

```expect
[{"type":"JsonSerializer"
```

## 4. JSON with jq pipelines

> Goal: Extract specific fields from JSON output using jq.

### 4a. Extract type names

```bash
dotnet-inspect find 'Command*' --package System.CommandLine --json --compact | python3 -c "import json,sys; [print(t['full_name']) for t in json.load(sys.stdin)]"
```

```expect
System.CommandLine.Command
System.CommandLine.Parsing.CommandLineParser
```

### 4b. Count results

```bash
dotnet-inspect find 'Command*' --package System.CommandLine --json --compact | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

```expect
4
```
