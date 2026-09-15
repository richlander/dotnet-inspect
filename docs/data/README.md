# docs/data

Static data files used by dotnet-inspect for package and assembly classification.

## Files

### nuget-top-packages.json

Top NuGet packages by download count, sourced from <https://www.nuget.org/stats/packages>.
The weekly/on-demand Deep Inspect `package-sweep` lane consumes a bounded rank
window from this list, resolves current stable versions, and records the exact
package/version/TFM provenance in its run artifact. The list selects packages;
it is not a quality baseline.

### Inspect Web storage budget census

`inspect-web-storage-budget-census-2026-09-14.tsv` records the exact immutable
NuGet artifacts used to validate Inspect Web's isolated package-storage
budgets. `archive_bytes` is the downloaded nupkg length.
`max_managed_target_bytes` is the largest sum of top-level DLL entries under
one `lib/<tfm>`, `ref/<tfm>`, or `runtimes/<rid>/lib/<tfm>` target. The first
ten rows resolve the current stable versions of ranks 1-10 above; the remaining
rows are large compiler and runtime-pack stress witnesses.

`inspect-web-workspace-budget-census-2026-09-15.tsv` measures the first complex
Workspace scenario: every member of the shipped
`package-set.microsoft-extensions` set. The file pins the exact stable
major-10 versions selected by the census and records package archive bytes plus
the shared package selector's surface and implementation role demands.
Regenerate intentionally with:

```bash
dotnet run -c Release eng/measure-inspect-web-workspace-budget.cs -- \
  --refresh docs/data/inspect-web-workspace-budget-census-2026-09-15.tsv
```

Reacquire the pinned immutable packages, rerun the real Workspace role
realization, and verify every recorded measurement with:

```bash
dotnet run -c Release eng/measure-inspect-web-workspace-budget.cs -- \
  --check docs/data/inspect-web-workspace-budget-census-2026-09-15.tsv
```

### Platform assembly lists

Flat lists of `.dll` filenames shipped with .NET 10.0. One filename per line (e.g. `System.Collections.dll`). Used to identify which assemblies belong to each platform pack, enabling de-duplication when resolving packages.

| File | Source | Count |
| ------ | -------- | ------- |
| `Microsoft.NETCore.App.Shared.txt` | `/usr/lib/dotnet/shared/Microsoft.NETCore.App/` | 172 |
| `Microsoft.AspNetCore.App.Shared.txt` | `/usr/lib/dotnet/shared/Microsoft.AspNetCore.App/` | 141 |
| `Microsoft.NETCore.App.Ref.txt` | `/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/` | 167 |
| `Microsoft.AspNetCore.App.Ref.txt` | `/usr/lib/dotnet/packs/Microsoft.AspNetCore.App.Ref/` | 140 |

The Shared lists include implementation assemblies (e.g. `System.Private.CoreLib.dll`) that are absent from the corresponding Ref lists, which contain only the public reference assemblies.
