# Package asset-selection correspondence

## Status

Implemented as a focused prerequisite for the PackageHouse resource-free
contract slice in #6433.

## Authority and exact claim

The existing runtime and compile asset selectors own one additional result
contract:

> A selector evaluation retains the exact package-content generation and
> request arguments that produced its existing typed selection outcome,
> without retaining package content.

This owner does not change framework compatibility, runtime-identifier
selection, compile/reference preference, default-asset selection, or
compile-to-implementation correspondence. Those remain properties of
`PackageAssetSelection` and `PackageCompileAssetSelection`.

## Receipts

`PackageAssetSelector.Evaluate` returns `PackageAssetSelectionReceipt` with:

- the `PackageContentGenerationIdentity` read from the input content;
- the exact requested target-framework string;
- the exact optional requested runtime identifier; and
- the existing `PackageAssetSelection` outcome.

`PackageCompileAssetSelector.Evaluate` returns
`PackageCompileAssetSelectionReceipt` with the same generation and request
evidence plus the exact package ID used for default-asset selection and the
existing `PackageCompileAssetSelection` outcome.

The receipts are resource-free. They do not retain `IPackageContent`, streams,
filesystem paths outside the existing package-relative asset outcomes, package
source clients, or leases.

The existing `Select` methods remain convenience projections returning
`Evaluate(...).Selection`. Existing consumers therefore keep their current
result and behavior while consumers that join selection with acquisition can
retain owner-issued correspondence.

## Composition and adoption

[#6426](https://github.com/richlander/dotnet-inspect/issues/6426) is the
eleven-step PackageHouse production-adoption tracker. The immediate consumer is
the resource-free PackageHouse contract slice in #6433.

PackageHouse may compare a selector receipt's generation and exact request
arguments with its acquisition and target-context receipts. It does not
construct selector correspondence around an independently supplied selection
outcome.

This slice does not implement PackageHouse, Workspace admission, package
acquisition, dependency traversal, CLI or browser/Wasm behavior, rendering, or
content lifetime.

## Gates

The focused gates run with:

```text
dotnet run --project tests/DotnetInspector.Services.Tests -c Release -- \
  --filter-class '*PackageAssetSelectorTests' \
  '*PackageCompileAssetSelectorTests'
```

| Property | Gate |
| --- | --- |
| Runtime evaluation retains generation, request, and outcome | `Evaluate_RetainsGenerationRequestAndSelection` |
| Compile evaluation retains generation, request, and outcome | `Evaluate_RetainsGenerationRequestAndSelection` |
| A null runtime framework remains a typed invalid outcome | `Evaluate_PreservesNullTargetAsTypedInvalidSelection` |
| Receipt instance type graphs retain no content or disposable resource | `SelectionReceiptsAreResourceFree` |
| Existing runtime selection remains the evaluation projection | Existing `PackageAssetSelectorTests` |
| Existing compile selection remains the evaluation projection | Existing `PackageCompileAssetSelectorTests` |

## Non-claims

The receipts do not prove package coordinate, source, producer, authority,
operation, dependency edge, or platform correspondence. Those associations
require their respective owner-issued evidence at a composition boundary.
