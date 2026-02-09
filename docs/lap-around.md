# Lap around `dotnet-inspect`

`dotnet-inspect` exposes a lot of functionality. The following "lap around" walkthough demonstrates a set of highlights and also a natural progression of using the commands.

The tool operates of three types of content:

- Packages
- Libraries
- Platform libraries

Packages download honors `nuget.config`.

There are mulptiple ways to run the tool, via `dnx`, as an installed tool (`dotnet-inspect` / `dotnet inspect`), or from source via `dotnet run`. These commands use `dotnet-inspect` as a tool. Only the commands are recorded, not the output.

## Tool help

No args launch:

```bash
dotnet inspect                          # tool help
```

## Packages

Metadata-oriented markdown documents:

```bash
dotnet inspect System.Text.Json         # Metadata view for package (same as -v:m)
dotnet inspect System.Text.Json -v:d    # Metadata, statistics, and direct package dependencies
dotnet inspect System.Text.Json -v:q    # Terse 3-line details about latest package version
```

Pure data:

