import type {
  BrowserPackageQueryEvent,
  BrowserPackageQueryFacetCatalog,
  BrowserPackageQueryFacetDescriptor,
  BrowserPackageQueryCompletion,
  BrowserPackageQueryFailure,
  BrowserPackageQueryProgress,
  BrowserPackageQueryRow,
} from "./facades/inspect-web-package.d.ts";
import type {
  PackageQueryDataSource,
  QueryFacetTerm,
  QueryProgress,
  QueryResultRow,
  TerminalQueryCompletion,
} from "./package-query.ts";
import { PACKAGE_QUERY_INITIAL_MATCH_CREDIT } from "./package-query.ts";

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
  ): Promise<BrowserPackageQueryEvent>;
}

export function packageQueryFacets(
  catalog: BrowserPackageQueryFacetCatalog,
): QueryFacetTerm[] {
  return catalog.facets.map(toQueryFacet);
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
    async run(request, onPage, onFailure, onProgress, abortSignal) {
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
      const pendingEvents: BrowserPackageQueryEvent[] = [];
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
        const finalEvent = await engine.run(
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

function parseBrowserEvent(json: string): BrowserPackageQueryEvent {
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
      };
    case "Match":
      return {
        kind: "Match",
        row: parseRow(event.row),
        failure: null,
        completion: null,
        progress: null,
      };
    case "Failure":
      return {
        kind: "Failure",
        row: null,
        failure: parseFailure(event.failure),
        completion: null,
        progress: null,
      };
    case "Completed":
      return {
        kind: "Completed",
        row: null,
        failure: null,
        completion: parseCompletion(event.completion),
        progress: null,
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

function nullableStringValue(
  value: unknown,
  description: string,
): string | null {
  return value === null ? null : stringValue(value, description);
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

function booleanValue(value: unknown, description: string): boolean {
  if (typeof value !== "boolean") {
    throw new TypeError(`The Browser ${description} was not a boolean.`);
  }
  return value;
}

function parseRow(value: unknown): BrowserPackageQueryRow {
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
  };
}

function parseFailure(value: unknown): BrowserPackageQueryFailure {
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

function parseProgress(value: unknown): BrowserPackageQueryProgress {
  const progress = objectValue(value, "package-query progress");
  return {
    phase: progressPhaseValue(progress.phase),
    completed: numberValue(
      progress.completed,
      "package-query completed progress"),
    limit: numberValue(progress.limit, "package-query progress limit"),
  };
}

function parseCompletion(value: unknown): BrowserPackageQueryCompletion {
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
    kind: completionKindValue(completion.kind),
  };
}

function rowTierValue(
  value: unknown,
): BrowserPackageQueryRow["tier"] {
  if (value === "SearchMetadata"
    || value === "Nuspec"
    || value === "PackageContent") return value;
  throw new TypeError(
    `Unsupported package-query row tier '${String(value)}'.`);
}

function failureKindValue(
  value: unknown,
): BrowserPackageQueryFailure["kind"] {
  switch (value) {
    case "Search":
    case "SearchContract":
    case "ManifestAcquisition":
    case "ManifestContract":
    case "InvalidManifest":
    case "PackageContentAcquisition":
    case "PackageContentEvaluation":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query failure kind '${String(value)}'.`);
  }
}

function completionKindValue(
  value: unknown,
): BrowserPackageQueryCompletion["kind"] {
  switch (value) {
    case "Exhausted":
    case "MatchLimitReached":
    case "CandidateLimitReached":
    case "SourcePageLimitReached":
    case "ClientPageLimitReached":
    case "GalleryResponseComplete":
    case "Failed":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query completion '${String(value)}'.`);
  }
}

function progressPhaseValue(
  value: unknown,
): BrowserPackageQueryProgress["phase"] {
  switch (value) {
    case "Search":
    case "Manifest":
    case "PackageContent":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query progress phase '${String(value)}'.`);
  }
}

function dispatchEvent(
  queryEvent: BrowserPackageQueryEvent,
  onPage: (rows: readonly QueryResultRow[]) => void,
  onFailure: (failure: string) => void,
  onProgress: (progress: QueryProgress) => void,
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
  progress: BrowserPackageQueryProgress,
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
  row: NonNullable<BrowserPackageQueryEvent["row"]>,
): QueryResultRow {
  const evidence = row.evidence.map(item => item.text);
  if (!evidence.length || evidence.some(item => item.trim().length === 0)) {
    throw new TypeError("A package-query row contained no evidence.");
  }
  return {
    packageId: row.packageId,
    version: row.version,
    tier: toQueryTier(row.tier),
    evidence: [evidence[0]!, ...evidence.slice(1)],
    totalDownloads: row.totalDownloads,
    description: row.description,
    producer: row.producer,
  };
}

function toQueryTier(
  tier: BrowserPackageQueryRow["tier"],
): QueryResultRow["tier"] {
  return tier === "SearchMetadata" ? "search-metadata" : toInspectionTier(tier);
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

function formatFailure(failure: BrowserPackageQueryFailure): string {
  const coordinate = failure.packageId
    ? `${failure.packageId}${failure.version ? `@${failure.version}` : ""}`
    : failure.producer;
  return `${coordinate}: ${failure.message}`;
}

function toTerminalCompletion(
  completion: NonNullable<BrowserPackageQueryEvent["completion"]>,
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
    case "Failed":
      return {
        kind: "failed",
        reason: "The package source failed before returning usable package input.",
      };
    default:
      throw new TypeError(
        `Unknown package-query completion '${String(completion.kind)}'.`);
  }
}

function galleryCompletionReason(
  completion: BrowserPackageQueryCompletion,
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
