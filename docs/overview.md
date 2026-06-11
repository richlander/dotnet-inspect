# dotnet-inspect overview

`dotnet-inspect` is a CLI tool for inspecting .NET packages, platform libraries, local assemblies, public APIs, dependencies, SourceLink/symbol provenance, and version-to-version API changes.

It is built for both humans and agents. Markdown is the default output because headings, compact context rows, tables, and code fences are readable and easy for agents to quote. JSON, `--table`, and `--tsv` are available when structured automation or compact row output is more useful.

## Core architecture

- `src/dotnet-inspect/` contains the CLI, command routing, parsers, options, output views, section descriptors, and inspectors.
- `src/DotnetInspector.Metadata/` reads PE metadata, API surfaces, SourceLink/PDB data, method classification, and assembly details.
- `src/DotnetInspector.Packages/` handles NuGet package extraction, package/source caches, feeds, symbol package acquisition, and version resolution.
- `src/DotnetInspector.Services/` contains shared services such as platform/package resolution, dependency resolution, signatures, source fetching, and nuspec parsing.
- `src/DotnetInspector.Decompiler/` emits lowered C#, raw IL, and annotated IL from method bodies.

## Agent contract

Agents working in this repo should preserve these principles:

1. Prefer factual inspection over guesses; keep `dotnet-inspect skill` guidance current with CLI behavior.
2. Keep output section ownership clear: sectioned output should avoid duplicated rows across sections.
3. Preserve behavior-safe defaults. Expensive network/source checks should stay explicit or capability-gated.
4. Preserve the progressive-disclosure model: verbosity for curated defaults, `-D`/`-S` for query and backpressure, opt-in sections for expensive work, and `-S @All` only for intentional exhaustive output.
5. Use built-in query/limiter concepts (`-D`, `-S`, `--columns`, `--fields`, `--count`, `--rows`) instead of shell-pipe workarounds when possible.
6. Treat cache schema changes as versioned categories and invalidate/clean stale metadata caches deliberately.

## Important systems

- [Architecture](architecture.md): command and metadata architecture.
- [Signals](assembly-audit.md): package/library signal semantics and network scope.
- [PDB acquisition](pdb-acquisition.md): symbols and SourceLink acquisition.
- [Rendering model](design/rendering-model.md): output mode and verbosity design.
- [Progressive disclosure](design/progressive-disclosure.md): verbosity, `-D`/`-S`, opt-in sections, `-S @All`, and limiter behavior.
- [Integrations](design/integrations.md): library ecosystem integration roll-ups and focused API currency.
- [Section model](design/section-model.md): section selection and query behavior.
- [Member ordering](design/member-order.md): canonical type/member section order and member-kind mapping.
- [Version resolution](design/version-resolution.md): package/platform version and cache behavior.
- [Skill guidance taste](../taste/skill-guidance.md): how to maintain the embedded agent skill.
