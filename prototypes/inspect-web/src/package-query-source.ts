import type {
  BrowserPackageQueryFacetCatalog,
  BrowserPackageQueryFacetDescriptor,
  BrowserPackageAssemblyQueryPattern,
  BrowserPackageAssemblyAssessment,
  BrowserPackageQueryCompletion as BrowserPackageQueryCompletionPayload,
  BrowserPackageQueryFailure as BrowserPackageQueryFailurePayload,
  BrowserPackageQueryProgress as BrowserPackageQueryProgressPayload,
  BrowserPackageQueryRow as BrowserPackageQueryRowPayload,
  BrowserPackageQueryEvent as BrowserPackageQueryEventPayload,
} from "./facades/inspect-web-package.d.ts";
import type {
  PackageQueryDataSource,
  QueryAssemblyAssessment,
  QueryAssemblyPatternDescriptor,
  QueryAssemblyPatternRequest,
  QueryFacetTerm,
  QueryProgress,
  QueryResultRow,
  TerminalQueryCompletion,
} from "./package-query.ts";
import { PACKAGE_QUERY_INITIAL_MATCH_CREDIT } from "./package-query.ts";

export type { BrowserPackageAssemblyQueryPattern } from "./facades/inspect-web-package.d.ts";

export interface BrowserPackageQueryEngine {
  cancel(): void;
  requestMatches(additionalMatchCredit: number): boolean;
  run(
    searchText: string,
    facetIdsJson: string,
    maximumCandidates: number,
    maximumMatches: number,
    includePrerelease: boolean,
    initialMatchCredit: number,
    eventSink: unknown,
    packageType: string | null,
    sourceOrderId: string | null,
  ): Promise<BrowserPackageQueryEventPayload>;
  runAssembly?(
    patternId: string,
    operand: string,
    packageCoordinatesJson: string,
    targetFramework: string,
    initialMatchCredit: number,
    eventSink: unknown,
  ): Promise<BrowserPackageQueryEventPayload>;
}

export function packageQueryFacets(
  catalog: BrowserPackageQueryFacetCatalog,
): QueryFacetTerm[] {
  return catalog.facets.map(toQueryFacet);
}

export function packageQueryAssemblyPatterns(
  patterns: readonly BrowserPackageAssemblyQueryPattern[],
): QueryAssemblyPatternDescriptor[] {
  return patterns.map(pattern => ({
    id: pattern.id,
    label: pattern.label,
    summary: pattern.summary,
    maximumOperandLength: pattern.maximumOperandLength,
    maximumPackages: pattern.maximumPackages,
  }));
}

function toQueryFacet(
  descriptor: BrowserPackageQueryFacetDescriptor,
): QueryFacetTerm {
  return {
    key: descriptor.id,
    label: descriptor.label,
    summary: descriptor.summary,
    weight: descriptor.weight,
    tier: toInspectionTier(descriptor.tier),
    selectionGroupId: descriptor.selectionGroupId,
    combinesWithinSelectionGroup: descriptor.combinesWithinSelectionGroup,
    displayGroupId: descriptor.displayGroupId,
    displayGroupLabel: descriptor.displayGroupLabel,
  };
}

