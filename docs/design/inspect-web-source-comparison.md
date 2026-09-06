# Inspect Web authored Source comparison

## Owner and claim

Inspect Web Source Comparison owns the explicit version-pair interaction, its
managed feature projection, and the Source Diff view.
The focused delivery is
[#6076](https://github.com/richlander/dotnet-inspect/issues/6076).

> An explicitly selected member and two package versions produce a view tied
> to that ordered request and the shared paired Source query, preserving
> authored-source changes, exactness, endpoint provenance, and non-success.

The immediate consumer is a person inspecting a package member who wants to
see how its authored declaration changed in another version. This delivers
S4 and S5 together in the six-milestone
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706) route, under
the browser experience tracker
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).
S1-S3 already landed, including the shared query and its CLI consumer in
[#5984](https://github.com/richlander/dotnet-inspect/pull/5984).
S6 is subsequent scoped retirement, not permission to delete broader callers.

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

## Explicit comparison

Offer **Compare authored source** for one selected method in a package.
Before is the launching package version and member; the user explicitly
chooses After's version of that same package. The assembly and logical member
selection remain fixed. An exact version field is sufficient for this bounded
adopter; it does not require new package discovery or candidate correspondence.
The same version is valid.

An unavailable implementation or a selection that cannot represent one
MethodDef exposes its reason. Runtime/platform selections and arbitrary
cross-package or renamed-member comparisons are outside this package-version
feature. Accessors must not silently become their enclosing declaration.

Opening the dialog and editing its version do not acquire Source. **Compare**
submits the ordered pair. Editing the pair invalidates the old result before
another request may publish. The result's labels come from its submitted
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

## Source Diff presentation

Always identify Before and After separately. Show exact, changed,
unavailable, and failed outcomes explicitly. Available declarations and their
provenance remain inspectable even when the other endpoint has no Source.
Missing source is not an empty declaration, a deletion, or decompiled C#.

The managed projection preserves native line-pair polarity and the independent
movement facet, including moves mixed with content edits. Both line coordinates
are declaration-relative and one-based. The browser renders these supplied
relations; it does not run another matcher or infer exactness from empty rows.

This interactive view deliberately uses the existing Source/Method Body Diff
DOM presentation rather than parsing CLI output or creating a Markout text
round trip. Structured native evidence reaches the facade before this
host-specific lowering; Queries/Findings remain the matching owners.

## Demo and gates

The canonical demonstration uses compiler-produced versions whose declaration
changes from `1 + 2` to `3` while native bodies remain equal. Neighboring cases
cover equal Source, moved comment blocks, moves with content edits, and missing
PDB/source. Real package/source acquisition and the paired query construct the
evidence; the UI does not manufacture a successful comparison.

`BrowserSourceComparisonOperationTests` exercises the public export for
independently resolved endpoints, visible non-success, and cancellation/release.
Its positive query/projection cases use the production Source fetch policy
with supplied transport. `source-comparison.test.ts` and
`source-comparison-view.test.ts` cover explicit submission, immutable labels,
replacement and late completion, non-success, and context disposal.

`eng/test-inspect-web-source-comparison-gate.sh` exercises the public generated
facade and actual dialog in Firefox against the Release-published engine.
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
cd prototypes/inspect-web
node --test test/source-comparison.test.ts test/source-comparison-view.test.ts
cd ../..
eng/test-inspect-web-source-comparison-gate.sh
```

The last command requires the already-published site at
`artifacts/inspect-web-publish/wwwroot`, or `INSPECT_WEB_SOURCE_DIFF_SITE`.
The same browser spec has opt-in live-package facade/dialog cases using
`INSPECT_WEB_SOURCE_DIFF_URL`; live symbol unavailability remains valid
non-success evidence, not a positive Source comparison.
