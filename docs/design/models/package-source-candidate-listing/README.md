# Candidate-scoped Source listing evidence

Interaction companion to
[candidate-scoped version metadata](../../package-source-model.md#candidate-scoped-version-metadata),
tracked by [#10833](https://github.com/richlander/dotnet-inspect/issues/10833)
under [#10404](https://github.com/richlander/dotnet-inspect/issues/10404).

## Boundary and assumptions

The join currency is the product's operation/configured-authority/runtime-source/
package tuple, not producer equality or a displayed source label. This finite
instance fixes one operation, two authority/runtime pairs, one package, three
version identities and two pages. One authority reports all versions; the other
does not report the middle version. All 64 listing-truth assignments are
explored, including complete absence of a listed match.

Complete identity discovery and current authorization have already settled.
One higher-version fact is inline; other facts begin unknown. The two
declarations consume different owner-provided preference sequences: ascending
selection and a descending bounded subset. NuGet grammar and preference
production remain with `PackageDependencyVersionRange`, outside this model.

Page acquisition for a tuple is serialized. A successful page exposes all its
reported version facts; concurrent declarations may then reuse neighboring
facts instead of fetching the page again. Metadata is truthful and known facts
do not change within this observation. Weak fairness assumes admitted page
work settles and enabled requests can resume.

## Checked obligations

- Every completed scoped result agrees with selection after complete listing
  discovery, including proven no match.
- Known facts agree with the source truth, and a successfully acquired page is
  not acquired again merely for another declaration.
- Both requests eventually settle under the stated fairness assumptions.

`BrokenIgnoreUnknown.cfg` deliberately excludes unknown versions from possible
matches. Its expected safety counterexample demonstrates that known higher
facts cannot justify skipping an unexamined better candidate.

This model does not duplicate root admission, retirement, cancellation or
deadline mechanics. Those remain with the
[operation-concurrency model](../package-source-operation-concurrency/README.md)
and actual Source ownership tests. Protocol parsing, unavailable/failed pages,
listing changes, replacement identities and byte/leaf limits are outside this
successful-evidence instance and require implementation gates.

## Execution

```sh
TLA_TOOLS_JAR=/path/to/tla2tools.jar bash eng/run-tla-checks.sh \
  docs/design/models/package-source-candidate-listing
```

The repository-pinned TLC build is `2026.08.11.125311`, SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
`Safety.cfg` must exit 0; `BrokenIgnoreUnknown.cfg` must exit 12.
These are bounded model obligations, not a proof of implementation equivalence.

The checked instance produced the following exact outcomes with OpenJDK
21.0.12.1 and a 2 GiB heap:

| Configuration | Exit | Generated states | Distinct states | Depth |
| --- | --- | --- | --- | --- |
| `Safety.cfg` | 0 | 3,520 | 2,136 | 9 |
| `BrokenIgnoreUnknown.cfg` | 12 | 112 | 112 | 2 |

The safety run also checked eventual settlement. Counterexample state counts
can vary with worker exploration order; its required outcome is exit 12.
