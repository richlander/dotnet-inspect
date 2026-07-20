# Fixture governance

Fixtures are product inputs, not ad hoc test setup. They should be built by the
normal solution graph, registered in `FixtureCatalog`, and consumed by stable
fixture IDs rather than by test-local path arithmetic or one-off compiler calls.

Decompiler fixtures additionally follow the
[original-source plan](design/decompiler-fixture-original-source.md), which
binds compiler-produced targets to verified compiler input rather than treating
a current checkout source path as authoritative.

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
separate project, build, or assembly shape meaningful. Tags remain useful for
selection and groups; boundaries explain why a fixture cannot be mechanically
merged or why a consumer must preserve a specific binary shape.

Every project that is not an intentional consolidated source bucket should have
boundary metadata. Consolidated buckets, such as
`ILInspector.Analysis.Fixtures` and `ILInspector.Decompiler.Fixtures.Ladder`,
can contain multiple fixture IDs when their source-level subjects do not require
distinct project boundaries; those fixture IDs should not carry project-boundary
metadata just because consumers observe them from another assembly.

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

## Expectation ownership

Fixtures are test assets, not product. Reserve adversarial rigor for the
boundary between a test asset and the product under test — not for the boundary
between two test assets. A check that pins one test asset against another buys
maintenance cost without adversarial safety: both sides are co-located, edited
in the same change, and reviewed together, so they move as one.

This applies to both the built-assembly `FixtureCatalog` and the generated
source-string `GeneratedFixtureCatalog` (`tools/DecompilerHarness`), whose
targets declare their own expected outcome (status, shape, and fragments).

Rules:

- A fixture owns its input and its expected outcome together. The fixture's
  declared expectation is the single source of truth for what the product should
  do with that input.
- Tests iterate the catalog's advertised inventory and compare the actual
  product result against each fixture's declared expectation. Prefer data-driven
  tests (`[Theory]`/`[MemberData]` or a `foreach` over the catalog) so a new
  fixture registers its own coverage without a matching test edit.
- Do not re-encode a fixture's expected outcome as a second hard-coded copy in
  the test to act as an "independent oracle" against the fixture. Two test
  assets are not independent; the copy only adds a place to drift.
- A test whose failure mode is "someone added a valid fixture, now update my
  literal list" is guarding the wrong invariant. Test selection and contract
  logic against small synthetic inputs, not against the live inventory.
