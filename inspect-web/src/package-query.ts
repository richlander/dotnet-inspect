// Shape for the package query experience (docs/design/package-query-experience.md).
//
// This module owns the request/outcome contract and pure state transitions for a
// wide, streaming query over a package source (nuget.org today; other feeds
// possible later), narrowed by product-issued package facets.
//
// It is deliberately data-source-agnostic: `PackageQueryDataSource` is supplied
// by the caller so this module can be built and tested against fake sources
// independently from the Browser engine adapter.

/** One product-issued package-query facet descriptor. */
export interface QueryFacetTerm {
  key: string;
  label: string;
  summary?: string;
  weight?: number;
  tier: "nuspec" | "package-content";
  selectionGroupId?: string | null;
  combinesWithinSelectionGroup?: boolean;
  displayGroupId?: string | null;
  displayGroupLabel?: string | null;
}

const DEFAULT_QUERY_CANDIDATE_LIMIT = 200;
const PACKAGE_CONTENT_QUERY_CANDIDATE_LIMIT = 20;
export const PACKAGE_QUERY_INITIAL_MATCH_CREDIT = 20;
const PACKAGE_QUERY_MATCH_CREDIT_BATCH = 10;
const PACKAGE_QUERY_MATCH_CREDIT_THRESHOLD = 5;

export interface QuerySourceSelection {
  includePrerelease: boolean;
}

export interface QueryAssemblyPatternDescriptor {
  id: string;
  label: string;
  summary: string;
  maximumOperandLength: number;
  maximumPackages: number;
}

export interface QueryAssemblyPatternRequest {
  patternId: string;
  operand: string;
  packageCoordinates: readonly string[];
  targetFramework: string;
}
/** One rerunnable in-memory request. Never encodes a resolved outcome. */
export interface QueryRequest extends QuerySourceSelection {
  scopeQuery: string;
  facets: readonly QueryFacetTerm[];
  assemblyPattern?: QueryAssemblyPatternRequest;
  /** Declared cap communicated to the source. The bounded-complete footer
   * renders the source's own free-text `completion.reason` (see design doc
   * "States"), not this field directly — a real source is expected to keep
   * that text consistent with the cap it was given, but nothing here
   * enforces that. */
  requestedLimit: number;
  requestedMatchLimit: number;
}

export function createQueryRequest(
  scopeQuery: string,
): QueryRequest {
  return {
    scopeQuery,
    includePrerelease: false,
    facets: [],
    requestedLimit: DEFAULT_QUERY_CANDIDATE_LIMIT,
    requestedMatchLimit: 100,
  };
}

export function createAssemblyQueryRequest(
  patternId: string,
  operand: string,
  packageCoordinates: readonly string[],
  targetFramework: string,
): QueryRequest {
  return {
    ...createQueryRequest(""),
    assemblyPattern: {
      patternId,
      operand,
      packageCoordinates: [...packageCoordinates],
      targetFramework,
    },
  };
}

export function withSourceSelection(
  request: QueryRequest,
  selection: Partial<QuerySourceSelection>,
): QueryRequest {
  return {
    ...request,
    ...selection,
  };
}

export function shouldExecuteQuery(request: QueryRequest): boolean {
  return request.scopeQuery.trim().length > 0;
}

export function withScopeQuery(
  request: QueryRequest,
  scopeQuery: string,
): QueryRequest {
  return queryRequest(request, { scopeQuery });
}

export function withEditorDraft(
  request: QueryRequest,
  scopeQuery: string,
): QueryRequest {
  return withScopeQuery(request, scopeQuery);
}

export function withFacet(
  request: QueryRequest,
  facet: QueryFacetTerm,
): QueryRequest {
  if (request.facets.some(existing => existing.key === facet.key)) {
    return queryRequest(request, {});
  }
  return withFacets(request, [...request.facets, facet]);
}

export function withoutFacet(
  request: QueryRequest,
  facetKey: string,
): QueryRequest {
  return withFacets(
    request,
    request.facets.filter(facet => facet.key !== facetKey));
}

function withFacets(
  request: QueryRequest,
  facets: readonly QueryFacetTerm[],
): QueryRequest {
  return queryRequest(request, {
    facets,
    requestedLimit: facets.some(facet => facet.tier === "package-content")
      ? PACKAGE_CONTENT_QUERY_CANDIDATE_LIMIT
      : DEFAULT_QUERY_CANDIDATE_LIMIT,
  });
}

function queryRequest(
  request: QueryRequest,
  changes: Partial<QueryRequest>,
): QueryRequest {
  return {
    scopeQuery: request.scopeQuery,
    includePrerelease: request.includePrerelease,
    facets: request.facets,
    requestedLimit: request.requestedLimit,
    requestedMatchLimit: request.requestedMatchLimit,
    ...changes,
  };
}

