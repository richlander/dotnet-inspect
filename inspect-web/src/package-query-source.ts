import type {
  BrowserPackageQueryCatalog,
  BrowserPackageQueryPresetDescriptor,
  BrowserPackageQueryTermDescriptor,
  BrowserPackageAssemblyAssessment,
  BrowserPackageAssemblySemanticCandidateOutcome,
  BrowserPackageAssemblySemanticLibraryAssessment,
  BrowserPackageQueryCompletion as BrowserPackageQueryCompletionPayload,
  BrowserPackageQueryFailure as BrowserPackageQueryFailurePayload,
  BrowserPackageQueryInspection,
  BrowserPackageQueryManifest,
  BrowserPackageQueryProgress as BrowserPackageQueryProgressPayload,
  BrowserPackageQueryRow as BrowserPackageQueryRowPayload,
  BrowserPackageQueryEvent as BrowserPackageQueryEventPayload,
  BrowserPackageQueryMatchCreditResponse,
  BrowserPackageQueryResult,
  BrowserPackageQueryTerm,
} from "./facades/inspect-web-package.d.ts";
import type {
  PackageQueryDataSource,
  QueryAssemblyAssessment,
  QueryPreset,
  QueryProgress,
  QueryResultRow,
  QueryTermDescriptor,
  TerminalQueryCompletion,
} from "./package-query.ts";
import { isLibraryLiteralQuery, PACKAGE_QUERY_INITIAL_MATCH_CREDIT }
  from "./package-query.ts";

export type { BrowserPackageQueryInspection } from "./facades/inspect-web-package.d.ts";

type PackageQueryManifestFailureReason = Extract<
  BrowserPackageQueryFailurePayload["manifestFailureReason"],
  string
>;
type PackageQueryManifestIdentityProvenance = Extract<
  BrowserPackageQueryManifest["identityProvenance"],
  string
>;

export interface BrowserPackageQueryEngine {
  cancel(
    operationId: string,
    reason: string,
  ): void;
  requestMatches(
    operationId: string,
    additionalMatchCredit: number,
  ): BrowserPackageQueryMatchCreditResponse
    | Promise<BrowserPackageQueryMatchCreditResponse>;
  run(
    operationId: string,
    searchText: string,
    terms: ReadonlyArray<BrowserPackageQueryTerm>,
    targetFramework: string | null,
    maximumCandidates: number,
    maximumMatches: number,
    includePrerelease: boolean,
    initialMatchCredit: number,
    eventSink: unknown,
  ): Promise<BrowserPackageQueryResult>;
}

export interface BrowserPackageQueryDataSourceOptions {
  createOperationId?: () => string;
  onInspection?: (
    inspection: BrowserPackageQueryInspection | null,
  ) => void;
  reportUnexpectedFailure?: (
    operationId: string,
    error: Error,
    diagnostic: string | null,
  ) => void;
}

export function packageQueryCatalog(
  catalog: BrowserPackageQueryCatalog,
): {
  readonly presets: QueryPreset[];
  readonly terms: QueryTermDescriptor[];
} {
  return {
    presets: catalog.presets.map(toQueryPreset),
    terms: catalog.terms.map(toQueryTermDescriptor),
  };
}

function toQueryPreset(
  descriptor: BrowserPackageQueryPresetDescriptor,
): QueryPreset {
  return {
    id: `${descriptor.key}:${descriptor.operator}:${descriptor.value}`,
    key: descriptor.key,
    operator: descriptor.operator,
    value: descriptor.value,
    label: descriptor.label,
    summary: descriptor.summary,
    weight: descriptor.weight,
    tier: toInspectionTier(descriptor.tier),
    executionClass: toExecutionClass(descriptor.executionClass),
    selectionGroupId: descriptor.selectionGroupId,
    combinesWithinSelectionGroup: descriptor.combinesWithinSelectionGroup,
    replacementGroupId: descriptor.replacementGroupId,
    displayGroupId: descriptor.displayGroupId,
    displayGroupLabel: descriptor.displayGroupLabel,
  };
}

