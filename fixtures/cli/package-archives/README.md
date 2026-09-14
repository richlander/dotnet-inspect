# CLI package fixtures

These unchanged NuGet archives preserve real package shapes used by CLI
compatibility tests without requiring network access.

| Archive | Source | License |
| --- | --- | --- |
| `avalonia.12.1.2.nupkg` | [NuGet flat container](https://api.nuget.org/v3-flatcontainer/avalonia/12.1.2/avalonia.12.1.2.nupkg) | MIT expression in `Avalonia.nuspec` |

SHA-256:

```text
99987414c63ac3993346a84a852006df963ff839f96d140557ee31f8850db08f  avalonia.12.1.2.nupkg
```

`Avalonia.Markup.dll` forwards `Avalonia.Data.MultiBinding` to sibling compile
asset `Avalonia.Base.dll`. Keep the complete archive so package selection,
forwarder resolution, and portable source-coordinate retention exercise the
published NuGet shape together.
