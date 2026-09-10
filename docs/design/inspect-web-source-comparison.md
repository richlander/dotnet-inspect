# Inspect Web authored Source comparison

## Owner and claim

Inspect Web Source Comparison owns the shared paired Source query and its
managed feature projection. The focused delivery was
[#6076](https://github.com/richlander/dotnet-inspect/issues/6076).

> An explicitly selected member and two package versions produce a view tied
> to that ordered request and the shared paired Source query, preserving
> authored-source changes, exactness, endpoint provenance, and non-success.

**Retirement notice:** the interactive Source Diff consumer this document
originally described — **Compare authored source** and its modal — was
atomically retired by
[#6423](https://github.com/richlander/dotnet-inspect/issues/6423), which
activates [Library API diff presentation](library-api-diff-presentation.md)
as the browser's first Library-root comparison instead. See
[Inspect Web Library API Diff](inspect-web-library-api-diff.md) for that
owner. This document remains authoritative only for the managed paired
Source query, its facade projection, and their retained test coverage below —
not for a browser interaction. The shared query and structured evidence
remain available for a later on-demand annotated comparison consumer; no
placeholder browser action exists ahead of that consumer.

The immediate consumer is a person inspecting a package member who wants to
see how its authored declaration changed in another version. This delivers
S4 and S5 together in the six-milestone
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706) route, under
the browser experience tracker
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).
S1-S3 already landed, including the shared query and its CLI consumer in
[#5984](https://github.com/richlander/dotnet-inspect/pull/5984).
S6 is the separate scoped retirement in
[#6250](https://github.com/richlander/dotnet-inspect/issues/6250), not
permission to delete broader callers.

## Basis and consumed boundaries

The existing
[Method Body Diff](inspect-web-method-body-comparison.md) supplies the local
interaction precedent: a contextual, explicitly submitted pair and separate
query versus transport outcomes. Explicit version selection and an ordered
Before/After view are the conventional comparison baseline.

| Owner | Consumed contract |
| --- | --- |
| [Paired Source query](member-source-pair-query.md) | Independent exact member resolution, complete verified declarations, native text comparison, and both endpoint associations |
| Existing browser package workspace and member resolution | Package/version/TFM selection, implementation selection, and protected scope leases |
| Existing Source capability policy | Bounded PDB and SourceLink acquisition through the browser's allowed transports |
| [Operation authority](inspect-web-operation-authority.md) | Immutable operation input, current-view publication, supersession, cancellation, and disposal |
| [Managed bridge](inspect-web-managed-operation-bridge.md) | Keyed managed operation and terminal transport outcome |
| [Shell interaction](inspect-web-shell-interaction.md) | Modal accessibility, Escape, and focus return |
| [Surface composition](inspect-web-surface-composition.md) | Contextual action placement and responsive continuity |
| [Navigation consumer](inspect-web-navigation-consumer.md) | Existing member navigation, context replacement, canonical location, and history |

These are dependencies, not contracts redefined by this feature. Comparison
algorithms, acquisition, retained-image identity, navigation transitions, and
operation lifetime remain with their existing owners.

## Explicit comparison (retired browser interaction; managed shape retained)

The retired interaction offered **Compare authored source** for one selected
method in a package. Before was the launching package version and member; the
user explicitly chose After's version of that same package. The assembly and
logical member selection stayed fixed. An exact version field was sufficient
for this bounded adopter; it did not require new package discovery or
candidate correspondence. The same version was valid.

An unavailable implementation or a selection that cannot represent one
MethodDef exposes its reason. Runtime/platform selections and arbitrary
cross-package or renamed-member comparisons are outside this package-version
feature. Accessors must not silently become their enclosing declaration.

Opening the dialog and editing its version did not acquire Source. **Compare**
submitted the ordered pair. Editing the pair invalidated the old result before
another request could publish. The result's labels came from its submitted
request and resolved endpoints, never from the current version input.

The feature holds the pair and its result in session-local feature state under
the existing operation authority. Like Method Body Diff, the dialog does not
create a navigation subject or canonical packet. Dismissal or navigation that
replaces its launching context disposes its operation through existing surface
lifetime hooks. Ordinary member navigation and history remain unchanged;
refresh and shared links restore the underlying inspection, not the transient
comparison. Portable paired navigation remains broader #5083 work.

## Managed projection

The Source facade resolves the launching selection through existing browser
member resolution, derives the query's logical member anchor, and leases both
package implementation contexts until query and release settle. It invokes
`AssemblyContextMemberSourcePairQuery` once. The other version resolves the
anchor independently; the launching image's MethodDef token is never used as
the other image's identity.

The transport retains the submitted pair, each resolved package coordinate
and asset, assembly identity and module version where available, exact member
identity where available, source provenance, and the endpoint's outcome.
The query's opaque acquisition registrations remain managed associations;
they are not stringified or replaced by invented durable browser identities.
The facade projects each endpoint directly from its place in that query result.
Query unavailable/failed outcomes are successful transport of non-success
evidence, not transport errors. Failure to establish an input context remains
a visible managed-operation failure. Cancellation publishes no partial pair.

Source is explicit and uses existing browser capability policy. It neither
enables adjacent/local filesystem reads nor calls ordinary Source's
decompiled fallback. Unrequested native C#/IL results are not inferred.
The Source coordinator and keyed managed bridge retain their existing
acquisition supersession and cancellation meaning.

## Source Diff presentation (retired browser interaction)

The retired interaction always identified Before and After separately and
showed exact, changed, unavailable, and failed outcomes explicitly. Available
declarations and their provenance remained inspectable even when the other
endpoint had no Source. Missing source was not an empty declaration, a
deletion, or decompiled C#.

The managed projection preserves native line-pair polarity and the independent
movement facet, including moves mixed with content edits. Both line coordinates
are declaration-relative and one-based. A future consumer renders these
supplied relations directly; it must not run another matcher or infer
exactness from empty rows.

## Demo and gates

The canonical demonstration uses compiler-produced versions whose declaration
changes from `1 + 2` to `3` while native bodies remain equal. Neighboring cases
cover equal Source, moved comment blocks, moves with content edits, and missing
PDB/source. Real package/source acquisition and the paired query construct the
evidence; no harness manufactures a successful comparison.

`BrowserSourceComparisonOperationTests` exercises the public export for
independently resolved endpoints, visible non-success, and cancellation/release.
Its positive query/projection cases use the production Source fetch policy
with supplied transport. The retired `source-comparison.test.ts` and
`source-comparison-view.test.ts` UI-unit tests were deleted with the browser
interaction (#6423); no frontend module owns this facade today.

`eng/test-inspect-web-source-comparison-gate.sh` exercises the public generated
facade directly (no dialog) in Firefox against the Release-published engine.
It supplies cataloged compiler-produced packages and their exact SourceLink
bytes at network acquisition, not source endpoints or comparison results.
The embedded PDB and allowed SourceLink host preserve real production
capabilities. The gate covers Source-only changes, exactness, two-line moves
alone and mixed with edits, and an available Before with unavailable After.
It runs in the existing inspect-web CI job, reusing its published engine and
Firefox installation; its local browser execution took about seven seconds.
No test-only source-context factory or new corpus lane is needed.

```bash
dotnet run --project prototypes/inspect-web/engine.Tests -c Release -- \
  -class '*BrowserSourceComparisonOperationTests'
eng/test-inspect-web-source-comparison-gate.sh
```

The last command requires the already-published site at
`artifacts/inspect-web-publish/wwwroot`, or `INSPECT_WEB_SOURCE_DIFF_SITE`.
The same browser spec has opt-in live-package facade/dialog cases using
`INSPECT_WEB_SOURCE_DIFF_URL`; live symbol unavailability remains valid
non-success evidence, not a positive Source comparison.
