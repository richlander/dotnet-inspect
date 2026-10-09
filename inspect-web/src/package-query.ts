// Shape for the package query experience (docs/design/package-query-experience.md).
//
// This module owns the request/outcome contract and pure state transitions for a
// wide, streaming query over a package source (nuget.org today; other feeds
// possible later), narrowed by product-issued term presets and active terms.
//
// It is deliberately data-source-agnostic: `PackageQueryDataSource` is supplied
// by the caller so this module can be built and tested against fake sources
// independently from the Browser engine adapter.

type QueryExecutionClass =
  | "search-metadata"
  | "nuspec"
  | "nuspec-expensive"
  | "package-content"
  | "metadata"
  | "metadata-expensive";

/** One product-issued package-query preset descriptor. */
export interface QueryPreset {
  id: string;
  key: string;
  operator: string;
  value: string;
  label: string;
  summary?: string;
  weight?: number;
  tier: "search-metadata" | "nuspec" | "package-content";
  executionClass: QueryExecutionClass;
  selectionGroupId?: string | null;
  combinesWithinSelectionGroup?: boolean;
  replacementGroupId?: string | null;
  displayGroupId?: string | null;
  displayGroupLabel?: string | null;
}

/** One product-issued operand-bearing package-query term descriptor. */
export interface QueryTermDescriptor {
  key: string;
  label: string;
  summary: string;
  weight: number;
  tier: "search-metadata" | "nuspec" | "package-content";
  executionClass: QueryExecutionClass;
  operators: readonly string[];
  valueKind: string;
  example: string;
  multiline: boolean;
}

export type DependencyReach = "direct" | "2" | "3" | "4";

interface QueryTermEditor {
  operator: string;
  value: string;
  dependencyReach?: DependencyReach;
  dependencyTarget?: string;
}

interface QueryTermDraft extends QueryTermEditor {
  descriptor: QueryTermDescriptor;
}

/** One unresolved term retained exactly as the user applied it. */
interface QueryTerm {
  descriptor: QueryTermDescriptor;
  operator: string;
  value: string;
}

const DEFAULT_QUERY_CANDIDATE_LIMIT = 200;
const DEFAULT_QUERY_MATCH_LIMIT = 100;
const PACKAGE_CONTENT_QUERY_CANDIDATE_LIMIT = 20;
const NUSPEC_EXPENSIVE_QUERY_CANDIDATE_LIMIT = 5;
const METADATA_EXPENSIVE_QUERY_CANDIDATE_LIMIT = 5;
export const PACKAGE_QUERY_INITIAL_MATCH_CREDIT = 20;
export const ECOSYSTEM_PACKAGE_QUERY_INITIAL_MATCH_CREDIT = 24;
const ECOSYSTEM_PACKAGE_QUERY_MAXIMUM_MATCHES = 96;
const PACKAGE_QUERY_MATCH_CREDIT_BATCH = 10;
const PACKAGE_QUERY_MATCH_CREDIT_THRESHOLD = 5;

export interface QuerySourceSelection {
  includePrerelease: boolean;
}

/** One rerunnable in-memory request. Never encodes a resolved outcome. */
export interface QueryRequest extends QuerySourceSelection {
  ecosystemId?: string;
  initialMatchCredit: number;
  scopeQuery: string;
  presets: readonly QueryPreset[];
  terms: readonly QueryTerm[];
  targetFramework: string;
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
    initialMatchCredit: PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
    scopeQuery,
    includePrerelease: false,
    presets: [],
    terms: [],
    targetFramework: "net10.0",
    requestedLimit: DEFAULT_QUERY_CANDIDATE_LIMIT,
    requestedMatchLimit: DEFAULT_QUERY_MATCH_LIMIT,
  };
}

