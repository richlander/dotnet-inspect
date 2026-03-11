# docs/data

Static data files used by dotnet-inspect for package and assembly classification.

## Files

### nuget-top-packages.json

Top NuGet packages by download count, sourced from <https://www.nuget.org/stats/packages>.

### Platform assembly lists

Flat lists of `.dll` filenames shipped with .NET 10.0. One filename per line (e.g. `System.Collections.dll`). Used to identify which assemblies belong to each platform pack, enabling de-duplication when resolving packages.

| File | Source | Count |
| ------ | -------- | ------- |
| `Microsoft.NETCore.App.Shared.txt` | `/usr/lib/dotnet/shared/Microsoft.NETCore.App/` | 172 |
| `Microsoft.AspNetCore.App.Shared.txt` | `/usr/lib/dotnet/shared/Microsoft.AspNetCore.App/` | 141 |
| `Microsoft.NETCore.App.Ref.txt` | `/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/` | 167 |
| `Microsoft.AspNetCore.App.Ref.txt` | `/usr/lib/dotnet/packs/Microsoft.AspNetCore.App.Ref/` | 140 |

The Shared lists include implementation assemblies (e.g. `System.Private.CoreLib.dll`) that are absent from the corresponding Ref lists, which contain only the public reference assemblies.
