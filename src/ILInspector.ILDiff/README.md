# ILInspector.ILDiff

`ILInspector.ILDiff` owns IL body and assembly comparison over decoded
`ILInspector.Instructions` streams. It contains canonicalization, body and
member alignment, Finding projection, typed failures, and producer-owned diff
presentation.

The component is above the shared instruction substrate. It may depend on
Instructions, Findings, MetadataPrimitives, and Text; Instructions must not
depend on it or acquire its Findings and Text dependencies.

The public types use the `ILInspector.ILDiff` namespace, matching the
assembly, so a namespace search finds every consumer of the comparison
substrate. Assembly ownership, project references, tests, and design
documentation define the component boundary.

Run its executable xUnit suite in Release:

```bash
dotnet run --project tests/ILInspector.ILDiff.Tests -c Release
```