export function toggleFacet(
  request: QueryRequest,
  facet: QueryFacetTerm,
): QueryRequest {
  if (request.facets.some(existing => existing.key === facet.key)) {
    return withoutFacet(request, facet.key);
  }

  const compatible = facet.selectionGroupId
    ? request.facets.filter(existing =>
        existing.selectionGroupId !== facet.selectionGroupId
        || (facet.combinesWithinSelectionGroup === true
          && existing.combinesWithinSelectionGroup === true))
    : request.facets;
  return withFacet(withFacets(request, compatible), facet);
}

type QueryEvidenceScope = "package" | "query";

interface QueryEvidenceSummary {
  count: number;
  preview: readonly string[];
}

interface QueryEvidence {
  id: string;
  text: string;
  scope: QueryEvidenceScope;
  summary: QueryEvidenceSummary | null;
}

/** One package's projection plus product-authored evidence. Query-scoped
 * evidence supplies shared selection context; package-scoped evidence
 * describes inspected facts for this row. The non-empty tuple preserves
 * meaningful context even for metadata-only rows. */
export interface QueryResultRow {
  packageId: string;
  version: string;
  tier: "search-metadata" | "nuspec" | "package-content" | "assembly";
  evidence: readonly [QueryEvidence, ...QueryEvidence[]];
  totalDownloads: number | null;
  description?: string | null;
  producer?: string;
  rootRequest?: string;
}

export interface QueryAssemblyAssessment {
  packageId: string;
  version: string;
  disposition: "NoMatch" | "NotApplicable";
  message: string;
  assetPath: string | null;
  rootRequest: string;
}

export type QueryCompletion =
  | { kind: "idle" }
  | { kind: "streaming" }
  | TerminalQueryCompletion;

/** The subset of `QueryCompletion` that represents a source having actually
 * stopped (as opposed to still running). A `PackageQueryDataSource.run()`
 * call settles when the source has stopped producing pages, so it can never
 * legitimately resolve with `"idle"` or `"streaming"` — those kinds describe
 * controller state, not a source's verdict on its own completion. */
export type TerminalQueryCompletion =
  | { kind: "bounded"; reason: string }
  | { kind: "exhausted" }
  | { kind: "exact" }
  | { kind: "cancelled" }
  | { kind: "failed"; reason: string };

export interface QueryProgress {
  phase: "search" | "manifest" | "package-content" | "assembly";
  completed: number;
  limit: number;
}

/** Mirrors `NuGetSearchOutcome`'s shape: results and failures both carried, so
 * a partially-searched source never renders as a confident empty/complete
 * result (untrusted-data-threat-model.md's "reject, do not sanitize" extends
 * here to "never silently narrow a claim"). */
export interface QueryOutcome {
  rows: readonly QueryResultRow[];
  assessments: readonly QueryAssemblyAssessment[];
  failures: readonly string[];
  progress: readonly QueryProgress[];
  completion: QueryCompletion;
}

export function emptyOutcome(): QueryOutcome {
  return {
    rows: [],
    assessments: [],
    failures: [],
    progress: [],
    completion: { kind: "streaming" },
  };
}

function idleOutcome(): QueryOutcome {
  return {
    rows: [],
    assessments: [],
    failures: [],
    progress: [],
    completion: { kind: "idle" },
  };
}

export function appendRows(
  outcome: QueryOutcome,
  rows: readonly QueryResultRow[],
): QueryOutcome {
  return { ...outcome, rows: [...outcome.rows, ...rows] };
}

export function appendAssessment(
  outcome: QueryOutcome,
  assessment: QueryAssemblyAssessment,
): QueryOutcome {
  return {
    ...outcome,
    assessments: [...outcome.assessments, assessment],
  };
}

export function appendFailure(
  outcome: QueryOutcome,
  failure: string,
): QueryOutcome {
  return { ...outcome, failures: [...outcome.failures, failure] };
}

export function appendProgress(
  outcome: QueryOutcome,
  progress: QueryProgress,
): QueryOutcome {
  const existingIndex = outcome.progress.findIndex(
    item => item.phase === progress.phase);
  return {
    ...outcome,
    progress: existingIndex < 0
      ? [...outcome.progress, progress]
      : outcome.progress.map((item, index) =>
          index === existingIndex ? progress : item),
  };
}

export function withCompletion(
  outcome: QueryOutcome,
  completion: QueryCompletion,
): QueryOutcome {
  return { ...outcome, completion };
}

/** Supplies product-projected pages and failures for a request. */
export interface PackageQueryDataSource {
  /** Initial durable-match credit advertised to the producer. Sources without
   * a demand protocol omit this and retain their existing push behavior. */
  initialMatchCredit?: number;
  /** Adds durable-match credit to the active request. Returns false when no
   * request can accept the credit. */
  requestMore?(
    additionalMatchCredit: number,
  ): boolean | Promise<boolean>;
  run(
    request: QueryRequest,
    onPage: (rows: readonly QueryResultRow[]) => void,
    onFailure: (failure: string) => void,
    onProgress: (progress: QueryProgress) => void,
    /** Signaled when `cancel()` is called or a newer run supersedes this one,
     * so the source can stop in-flight network/manifest work instead of
     * running it to completion unobserved. */
    abortSignal: AbortSignal,
    onAssessment?: (assessment: QueryAssemblyAssessment) => void,
  ): Promise<TerminalQueryCompletion>;
}

