# Project Instructions

## Testing

This project uses **xunit v3** with `OutputType Exe`. Tests MUST be run with `dotnet run`, NOT `dotnet test`:

```bash
dotnet run --project src/dotnet-inspect.Tests
dotnet run --project src/DotnetInspector.Decompiler.Tests
dotnet run --project src/DotnetInspector.Services.Tests
dotnet run --project tests/DotnetInspector.Metadata.Tests
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests
```

`dotnet test` will silently produce no output and appear to hang.

Some tests require `ilasm`/`ildasm` and will skip if not installed.

`DotnetInspector.ILRoundtrip.Tests` requires the vendored managed ILAssembler
(orphan branch `vendor/ilassembler`); run `eng/restore-ilassembler.sh` once to
materialize it at `external/ILAssembler`. Edits under `external/ILAssembler`
commit directly to the vendor branch — see its README for the fork policy.

## Building

```bash
dotnet build src/dotnet-inspect -c Release
```

## File-Based Apps

Do NOT use `dotnet-script`, `dotnet script`, `dotnet-fsi`, or `.csx` files. Use .NET 10 file-based apps instead. Prefer file-based apps over Python unless a specific Python library is needed.

Run with `dotnet run /tmp/check.cs`. Write throwaway scripts to `/tmp/`.

To reference a project:

```csharp
#:project ../src/MyLib/MyLib.csproj

using MyLib.Domain;

var items = await MyService.LoadAsync();
Console.WriteLine($"Found {items.Count} items");
```

## Branching

The `main` branch is protected. Create feature branches like `feature/issue-3-assembly-references` or `fix/null-reference-in-parser`.

## Markdown Linting

All markdown files must pass `markdownlint` before committing. Run the auto-fixer first:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```

Run `markdownlint` on all changed markdown files when preparing a PR.