export function createEcosystemQueryRequest(
  ecosystemId: string,
): QueryRequest {
  if (!ecosystemId.trim()) {
    throw new TypeError("An Ecosystem package query requires an identity.");
  }
  return {
    ecosystemId,
    initialMatchCredit: ECOSYSTEM_PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
    scopeQuery: "",
    includePrerelease: false,
    presets: [],
    terms: [],
    targetFramework: "net10.0",
    requestedLimit: DEFAULT_QUERY_CANDIDATE_LIMIT,
    requestedMatchLimit: ECOSYSTEM_PACKAGE_QUERY_MAXIMUM_MATCHES,
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
  return request.ecosystemId !== undefined
    || request.scopeQuery.trim().length > 0;
}

export function withScopeQuery(
  request: QueryRequest,
  scopeQuery: string,
): QueryRequest {
  return queryRequest(request, { scopeQuery });
}

export function isLibraryLiteralQuery(request: QueryRequest): boolean {
  return request.terms.some(
    term => term.descriptor.key === "library-literal");
}

export function withEditorDraft(
  request: QueryRequest,
  scopeQuery: string,
): QueryRequest {
  return withScopeQuery(request, scopeQuery);
}

export function withPreset(
  request: QueryRequest,
  preset: QueryPreset,
): QueryRequest {
  if (request.presets.some(existing => existing.id === preset.id)) {
    return queryRequest(request, {});
  }
  return withPresets(request, [...request.presets, preset]);
}

export function withoutPreset(
  request: QueryRequest,
  presetId: string,
): QueryRequest {
  return withPresets(
    request,
    request.presets.filter(preset => preset.id !== presetId));
}

function withPresets(
  request: QueryRequest,
  presets: readonly QueryPreset[],
): QueryRequest {
  return queryRequest(request, {
    presets,
    requestedLimit: queryCandidateLimit(presets, request.terms),
  });
}

function queryCandidateLimit(
  presets: readonly QueryPreset[],
  terms: readonly QueryTerm[],
): number {
  if (terms.some(term =>
    term.descriptor.executionClass === "metadata-expensive")) {
    return METADATA_EXPENSIVE_QUERY_CANDIDATE_LIMIT;
  }
  if (presets.some(preset => preset.executionClass === "nuspec-expensive")
      || terms.some(term =>
        term.descriptor.executionClass === "nuspec-expensive")) {
    return NUSPEC_EXPENSIVE_QUERY_CANDIDATE_LIMIT;
  }
  return presets.some(preset => preset.tier === "package-content")
      || terms.some(term => term.descriptor.tier === "package-content")
    ? PACKAGE_CONTENT_QUERY_CANDIDATE_LIMIT
    : DEFAULT_QUERY_CANDIDATE_LIMIT;
}

function queryRequest(
  request: QueryRequest,
  changes: Partial<QueryRequest>,
): QueryRequest {
  return { ...request, ...changes };
}

export function togglePreset(
  request: QueryRequest,
  preset: QueryPreset,
): QueryRequest {
  if (request.presets.some(existing => existing.id === preset.id)) {
    return withoutPreset(request, preset.id);
  }

  const compatible = request.presets.filter(existing => {
    const combines = preset.selectionGroupId !== null
      && preset.selectionGroupId !== undefined
      && existing.selectionGroupId === preset.selectionGroupId
      && preset.combinesWithinSelectionGroup === true
      && existing.combinesWithinSelectionGroup === true;
    const replacesSelectionGroup = preset.selectionGroupId !== null
      && preset.selectionGroupId !== undefined
      && existing.selectionGroupId === preset.selectionGroupId;
    const replacesReplacementGroup = preset.replacementGroupId !== null
      && preset.replacementGroupId !== undefined
      && existing.replacementGroupId === preset.replacementGroupId;
    return combines
      || (!replacesSelectionGroup && !replacesReplacementGroup);
  });
  return withPreset(withPresets(request, compatible), preset);
}

export function withTerm(
  request: QueryRequest,
  descriptor: QueryTermDescriptor,
  operator: string,
  value: string,
): QueryRequest {
  const terms = [...request.terms, { descriptor, operator, value }];
  return queryRequest(request, {
    terms,
    requestedLimit: queryCandidateLimit(request.presets, terms),
  });
}

export function dependencyReach(request: QueryRequest): DependencyReach {
  const value = request.presets.find(
    preset => preset.key === "dependency-depth")?.value;
  return value === "2" || value === "3" || value === "4"
    ? value
    : "direct";
}

export function dependencyTarget(request: QueryRequest): string {
  return request.terms.find(
    term => term.descriptor.key === "dependency-target")?.value
    ?? request.targetFramework;
}

export function withDependencyTerm(
  request: QueryRequest,
  descriptor: QueryTermDescriptor,
  index: number | null,
  operator: string,
  value: string,
  reach: DependencyReach,
  targetFramework: string,
  depthPreset: QueryPreset | null,
  targetDescriptor: QueryTermDescriptor | null,
): QueryRequest {
  const selected = index === null ? null : request.terms[index] ?? null;
  if (index !== null && selected?.descriptor.key !== "depends") {
    return request;
  }

  const traverses = operator === "eq" && reach !== "direct";
  if (traverses && (!depthPreset || !targetDescriptor)) return request;

  const hadTraversal = request.presets.some(
    preset => preset.key === "dependency-depth");
  const preservesTraversal = hadTraversal
    && operator !== "eq"
    && request.terms.some((term, termIndex) =>
      termIndex !== index
      && term.descriptor.key === "depends"
      && term.operator === "eq");
  const terms = request.terms.flatMap((term, termIndex) => {
    if (term.descriptor.key === "dependency-target"
      && (traverses || (hadTraversal && !preservesTraversal))) return [];
    if (index !== null && termIndex === index) {
      return [{ descriptor, operator, value }];
    }
    return [term];
  });
  if (index === null) {
    terms.push({ descriptor, operator, value });
  }
  if (traverses) {
    if (!targetDescriptor) return request;
    terms.push({
      descriptor: targetDescriptor,
      operator: targetDescriptor.operators[0] ?? "eq",
      value: targetFramework,
    });
  }

  const presets = request.presets.filter(
    preset => preset.key !== "dependency-depth" || preservesTraversal);
  if (traverses) {
    if (!depthPreset) return request;
    presets.push(depthPreset);
  }
  return queryRequest(request, {
    presets,
    terms,
    requestedLimit: queryCandidateLimit(presets, terms),
  });
}

export function replaceTerm(
  request: QueryRequest,
  index: number,
  operator: string,
  value: string,
): QueryRequest {
  if (index < 0 || index >= request.terms.length) return request;
  const terms = request.terms.map((term, termIndex) =>
    termIndex === index ? { ...term, operator, value } : term);
  return queryRequest(request, {
    terms,
    requestedLimit: queryCandidateLimit(request.presets, terms),
  });
}

export function withoutTerm(
  request: QueryRequest,
  index: number,
): QueryRequest {
  if (index < 0 || index >= request.terms.length) return request;
  const terms = request.terms.filter((_term, termIndex) => termIndex !== index);
  return queryRequest(request, {
    terms,
    requestedLimit: queryCandidateLimit(request.presets, terms),
  });
}

export function withoutDependencyTerm(
  request: QueryRequest,
  index: number,
): QueryRequest {
  if (request.terms[index]?.descriptor.key !== "depends") return request;
  const terms = request.terms.filter((_term, termIndex) => termIndex !== index);
  const hasExactDependency = terms.some(term =>
    term.descriptor.key === "depends" && term.operator === "eq");
  const hasDependencyPredicate = terms.some(term =>
    term.descriptor.key === "depends"
    || term.descriptor.key === "depends-transitive"
    || term.descriptor.key === "depends-ecosystem")
    || request.presets.some(preset => preset.key === "dependencies");
  const retainedTerms = hasDependencyPredicate
    ? terms
    : terms.filter(term => term.descriptor.key !== "dependency-target");
  const presets = hasExactDependency
    ? request.presets
    : request.presets.filter(preset => preset.key !== "dependency-depth");
  return queryRequest(request, {
    presets,
    terms: retainedTerms,
    requestedLimit: queryCandidateLimit(presets, retainedTerms),
  });
}

type QueryEvidenceScope = "package" | "query";

interface QueryEvidenceSummary {
  count: number;
  preview: readonly string[];
}

interface QueryEvidence {
  id: string;
  scope: QueryEvidenceScope;
  summary: QueryEvidenceSummary | null;
  properties: readonly {
    name: string;
    value: string;
  }[];
  number: number | null;
  term?: {
    key: string;
    operator: string;
    value: string;
  } | null;
}

interface QueryAnswer {
  id: string;
  value: string;
  term?: {
    key: string;
    operator: string;
    value: string;
  } | null;
}

/** One package's projection plus semantic answers and structured evidence.
 * Query-scoped evidence supplies shared selection context; package-scoped
 * evidence describes inspected facts for this row. */
export interface QueryResultRow {
  packageId: string;
  version: string;
  tier: "search-metadata" | "nuspec" | "package-content" | "assembly";
  answers: readonly QueryAnswer[];
  evidence: readonly QueryEvidence[];
  totalDownloads: number | null;
  description?: string | null;
  producer?: string;
  rootRequest?: string;
  ecosystemAdmission?: {
    ecosystemId: string;
    basis: string;
    registration: string;
  } | null;
}

export interface QueryAssemblyAssessment {
  packageId: string;
  version: string;
  disposition:
    | "Matched"
    | "NoMatch"
    | "NotApplicable"
    | "Failure"
    | "NotEvaluated";
  message: string;
  assetPath: string | null;
  rootRequest: string | null;
  libraries: readonly QueryLibraryAssessment[];
}

interface QueryLibraryAssessment {
  path: string;
  assemblyName: string;
  targetFramework: string;
  ordinal: number;
  disposition: "Matched" | "NoMatch" | "Failure";
  occurrenceCount: number;
  failureStage: string | null;
  message: string | null;
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
  | {
      kind: "library-literal";
      population:
        | "ExactPackageComplete"
        | "PrefixExhausted"
        | "MatchLimitReached"
        | "CandidateLimitReached"
        | "SourcePageLimitReached"
        | "ClientPageLimitReached"
        | "SourceFailed";
      candidateCount: number;
      evaluatedCandidateCount: number;
      notEvaluatedCount: number;
      matchedPackageCount: number;
      occurrenceCount: number;
      semanticMissCount: number;
      notApplicableCount: number;
      failureCount: number;
      complete: boolean;
    }
  | { kind: "cancelled" }
  | { kind: "failed"; reason: string };

export interface QueryProgress {
  phase:
    | "search"
    | "manifest"
    | "package-content"
    | "dependency-traversal"
    | "assembly";
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
  termDraft?: QueryTermDraft | null;
  termEdits?: readonly (QueryTermEditor | null)[];
}

export function initialQueryState(): PackageQueryState {
  return {
    request: null,
    outcome: idleOutcome(),
    termDraft: null,
    termEdits: [],
  };
}

export interface PackageQueryController {
  configure(request: QueryRequest): void;
  run(request: QueryRequest): Promise<void>;
  cancel(): void;
  requestMore(): void;
  requestAdditionalMatchCredit(
    additionalMatchCredit: number,
  ): Promise<boolean>;
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

  async function requestAdditionalMatchCredit(
    additionalMatchCredit: number,
  ): Promise<boolean> {
    if (!Number.isInteger(additionalMatchCredit)
      || additionalMatchCredit <= 0) {
      throw new RangeError(
        "Additional Package Query match credit must be a positive integer.");
    }
    if (state.outcome.completion.kind !== "streaming"
      || !source.requestMore
      || matchCreditRequestPending
      || !Number.isFinite(grantedMatchCredit)) {
      return false;
    }
    const requestGeneration = generation;
    let result: boolean | Promise<boolean>;
    try {
      result = source.requestMore(additionalMatchCredit);
    } catch (error: unknown) {
      const reason = error instanceof Error ? error.message : String(error);
      state.outcome = withCompletion(state.outcome, {
        kind: "failed",
        reason,
      });
      onUpdate("stream");
      return false;
    }
    if (typeof result === "boolean") {
      if (result) {
        grantedMatchCredit += additionalMatchCredit;
        onUpdate("stream");
      }
      return result;
    }
    matchCreditRequestPending = true;
    try {
      const granted = await result;
      if (requestGeneration !== generation) return false;
      matchCreditRequestPending = false;
      if (granted) grantedMatchCredit += additionalMatchCredit;
      onUpdate("stream");
      return granted;
    } catch (error: unknown) {
      if (requestGeneration !== generation) return false;
      matchCreditRequestPending = false;
      const reason =
        error instanceof Error ? error.message : String(error);
      state.outcome = withCompletion(state.outcome, {
        kind: "failed",
        reason,
      });
      onUpdate("stream");
      return false;
    }
  }

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
        || matchCreditRequestPending
        || !Number.isFinite(grantedMatchCredit)
        || state.outcome.rows.length
          < grantedMatchCredit - PACKAGE_QUERY_MATCH_CREDIT_THRESHOLD) {
        return;
      }
      void requestAdditionalMatchCredit(PACKAGE_QUERY_MATCH_CREDIT_BATCH);
    },

    requestAdditionalMatchCredit,
  };
}