export function createBrowserPackageQueryDataSource(
  engine: BrowserPackageQueryEngine,
): PackageQueryDataSource {
  return {
    initialMatchCredit: PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
    requestMore: additionalMatchCredit =>
      engine.requestMatches(additionalMatchCredit),
    async run(
      request,
      onPage,
      onFailure,
      onProgress,
      abortSignal,
      onAssessment,
    ) {
      if (abortSignal.aborted) return { kind: "cancelled" };

      let completion: TerminalQueryCompletion | null = null;
      const flushState: {
        failed: boolean;
        error: unknown;
      } = {
        failed: false,
        error: undefined,
      };
      let flushScheduled = false;
      const pendingEvents: BrowserPackageQueryEventPayload[] = [];
      const flushEvents = () => {
        flushScheduled = false;
        const batch = pendingEvents.splice(0);
        let pendingRows: QueryResultRow[] = [];
        const flushRows = () => {
          if (!pendingRows.length) return;
          const rows = pendingRows;
          pendingRows = [];
          onPage(rows);
        };
        try {
          for (const queryEvent of batch) {
            if (queryEvent.kind === "Match") {
              if (!queryEvent.row) {
                throw new TypeError(
                  "A package-query match event contained no row.");
              }
              pendingRows.push(toQueryRow(queryEvent.row));
              continue;
            }
            flushRows();
            dispatchEvent(
              queryEvent,
              onPage,
              onFailure,
              onProgress,
              onAssessment,
              terminal => { completion = terminal; });
          }
          flushRows();
        } catch (error) {
          flushState.failed = true;
          flushState.error = error;
          pendingEvents.length = 0;
          engine.cancel();
        }
      };
      const scheduleFlush = () => {
        if (flushScheduled) return;
        flushScheduled = true;
        queueMicrotask(flushEvents);
      };
      const eventSink: Record<string, unknown> = {};
      Object.defineProperty(eventSink, "event", {
        set(value: unknown) {
          if (typeof value !== "string") {
            throw new TypeError(
              "The Browser package-query event payload was not JSON text.");
          }
          const queryEvent = parseBrowserEvent(value);
          if (queryEvent.kind === "Completed") {
            throw new TypeError(
              "The Browser package-query callback carried a terminal event.");
          }
          pendingEvents.push(queryEvent);
          scheduleFlush();
        },
      });

      const cancel = () => engine.cancel();
      abortSignal.addEventListener("abort", cancel, { once: true });
      try {
        const finalEvent = request.assemblyPattern
          ? await runAssemblyQuery(engine, request.assemblyPattern, eventSink)
          : await engine.run(
              request.scopeQuery,
              JSON.stringify(request.facets.map(facet => facet.key)),
              request.requestedLimit,
              request.requestedMatchLimit,
              request.includePrerelease,
              PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
              eventSink,
              request.packageType,
              request.sourceOrderId);
        flushEvents();
        if (flushState.failed) throw flushState.error;
        if (abortSignal.aborted) return { kind: "cancelled" };
        if (finalEvent.kind !== "Completed") {
          throw new TypeError(
            "The Browser package-query result was not a terminal event.");
        }
        dispatchEvent(
          finalEvent,
          onPage,
          onFailure,
          onProgress,
          onAssessment,
          terminal => { completion = terminal; });
        return completion
          ?? {
            kind: "failed",
            reason:
              "The Browser package-query stream ended without a completion event.",
          };
      } catch (error) {
        flushEvents();
        if (flushState.failed) throw flushState.error;
        if (abortSignal.aborted) return { kind: "cancelled" };
        throw error;
      } finally {
        abortSignal.removeEventListener("abort", cancel);
      }
    },
  };
}

async function runAssemblyQuery(
  engine: BrowserPackageQueryEngine,
  request: QueryAssemblyPatternRequest,
  eventSink: unknown,
): Promise<BrowserPackageQueryEventPayload> {
  if (!engine.runAssembly) {
    throw new Error(
      "Assembly-pattern package queries are unavailable in this Browser engine.");
  }
  return await engine.runAssembly(
    request.patternId,
    request.operand,
    JSON.stringify(request.packageCoordinates),
    request.targetFramework,
    PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
    eventSink);
}

function parseBrowserEvent(json: string): BrowserPackageQueryEventPayload {
  const parsed: unknown = JSON.parse(json);
  const event = objectValue(parsed, "package-query event");
  switch (event.kind) {
    case "Progress":
      return {
        kind: "Progress",
        row: null,
        failure: null,
        completion: null,
        progress: parseProgress(event.progress),
        assessment: null,
      };
    case "Match":
      return {
        kind: "Match",
        row: parseRow(event.row),
        failure: null,
        completion: null,
        progress: null,
        assessment: null,
      };
    case "Failure":
      return {
        kind: "Failure",
        row: null,
        failure: parseFailure(event.failure),
        completion: null,
        progress: null,
        assessment: null,
      };
    case "Assessment":
      return {
        kind: "Assessment",
        row: null,
        failure: null,
        completion: null,
        progress: null,
        assessment: parseAssessment(event.assessment),
      };
    case "Completed":
      return {
        kind: "Completed",
        row: null,
        failure: null,
        completion: parseCompletion(event.completion),
        progress: null,
        assessment: null,
      };
    default:
      throw new TypeError(
        `Unknown Browser package-query event '${String(event.kind)}'.`);
  }
}

