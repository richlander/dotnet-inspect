# dotnet-inspect

Inspect .NET libraries, NuGet packages, restored projects, and platform
assemblies without loading inspected code. Explore metadata, APIs,
dependencies, source, implementation details, and version differences from the
CLI or browser.

**Try it online:** [production](https://dotnet-inspect.net) ·
[working build](https://dotnet-inspect.ca) ·
[nightly CoreCLR interpreter](https://coreclr.dotnet-inspect.ca)

## Install or run

Install the global tool:

```bash
dotnet tool install -g dotnet-inspect
dotnet-inspect package System.Text.Json
```

Or run it without installing:

```bash
dnx dotnet-inspect -y -- package System.Text.Json
```

## Daily drivers

### Inspect a package

```bash
dnx dotnet-inspect -y -- package System.Text.Json
```

[Explore packages in Inspect Web](https://dotnet-inspect.net).

### Find and inspect an API

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json -m Serialize
```

[Explore APIs in Inspect Web](https://dotnet-inspect.net).

### Compare versions

```bash
dnx dotnet-inspect -y -- diff --package Markout@0.33.0..0.35.2
```

[Compare libraries in Inspect Web](https://dotnet-inspect.net).

### Understand dependencies

```bash
dnx dotnet-inspect -y -- depends Stream --markdown --mermaid
```

[Explore dependencies in Inspect Web](https://dotnet-inspect.net).

## Fancy demos

### Query rendered C# body shapes

```bash
dnx dotnet-inspect -y -- library System.Text.Json \
  --where "Kind=ObjectCreationExpression" \
  --columns "Member;Token;Match" --rows 3
```

[Inspect implementation details in Inspect Web](https://dotnet-inspect.net).

### Read authored or reconstructed source

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json Serialize:1 -S @Source
```

[Browse source in Inspect Web](https://dotnet-inspect.net).

### Create a browser-ready inspection

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json@10.0.0 Serialize:1 \
  --tfm net10.0 --share
```

[Open shared inspections in Inspect Web](https://dotnet-inspect.net).

## Learn more

- [CLI reference and examples](docs/cli-reference.md)
- [Documentation and contributor routes](docs/README.md)
- Current agent guidance: `dotnet-inspect skill`
- Repository workflow: [AGENTS.md](AGENTS.md)

Requires the .NET 10 SDK or later. Licensed under MIT.
