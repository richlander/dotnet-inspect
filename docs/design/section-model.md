# Section selection, Column filtering and rendering flow

One of the strongest features of dotnet-inspect and Markout is section filtering and view models. Every operation is a query that can be evaluated and executed. This clarity enables a variety of decisions and optimizations to be made.

There are three major aspects in play:

- Data scope (what's requested)
- Filter (data to shave off / retain)
- How to render the data

## Scoping

Scope plays two different ways:

- section queries
- curated queries, primarily set via verbosity

The major advantage of this system is that section queries / scoping pushes backpressure to the data generators. They are told for the specific sections requesting. The model isn't "give me everything and I'll filter down". We do use after-the-fact filtering, but within the section scope. Sections are the contract boundary for most of this system.

You can ask for the inventory of available sections for a given command. In this case, we're asking the "router" (default) command.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -S
Package                  section
Statistics               section
Package Dependencies     section
Vulnerabilities          section
RID Packages             section
Runtime Dependencies     section
Files                    section
```

Note: This content should be itself be data and printable with any of the renderers using the same system as "actual data". I consider this view to be oneline + no-heading. One can imagine either of the following (which would include a heading).

```bash=
dotnet run --project src/dotnet-inspect -- System.CommandLine -S --markdown
dotnet run --project src/dotnet-inspect -- System.CommandLine -S --json
```

The following prints a section.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -S Package
FIELD              VALUE
Version            2.0.3
Type               Library
Size               538.6 KB
TFM                net8.0
Built              2026-01-25
Source             NuGet
Authors            Microsoft
License            MIT
Repository         https://github.com/dotnet/dotnet
Content            lib
Target Frameworks  2
Readme             Yes
```

In this case, we see oneline since that's the default. You can also print multiple sections.

```bash
dotnet run --project src/dotnet-inspect -- System.CommandLine -S "Stats*,files, Target Frameworks,Foo"
```

You can ask for multiple and they can use globs, invariant case, have a leading space, or fully match the text.

A wrong section like "Foo" won't block the overall query. An error will be written for just that one request.

Oneline doesn't support multiple sections, so that would flip over to markdown by default. The user could also request JSON. An explict request for 

We can also ask for the list of fields if we want to know what to query for.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -S Package --fields
Version            field
Type               field
Size               field
TFM                field
Built              field
Source             field
Authors            field
License            field
Repository         field
Content            field
Target Frameworks  field
Readme             field
```

You can then filter the section.

```
dotnet run --project src/dotnet-inspect -- System.CommandLine -S Package --fields "Version,License,Read*"
```

We dont' currently have support for specifying multiple sections and using field selection.

## Verbosity queries

The other option is the curated verbosity queries. These mix query and formattting.

The following is the quiet verbosity command for a package.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -v:q
# System.CommandLine (2.0.3)

Version: 2.0.3 | Type: Library | TFM: net8.0 | Built: 2026-01-25 | Source: NuGet
```

It is doing multiple things:

- Rquesting the Package section
- Filtering to the top n fields
- Rendering a section
- Rendering data in an inline representation
- Rendering as markdown

The minimal markdown view is similar but prints a table instead and includes the package description.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine --markdown
# System.CommandLine (2.0.3)

Support for parsing command lines, supporting both POSIX and Windows conventions and shell-agnostic command line completions.

## Package

| Field | Value |
| ----- | ----- |
| Version | 2.0.3 |
| Type | Library |
| Size | 538.6 KB |
| TFM | net8.0 |
| Built | 2026-01-25 |
| Source | NuGet |
| Authors | Microsoft |
| License | MIT |
| Repository | https://github.com/dotnet/dotnet |
| Content | lib |
| Target Frameworks | 2 |
| Readme | Yes |
```

The detailed view prints all the sections available.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -S
Package                  section
Statistics               section
Package Dependencies     section
Vulnerabilities          section
RID Packages             section
Runtime Dependencies     section
Files                    section
```

Each view can be paired with a formatter

```bash=
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:q --oneline
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:d --markdown
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:d --json
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:d --oneline # this is an error since oneline cannot render multiple sections
```

All verbosity queries default to markdown, when explicitly specified. We previously played with the idea of `-v:m` being oneline. That doesn't actually makes sense. The problem is that `-v:q` and `-v:d` are markdown. We could also make`-v:q` markdown to break the tie, but that also doesn't makes sense. The markdown rendering is a really tight 3 lines. It's really a nicer experience for quiet. That leads to the idea that the default view is online, but when explicit commands are used (like v:anything), the rendering pops over to something else.

## View models

Each of these views have a certain shape. Some of the shapes are the same even if the rendering is different. Getting the shapes right is as important as the section backpressure, for example. The users sees the effect of a correctly carved shape but may not feel the benefit of the backpressure scheme.

Oneline is (almost) always the same shape. It only differs by columns. Markdown is the most dynamic. JSON should match the general shape of markdown, but with a different syntax.

There are some commands that should produce JSONL. They are really no different. It's just a prefernce for rows being presented as complete JSON documents and for the higher level structure to be represented by the presence of multiple lines.

## Changing views

We should add a bit more allowance for changing views. There are two places where this could make sense. It also pushes a bit harder of the distrinction between view modeling and rendering.

- `-v:q` displays the top-n fields in an inline state
- `-v:q` displays all the fields in a table state

There are actuall 5 field printers, for markdown. They should all be available for all markdown views.

In addition, the top-n view is not available to other writers. Perhaps `--fields top` should print the same top-n fields.

## Defaults

This has already been covered, implicitly, but we should write down.

- oneline is the default writer. It's chosen because it is terse and easy to parse with `awk` or `sed`. It's just limited in terms of what it can show.
- `-v` or `-v:*` implies `--markdown`
- `-S` queries with multiple sections implies `--markdown`; `--json` can override
- `type` defaults to tree
- Some other commands have different default, but that's details for implementation not the model.

## Tips

I love this experience:

```bash=
$ dotnet run --project src/dotnet-inspect -- type System.Text.Json JsonSeria
Error: Type 'JsonSeria' not found.

Did you mean:
  System.Text.Json.JsonSerializer
```

The same model exists for sections:

```bash=
$ dotnet run --project src/dotnet-inspect -- System.Text.Json -S Symb
Error: Select value 'Symb' not found.

Did you mean:
  Symbols
```

That's a really good model that we should use throughput the tool. It's basically a "happy 404" page that you see on a lot of polished sites.

For example, this command should produce a happy 404.

```bash=
dotnet run --project src/dotnet-inspect -- System.Text.Json -S Symbols,Resources --oneline
```

The request for >1 sections will pop over to markdown by default, but if there is an explicit oneline request, that should be force a message about oneline not supporting >1 sections.

Most of these flows should not be hard-coded, but a sort of emergent effect of the way that the formatter capabilities and the views and the data compose.

I called this section tips, because these errors are basically "Tips". In fact, we could just use that system. That way, it's not a separate thing. We already have a way to quiet tips. This would just be tips of a different kind. Less systems is more good.

## Data flow

The flow is straightforward. It is intended to force low-coupling with the desired result being that domain logic needs to be centralized not distributed.

Flow:

- Define scope of query, via explicit or implicit (like verbosity) selection
- Limit rows or columns with filtering gestures, like `-k`, `-m`, `-n`, `--columns`, `--fields`
- Select a view model for the data per the CLI gestures
- Pick a formatters, per default or explict request
- Print results and/or print warnings or errors

## Ideas

Architectures sometimes benefit from a fly in the ointment to break ties. We could the markout plaintext writer only in debug builds. This approach woudl further force a lack of coupling. It's really hard to have special rules for a writer that is only present in the code some of time. The rules describes should naturally flow to any rider provided that it is able to adequately describe its capabilities and accept the contractual input of this system.

## Prior art

ZFS uses the same column projection pattern with `-o`. The default view shows all columns:

```bash
$ zfs list -r offsite
NAME                   USED  AVAIL  REFER  MOUNTPOINT
offsite               1.62T  20.1T    96K  none
offsite/appdata       2.54G  20.1T  2.54G  none
offsite/git           1.54G  20.1T  1.54G  none
offsite/homes          818G  20.1T    96K  none
offsite/homes/annie   99.4G  20.1T  99.4G  none
offsite/homes/rich     719G  20.1T   719G  none
offsite/media          480K  20.1T    96K  none
offsite/media/family    96K  20.1T    96K  none
offsite/media/movies    96K  20.1T    96K  none
offsite/media/music     96K  20.1T    96K  none
offsite/media/shows     96K  20.1T    96K  none
offsite/memories       384G  20.1T    96K  none
```

With `-o`, you select exactly which columns appear and in what order:

```bash
$ zfs list -o name,used,refer -r offsite
NAME                   USED  REFER
offsite               1.60T    96K
offsite/appdata       2.54G  2.54G
offsite/git           1.54G  1.54G
offsite/homes          818G    96K
offsite/homes/annie   99.4G  99.4G
offsite/homes/rich     719G   719G
offsite/media          480K    96K
offsite/media/family    96K    96K
offsite/media/movies    96K    96K
offsite/media/music     96K    96K
offsite/media/shows     96K    96K
offsite/memories       366G    96K
```

This maps directly to dotnet-inspect's `--columns` flag. Key similarities:

- **Comma-separated, single argument**: `-o name,used,refer` is the same idiom as `--columns Name,Used,Refer`
- **Case-insensitive**: display header is `USED`, filter name is `used`
- **Projection controls both visibility and order**: the columns appear in the order you specify
- **Default shows everything**: omitting `-o` shows the full schema

The difference is that ZFS data is flat (one table), while dotnet-inspect has hierarchical output with sections. Our model adds a level: `-S Package --columns Version,TFM` is the ZFS `-o` pattern scoped to a section. Sections are the contract boundary that ZFS doesn't need because its schema is always a single table.

Docker uses a similar pattern but with Go templates for column selection. The default view shows fixed columns:

```bash
$ docker images
REPOSITORY   TAG       IMAGE ID       CREATED        SIZE
ubuntu       latest    59ab366372d5   2 weeks ago    78.1MB
nginx        1.25      a8758716bb6a   3 months ago   187MB
```

Column projection uses `--format` with Go template syntax:

```bash
$ docker images --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}"
REPOSITORY   TAG       SIZE
ubuntu       latest    78.1MB
nginx        1.25      187MB
```

And JSON output is per-line (JSONL):

```bash
$ docker images --format "{{json .}}"
{"CreatedAt":"...","ID":"59ab366372d5","Repository":"ubuntu","Size":"78.1MB","Tag":"latest"}
{"CreatedAt":"...","ID":"a8758716bb6a","Repository":"nginx","Size":"187MB","Tag":"1.25"}
```

Docker's approach differs in that projection requires Go template syntax rather than column names. Compare:

| Tool | Column projection |
| ---- | ----------------- |
| ZFS | `zfs list -o name,used,refer` |
| dotnet-inspect | `dotnet-inspect ... --columns Version,TFM` |
| Docker | `docker images --format "table {{.Repository}}\t{{.Size}}"` |

ZFS and dotnet-inspect use the simpler comma-separated name list. Docker's Go template approach is more powerful (supports conditionals, formatting functions, padding) but harder to type and remember. The name-based approach covers the common case — selecting which columns appear — without requiring template syntax. Docker's JSONL output (`{{json .}}`) is also worth noting; dotnet-inspect uses the same pattern for `--json` on commands that produce rows.

### kubectl

kubectl's `custom-columns` output is the closest analog to our model:

```bash
$ kubectl get pods -o custom-columns=NAME:.metadata.name,STATUS:.status.phase
```

It also has `--field-selector` for server-side filtering — the API server skips work for fields you don't request, which parallels our section-scoped backpressure. The difference is that kubectl's field paths address a JSON tree (`.metadata.name`), while our model uses named sections and columns as the addressing layer. kubectl also supports jsonpath and Go templates for power users.

### GitHub CLI

`gh` combines field selection with output format in a single `--json` flag:

```bash
$ gh pr list --json number,title,author
```

This is simpler surface area — one flag does both "output JSON" and "select these fields." Our model keeps them orthogonal: `--fields` for projection, `--json` for format. The tradeoff is an extra flag vs the ability to project fields in any format, not just JSON.

### PowerShell

PowerShell separates view modeling from data in the same way we do:

```powershell
Get-Process | Select-Object Name,CPU | Format-Table
```

This is the pipeline version of our "scope → filter → pick formatter" flow. PowerShell's `.format.ps1xml` files define default views per type — declarative view model registrations, which is what our verbosity presets do in code via `SectionPipeline` descriptors.

### DuckDB

DuckDB's CLI modes map almost 1:1 to our formatter selection:

| DuckDB | dotnet-inspect |
| ------ | -------------- |
| `.mode line` | `--oneline` |
| `.mode markdown` | `--markdown` |
| `.mode json` | `--json` |

Same data, different rendering. The formatter is orthogonal to the query.

### Summary

| Tool | Projection | Format selection | Backpressure | Hierarchical |
| ---- | ---------- | ---------------- | ------------ | ------------ |
| ZFS | `-o name,used` (names) | — | No | No |
| Docker | `--format` (Go templates) | `--format {{json .}}` | No | No |
| kubectl | `custom-columns` / jsonpath | `-o json/yaml/wide` | `--field-selector` | jsonpath only |
| gh | `--json field1,field2` | `--json` (combined) | No | No |
| PowerShell | `Select-Object` | `Format-Table/List/Wide` | No | No |
| DuckDB | SQL `SELECT` | `.mode` | SQL `WHERE` | No |
| dotnet-inspect | `--columns`/`--fields` | `--oneline/--markdown/--json` | `-S` sections | **Yes** |

The pattern most tools share is flat column projection. Our model's differentiator is hierarchical scoping — sections first, then columns/fields within them. `-S Package --columns Version,TFM` is a two-level projection that none of these tools express natively. It's closer to a GraphQL "pick your subtree" model than a traditional CLI column selector.
