# Fixture governance

Fixtures are product inputs, not ad hoc test setup. They should be built by the
normal solution graph, registered in `FixtureCatalog`, and consumed by stable
fixture IDs rather than by test-local path arithmetic or one-off compiler calls.

## Project-boundary rule

Prefer shared fixture projects when a project is only a source bucket. Keep a
fixture project separate only when the assembly or build boundary is part of the
evidence under test.

Separate projects are justified for these semantic axes:

| Axis | Why the boundary matters |
| --- | --- |
| Assembly identity | The test distinguishes otherwise similar types or members by source assembly. |
| Assembly name | The output DLL name is the adversarial identity, such as `System.Linq.dll`. |
| Compiler lowering | The compiler-produced body shape is the fixture subject. |
| Cross-assembly boundary | References or unresolved type facts across assemblies are the evidence. |
| Extern alias | The test needs compile-time access to a colliding external identity. |
| Framework reference | The fixture must reference a trusted framework assembly. |
| Module attribute | The module-level metadata is the evidence. |
| Output kind | The fixture must be an executable or otherwise non-library output. |
| Sidecar asset | A binary is coupled to a trace or other sidecar artifact. |
| Target framework | The TFM changes emitted references or facade behavior. |
| Version pair | Old and new assemblies are compared as separate binaries. |

If none of those axes applies, consolidate the source into an existing fixture
project or create one shared fixture project for that area.

## Catalog metadata

`FixtureDefinition.Boundaries` records the semantic axes that make a fixture's
build or assembly shape meaningful. Tags remain useful for selection and groups;
boundaries explain why a fixture cannot be mechanically merged or why a consumer
must preserve a specific binary shape.

Every intentionally single-fixture project should have at least one boundary
entry. Consolidated buckets, such as `ILInspector.Analysis.Fixtures` and
`ILInspector.Decompiler.Fixtures.Ladder`, can contain multiple fixture IDs when
their source-level subjects do not require distinct project boundaries.

## Consumer rules

- Use `FixtureCatalog.Get`, `AssemblyPath`, `AssetPath`, groups, or tags instead
  of hardcoded artifact paths.
- Build fixtures through `dotnet build dotnet-inspect.slnx -c Release`; harnesses
  should assume inputs are already binaries.
- Do not add scripts or dynamic compilation for fixture binaries. If a fixture
  needs a special build shape, encode it as an MSBuild project and catalog
  boundary.
- Add or update contract tests when introducing a new boundary so the semantic
  axis cannot be erased by later cleanup.
