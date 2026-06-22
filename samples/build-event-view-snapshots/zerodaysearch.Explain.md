# Explain

Selected diagnostics: 51
Matched clusters: 1

## System.CommandLine API shape mismatch

Cluster: `system-commandline-api-mismatch`

The project appears to use one System.CommandLine API shape while referencing another version.

### Applies to

| Severity | Code | Count | Example |
| --- | --- | ---: | --- |
| error | CS1061 | 34 | 'Command' does not contain a definition for 'AddOption' and no accessible extension method 'AddOpti… |
| error | CS0103 | 7 | The name 'Handler' does not exist in the current context |
| error | CS1739 | 6 | The best overload for 'Option' does not have a parameter named 'description' |
| error | CS1729 | 4 | 'Argument<string>' does not contain a constructor that takes 2 arguments |

### Likely cause

The source likely targets older System.CommandLine APIs such as AddOption/AddArgument/Handler, but the referenced package exposes the newer API shape.

### First fixes

1. Check the referenced System.CommandLine package version.
2. Update command construction to the referenced API shape.
3. Fix repeated API-shape errors together instead of one line at a time.
4. After updating one command pattern, rebuild and inspect the remaining diagnostic types.

### Useful follow-up

- `dotnet-inspect build <log> -S Errors --code CS1061 --tsv`
- `dotnet-inspect build <log> -S Details --code CS1061 --markdown`
