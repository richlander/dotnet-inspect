# Progressive disclosure model

dotnet-inspect uses progressive disclosure to control noise, latency, and
network use. The core rule is: start with cheap evidence in the command's base
scope, then require an explicit gesture for broader domains or larger probe
budgets.

The model combines four mechanisms:

1. **Verbosity** supplies automatic presets over base categories.
2. **Section selection** with `-S` supplies explicit scope and backpressure.
3. **Discovery** with `-D` describes the catalog before content is requested.
4. **Capabilities** gate network, source-content, and other expensive work.

`-D` and `-S` are intentionally capitalized. They form a query namespace that
is less likely to collide with command-specific lowercase options.

## Verbosity

Verbosity reveals more about the same subject. It must not silently enter
unrelated domain categories.

| Level | Flag | Intent |
| --- | --- | --- |
| Quiet | `-v:q` | Compact identity/context only |
| Minimal | `-v:m` | One high-value base section |
| Normal | `-v:n` | Multiple network-free base sections |
| Detailed | `-v:d` | All applicable base sections |

Minimal views should remain close to one screenful. Prefer compact fields,
counts, and summaries over unbounded inventories.

## Categories

Base categories define ordinary command evidence. Domain categories are
separate lenses such as `@Performance`, `@Metadata`, and `@SourceLink`.

Automatic verbosity uses only the base-category union. Selecting an exact
domain category is the gesture that enters that domain.

There are no user-facing `@All`, `@Default`, or `@Hidden` categories. Users who
need broad evidence select the relevant authored categories explicitly.

## Section selection

`-S <name>` selects sections or categories by exact name, legacy alias, or
wildcard:

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "Async*"
dotnet-inspect library System.Text.Json -S @Performance
```

Selection controls both rendering and data collection. Only producers needed
by the requested sections should run.

Focused output retains compact target context where needed so a section query
does not lose identity, version, TFM, or source information.

### Bare `-S`

Bare `-S` renders the command's compact network-free overview:

```text
Base union AND Fixed AND NetworkFree AND Effective
```

This is a stable candidate rule, not a promise of an identical rendered set
for every target. A missing README or unavailable symbol record legitimately
removes that section.

Other command contexts may use an equivalent focused preset while they migrate
to authored base categories. See [Bare `-S` default view](info-view.md).

## Discovery

The library command is the reference discovery model:

| Gesture | Meaning |
| --- | --- |
| `-D` | Cheap, target-aware base catalog and applicable category doors |
| `-D --effective` | Full effective base catalog |
| `-D @Category` | Structural category membership |
| `-D @Category --effective` | Effective category membership |
| `-D --schema` | Complete structural graph without target inspection |
| `-D Section` | Structural section fields |
| `-D Section --effective` | Fields backed by a full section probe |

Plain `-D` must remain network-free and should return in under 0.5 seconds for
a local target. Resolving a package that is not local is target acquisition and
can exceed that budget.

`-D --effective` spends the larger producer budget. Without an explicit
category, it remains scoped to base categories so it cannot implicitly run
performance, metadata, SourceLink, and other domains together.

Commands not yet migrated may retain their existing discovery behavior. New
work should follow the reference model rather than copy a legacy command.

## Network and source capabilities

Package acquisition and symbol/source acquisition are separate.

- A package may be downloaded to resolve the requested target.
- Default gestures must not automatically fetch symbols or source content.
- Embedded, adjacent, or cached symbols may be used by network-free gestures
  when their latency budget permits.
- Selecting a network-bound section or running full effective discovery for a
  category may authorize the capability declared by that section.

Capability authorization comes from the user's gesture, not from an internal
verbosity promotion.

## Projection

After selecting a section, `--columns` and `--fields` project its data:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json \
  -m Serialize -S Methods --columns "Name;Signature;Obsolete"
```

Projection is validated against the selected section schema. Unknown names
produce diagnostics; valid-but-empty names are reported as no data.

Future filtering and ordering should extend the shared row-query path rather
than add one flag per column. See
[Row query and ordering](row-query-order.md).

## Counts and limits

Use built-in limiters instead of shell pipes:

```bash
dotnet-inspect library System.Private.CoreLib -S "Async*" --count
dotnet-inspect library System.Private.CoreLib -S "Async*" --rows 10
dotnet-inspect package System.Text.Json -n 12
```

- `--count` reports rows for the selected candidate set, including zero-row
  sections when category membership is being counted.
- `-n N` and numeric shorthand such as `-6` limit output lines.
- `--tail` takes lines from the end.
- `--rows` limits rows within each table while preserving headings and headers.

## Explicit-only execution

Some sections never enter automatic verbosity because they fetch content,
scale without a useful bound, or represent specialized diagnostics.
`ExplicitOnly` is an internal execution policy, not part of the section's
user-facing identity.

Select those sections or their authored category explicitly:

```bash
dotnet-inspect library System.Text.Json -S "SourceLink: Integrity"
dotnet-inspect library System.Text.Json -S @Performance
```

## Maintenance guidance

- Preserve cheap, network-free defaults.
- Put unrelated domains behind authored category doors.
- Keep selection backpressure wired through producer demand.
- Add structural and effective discovery coverage for new sections.
- Do not infer category membership from section names.
- Update `skills/dotnet-inspect/SKILL.md` when user-facing disclosure changes.
