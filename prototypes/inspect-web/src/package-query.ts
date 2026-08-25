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
// by the caller so this module can be built, tested, and wired into the shell
// before the real #4551-backed source client exists. Swapping the stub source
// for the real one is the whole integration step; nothing else here changes.

/** A single named predicate a facet contributes to the request (1:1 with a
 * CLI-shipped profile flag; see the design doc's v1 non-goals). */
export interface QueryFacetTerm {
  key: string;
  label: string;
  tier: "nuspec" | "promoted";
}

/** The shareable, re-runnable unit. Never encodes a resolved outcome. */
export interface QueryRequest {
  scopeLabel: string;
  scopeQuery: string;
  facets: readonly QueryFacetTerm[];
  /** Declared cap communicated to the source; also the honesty label surfaced
   * in the bounded-complete footer (see design doc "States"). */
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
 * for the existing "evidence over checkmark" convention this follows). */
export interface QueryResultRow {
  packageId: string;
  version: string;
  tier: "nuspec" | "promoted";
  evidence: readonly string[];
  totalDownloads: number;
}

export type QueryCompletion =
  | { kind: "streaming" }
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
  ): Promise<QueryCompletion>;
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
      abortController = new AbortController();
      const requestGeneration = ++generation;
      state.request = request;
      state.outcome = emptyOutcome();
      state.selected = new Set();
      onUpdate();

      let completion: QueryCompletion;
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
          abortController.signal,
        );
      } catch (error) {
        // An unhandled rejection here (as opposed to a page-level onFailure
        // call) means the whole request never reached a completion at all —
        // it must not leave the outcome stuck labeled "streaming" forever,
        // which would silently look like an in-progress query rather than a
        // failed one.
        if (requestGeneration !== generation) return;
        const reason = error instanceof Error ? error.message : String(error);
        state.outcome = appendFailure(state.outcome, reason);
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