function toQueryTermDescriptor(
  descriptor: BrowserPackageQueryTermDescriptor,
): QueryTermDescriptor {
  return {
    key: descriptor.key,
    label: descriptor.label,
    summary: descriptor.summary,
    weight: descriptor.weight,
    tier: toInspectionTier(descriptor.tier),
    executionClass: toExecutionClass(descriptor.executionClass),
    operators: [...descriptor.operators],
    valueKind: descriptor.valueKind,
    example: descriptor.example,
    multiline: descriptor.multiline,
  };
}

export function createBrowserPackageQueryDataSource(
  engine: BrowserPackageQueryEngine,
  options: BrowserPackageQueryDataSourceOptions = {},
): PackageQueryDataSource {
  let activeOperationId: string | null = null;
  const createOperationId =
    options.createOperationId ?? (() => globalThis.crypto.randomUUID());
  const reportUnexpectedFailure =
    options.reportUnexpectedFailure
    ?? ((operationId, error, diagnostic) => {
      console.error(
        `Package Query managed operation '${operationId}' failed unexpectedly.`,
        diagnostic ?? error);
    });
  const onInspection = options.onInspection ?? (() => {});
  return {
    initialMatchCredit: PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
    requestMore: async additionalMatchCredit => {
      const operationId = activeOperationId;
      if (operationId === null) return false;
      const result =
        await engine.requestMatches(operationId, additionalMatchCredit);
      return result.kind === "Granted"
        && result.additionalMatchCredit === additionalMatchCredit;
    },
    async run(
      request,
      onPage,
      onFailure,
      onProgress,
      abortSignal,
      onAssessment,
    ) {
      if (abortSignal.aborted) return { kind: "cancelled" };
      const operationId = createOperationId();
      if (!operationId) {
        throw new Error(
          "The Browser package-query operation ID allocator returned no ID.");
      }
      activeOperationId = operationId;
      onInspection(null);

      let completion: TerminalQueryCompletion | null = null;
      const streamedAssessmentKeys = new Set<string>();
      const streamedFailureCounts = new Map<string, number>();
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
            if (queryEvent.kind === "Assessment"
                && queryEvent.assessment !== null) {
              streamedAssessmentKeys.add(packageCoordinateKey(
                queryEvent.assessment.packageId,
                queryEvent.assessment.version));
            } else if (
              queryEvent.kind === "Failure"
              && queryEvent.failure !== null
            ) {
              const key = failureKey(queryEvent.failure);
              streamedFailureCounts.set(
                key,
                (streamedFailureCounts.get(key) ?? 0) + 1);
            }
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
          engine.cancel(operationId, "feature-observer-failed");
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

      const cancel = () =>
        engine.cancel(operationId, cancellationReason(abortSignal.reason));
      abortSignal.addEventListener("abort", cancel, { once: true });
      try {
        const result = await engine.run(
              operationId,
              request.scopeQuery,
              [
                ...request.presets.map(preset => ({
                  key: preset.key,
                  operator: preset.operator,
                  value: preset.value,
                })),
                ...request.terms.map(term => ({
                  key: term.descriptor.key,
                  operator: term.operator,
                  value: term.value,
                })),
              ],
              isLibraryLiteralQuery(request)
                ? request.targetFramework
                : null,
              request.requestedLimit,
              request.requestedMatchLimit,
              request.includePrerelease,
              PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
              eventSink);
        flushEvents();
        let unexpectedFailure: Error | null = null;
        if (result.version === 3
            && result.kind === "Failed"
            && result.failureKind === "Unexpected") {
          unexpectedFailure = new Error(
            result.error ?? "The Browser package query failed without an error.");
          try {
            reportUnexpectedFailure(
              operationId,
              unexpectedFailure,
              result.diagnostic);
          } catch (reportingError: unknown) {
            throw new AggregateError(
              [unexpectedFailure, reportingError],
              "The Browser package query and its diagnostic observer failed.",
              { cause: reportingError });
          }
        }
        if (flushState.failed) throw flushState.error;
        if (result.version !== 3) {
          throw new Error(
            "The Browser package-query result version is unsupported.");
        }
        if (result.kind === "Canceled") return { kind: "cancelled" };
        if (result.kind === "Failed") {
          if (unexpectedFailure) throw unexpectedFailure;
          const reason =
            result.error ?? "The Browser package query failed without an error.";
          return { kind: "failed", reason };
        }
        if (result.kind !== "Succeeded") {
          throw new TypeError(
            "The Browser package-query result was not a supported terminal result.");
        }
        if (abortSignal.aborted) return { kind: "cancelled" };
        if (result.value !== null || result.inspection === null) {
          throw new TypeError(
            "The Browser package-query result did not contain its inspection envelope.");
        }
        for (
          const assessment
          of result.inspection.content.libraryLiteralAssessments
        ) {
          const projected = toQueryAssessmentFromOutcome(assessment);
          if (!streamedAssessmentKeys.has(packageCoordinateKey(
            projected.packageId,
            projected.version))) {
            onAssessment?.(projected);
          }
        }
        const unmatchedStreamedFailures = new Map(streamedFailureCounts);
        for (const failure of result.inspection.content.failures) {
          const key = failureKey(failure);
          const remaining = unmatchedStreamedFailures.get(key) ?? 0;
          if (remaining > 0) {
            unmatchedStreamedFailures.set(key, remaining - 1);
          } else {
            onFailure(formatFailure(failure));
          }
        }
        const finalEvent: BrowserPackageQueryEventPayload = {
          kind: "Completed",
          row: null,
          failure: null,
          completion: result.inspection.content.completion,
          progress: null,
          assessment: null,
        };
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
        if (completion === null) {
          return {
            kind: "failed",
            reason:
              "The Browser package-query stream ended without a completion event.",
          };
        }
        onInspection(result.inspection);
        return completion;
      } catch (error) {
        flushEvents();
        if (flushState.failed) throw flushState.error;
        if (abortSignal.aborted) return { kind: "cancelled" };
        throw error;
      } finally {
        abortSignal.removeEventListener("abort", cancel);
        if (activeOperationId === operationId)
          activeOperationId = null;
      }
    },
  };
}

