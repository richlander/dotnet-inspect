// Shape for the package query experience (docs/design/package-query-experience.md).
//
// This module owns the request/outcome contract and pure state transitions for a
// wide, streaming query over a package source (nuget.org today; other feeds
// possible later), narrowed by facets, and evaluated in two tiers:
//
//   "nuspec"   — answerable from search metadata + .nuspec alone (bounded by
//                #4551's nuspec-only manifest profile once it lands).
//   "promoted" — requires opening the assembly/IL for an explicitly selected,
//                bounded subset of rows ("Deepen").
//
// It is deliberately data-source-agnostic: `PackageQueryDataSource` is supplied
// by the caller so this module can be built and tested against fake sources
// before the real #4551-backed source client exists and the shell wiring
// lands. Swapping in the real source for whatever satisfies this interface at
// wiring time is *expected* to be the whole integration step, but that
// expectation is unverified until it actually happens — see the design doc's
// Landing sequence for the same caveat.

/** A single named predicate a facet contributes to the request (1:1 with a
 * CLI-shipped profile flag; see the design doc's v1 non-goals). */
export interface QueryFacetTerm {
  key: string;
  label: string;
  tier: "nuspec" | "promoted";
}

/** The re-runnable unit intended to become shareable via the persisted form
 * (see design doc's Sharing section; the conversion itself is not yet
 * implemented). Never encodes a resolved outcome. */
export interface QueryRequest {
  scopeLabel: string;
  scopeQuery: string;
  facets: readonly QueryFacetTerm[];
  /** Declared cap communicated to the source. The bounded-complete footer
   * renders the source's own free-text `completion.reason` (see design doc
   * "States"), not this field directly — a real source is expected to keep
   * that text consistent with the cap it was given, but nothing here
   * enforces that. */
  requestedLimit: number;
}

export function createQueryRequest(
  scopeLabel: string,
  scopeQuery: string,
): QueryRequest {
  return { scopeLabel, scopeQuery, facets: [], requestedLimit: 200 };
}

export function withFacet(
  request: QueryRequest,
  facet: QueryFacetTerm,
): QueryRequest {
  if (request.facets.some(existing => existing.key === facet.key)) return request;
  return { ...request, facets: [...request.facets, facet] };
}

export function withoutFacet(
  request: QueryRequest,
  facetKey: string,
): QueryRequest {
  return { ...request, facets: request.facets.filter(f => f.key !== facetKey) };
}

/** One package's projection plus which predicate terms matched and why. Never
 * a bare pass/fail — the evidence is the point (see package-opportunities.ts
 * for the existing "evidence over checkmark" convention this follows). The
 * non-empty tuple type on `evidence` is what actually enforces that: an
 * empty-array row would silently render a blank evidence section (see
 * package-query-view.ts's renderRow). */
export interface QueryResultRow {
  packageId: string;
  version: string;
  tier: "nuspec" | "promoted";
  evidence: readonly [string, ...string[]];
  totalDownloads: number;
}

export type QueryCompletion =
  | { kind: "streaming" }
  | TerminalQueryCompletion;

/** The subset of `QueryCompletion` that represents a source having actually
 * stopped (as opposed to still running). A `PackageQueryDataSource.run()`
 * call settles when the source has stopped producing pages, so it can never
 * legitimately resolve with `"streaming"` — that kind is never a source's
 * own verdict on its own completion (it only ever describes a query the
 * controller considers in-flight, whether or not one has actually been
 * started yet — see `emptyOutcome()`). */
export type TerminalQueryCompletion =
  | { kind: "bounded"; reason: string }
  | { kind: "exhausted" }
  | { kind: "cancelled" }
  | { kind: "failed"; reason: string };

/** Mirrors `NuGetSearchOutcome`'s shape: results and failures both carried, so
 * a partially-searched source never renders as a confident empty/complete
 * result (untrusted-data-threat-model.md's "reject, do not sanitize" extends
 * here to "never silently narrow a claim"). */
export interface QueryOutcome {
  rows: readonly QueryResultRow[];
  failures: readonly string[];
  completion: QueryCompletion;
}

export function emptyOutcome(): QueryOutcome {
  return { rows: [], failures: [], completion: { kind: "streaming" } };
}

export function appendRows(
  outcome: QueryOutcome,
  rows: readonly QueryResultRow[],
): QueryOutcome {
  return { ...outcome, rows: [...outcome.rows, ...rows] };
}

