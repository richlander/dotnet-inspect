# Library family-role composition

## Status, owner, and claim

Focused Research composition design for
[#9329](https://github.com/richlander/dotnet-inspect/issues/9329), within the
architecture narrative in
[#8634](https://github.com/richlander/dotnet-inspect/issues/8634) and the
learn-the-codebase scenario in
[#8744](https://github.com/richlander/dotnet-inspect/issues/8744).

The **Library Family-Role Composition** owner defines one claim:

> Given name-family and structural-salience results produced for the same exact
> acquired Library generation, join their Type rows by
> `MetadataTypeDefinitionAddress`, preserve both owners' methodology and
> qualification receipts, and issue exact Type and family-role rows without
> assigning semantic meaning or quality.

The owner is `ILInspector.Research`. It owns only the correspondence,
qualification, population accounting, and resource-free composed document.

[Library name-family summary](library-name-family-summary.md) continues to own
identifier-word evidence, suffix-family membership, source populations, and
their receipts.
[Library metrics report](library-structural-report.md) continues to own
structural-salience relationship populations, Graph-issued distinct-peer
degree, structural roles, poles, and their qualifications.
Metadata continues to own exact Type-definition identity and artifact-image
binding. QuerySpace and hosts retain request selection and presentation.

This composition does not transfer or redefine those responsibilities.

## User question

> How do this Library's recurring Type-name families correspond to its
> Graph-issued structural roles, and which exact Types support each
> observation?

The composition lets a person or agent ask:

- which suffix families contain foundations, hubs, or orchestrators;
- which families contain sea-level or mountain-peak Types;
- whether one family's members are structurally concentrated or varied; and
- which exact Types and qualifications support the counts.

The result is coordinated evidence. It is not a semantic-role classifier and
is not another graph visualization.

## Basis and smallest sufficient design

The #8634 survey found that suffix families reduce thousands of Type names to
tens or hundreds of vocabulary rows. Structural salience independently reduces
complete Type relationship populations to exact per-Type degree, role, and
pole evidence. An agent can retrieve both documents today, but joining and
partitioning thousands of Type rows in host scripts repeats deterministic work
and risks joining by rendered name.

Both owners already issue `MetadataTypeDefinitionAddress`. One Research
composition over those complete documents is therefore the smallest sufficient
design:

- no new graph;
- no new name grammar;
- no host-side identity reconstruction;
- no combined score; and
- no dependency on Library Metrics implementation profiles.

The complexity is the minimum needed to prove exact correspondence and preserve
two independently qualified methodologies. It also gives CLI and Browser/Wasm
one typed result rather than parallel host compositions.

## Imported owner results

### Exact Library binding

One execution carries a `LibraryFamilyRoleBinding` with:

```text
AssemblyArtifactIdentity
AssemblyReferenceIdentity
NonEmptyModuleVersionId
```

The binding is created while the acquisition-owned artifact and Metadata
session remain associated. It is the composition's join scope.

`LibraryNameFamilyDocument.Binding` must name that exact artifact, assembly,
and module. Structural salience retains owner-issued assembly identity and
module version in its signature-use and body-use receipts but does not retain
artifact identity. The host-neutral query therefore supplies the salience
document only with the acquisition-issued artifact binding under which it ran.
Detached salience content without that binding is not sufficient input.

This is a trusted in-process construction boundary, not a defense against a
host deliberately mislabeling a document. The query and tests prove ordinary
construction uses one artifact association.

The composition never treats assembly identity plus MVID as global artifact
identity.

### Name-family document

The required `LibraryNameFamilyDocument` contributes:

- exact Type-definition addresses and structured metadata names;
- one-word and two-word family membership or residual reasons;
- exact family identities and family member addresses;
- the selected all, ordinary-evidence-only, generated-evidence-only,
  mixed-evidence, and unknown source populations;
- source-provenance qualification;
- word-oracle and numbered-family receipts; and
- family methodology and partition accounting.

The composition consumes these values unchanged. It does not call CSharpText,
strip metadata arity, interpret suffixes, or rebuild family populations.

### Structural-salience document

The required `LibraryStructuralSalienceDocument` contributes:

- its production signature-use evidence mode;
- the exact namespace index and canonical namespace shard order;
- exact connected-Type definition addresses and structured metadata names;
- signature incoming and outgoing distinct-peer degree;
- structural role and optional pole;
- relationship-population qualification; and
- Graph execution work receipts.

The composition consumes owner-issued rows and designations unchanged. It does
not select Graph relationships, calculate degree, apply role thresholds, merge
signature and body degrees, or choose a pole.

The detached body-use pilot has a different result contract and no production
transport. Adopting it is a separate focused successor, not an implicit blend
in this composition.

## Correspondence

### Join currency

Within the exact bound artifact and module, the sole row join is:

```text
MetadataTypeDefinitionAddress
```

Display names, `TypeRef` equality, namespaces, family spelling, escaped full
names, array positions, and host identity never establish correspondence.

The composition validates:

1. name-family binding equals the composition binding;
2. every structural receipt names the same assembly identity and module
   version;
3. namespace shards are complete, unique, and in the owner-issued index order;
4. each input contains at most one row for an exact Type address;
5. each namespace index Type count equals the name-family Type count for that
   exact namespace;
6. every structural leverage row addresses one name-family Type in the same
   exact namespace;
7. joined rows agree on structured metadata name; and
8. every structural order and pole references one issued structural row.

A structural row with no name-family Type, a duplicate row, a namespace-count
mismatch, or a mismatched structured name rejects composition visibly. The
owner does not issue a partial available document or repair the population
through a name lookup.

The structural owner issues leverage rows only for connected Types. A
name-family Type without a structural leverage row is therefore valid and
receives no issued structural role in this composition. Under qualified
structural evidence, that absence must not be strengthened to a claim that the
Type is isolated.

### Library Dependency Structure boundary

[Library dependency structure](library-dependency-structure.md) currently
publishes internal Type nodes through a textual `TypeKey` and `TypeRef`. Its
design intends exact-identity composition, but the current document does not
carry `MetadataTypeDefinitionAddress`.

This composition must not join name-family rows to that key. Publishing exact
Type addresses from the Dependency Structure owner is the focused prerequisite
[#9330](https://github.com/richlander/dotnet-inspect/issues/9330) to any later
family-to-namespace edge aggregation. Until then, Dependency Structure remains
separately navigable evidence.

## Issued document

The result is a typed `LibraryFamilyRoleCompositionOutcome`.

Its available case carries one resource-free
`LibraryFamilyRoleCompositionDocument` containing:

- exact `LibraryFamilyRoleBinding`;
- a composition methodology version;
- imported name-family methodology, receipt, and provenance qualification;
- imported structural methodology, evidence mode, dispositions, and Graph work
  receipts;
- one `LibraryFamilyRoleTypeRow` per exact Type;
- family-role populations for every admitted source population; and
- one `LibraryFamilyRoleCompositionReceipt`.

Expected correspondence failures produce typed rejected outcomes. An imported
unavailable or failed owner result remains that owner's visible outcome before
composition starts; the composition does not translate it into an empty
document.

No live artifact, Metadata reader, Graph document, query capability, or host
handle enters the detached document.

## Type rows

One `LibraryFamilyRoleTypeRow` retains:

- `MetadataTypeDefinitionAddress`;
- structured metadata name, definition kind, and exact namespace;
- one-word and two-word family identities or their residual reasons;
- source-evidence disposition;
- nullable signature incoming and outgoing distinct-peer degree;
- nullable Metadata structural classification;
- nullable structural role;
- nullable pole; and
- the structural evidence disposition qualifying those facts.

Rows are ordered by exact metadata Type address. Input enumeration and hash
order do not affect output.

The composition copies owner-issued facts. It does not:

- choose one-word over two-word membership;
- select a "strongest" family;
- infer a role or isolation from an absent structural row;
- relabel a qualified role as complete; or
- turn missing designation into zero structural value.

## Family-role populations

The document issues one population for each name-family source population:

- all Types;
- ordinary evidence only;
- generated evidence only;
- mixed evidence; and
- unknown source evidence.

Each population retains the source population's exact denominator and
qualification.

For each exact one-word or two-word family identity,
`LibraryFamilyRoleRow` contains:

- total member Type count;
- foundation, hub, and orchestrator counts;
- sea-level and mountain-peak counts;
- no-issued-structural-role count;
- distinct namespace count;
- exact member Type addresses; and
- exact supporting Type addresses for each structural role and pole.

Role counts partition family members according to the imported structural
row's role or absence. No-issued-role does not mean unused or isolated when
the structural evidence is qualified. Pole counts are independent evidence and
do not form a partition: a Type may have a role without a pole, while an issued
pole retains its owner-defined relationship to role.

One-word and two-word families remain separate exact identities. Casing,
acronym spelling, plurality, and exact two-word separators remain distinct.
The composition does not merge synonyms or assign a domain label.

Family rows follow `LibraryNameFamilyOrder.Prevalence`. Exact Type addresses
break any remaining tie. The composition does not create a score from role,
pole, family size, or source disposition.

## Qualification and accounting

`LibraryFamilyRoleCompositionReceipt` records:

- exact Type count;
- count with each structural role;
- count with no structural role;
- count with each pole;
- source-population denominators;
- one-word and two-word family row counts; and
- imported methodology and work receipt identities.

For each source population:

```text
foundation + hub + orchestrator + no-issued-role = population Type count
```

Every family role partition closes against that family's exact member count.
The same Type may participate in one one-word and one two-word family, so those
two family spaces never sum against one another.

Qualification is monotone. Qualified source provenance or structural evidence
cannot produce an unqualified composed row. The result preserves the exact
reason and receipt from each owner rather than collapsing them to one Boolean.

## QuerySpace and work boundary

The composition declares two QuerySpace row sets beside its Research document:

- `family-role-rows`; and
- `type-role-rows`.

One request selects:

- one source population;
- one row set; and
- ordinary QuerySpace predicates, ordering, terminals, and limits.

The first implementation is an explicit reference slice: it constructs one
complete resource-free composition document from the two already-completed
owner documents, then QuerySpace selects rows. Full population construction is
required for family counts and partition receipts. The query reports total and
selected counts and does not claim acquisition pushdown.

The operation reuses completed name-family and structural-salience results when
both are selected in one request. It must not:

- execute Graph a second time;
- request Library Metrics implementation profiles;
- request Dependency Structure;
- rebuild identifier words; or
- build every presentation row before a Count or Exists terminal.

The Research document contains domain rows. QuerySpace owns selection and
terminal execution over those rows.

## Interpretation boundary

The composition can state:

- an exact family has a measured number of Types in each owner-issued
  structural role;
- an exact Type belongs to an exact suffix family and has an owner-issued role
  or pole;
- a family has no designated Types under one explicit evidence mode; and
- every count is qualified by the imported source and structural receipts.

It cannot state:

- that a suffix proves semantic responsibility;
- that one family is architecturally better, more modern, or more important;
- that a family "should" occupy one structural role;
- that structural concentration proves intended design;
- that differently spelled families are synonyms; or
- that an isolated or undesignated Type is unused.

Those are interpretations under #8516. An agent may make them only with that
label and links to the exact family, Type, methodology, and qualification rows.

## Rendering strategy

### CLI

The CLI adds one exact-name-only, non-default Library section,
`Name Family Roles`.

Markout remains the multi-format lowering. Markdown and tables lead with
family-role rows and permit exact Type support rows through section selection.
TSV, JSONL, and projected JSON expose the same QuerySpace rows. Complete JSON
and `InspectionEnvelope<TContent>` retain the full resource-free document,
Share disposition, and diagnostics.

The CLI does not parse names, run Graph, or join owner documents.

### Browser/Wasm

The ordinary Library Type inventory adds an explicit **Show name families**
gesture. Initial Type inventory and structural-salience loading remain
unchanged until that gesture requests the focused managed composition.

The Browser presents:

- an exact family list with measured role and pole counts;
- family selection that filters the ordinary exact Type inventory;
- exact Type activation through the existing Type route; and
- owner qualifications and residual counts in family detail.

Existing structural-salience glyphs remain independently owner-issued and
appear on the selected exact Types. Family filtering does not add family color
or another glyph to the Dependency Structure graph. The existing second cue
slot is not consumed merely to indicate that a Type has a suffix family.

This host-specific interactive lowering deliberately bypasses Markout.
TypeScript owns interaction and layout only. It receives exact Type keys and
managed-computed rows; it does not tokenize, aggregate roles, or infer family
membership.

## Production adoption

1. **Design:** this document locks the exact composition and its boundary.
2. **Research:** implement the resource-free outcome, document, correspondence,
   receipts, and fixture gates.
3. **QuerySpace:** declare and register the two row vocabularies beside the
   Research owner.
4. **CLI:** add the exact-name-only `Name Family Roles` section through Markout
   and complete envelope transport.
5. **Browser/Wasm:** add the explicit family gesture, family detail, ordinary
   Type filtering, and exact-Type activation through the same managed query.
6. **Workflow:** the `project-analysis` architecture-narrative workflow
   consumes typed family-role rows and labels semantic conclusions as
   interpretation.

Steps 1-3 establish the shared owner path. The CLI reaches production in step
4 and Browser/Wasm in step 5. Step 6 is a release-managed product-skill update,
not part of ordinary implementation PRs.

Family aggregation over Dependency Structure edges remains a separate
successor after that owner publishes exact Type-definition addresses.

## Required evidence

Ordinary Release CI gates must cover:

- one fixture whose two suffix families span foundation, hub, orchestrator,
  pole, and isolated Types;
- exact Type-address correspondence despite same or confusing display
  spellings;
- structural connected rows joining while structurally unassigned name-family
  Types remain valid;
- rejection of extra structural rows, duplicated rows, namespace-count
  mismatches, reordered shards, or mismatched structured names;
- deterministic output under reversed input and hash order;
- one-word and two-word families remaining separate;
- all five source populations closing independently;
- qualified structural or provenance input remaining qualified;
- role partitions and independent pole counts closing exactly;
- QuerySpace Count and row limits preserving document totals without rerunning
  Graph; and
- CLI and Browser DTO parity over the same managed document.

Real-asset evidence records:

- FluentValidation 12.1.1, including the `Validator` family; and
- `dotnet-inspect.dll`, including `Result`, `Outcome`, `Evidence`, and
  `Identity` families.

NativeAOT base/head evidence covers Research plus each supported CLI terminal.
Browser adoption separately measures Wasm first demand and cached demand while
confirming that initial Type inventory work remains unchanged.

## Analogous implementations

Analogous implementations inform presentation and interpretation boundaries;
they transfer no code.

| Implementation | Relevant evidence | Deliberate difference |
| --- | --- | --- |
| NDepend code queries and dependency matrix | Naming conventions and structural dependencies can be queried together | This composition preserves owner-issued evidence and exact receipts rather than defining a quality rule |
| Structure101 dependency views | Structural position and declared grouping are useful coordinated lenses | This composition adds no authored layer rule or host-derived graph |
| CodeScene hotspots and knowledge maps | Separate measures become useful when coordinated over stable subjects | This composition does not create a blended importance score |
| IDE symbol filters | A vocabulary facet can narrow a Type inventory without changing the underlying model | Family membership remains a measured Library population with qualifications, not a filename or display-text search |

## Non-claims

- No semantic family taxonomy, synonym table, or quality score.
- No family color, clustering, or overlay on Dependency Structure.
- No Library Metrics amplitude, implementation heat, or combined importance
  score.
- No body-use pilot composition or comparison between structural evidence
  modes.
- No graph construction, relationship selection, degree calculation, or role
  threshold.
- No join through `LibraryDependencyTypeNode.TypeKey`, `TypeRef`, namespace,
  display name, or Browser DOM identity.
- No repository-wide, Package-wide, or cross-Library family aggregation.
- No cross-version family correspondence or trend.
- No default CLI or Browser cost before explicit section or gesture demand.
- No edit to the release-managed `project-analysis` skill in this design
  slice.
