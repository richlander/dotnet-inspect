# Section test evidence

[Evidence and validation](../evidence-and-validation.md) owns the repository's
general evidence policy. [Section model](section-model.md) owns section
behavior. This document owns how tests compose evidence for that behavior
without making live product catalogs carry every section-system proof.

The governing claim is:

> Section testing separates exhaustive mechanism evidence, focused product
> catalog conformance, and targeted production smoke evidence.

The three layers are complementary. A test should use the lowest layer that
can prove its claim, and a higher layer should not repeat lower-layer
combinatorics merely because it can reach the same code.

## 1. Section-system mechanism tests

Mechanism tests use compact synthetic models, descriptors, schemas, category
maps, capabilities, and producer results. They deeply cover behavior owned by
the reusable section system:

- candidate, effective, and rendered selection;
- verbosity, size, cost, explicit-only, and fixed-overview rules;
- category expansion, ordering, and shared membership;
- query demand and prerequisite planning;
- discovery identity, capability intersection, and selection;
- invalid declarations, empty states, and boundary cases.

Synthetic inputs make every relevant state intentional and keep failures
attributable to the mechanism. A live product catalog must not be used merely
as a convenient source of enough sections, duplicate item names, overlapping
categories, or mixed capabilities.

Mechanism tests should normally be PR-fast. Exhaustive state-space or corpus
sweeps follow [test cost classification](../testing-cost-classification.md).

## 2. Product catalog conformance tests

Conformance tests compile a real product catalog and verify the declarations
that constitute that product's contract. They are comprehensive where the
contract is declarative, but focused in what each test restates.

Appropriate claims include:

- a section is registered and selectable;
- a section belongs to its intended base or domain categories;
- its size and cost place it in the intended automatic views;
- its query demand, capability, or renderer is wired;
- generic catalog invariants derived from declarations hold for every entry.

Prefer assertions derived from the catalog's declarations and focused
membership assertions owned by individual features. Do not copy complete
section arrays or pin literal section counts unless the complete inventory is
itself an intentional compatibility contract. When it is, keep one canonical
assertion or snapshot, state why completeness is user-visible, and update it as
part of the product change.

Conformance tests do not need to acquire or inspect a real artifact unless the
declaration cannot otherwise be exercised.

## 3. Production smoke tests

Smoke tests use a real package, assembly, project, or platform asset through
the production route and presentation path. They prove that representative
catalog declarations compose into user-visible behavior.

Use a shallow set of high-value cases:

- the changed or defining section renders through its ordinary gesture;
- exact or category selection reaches it;
- its primary output shape is correct;
- an important empty, unavailable, or failed case remains visible.

A new section normally needs focused conformance and smoke evidence. It does
not need a new mechanism test when existing synthetic tests already prove the
unchanged rule it relies on. Likewise, smoke coverage need not execute every
section through every format. Broader real-asset matrices require a specific
product, corpus, or compatibility claim and the corresponding cost
classification.

## Choosing and interpreting a gate

| Question | Evidence layer |
| --- | --- |
| Does the reusable selection or discovery rule work? | Synthetic mechanism |
| Is this product section declared in the right place? | Catalog conformance |
| Does a user reach and understand the result? | Production smoke |

A mechanism failure identifies a reusable contract regression. A conformance
failure identifies product declaration drift. A smoke failure identifies a
composition or presentation regression. Keeping those meanings distinct is
more useful than one exhaustive product test whose failure could mean any of
the three.

The CLI test harness is the first production consumer of this pattern.
`DiscoveryDocumentFactoryTests` demonstrates the split: synthetic schemas own
category projection and identity mechanics, while the Library catalog retains
focused conformance coverage. Existing `CommandExecutionTests` for Library
discovery and ecosystem dependencies provide production-route smoke evidence.

## Non-claims

This pattern does not weaken an explicitly owned complete product inventory,
require every section to have an end-to-end test, prescribe test-file layout,
or prohibit real assets in lower layers when the owning contract genuinely
depends on compiler- or package-produced evidence.
