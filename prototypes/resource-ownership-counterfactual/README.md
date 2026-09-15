# Resource ownership Rust counterfactual

This standalone Rust 2024 prototype asks which parts of selected
dotnet-inspect lifecycle types remain when the compiler supplies move
invalidation, borrowing, and synchronous lexical drop.

The prototype is executable design evidence for
[issue #6629](https://github.com/richlander/dotnet-inspect/issues/6629). It is
not a product library, a proposed Rust port, or a normative replacement for
[`resource-ownership-and-borrowing.md`](../../docs/design/resource-ownership-and-borrowing.md).
That document remains the owner of the shared lifecycle vocabulary. The
portable declaration language and normalized Analysis input remain owned by
[issue #6631](https://github.com/richlander/dotnet-inspect/issues/6631).

The user explicitly approved a repository prototype as the evidence host.
Synthetic Rust types model responsibilities already present in the real
`AssemblyInspectionSession`, Artifact, Package Source, and `PackageHouse`
families. They do not claim that those independently owned APIs should adopt
the Rust surface.

## Demo

Run the executable comparison:

```bash
cd prototypes/resource-ownership-counterfactual
cargo run --example counterfactual
```

The output distinguishes compiler-enforced ownership from responsibilities
that remain explicit:

```text
Rust 2024 resource ownership counterfactual
session: detached snapshot = 4 bytes
artifact: scoped borrow = 8 bytes; revocation remains runtime state
settlement: pending with active work; completed operations = 1
package: durable receipt survives release of the live payload
```

Run every runtime and compile-fail case:

```bash
cargo test --all-targets
cargo test --doc
cargo clippy --all-targets -- -D warnings
cargo fmt --check
```

The crate has no third-party dependencies. Rust edition 2024 requires Rust
1.85 or newer. The initial evidence was produced with the latest stable
toolchain, Rust 1.98.1.

## Representative cases

| Repository responsibility | Rust counterfactual | Evidence |
| --- | --- | --- |
| `AssemblyInspectionSession` owns or borrows one image and produces detached snapshots | An owned enum variant drops once; a borrowed variant carries the lender lifetime; a higher-ranked callback prevents the view from escaping | Compile-fail doctests reject use after move, lender release before borrower release, and snapshot escape |
| Artifact access validates generation and revocation while exposing scoped content | A non-`Clone` lease drops at most once and an ordinary borrow prevents content from outliving it; generation identity and revocation still require runtime state | Compile-fail doctest rejects releasing a live lease; runtime tests reject wrong-generation and revoked future access while preserving an admitted view |
| Package Source or artifact settlement retires admission and waits for active work | `settle(self)` consumes the root and returns a future; operation leases report success, cancellation, failure, or abandonment separately from release; the future remains pending until all work ends | Runtime tests prove quiescence, non-success evidence, and issuer-visible root or observation abandonment |
| `PackageHouse` separates a live payload from durable evidence | An opaque settlement owns the live payload only in its acquired state; an owner-issued opaque identity binds the payload to detached receipt values | Runtime tests reject mismatched generation or correspondence and retain a cloned receipt after the payload is dropped |

## Resulting minimum shape

Rust removes generic scaffolding for:

- invalidating the source after ownership transfer;
- preventing a non-`Clone` release obligation from being copied;
- preventing use after explicit consuming operations;
- invoking synchronous `Drop` at most once when an ordinary, non-leaked value
  reaches lexical scope exit;
- preventing a borrowed view from outliving its owner or lease; and
- preventing release of an owner or lease while its borrow is live.

Rust does not remove:

- issuer identity, generation correspondence, or authorization checks;
- revocation and retirement state;
- resource-specific operations and validity rules;
- active-operation accounting, cancellation, failure, abandonment, or
  quiescence;
- scenario results, typed non-success, or live-payload transfer;
- durable receipts, provenance, identities, and failure evidence; or
- validation that a live payload corresponds to the receipt that describes it.

Most importantly, Rust has no asynchronous `Drop`. An explicit settlement
future is still required when release must await active work. `#[must_use]`
can warn when that future is ignored, but the language permits callers to drop
it. The prototype therefore retains issuer-visible root and observation
abandonment plus operation success, cancellation, failure, and abandonment.
Required observation remains a runtime, API, lint, or Analysis concern rather
than disappearing under compiler ownership.

Rust ownership is affine rather than a guarantee of eventual destruction.
Safe operations such as `mem::forget` can intentionally leak an owning value
and suppress `Drop`. The compiler establishes move invalidation, borrow
validity, and at-most-once destruction when destruction occurs; it does not
prove that every release obligation eventually settles.

## C# delta

The experiment suggests that current C# needs additional shape only where it
cannot express the compiler-enforced column:

| Concern | Minimum Rust shape | Additional current-C# shape |
| --- | --- | --- |
| Unique synchronous ownership | Non-`Clone` value plus at-most-once `Drop` on ordinary non-leaked paths | Explicit lease/session object, idempotent release state, ownership-effect declaration, and Analysis for aliases and use after transfer |
| Scoped read borrow | `&T` or a view carrying the borrow lifetime | `ref struct`, `scoped`, span-based access, or an owner-controlled snapshot callback |
| Lender-bound owner | Lifetime parameter connecting borrower to lender | Runtime lender association and validity checks where C# cannot carry the relationship |
| Revocable authority | Shared domain state checked by operations | The same shared domain state and checks; weaker ownership is not the reason they exist |
| Awaited quiescence | Consuming method returning a future | Explicit `IAsyncDisposable` or settlement result, active-work accounting, visible failure, and Analysis of unobserved completion |
| Durable evidence | Detached, cloneable receipt values | The same detached receipt; no lease should be hidden in it |

These are prototype conclusions, not owner changes. Any recommendation for a
production family belongs in that family's focused design and implementation
issue.

## Non-claims

- The four cases are representative, not an exhaustive suffix census.
- Rust API spelling is not a proposed C# naming scheme.
- The prototype does not define resource-effect attributes or JSON mappings.
- Compile-fail cases prove only the modeled Rust signatures.
- Runtime tests do not prove that every repository receipt is resource-free or
  that every lifecycle family matches one of these cases.
- No product or CI path consumes this crate. Its harness is a reproducible
  non-CI design probe.
