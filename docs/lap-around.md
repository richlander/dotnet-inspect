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
dotnet-inspect                              # tool help
dotnet-inspect --tips:d                     # more tips
dotnet-inspect --tips:q                     # quiet tips
dotnet-inspect 2>/dev/null                  # another way to quiet tips
DOTNET_INSPECT_TIPS=quiet dotnet-inspect    # another way to quiet tips
```

Tips are contextual suggestions written to `stderr` to guide the next step.

For example:

```bash
$ dotnet-inspect | grep Tips
Tip: package <package>   # inspect a NuGet package
Tip: llmstxt             # complete usage examples
Tip: --tips:d            # show more tips per command
```

## Packages

Metadata-oriented markdown documents:

```bash
dotnet-inspect System.Text.Json             # Metadata view for package (same as -v:m)
dotnet-inspect System.Text.Json@10.0.2      # Specify a version; positional and --version also works
dotnet-inspect System.Text.Json -v:d        # Metadata, statistics, and direct package dependencies
dotnet-inspect System.Text.Json -v:q        # Terse 3-line details about latest package version
```

Pure data:

```bash
dotnet-inspect System.Text.Json --files     # List all file in the package, package-root qualified, one per line
dotnet-inspect System.Text.Json --tfms      # Lists TFM folders in the package, one per line
dotnet-inspect System.Text.Json --files --tfm net10.0   # Lists files in a given TFM file, filesnames only, one per line
```

Fancy:

```bash
dotnet-inspect System.Text.Json --layout    # Tree view of all files in the package
```
