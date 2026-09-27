# Member accessibility composition

## Status

This is a thin composition map, tracked by
[#8718](https://github.com/richlander/dotnet-inspect/issues/8718). It
connects two owner contracts and the consumers that adopt them. It states no
owner rule of its own. Like
[Type, MemberGroup, and Member inspection documents](type-member-inspection-documents.md#requirements-on-related-work),
it records requirements on related work, which the implementing owners adopt.
The operator approved this cross-owner scope.

| Owner | Contract this map relies on |
| --- | --- |
| [API and implementation population scope](api-population-scope.md#accessibility-within-api-visibility-scope) | Accessibility buckets and hidden status as independent visibility axes; `public` without hidden declarations as the default; `--all` and the `accessibility` term |
| [Type, MemberGroup, and Member inspection documents](type-member-inspection-documents.md#composition-count) | The `accessibility` projection over the Type Members row set, and Composition Count in one pass |

## What the reader sees

In Inspect Web, a reader opens System.Text.Json 10.0.0 `JsonDocument`. The
accessibility chips state the whole admitted population before any non-public row is
loaded:

```text
public | 8    protected | 0    internal | 24    private | 26
```

The list shows the 8 public Member-group rows. The reader selects `private`.
The Browser requests that Type's Rows under `accessibility = private` and shows
26 rows, including a `Parse` row with the two private overloads that the
public view never listed. The chips keep their counts, because the composition covers every
bucket whatever bucket is selected. The selection stays for the session, so the next Type opens on
`private` with its own truthful count, even when that count is 0.

The CLI asks the same questions of the same population. Its Type-subject
Count keeps its row unit, the Member Index declaration (see
[subject-default API count](progressive-disclosure.md#subject-default-api-count)):

```console
$ dotnet-inspect type JsonDocument --package System.Text.Json@10.0.0 --count
16
$ dotnet-inspect type JsonDocument --package System.Text.Json@10.0.0 \
    --where "accessibility=private" --count
27
```

Each host counts its own row unit: Member-group rows in the Browser list,
declarations in the CLI's Member Index. Both numbers come from the Composition
Count, which reports both units. Neither host counts rows it has loaded.

## Typed handoffs

1. The Metadata owner issues, for each declaration of a Type, its admission as
   an API declaration, its accessibility bucket, its hidden status, and its
   receiver form. One predicate issues these facts for both extraction and
   counting.
2. The Type document owner binds the `accessibility` and `receiver`
   projections to those facts. It returns Member-group Rows for one intent, or
   the Composition Count for the whole population.
3. The CLI lowers `--where "accessibility=…"`, `--all`, and `--count` to that
   request. Inspect Web lowers the chip selection to the same request.

## Requirements on related work

- **Metadata admission.** Extraction and the Count kernel must share one
  admission predicate. Today the rules are repeated per member kind in
  `ApiSurfaceExtractor` and again in `CountSummaryMembers`. Two copies would
  let a Count disagree with its Rows. A raw metadata scan is not enough: it
  counted 4651 System.Text.Json members where include-all extraction admits
  4357. The Metadata admission also decides what is compiler-generated. The
  CLI's name heuristic (`MemberFilters.IsCompilerGenerated`) drops ordinary
  fields such as `JsonDocument.s_nullLiteral`, so it cannot stay between the
  Count and the CLI's Rows.
- **Contract roles.** An explicit interface implementation or finalizer that
  the public default admits today stays in the `public` bucket, with its role
  visible, as `ApiAccessibility.Classify` already does. The Browser's host-side
  mapping of those roles to `private` and `protected` retires. Explicit
  implementations of every member kind classify the same way. Today an
  explicit-implementation method is admitted as `public`, while the property
  it implements (such as `JsonElement.ArrayEnumerator`'s
  `IEnumerator.Current`) is excluded by default and spelled `private`. The
  shared predicate resolves that split toward `public`, so the property joins
  the default population beside its accessor methods.
- **Heat over non-public rows.**
  [Implementation profiles](inspect-web-implementation-profiles.md) currently
  states that methods outside the public roster are never rows. Showing heat
  on non-public rows is a separate focused change to that owner. The Type heat
  record already measures those methods, so the change needs no new request.

## Evidence

A metadata-only pass over every Type counts members by accessibility, static,
and extension. It costs a small fraction of the type document the product
already builds. These are CoreCLR medians of five warm runs:

| Assembly | Type document today | Include-all extraction | Composition pass, whole assembly |
| --- | ---: | ---: | ---: |
| System.Text.Json 10.0.0 | 63.9 ms | 104.7 ms | 0.3 ms |
| System.Private.CoreLib 11.0.0-rc.1 | 607.6 ms | 641.0 ms | 2.7 ms |

The composition therefore travels with the type document. A staged minimal
document followed by a corrective count request is not needed.

## Counted adoption

The tracker owns this path. Every step reaches both production hosts unless
the step says otherwise.

1. Lock this map and the two owner amendments.
2. Share one Metadata admission predicate between extraction and the Count
   kernel, and add the one-pass composition kernel.
3. Deliver #8430 step 7 with the `accessibility` projection and Composition
   Count.
4. CLI: route Type-subject Rows and `--count` through that population. This
   retires the Type-subject materialize-then-count path and the Member Index
   name heuristic.
5. Inspect Web: show the Composition Count on the chips. Request Rows per
   selected bucket, and keep the selection for the session. This retires the
   host-side composition counting and role mapping.
6. Implementation profiles: show heat on non-public rows (a separate focused
   change).

## Non-claims

This map does not decide:

- what count a Type row in the Library's type list shows (today, public
  declarations, owned by
  [Library inspection](library-inspection-document.md#type-row-shape));
- how the Type declaration view classifies API visibility
  ([Type API declarations](type-api-declarations.md)), which stays
  independent of the Member population's buckets;
- Type importance ranking; or
- any presentation beyond the chips' counts and the session-sticky selection.
