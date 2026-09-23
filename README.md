# dotnet-inspect

Inspect .NET libraries, NuGet packages, restored projects, and platform
assemblies without loading inspected code. Explore metadata, APIs,
dependencies, source, implementation details, and version differences from the
CLI or browser.

**Try it online:** [production demo][api-demo] ·
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

## Daily driver

### Inspect an API

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json@10.0.0 Serialize:1 --tfm net10.0
```

[Open the same API in Inspect Web][api-demo].

### Review package dependencies

```bash
dnx dotnet-inspect -y -- depends \
  --package System.Text.Json@10.0.0 --tfm net10.0
```

[Open the same dependency view in Inspect Web][package-dependencies].

### Trace type dependencies

```bash
dnx dotnet-inspect -y -- depends JsonSerializer \
  --package System.Text.Json@10.0.0 --tfm net10.0
```

[Open the same type dependency view in Inspect Web][type-dependencies].

## Delightful demos

### Explore the .NET Options hub

```bash
dnx dotnet-inspect -y -- demo options-add-callgraph --mermaid
```

[Explore the same inbound call graph in Inspect Web][options-graph].

### Follow the .NET JSON number parser

```bash
dnx dotnet-inspect -y -- demo stj-getdecimal-callgraph --mermaid
```

[Explore the same runtime call graph in Inspect Web][get-decimal-graph].

### See how Aspire registers PostgreSQL

```bash
dnx dotnet-inspect -y -- demo aspire-postgres-callgraph --mermaid
```

[Explore the same resource registration graph in Inspect Web][aspire-postgres].

## Learn more

- [CLI reference and examples](docs/cli-reference.md)
- [Documentation and contributor routes](docs/README.md)
- Current agent guidance: `dotnet-inspect skill`
- Repository workflow: [AGENTS.md](AGENTS.md)

Requires the .NET 10 SDK or later. Licensed under MIT.

[api-demo]: https://dotnet-inspect.net/?w=eyJmIjoxLCJ0IjpbWyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpbWzBdXSwiYSI6MCwieCI6MCwidiI6ImFwaSIsInkiOiJTeXN0ZW0uVGV4dC5Kc29uLkpzb25TZXJpYWxpemVyIiwibSI6IjFkYzE0ZGQxZmIiLCJsIjpbIlN5c3RlbS5UZXh0Lkpzb24iXX0
[package-dependencies]: https://dotnet-inspect.net/?w=eyJmIjoxLCJ0IjpbWyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpbWzBdXSwiYSI6MCwieCI6MCwidiI6ImRlcGVuZGVuY2llcyJ9
[type-dependencies]: https://dotnet-inspect.net/?w=eyJmIjoxLCJ0IjpbWyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpbWzBdXSwiYSI6MCwieCI6MCwidiI6ImRlcGVuZGVuY2llcyIsInkiOiJTeXN0ZW0uVGV4dC5Kc29uLkpzb25TZXJpYWxpemVyIn0
[options-graph]: https://dotnet-inspect.net/?w=eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEyIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpbWzBdXSwiYSI6MCwieCI6MCwidiI6ImFwaSIsInkiOiJNaWNyb3NvZnQuRXh0ZW5zaW9ucy5PcHRpb25zOk1pY3Jvc29mdC5FeHRlbnNpb25zLkRlcGVuZGVuY3lJbmplY3Rpb24uT3B0aW9uc1NlcnZpY2VDb2xsZWN0aW9uRXh0ZW5zaW9ucyIsIm0iOiIxZTZiZmFmMmFlIiwiYyI6ImNhbGwtZ3JhcGgiLCJsIjpbIltcImFzcG5ldGNvcmUuYXBwXCIsXCJNaWNyb3NvZnQuRXh0ZW5zaW9ucy5PcHRpb25zLmRsbFwiXSJdfQ
[get-decimal-graph]: https://dotnet-inspect.net/?package=&w=eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEyIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpbWzBdXSwiYSI6MCwieCI6MCwidiI6ImFwaSIsInkiOiJTeXN0ZW0uVGV4dC5Kc29uOlN5c3RlbS5UZXh0Lkpzb24uSnNvbkVsZW1lbnQiLCJtIjoiY2ZkOTk4MGE2YyIsImMiOiJjYWxsLWdyYXBoIiwibCI6WyJbXCJuZXRjb3JlLmFwcFwiLFwiU3lzdGVtLlRleHQuSnNvbi5kbGxcIl0iXX0
[aspire-postgres]: https://dotnet-inspect.net/?package=Aspire.Hosting.PostgreSQL&w=eyJmIjoxLCJ0IjpbWyJBc3BpcmUuSG9zdGluZy5Qb3N0Z3JlU1FMIiwiMTMuNS4zIiwibmV0OC4wIixudWxsXV0sImciOltbMF1dLCJhIjowLCJ4IjowLCJ2IjoiYXBpIiwieSI6IkFzcGlyZS5Ib3N0aW5nLlBvc3RncmVzQnVpbGRlckV4dGVuc2lvbnMiLCJtIjoiZTVhNjZhMmJkOSIsImMiOiJjYWxsLWdyYXBoIiwibCI6WyJjb21waWxlOmxpYi9uZXQ4LjAvQXNwaXJlLkhvc3RpbmcuUG9zdGdyZVNRTC5kbGwiXX0