function cancellationReason(reason: unknown): string {
  switch (reason) {
    case "user":
    case "superseded":
    case "disposed":
    case "feature-observer-failed":
    case "timeout":
    case "worker-restarted":
      return reason;
    default:
      return "user";
  }
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

function countValue(value: unknown, description: string): number {
  const count = numberValue(value, description);
  if (!Number.isInteger(count) || count < 0) {
    throw new TypeError(
      `The Browser ${description} was not a non-negative integer.`);
  }
  return count;
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
  if (!Array.isArray(row.answers)) {
    throw new TypeError(
      "The Browser package-query row answers were not an array.");
  }
  if (!Array.isArray(row.evidence)) {
    throw new TypeError(
      "The Browser package-query row evidence was not an array.");
  }
  if (!Array.isArray(row.owners)) {
    throw new TypeError(
      "The Browser package-query row owners were not an array.");
  }
  return {
    packageId: stringValue(row.packageId, "package-query package ID"),
    version: stringValue(row.version, "package-query version"),
    description: nullableStringValue(
      row.description,
      "package-query description"),
    tier: rowTierValue(row.tier),
    answers: row.answers.map(item => {
      const answer = objectValue(item, "package-query answer");
      return {
        id: stringValue(answer.id, "package-query answer ID"),
        value: stringValue(answer.value, "package-query answer value"),
        term: parseEvidenceTerm(answer.term),
      };
    }),
    evidence: row.evidence.map(item => {
      const evidence = objectValue(item, "package-query evidence");
      const scope = evidenceScopeValue(evidence.scope);
      if (!Array.isArray(evidence.properties)) {
        throw new TypeError(
          "The Browser package-query evidence properties were not an array.");
      }
      return {
        id: stringValue(evidence.id, "package-query evidence ID"),
        scope,
        summary: parseEvidenceSummary(evidence.summary),
        properties: evidence.properties.map(propertyItem => {
          const property = objectValue(
            propertyItem,
            "package-query evidence property");
          return {
            name: stringValue(
              property.name,
              "package-query evidence property name"),
            value: stringValue(
              property.value,
              "package-query evidence property value"),
          };
        }),
        number: nullableNumberValue(
          evidence.number,
          "package-query evidence number"),
        term: parseEvidenceTerm(evidence.term),
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
    owners: row.owners.map(item =>
      stringValue(item, "package-query owner")),
    manifest: parseManifest(row.manifest),
  };
}

function parseManifest(
  value: unknown,
): BrowserPackageQueryManifest | null {
  if (value === null) return null;
  const manifest = objectValue(value, "package-query manifest");
  if (!Array.isArray(manifest.packageTypes)) {
    throw new TypeError(
      "The Browser package-query manifest package types were not an array.");
  }
  if (!Array.isArray(manifest.dependencyGroups)) {
    throw new TypeError(
      "The Browser package-query manifest dependency groups were not an array.");
  }
  return {
    packageId: stringValue(
      manifest.packageId,
      "package-query manifest package ID"),
    version: stringValue(
      manifest.version,
      "package-query manifest version"),
    manifestVersion: stringValue(
      manifest.manifestVersion,
      "package-query manifest schema version"),
    description: nullableStringValue(
      manifest.description,
      "package-query manifest description"),
    authors: nullableStringValue(
      manifest.authors,
      "package-query manifest authors"),
    repository: nullableStringValue(
      manifest.repository,
      "package-query manifest repository"),
    repositoryType: nullableStringValue(
      manifest.repositoryType,
      "package-query manifest repository type"),
    repositoryCommit: nullableStringValue(
      manifest.repositoryCommit,
      "package-query manifest repository commit"),
    license: nullableStringValue(
      manifest.license,
      "package-query manifest license"),
    licenseUrl: nullableStringValue(
      manifest.licenseUrl,
      "package-query manifest license URL"),
    packageTypes: manifest.packageTypes.map(item =>
      stringValue(item, "package-query manifest package type")),
    isToolPackage: booleanValue(
      manifest.isToolPackage,
      "package-query tool package flag"),
    readmeFile: nullableStringValue(
      manifest.readmeFile,
      "package-query manifest README file"),
    dependencyGroups: manifest.dependencyGroups.map(groupValue => {
      const group = objectValue(
        groupValue,
        "package-query manifest dependency group");
      if (!Array.isArray(group.dependencies)) {
        throw new TypeError(
          "The Browser package-query manifest dependencies were not an array.");
      }
      return {
        targetFramework: stringValue(
          group.targetFramework,
          "package-query dependency target framework"),
        dependencies: group.dependencies.map(dependencyValue => {
          const dependency = objectValue(
            dependencyValue,
            "package-query manifest dependency");
          return {
            id: stringValue(
              dependency.id,
              "package-query dependency ID"),
            versionRange: stringValue(
              dependency.versionRange,
              "package-query dependency version range"),
          };
        }),
        isImplicitManifestGroup: booleanValue(
          group.isImplicitManifestGroup,
          "package-query implicit manifest group"),
      };
    }),
    iconFile: nullableStringValue(
      manifest.iconFile,
      "package-query manifest icon file"),
    iconUrl: nullableStringValue(
      manifest.iconUrl,
      "package-query manifest icon URL"),
    identityProvenance: manifestIdentityProvenanceValue(
      manifest.identityProvenance),
  };
}

function parseAssessment(value: unknown): BrowserPackageAssemblyAssessment {
  const assessment = objectValue(value, "package-query assessment");
  if (!Array.isArray(assessment.libraries)) {
    throw new TypeError(
      "The Browser package-query assessment Libraries were not an array.");
  }
  return {
    packageId: stringValue(
      assessment.packageId,
      "package-query assessment package ID"),
    version: stringValue(
      assessment.version,
      "package-query assessment version"),
    disposition: browserAssessmentDispositionValue(assessment.disposition),
    message: stringValue(
      assessment.message,
      "package-query assessment message"),
    assetPath: nullableStringValue(
      assessment.assetPath,
      "package-query assessment asset path"),
    rootRequest: nullableStringValue(
      assessment.rootRequest,
      "package-query assessment Root request"),
    libraries: assessment.libraries.map(libraryValue => {
      const library = objectValue(
        libraryValue,
        "package-query Library assessment");
      const selectedAsset = objectValue(
        library.selectedAsset,
        "package-query Library selected asset");
      return {
        selectedAsset: {
          path: stringValue(
            selectedAsset.path,
            "package-query Library path"),
          assemblyName: stringValue(
            selectedAsset.assemblyName,
            "package-query Library assembly name"),
          targetFramework: stringValue(
            selectedAsset.targetFramework,
            "package-query Library target framework"),
          sequence: stringValue(
            selectedAsset.sequence,
            "package-query Library sequence"),
          ordinal: countValue(
            selectedAsset.ordinal,
            "package-query Library ordinal"),
          unevaluatedSiblings: countValue(
            selectedAsset.unevaluatedSiblings,
            "package-query Library unevaluated sibling count"),
          rootRequest: nonBlankStringValue(
            selectedAsset.rootRequest,
            "package-query Library Root request"),
        },
        kind: libraryAssessmentDispositionValue(library.kind),
        occurrences: countValue(
          library.occurrences,
          "package-query Library occurrence count"),
        failureStage: nullableStringValue(
          library.failureStage,
          "package-query Library failure stage"),
        message: nullableStringValue(
          library.message,
          "package-query Library assessment message"),
      };
    }),
  };
}

function evidenceScopeValue(
  value: unknown,
): BrowserPackageQueryRowPayload["evidence"][number]["scope"] {
  switch (value) {
    case "Package":
    case "Query":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query evidence scope '${String(value)}'.`);
  }
}

function parseEvidenceSummary(
  value: unknown,
): BrowserPackageQueryRowPayload["evidence"][number]["summary"] {
  if (value === null) return null;
  const summary = objectValue(value, "package-query evidence summary");
  if (!Array.isArray(summary.preview)) {
    throw new TypeError(
      "The Browser package-query evidence preview was not an array.");
  }
  return {
    count: countValue(summary.count, "package-query evidence count"),
    preview: summary.preview.map(item =>
      stringValue(item, "package-query evidence preview")),
  };
}

function parseEvidenceTerm(
  value: unknown,
): BrowserPackageQueryRowPayload["evidence"][number]["term"] {
  if (value === null) return null;
  const term = objectValue(value, "package-query evidence term");
  return {
    key: stringValue(term.key, "package-query evidence term key"),
    operator: stringValue(
      term.operator,
      "package-query evidence term operator"),
    value: stringValue(term.value, "package-query evidence term value"),
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
    manifestFailureReason: manifestFailureReasonValue(
      failure.manifestFailureReason),
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
    semanticMisses: optionalNullableNumberValue(
      completion.semanticMisses,
      "package-query semantic miss count"),
    notApplicable: optionalNullableNumberValue(
      completion.notApplicable,
      "package-query not-applicable count"),
    scope: optionalNullableStringValue(
      completion.scope,
      "package-query completion scope"),
    occurrences: optionalNullableNumberValue(
      completion.occurrences,
      "package-query occurrence count"),
    notEvaluated: optionalNullableNumberValue(
      completion.notEvaluated,
      "package-query not-evaluated count"),
    evaluatedCandidates: optionalNullableNumberValue(
      completion.evaluatedCandidates,
      "package-query evaluated candidate count"),
    semanticMatches: optionalNullableNumberValue(
      completion.semanticMatches,
      "package-query semantic match count"),
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
    case "DependencyTraversal":
    case "AssemblyAcquisition":
    case "AssemblyEvaluation":
    case "AssemblyNotEvaluated":
      return value;
    default:
      throw new TypeError(
        `Unknown package-query failure kind '${String(value)}'.`);
  }
}

function manifestFailureReasonValue(
  value: unknown,
): PackageQueryManifestFailureReason | null {
  if (value === null) return null;
  switch (value) {
    case "MalformedXml":
      return "MalformedXml";
    case "UnsupportedDocumentShape":
      return "UnsupportedDocumentShape";
    case "IdentityMismatch":
      return "IdentityMismatch";
    case "InvalidDependencyContract":
      return "InvalidDependencyContract";
    case "ConfiguredLimitExceeded":
      return "ConfiguredLimitExceeded";
    case "InvalidIdentityContract":
      return "InvalidIdentityContract";
    default:
      throw new TypeError(
        "Unknown package-query manifest failure reason "
          + `'${typeof value === "string" ? value : typeof value}'.`);
  }
}

function manifestIdentityProvenanceValue(
  value: unknown,
): PackageQueryManifestIdentityProvenance {
  switch (value) {
    case "ExpectedCoordinate":
      return "ExpectedCoordinate";
    case "SelfAttested":
      return "SelfAttested";
    default:
      throw new TypeError(
        "Unknown package-query manifest identity provenance "
          + `'${typeof value === "string" ? value : typeof value}'.`);
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
    case "ExactPackageComplete":
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
    case "DependencyTraversal":
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
    case "DependencyTraversal":
      phase = "dependency-traversal";
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
  const evidence = row.evidence.map(item => ({
    id: item.id,
    scope: item.scope === "Package" ? "package" as const : "query" as const,
    summary: item.summary === null
      ? null
      : {
          count: item.summary.count,
          preview: [...item.summary.preview],
        },
    properties: item.properties.map(property => ({ ...property })),
    number: item.number,
    ...(item.term === null
      ? {}
      : { term: {
          key: item.term.key,
          operator: item.term.operator,
          value: item.term.value,
        } }),
  }));
  const answers = row.answers.map(answer => ({
    id: answer.id,
    value: answer.value,
    ...(answer.term === null
        ? {}
        : { term: {
            key: answer.term.key,
            operator: answer.term.operator,
            value: answer.term.value,
          } }),
  }));
  const rootRequest = row.rootRequest;
  const result: QueryResultRow = {
    packageId: row.packageId,
    version: row.version,
    tier: toQueryTier(row.tier),
    answers,
    evidence,
    totalDownloads: row.totalDownloads,
    description: row.description,
    producer: row.producer,
  };
  if (rootRequest !== null && rootRequest !== undefined)
    result.rootRequest = rootRequest;
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
  tier: BrowserPackageQueryPresetDescriptor["tier"],
): QueryPreset["tier"] {
  switch (tier) {
    case "SearchMetadata":
      return "search-metadata";
    case "Nuspec":
      return "nuspec";
    case "PackageContent":
      return "package-content";
    default:
      throw new TypeError(
        `Unsupported package-query tier '${String(tier)}'.`);
  }
}

function toExecutionClass(
  executionClass: BrowserPackageQueryPresetDescriptor["executionClass"],
): QueryPreset["executionClass"] {
  switch (executionClass) {
    case "SearchMetadata":
      return "search-metadata";
    case "Nuspec":
      return "nuspec";
    case "NuspecExpensive":
      return "nuspec-expensive";
    case "PackageContent":
      return "package-content";
    case "Metadata":
      return "metadata";
    case "MetadataExpensive":
      return "metadata-expensive";
    default:
      throw new TypeError(
        `Unsupported package-query execution class '${String(executionClass)}'.`);
  }
}

function assessmentDispositionValue(
  value: unknown,
): QueryAssemblyAssessment["disposition"] {
  if (value === "Matched"
    || value === "NoMatch"
    || value === "NotApplicable"
    || value === "Failure"
    || value === "NotEvaluated") {
    return value;
  }
  throw new TypeError(
    `Unknown package-query assessment disposition '${String(value)}'.`);
}

function browserAssessmentDispositionValue(
  value: unknown,
): Extract<BrowserPackageAssemblyAssessment["disposition"], string> {
  if (value === "Matched"
    || value === "NoMatch"
    || value === "NotApplicable"
    || value === "Failure"
    || value === "NotEvaluated") {
    return value;
  }
  throw new TypeError(
    `Unknown Browser package-query assessment disposition '${String(value)}'.`);
}

function libraryAssessmentDispositionValue(
  value: unknown,
): "Matched" | "NoMatch" | "Failure" {
  if (value === "Matched" || value === "NoMatch" || value === "Failure") {
    return value;
  }
  throw new TypeError(
    `Unknown Browser package-query Library assessment disposition '${String(value)}'.`);
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
    libraries: toQueryLibraryAssessments(assessment.libraries),
  };
}

function toQueryAssessmentFromOutcome(
  assessment: BrowserPackageAssemblySemanticCandidateOutcome,
): QueryAssemblyAssessment {
  return {
    packageId: assessment.packageId,
    version: assessment.version,
    disposition: assessmentDispositionValue(assessment.kind),
    message: assessment.message
      ?? "The selected implementation libraries contain matching decoded ldstr uses.",
    assetPath: assessment.selectedAsset?.path ?? null,
    rootRequest: assessment.rootRequest,
    libraries: toQueryLibraryAssessments(assessment.libraries),
  };
}

function toQueryLibraryAssessments(
  libraries: readonly BrowserPackageAssemblySemanticLibraryAssessment[],
): QueryAssemblyAssessment["libraries"] {
  return libraries.map(library => ({
    path: library.selectedAsset.path,
    assemblyName: library.selectedAsset.assemblyName,
    targetFramework: library.selectedAsset.targetFramework,
    ordinal: library.selectedAsset.ordinal,
    disposition: libraryAssessmentDispositionValue(library.kind),
    occurrenceCount: library.occurrences,
    failureStage: library.failureStage,
    message: library.message,
  }));
}

function packageCoordinateKey(packageId: string, version: string): string {
  return `${packageId.toUpperCase()}\n${version.toUpperCase()}`;
}

function failureKey(failure: BrowserPackageQueryFailurePayload): string {
  return JSON.stringify([
    failure.packageId?.toUpperCase() ?? null,
    failure.version?.toUpperCase() ?? null,
    failure.producer,
    failure.kind,
    failure.message,
    failure.manifestFailureReason,
  ]);
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
  if (completion.semanticMatches !== null) {
    if (completion.evaluatedCandidates === null
      || completion.notEvaluated === null
      || completion.occurrences === null
      || completion.semanticMisses === null
      || completion.notApplicable === null) {
      throw new TypeError(
        "A library-literal completion omitted semantic accounting.");
    }
    return {
      kind: "library-literal",
      population: semanticPopulationCompletion(completion.kind),
      candidateCount: completion.candidates,
      evaluatedCandidateCount: completion.evaluatedCandidates,
      notEvaluatedCount: completion.notEvaluated,
      matchedPackageCount: completion.semanticMatches,
      occurrenceCount: completion.occurrences,
      semanticMissCount: completion.semanticMisses,
      notApplicableCount: completion.notApplicable,
      failureCount: completion.failures,
      complete: (completion.kind === "ExactPackageComplete"
          || completion.kind === "Exhausted"
          || completion.kind === "MatchLimitReached")
        && completion.failures === 0
        && completion.notEvaluated === 0,
    };
  }
  switch (completion.kind) {
    case "Exhausted":
      return { kind: "exhausted" };
    case "MatchLimitReached":
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
    case "ExactPackageComplete":
      return { kind: "exact" };
    case "ExplicitCandidatesComplete":
      return {
        kind: "bounded",
        reason: explicitCandidatesCompletionReason(completion),
      };
    case "Failed":
      return {
        kind: "failed",
        // An assembly-query failure carries its product-issued scope; the
        // shared source profile keeps its own generic completion message.
        reason: completion.scope?.trim()
          ? completion.scope
          : "Package source work failed before the query completed.",
      };
    default:
      throw new TypeError(
        `Unknown package-query completion '${String(completion.kind)}'.`);
  }
}

function semanticPopulationCompletion(
  value: BrowserPackageQueryCompletionPayload["kind"],
): Extract<TerminalQueryCompletion, { kind: "library-literal" }>["population"] {
  switch (value) {
    case "ExactPackageComplete":
      return "ExactPackageComplete";
    case "Exhausted":
      return "PrefixExhausted";
    case "MatchLimitReached":
      return "MatchLimitReached";
    case "CandidateLimitReached":
      return "CandidateLimitReached";
    case "SourcePageLimitReached":
      return "SourcePageLimitReached";
    case "ClientPageLimitReached":
      return "ClientPageLimitReached";
    case "Failed":
      return "SourceFailed";
    default:
      throw new TypeError(
        `Unknown library-literal population completion '${String(value)}'.`);
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
