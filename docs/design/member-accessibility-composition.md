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
| [API and implementation population scope](api-population-scope.md#accessibility-within-api-visibility-scope) | Accessibility buckets and hidden status as independent visibility axes; `public` without hidden declarations as the default; `--all` and the `accessibility` term; [C# and metadata spelling](api-population-scope.md#spelling-within-api-visibility-scope) |
| [Type, MemberGroup, and Member inspection documents](type-member-inspection-documents.md#composition-count) | The `accessibility` projection over the Type Members row set, and Composition Count in one pass |

## What the reader sees

Every member count the product reports is of actual members: exact
declarations, so each overload counts. That covers the accessibility chips, the
Type row's member count (already public declarations, per
[Library inspection](library-inspection-document.md#type-row-shape)), the Type
API `Members` heading
([Inspect Web presentation language](inspect-web-presentation-language.md#api-source-metadata-and-package-dependencies-lenses)),
the CLI's Type tree headings, and the CLI's `--count`. No host reports a
Member-group count.

In Inspect Web, a reader opens System.Text.Json 10.0.0 `JsonDocument`. The
accessibility chips state the whole admitted population before any non-public
row is loaded:

```text
public | 16    protected | 0    internal | 44    private | 27
```

The list shows 8 rows for those 16 members. Each overload family's row, such as
`Parse 5×` or `Deserialize 5×`, colors its name differently. That text color
tells the reader that the row holds overloads, and where the difference
between 8 rows and 16 members lives. It is a different channel from heat,
which tints a row's background and colors the parent row's status text.

The reader selects `private`. The Browser requests that Type's Rows under
`accessibility = private` and shows the 27 private members as 26 rows,
including a `Parse` family row with the two private overloads that the public view never
listed. The chips keep their counts, because the composition covers every
bucket whatever bucket is selected. The selection stays for the session, so
the next Type opens on `private` with its own truthful count, even when that
count is 0.

The same `Filters` disclosure, collapsed by default, offers a spelling row:

```text
Accessibility   public 16 · protected 0 · internal 44 · private 27
Spelling        C# spelling (selected) · Metadata spelling
```

C# spelling serves consumers: one row per declaration, as the code that uses
the Type sees it. Metadata spelling serves readers who need the assembly as it
is, such as .NET team engineers: one row per metadata record. On
`JsonElement.ArrayEnumerator`, C# spelling lists `IEnumerator.Current` once, as
a `public` property (8 public, 1 internal, 3 private). Metadata spelling lists
the private `IEnumerator.Current` property record and its private
`IEnumerator.get_Current` method separately (6 public, 1 internal, 7 private).
The chips follow the selected spelling, and the spelling choice stays for the
session like the accessibility choice.

The CLI asks the same questions of the same population and reports the same
counts, with `--spelling metadata` as its
[spelling gesture](api-population-scope.md#accessibility-within-api-visibility-scope).
Its Type-subject Count already counts actual members (see
[subject-default API count](progressive-disclosure.md#subject-default-api-count)).
After adoption:

```console
$ dotnet-inspect type JsonDocument --package System.Text.Json@10.0.0 --count
16
$ dotnet-inspect type JsonDocument --package System.Text.Json@10.0.0 \
    --where "accessibility=private" --count
27
```

Both hosts read their numbers from the Composition Count. Neither host counts
rows it has loaded.

## Typed handoffs

1. The Metadata owner issues, for each metadata record of a Type, its
   admission as an API record, its accessibility flag, its hidden status, and
   its receiver form, together with which records compose into one C#
   declaration and that declaration's bucket. One predicate issues these facts
   for extraction and counting in both spellings.
2. The Type document owner binds the `accessibility` and `receiver`
   projections to those facts. It returns Member-group Rows for one intent, or
   the Composition Count, whose declaration Counts total the nested overload
   Counts of those Rows.
3. The CLI lowers `--where "accessibility=…"`, `--spelling`, `--all`, and
   `--count` to that request. Inspect Web lowers the chip and spelling
   selections to the same request.

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
- **Composition.** The predicate composes records into C# declarations by the
  [spelling rule](api-population-scope.md#spelling-within-api-visibility-scope).
  Today `--all` lists the explicit `IEnumerator.Current` twice: as a public
  `IEnumerator.get_Current` method row and as a private property row. Under C#
  spelling it becomes one `public` declaration, and under metadata spelling
  two `private` records. A C# finalizer, which the public default shows today
  through its MethodImpl, moves to `protected`, its declared accessibility.
  Explicit implementations of internal interfaces, which the public default
  also shows today (`JsonSerializerContext`, and 766 MethodImpl rows on public
  System.Private.CoreLib 10.0.8 Types such as `System.Byte`'s `IUtfChar<byte>`
  members), move to
  their interface's bucket. The Browser's host-side mapping of explicit
  implementations to `private` and finalizers to `protected` retires.
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

1. Lock this map and its owner amendments.
2. Share one Metadata admission predicate between extraction and the Count
   kernel, including the C# composition rule, and add the one-pass
   composition kernel for both spellings.
3. Deliver #8430 step 7 with the `accessibility` projection and Composition
   Count.
4. CLI: route Type-subject Rows and `--count` through that population, and
   report actual members in Type tree headings (`Methods (10)` rather than
   `Methods (6 logical, 10 overloads)`). This retires the Type-subject
   materialize-then-count path, the Member Index name heuristic, and the
   `N logical` heading.
5. Inspect Web: show the Composition Count on the chips and in the `Members`
   heading, and color overload family rows' names as
   [Inspect Web navigation presentation](inspect-web-navigation-presentation.md#type-navigation)
   states.
   Offer the spelling row in `Filters`. Request Rows per selected bucket and
   spelling, and keep both selections for the session. This retires the
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
- any presentation beyond the member counts above, the family-row name color,
  and the session-sticky selection.