export interface PackageQueryState {
  request: QueryRequest | null;
  outcome: QueryOutcome;
}

export function initialQueryState(): PackageQueryState {
  return { request: null, outcome: idleOutcome() };
}

export interface PackageQueryController {
  configure(request: QueryRequest): void;
  run(request: QueryRequest): Promise<void>;
  cancel(): void;
  requestMore(): void;
}

export type PackageQueryUpdateKind = "reset" | "stream";

/** Owns one in-flight generation counter so a superseded request's late pages
 * never append into a newer request's outcome (same race-safety idiom as
 * spotlight-package-search.ts's `generation` counter). */
export function createPackageQueryController(
  state: PackageQueryState,
  source: PackageQueryDataSource,
  onUpdate: (kind: PackageQueryUpdateKind) => void,
): PackageQueryController {
  let generation = 0;
  let abortController = new AbortController();
  let grantedMatchCredit = Number.POSITIVE_INFINITY;
  let matchCreditRequestPending = false;

  return {
    configure(request: QueryRequest) {
      abortController.abort("superseded");
      abortController = new AbortController();
      generation++;
      state.request = request;
      state.outcome = idleOutcome();
      grantedMatchCredit = Number.POSITIVE_INFINITY;
      matchCreditRequestPending = false;
      onUpdate("reset");
    },

    async run(request: QueryRequest) {
      abortController.abort("superseded");
      const runController = new AbortController();
      abortController = runController;
      const requestGeneration = ++generation;
      state.request = request;
      state.outcome = emptyOutcome();
      grantedMatchCredit =
        source.initialMatchCredit ?? Number.POSITIVE_INFINITY;
      matchCreditRequestPending = false;
      // Capture this run's own signal before onUpdate() runs: onUpdate() is
      // caller-supplied and may reentrantly call run() again synchronously
      // (e.g. a state-change handler that immediately kicks off a new
      // query), which would reassign the closure's `abortController` before
      // `source.run()` below gets a chance to read it — silently handing
      // this run the *next* run's signal instead of its own.
      const signal = runController.signal;
      onUpdate("reset");

      let completion: TerminalQueryCompletion;
      try {
        completion = await source.run(
          request,
          rows => {
            if (requestGeneration !== generation) return;
            state.outcome = appendRows(state.outcome, rows);
            onUpdate("stream");
          },
          failure => {
            if (requestGeneration !== generation) return;
            state.outcome = appendFailure(state.outcome, failure);
            onUpdate("stream");
          },
          progress => {
            if (requestGeneration !== generation) return;
            state.outcome = appendProgress(state.outcome, progress);
            onUpdate("stream");
          },
          signal,
          assessment => {
            if (requestGeneration !== generation) return;
            state.outcome = appendAssessment(state.outcome, assessment);
            onUpdate("stream");
          },
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
        onUpdate("stream");
        return;
      }

      if (requestGeneration !== generation) return;
      state.outcome = withCompletion(state.outcome, completion);
      onUpdate("stream");
    },

    cancel() {
      // A run that already finished (bounded/exhausted) owns its own
      // completion label; cancelling after the fact must not overwrite it.
      if (state.outcome.completion.kind !== "streaming") return;
      generation++;
      abortController.abort("user");
      state.outcome = withCompletion(state.outcome, { kind: "cancelled" });
      onUpdate("stream");
    },

    requestMore() {
      if (state.outcome.completion.kind !== "streaming"
        || !source.requestMore
        || matchCreditRequestPending
        || !Number.isFinite(grantedMatchCredit)
        || state.outcome.rows.length
          < grantedMatchCredit - PACKAGE_QUERY_MATCH_CREDIT_THRESHOLD) {
        return;
      }
      const requestGeneration = generation;
      let result: boolean | Promise<boolean>;
      try {
        result = source.requestMore(PACKAGE_QUERY_MATCH_CREDIT_BATCH);
      } catch (error: unknown) {
        const reason = error instanceof Error ? error.message : String(error);
        state.outcome = withCompletion(state.outcome, {
          kind: "failed",
          reason,
        });
        onUpdate("stream");
        return;
      }
      if (typeof result === "boolean") {
        if (result)
          grantedMatchCredit += PACKAGE_QUERY_MATCH_CREDIT_BATCH;
        return;
      }
      matchCreditRequestPending = true;
      void result.then(
        granted => {
          if (requestGeneration !== generation) return undefined;
          matchCreditRequestPending = false;
          if (granted)
            grantedMatchCredit += PACKAGE_QUERY_MATCH_CREDIT_BATCH;
          return undefined;
        },
        (error: unknown) => {
          if (requestGeneration !== generation) return undefined;
          matchCreditRequestPending = false;
          const reason =
            error instanceof Error ? error.message : String(error);
          state.outcome = withCompletion(state.outcome, {
            kind: "failed",
            reason,
          });
          onUpdate("stream");
          return undefined;
        },
      );
    },
  };
}