function objectValue(
  value: unknown,
  description: string,
): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new TypeError(`The Browser ${description} was not an object.`);
  }
  return Object.fromEntries(Object.entries(value));
}

function stringValue(value: unknown, description: string): string {
  if (typeof value !== "string") {
    throw new TypeError(`The Browser ${description} was not text.`);
  }
  return value;
}

function nonBlankStringValue(value: unknown, description: string): string {
  const text = stringValue(value, description);
  if (!text.trim()) {
    throw new TypeError(`The Browser ${description} was empty.`);
  }
  return text;
}

function nullableStringValue(
  value: unknown,
  description: string,
): string | null {
  return value === null ? null : stringValue(value, description);
}

function optionalNullableStringValue(
  value: unknown,
  description: string,
): string | null {
  return value === undefined
    ? null
    : nullableStringValue(value, description);
}

function numberValue(value: unknown, description: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new TypeError(`The Browser ${description} was not a finite number.`);
  }
  return value;
}

function nullableNumberValue(
  value: unknown,
  description: string,
): number | null {
  return value === null ? null : numberValue(value, description);
}

function optionalNullableNumberValue(
  value: unknown,
  description: string,
): number | null {
  return value === undefined
    ? null
    : nullableNumberValue(value, description);
}

function booleanValue(value: unknown, description: string): boolean {
  if (typeof value !== "boolean") {
    throw new TypeError(`The Browser ${description} was not a boolean.`);
  }
  return value;
}

function parseRow(value: unknown): BrowserPackageQueryRowPayload {
  const row = objectValue(value, "package-query row");
  if (!Array.isArray(row.evidence)) {
    throw new TypeError(
      "The Browser package-query row evidence was not an array.");
  }
  return {
    packageId: stringValue(row.packageId, "package-query package ID"),
    version: stringValue(row.version, "package-query version"),
    description: nullableStringValue(
      row.description,
      "package-query description"),
    tier: rowTierValue(row.tier),
    evidence: row.evidence.map(item => {
      const evidence = objectValue(item, "package-query evidence");
      return {
        id: stringValue(evidence.id, "package-query evidence ID"),
        text: stringValue(evidence.text, "package-query evidence text"),
      };
    }),
    totalDownloads: nullableNumberValue(
      row.totalDownloads,
      "package-query download count"),
    verified: row.verified === null
      ? null
      : booleanValue(row.verified, "package-query verification flag"),
    producer: stringValue(row.producer, "package-query producer"),
    rootRequest: optionalNullableStringValue(
      row.rootRequest,
      "package-query Root request"),
  };
}

function parseAssessment(value: unknown): BrowserPackageAssemblyAssessment {
  const assessment = objectValue(value, "package-query assessment");
  return {
    packageId: stringValue(
      assessment.packageId,
      "package-query assessment package ID"),
    version: stringValue(
      assessment.version,
      "package-query assessment version"),
    disposition: assessmentDispositionValue(assessment.disposition),
    message: stringValue(
      assessment.message,
      "package-query assessment message"),
    assetPath: nullableStringValue(
      assessment.assetPath,
      "package-query assessment asset path"),
    rootRequest: nonBlankStringValue(
      assessment.rootRequest,
      "package-query assessment Root request"),
  };
}