export function appendFailure(
  outcome: QueryOutcome,
  failure: string,
): QueryOutcome {
  return { ...outcome, failures: [...outcome.failures, failure] };
}

export function withCompletion(
  outcome: QueryOutcome,
  completion: QueryCompletion,
): QueryOutcome {
  return { ...outcome, completion };
}

/** Supplies pages of rows for a request. The real implementation streams from
 * the #4551 nuspec-only source client; a stub (see package-query.stub.ts,
 * added when wiring begins) can satisfy this for demos before that lands. */
export interface PackageQueryDataSource {
  run(
    request: QueryRequest,
    onPage: (rows: readonly QueryResultRow[]) => void,
    onFailure: (failure: string) => void,
    /** Signaled when `cancel()` is called or a newer run supersedes this one,
     * so the source can stop in-flight network/manifest work instead of
     * running it to completion unobserved. */
    abortSignal: AbortSignal,
  ): Promise<TerminalQueryCompletion>;
}

export interface PackageQueryState {
  request: QueryRequest | null;
  outcome: QueryOutcome;
  selected: ReadonlySet<string>;
}

export function initialQueryState(): PackageQueryState {
  return { request: null, outcome: emptyOutcome(), selected: new Set() };
}

export interface PackageQueryController {
  run(request: QueryRequest): Promise<void>;
  cancel(): void;
  toggleSelection(packageId: string): void;
  clearSelection(): void;
}

/** Owns one in-flight generation counter so a superseded request's late pages
 * never append into a newer request's outcome (same race-safety idiom as
 * spotlight-package-search.ts's `generation` counter). */
export function createPackageQueryController(
  state: PackageQueryState,
  source: PackageQueryDataSource,
  onUpdate: () => void,
): PackageQueryController {
  let generation = 0;
  let abortController = new AbortController();

  return {
    async run(request: QueryRequest) {
      abortController.abort();
      const runController = new AbortController();
      abortController = runController;
      const requestGeneration = ++generation;
      state.request = request;
      state.outcome = emptyOutcome();
      state.selected = new Set();
      // Capture this run's own signal before onUpdate() runs: onUpdate() is
      // caller-supplied and may reentrantly call run() again synchronously
      // (e.g. a state-change handler that immediately kicks off a new
      // query), which would reassign the closure's `abortController` before
      // `source.run()` below gets a chance to read it — silently handing
      // this run the *next* run's signal instead of its own.
      const signal = runController.signal;
      onUpdate();

      let completion: TerminalQueryCompletion;
      try {
        completion = await source.run(
          request,
          rows => {
            if (requestGeneration !== generation) return;
            state.outcome = appendRows(state.outcome, rows);
            onUpdate();
          },
          failure => {
            if (requestGeneration !== generation) return;
            state.outcome = appendFailure(state.outcome, failure);
            onUpdate();
          },
          signal,
        );
      } catch (error) {
        // An unhandled rejection here (as opposed to a page-level onFailure
        // call) means the whole request never reached a completion at all —
        // it must not leave the outcome stuck labeled "streaming" forever,
        // which would silently look like an in-progress query rather than a
        // failed one.
        //
        // This is deliberately NOT also appended to `failures`: that list is
        // reserved for the "Partial failure" state (one source/page fails,
        // the design doc's States table), a different, distinct signal from
        // "Failed" (the request itself never reached completion). Recording
        // the same reason in both would render a total failure as if it
        // were merely a partial one — a "some sources failed" banner next
        // to a "query failed" state that already names the same error.
        if (requestGeneration !== generation) return;
        const reason = error instanceof Error ? error.message : String(error);
        state.outcome = withCompletion(state.outcome, { kind: "failed", reason });
        onUpdate();
        return;
      }

      if (requestGeneration !== generation) return;
      state.outcome = withCompletion(state.outcome, completion);
      onUpdate();
    },

    cancel() {
      // A run that already finished (bounded/exhausted) owns its own
      // completion label; cancelling after the fact must not overwrite it.
      if (state.outcome.completion.kind !== "streaming") return;
      generation++;
      abortController.abort();
      state.outcome = withCompletion(state.outcome, { kind: "cancelled" });
      onUpdate();
    },

    toggleSelection(packageId: string) {
      const next = new Set(state.selected);
      if (next.has(packageId)) next.delete(packageId);
      else next.add(packageId);
      state.selected = next;
      onUpdate();
    },

    clearSelection() {
      state.selected = new Set();
      onUpdate();
    },
  };
}