function parseFailure(value: unknown): BrowserPackageQueryFailurePayload {
  const failure = objectValue(value, "package-query failure");
  return {
    packageId: nullableStringValue(
      failure.packageId,
      "package-query failure package ID"),
    version: nullableStringValue(
      failure.version,
      "package-query failure version"),
    producer: stringValue(failure.producer, "package-query failure producer"),
    kind: failureKindValue(failure.kind),
    message: stringValue(failure.message, "package-query failure message"),
  };
}

function parseProgress(value: unknown): BrowserPackageQueryProgressPayload {
  const progress = objectValue(value, "package-query progress");
  return {
    phase: progressPhaseValue(progress.phase),
    completed: numberValue(
      progress.completed,
      "package-query completed progress"),
    limit: numberValue(progress.limit, "package-query progress limit"),
  };
}

function parseCompletion(value: unknown): BrowserPackageQueryCompletionPayload {
  const completion = objectValue(value, "package-query completion");
  return {
    prefix: stringValue(completion.prefix, "package-query completion prefix"),
    producer: stringValue(
      completion.producer,
      "package-query completion producer"),
    candidateLimit: numberValue(
      completion.candidateLimit,
      "package-query candidate limit"),
    matchLimit: numberValue(
      completion.matchLimit,
      "package-query match limit"),
    candidates: numberValue(
      completion.candidates,
      "package-query candidate count"),
    matches: numberValue(completion.matches, "package-query match count"),
    failures: numberValue(completion.failures, "package-query failure count"),
    sourceCandidates: nullableNumberValue(
      completion.sourceCandidates,
      "package-query source candidate count"),
    estimatedTotalHits: nullableNumberValue(
      completion.estimatedTotalHits,
      "package-query estimated total hits"),
    semanticMisses: optionalNullableNumberValue(
      completion.semanticMisses,
      "package-query semantic miss count"),
    notApplicable: optionalNullableNumberValue(
      completion.notApplicable,
      "package-query not-applicable count"),
    scope: optionalNullableStringValue(
      completion.scope,
      "package-query completion scope"),
    kind: completionKindValue(completion.kind),
  };
}

function rowTierValue(
  value: unknown,
): BrowserPackageQueryRowPayload["tier"] {
  if (value === "SearchMetadata"
    || value === "Nuspec"
    || value === "PackageContent"
    || value === "Assembly") return value;
  throw new TypeError(
    `Unsupported package-query row tier '${String(value)}'.`);
}

function failureKindValue(
  value: unknown,
): BrowserPackageQueryFailurePayload["kind"] {
  switch (value) {
    case "Search":
    case "SearchContract":
    case "ManifestAcquisition":
    case "ManifestContract":
    case "InvalidManifest":
    case "PackageContentAcquisition":
    case "PackageContentEvaluation":
    case "AssemblyAcquisition":
    case "AssemblyEvaluation":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query failure kind '${String(value)}'.`);
  }
}

function completionKindValue(
  value: unknown,
): BrowserPackageQueryCompletionPayload["kind"] {
  switch (value) {
    case "Exhausted":
    case "MatchLimitReached":
    case "CandidateLimitReached":
    case "SourcePageLimitReached":
    case "ClientPageLimitReached":
    case "GalleryResponseComplete":
    case "ExplicitCandidatesComplete":
    case "Failed":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query completion '${String(value)}'.`);
  }
}

function progressPhaseValue(
  value: unknown,
): BrowserPackageQueryProgressPayload["phase"] {
  switch (value) {
    case "Search":
    case "Manifest":
    case "PackageContent":
    case "Assembly":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query progress phase '${String(value)}'.`);
  }
}

function dispatchEvent(
  queryEvent: BrowserPackageQueryEventPayload,
  onPage: (rows: readonly QueryResultRow[]) => void,
  onFailure: (failure: string) => void,
  onProgress: (progress: QueryProgress) => void,
  onAssessment: ((assessment: QueryAssemblyAssessment) => void) | undefined,
  onCompleted: (completion: TerminalQueryCompletion) => void,
): void {
  switch (queryEvent.kind) {
    case "Progress":
      if (!queryEvent.progress) {
        throw new TypeError(
          "A package-query progress event contained no progress.");
      }
      onProgress(toQueryProgress(queryEvent.progress));
      return;
    case "Match":
      if (!queryEvent.row) {
        throw new TypeError("A package-query match event contained no row.");
      }
      onPage([toQueryRow(queryEvent.row)]);
      return;
    case "Failure":
      if (!queryEvent.failure) {
        throw new TypeError(
          "A package-query failure event contained no failure.");
      }
      onFailure(formatFailure(queryEvent.failure));
      return;
    case "Assessment":
      if (!queryEvent.assessment) {
        throw new TypeError(
          "A package-query assessment event contained no assessment.");
      }
      onAssessment?.(toQueryAssessment(queryEvent.assessment));
      return;
    case "Completed":
      if (!queryEvent.completion) {
        throw new TypeError(
          "A package-query completion event contained no summary.");
      }
      onCompleted(toTerminalCompletion(parseCompletion(queryEvent.completion)));
      return;
    default:
      throw new TypeError(
        `Unknown Browser package-query event '${String(queryEvent.kind)}'.`);
  }
}

function toQueryProgress(
  progress: BrowserPackageQueryProgressPayload,
): QueryProgress {
  let phase: QueryProgress["phase"];
  switch (progress.phase) {
    case "Search":
      phase = "search";
      break;
    case "Manifest":
      phase = "manifest";
      break;
    case "PackageContent":
      phase = "package-content";
      break;
    case "Assembly":
      phase = "assembly";
      break;
    default:
      throw new TypeError(
        `Unknown package-query progress phase '${String(progress.phase)}'.`);
  }
  return {
    phase,
    completed: progress.completed,
    limit: progress.limit,
  };
}

function toQueryRow(
  row: BrowserPackageQueryRowPayload,
): QueryResultRow {
  const evidence = row.evidence.map(item => item.text);
  if (!evidence.length || evidence.some(item => item.trim().length === 0)) {
    throw new TypeError("A package-query row contained no evidence.");
  }
  const assemblyRootRequest = row.tier === "Assembly"
    ? row.rootRequest
    : null;
  if (row.tier === "Assembly") {
    if (!assemblyRootRequest?.trim()) {
      throw new TypeError(
        "An assembly package-query row contained no Root request.");
    }
  }
  const result: QueryResultRow = {
    packageId: row.packageId,
    version: row.version,
    tier: toQueryTier(row.tier),
    evidence: [evidence[0]!, ...evidence.slice(1)],
    totalDownloads: row.totalDownloads,
    description: row.description,
    producer: row.producer,
  };
  if (assemblyRootRequest !== null && assemblyRootRequest !== undefined)
    result.rootRequest = assemblyRootRequest;
  return result;
}

function toQueryTier(
  tier: BrowserPackageQueryRowPayload["tier"],
): QueryResultRow["tier"] {
  if (tier === "SearchMetadata") return "search-metadata";
  if (tier === "Assembly") return "assembly";
  return toInspectionTier(tier);
}

function toInspectionTier(
  tier: BrowserPackageQueryFacetDescriptor["tier"],
): QueryFacetTerm["tier"] {
  switch (tier) {
    case "Nuspec":
      return "nuspec";
    case "PackageContent":
      return "package-content";
    default:
      throw new TypeError(
        `Unsupported package-query tier '${String(tier)}'.`);
  }
}

function assessmentDispositionValue(
  value: unknown,
): QueryAssemblyAssessment["disposition"] {
  if (value === "NoMatch" || value === "NotApplicable") return value;
  throw new TypeError(
    `Unknown package-query assessment disposition '${String(value)}'.`);
}

function toQueryAssessment(
  assessment: BrowserPackageAssemblyAssessment,
): QueryAssemblyAssessment {
  return {
    packageId: assessment.packageId,
    version: assessment.version,
    disposition: assessmentDispositionValue(assessment.disposition),
    message: assessment.message,
    assetPath: assessment.assetPath,
    rootRequest: assessment.rootRequest,
  };
}

function formatFailure(failure: BrowserPackageQueryFailurePayload): string {
  const coordinate = failure.packageId
    ? `${failure.packageId}${failure.version ? `@${failure.version}` : ""}`
    : failure.producer;
  return `${coordinate}: ${failure.message}`;
}

function toTerminalCompletion(
  completion: BrowserPackageQueryCompletionPayload,
): TerminalQueryCompletion {
  switch (completion.kind) {
    case "Exhausted":
      return { kind: "exhausted" };
    case "MatchLimitReached":
      if (completion.sourceCandidates !== null) {
        return {
          kind: "bounded",
          reason: galleryCompletionReason(completion),
        };
      }
      return {
        kind: "bounded",
        reason: `first ${completion.matchLimit.toLocaleString()} matches`,
      };
    case "CandidateLimitReached":
      return {
        kind: "bounded",
        reason:
          `first ${completion.candidateLimit.toLocaleString()} candidates`,
      };
    case "SourcePageLimitReached":
      return {
        kind: "bounded",
        reason: "the source page limit",
      };
    case "ClientPageLimitReached":
      return {
        kind: "bounded",
        reason: "the client page limit",
      };
    case "GalleryResponseComplete":
      return {
        kind: "bounded",
        reason: galleryCompletionReason(completion),
      };
    case "ExplicitCandidatesComplete":
      return {
        kind: "bounded",
        reason: explicitCandidatesCompletionReason(completion),
      };
    case "Failed":
      return {
        kind: "failed",
        reason: completion.scope?.trim()
          ? completion.scope
          : "The package source failed before returning usable package input.",
      };
    default:
      throw new TypeError(
        `Unknown package-query completion '${String(completion.kind)}'.`);
  }
}

function explicitCandidatesCompletionReason(
  completion: BrowserPackageQueryCompletionPayload,
): string {
  const semanticMisses = completion.semanticMisses;
  const notApplicable = completion.notApplicable;
  const scope = completion.scope;
  if (semanticMisses == null
    || notApplicable == null
    || !scope?.trim()) {
    throw new TypeError(
      "An explicit-candidate package-query completion omitted its accounting or scope.");
  }
  const counts = [
    completion.candidates,
    completion.matches,
    semanticMisses,
    notApplicable,
    completion.failures,
  ];
  if (counts.some(count => !Number.isInteger(count) || count < 0)) {
    throw new TypeError(
      "An explicit-candidate package-query completion contained invalid accounting.");
  }
  if (completion.candidates
    !== completion.matches
      + semanticMisses
      + notApplicable
      + completion.failures) {
    throw new TypeError(
      "An explicit-candidate package-query completion did not account for every candidate.");
  }
  return [
    countWithLabel(completion.candidates, "explicit candidate"),
    countWithLabel(completion.matches, "match"),
    countWithLabel(semanticMisses, "semantic no-match", "semantic no-matches"),
    `${notApplicable.toLocaleString()} not applicable`,
    countWithLabel(completion.failures, "failure"),
    `scope: ${scope}`,
  ].join("; ");
}

function countWithLabel(
  count: number,
  singular: string,
  plural = `${singular}s`,
): string {
  return `${count.toLocaleString()} ${count === 1 ? singular : plural}`;
}

function galleryCompletionReason(
  completion: BrowserPackageQueryCompletionPayload,
): string {
  if (completion.sourceCandidates === null) {
    throw new TypeError(
      "A Gallery package-query completion contained no source candidate count.");
  }
  const matchLimit = completion.kind === "MatchLimitReached"
    ? `; local match limit ${completion.matchLimit.toLocaleString()} reached`
    : "";
  const estimate = completion.estimatedTotalHits === null
    ? "unavailable"
    : `${completion.estimatedTotalHits.toLocaleString()} (estimate only)`;
  return `one finite Gallery response (capacity ${completion.candidateLimit.toLocaleString()} candidates); acquired ${completion.sourceCandidates.toLocaleString()} candidates${matchLimit}; estimated total hits: ${estimate}`;
}
