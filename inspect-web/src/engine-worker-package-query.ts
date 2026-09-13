import type {
  BrowserPackageAssemblyAssessment,
  BrowserPackageQueryCompletion,
  BrowserPackageQueryEvidence,
  BrowserPackageQueryEvidenceSummary,
  BrowserPackageQueryFailure,
  BrowserPackageQueryProgress,
  BrowserPackageQueryResult,
  BrowserPackageQueryRow,
} from "./facades/inspect-web-package.d.ts";
import type {
  QueryRequest,
} from "./package-query.ts";
import { PACKAGE_QUERY_INITIAL_MATCH_CREDIT } from "./package-query.ts";
import type {
  WorkerRuntimeControlledOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
  ManagedOperationSettlement,
  WorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";
import { isWorkerOperationCancelReason } from "./worker-runtime-protocol.ts";
import type {
  WorkerOperationCatalog,
  WorkerOperationContext,
} from "./worker-runtime-realm.ts";
import {
  engineWorkerBoundaryErrors,
  mapEngineWorkerBoundaryErrors,
} from "./engine-worker-contract.ts";

const engineWorkerPackageQueryKind = "package-query";

const maximumRequestCharacters = 1_048_576;
const maximumEventCharacters = 1_048_576;
const maximumCollectionItems = 4_096;
const maximumDiagnosticCharacters = 64 * 1024;

type PackageQueryFacetTier =
  Extract<BrowserPackageQueryRow["tier"], string>;
type PackageQueryEvidenceScope =
  Extract<BrowserPackageQueryEvidence["scope"], string>;
type PackageQueryFailureKind =
  Extract<BrowserPackageQueryFailure["kind"], string>;
type PackageQueryProgressPhase =
  Extract<BrowserPackageQueryProgress["phase"], string>;
type PackageQueryCompletionKind =
  Extract<BrowserPackageQueryCompletion["kind"], string>;
type PackageQueryAssessmentKind =
  Extract<BrowserPackageAssemblyAssessment["disposition"], string>;

interface EngineWorkerPackageQueryEvidenceSummary
  extends Omit<BrowserPackageQueryEvidenceSummary, "preview"> {
  readonly preview: readonly string[];
}

interface EngineWorkerPackageQueryEvidence
  extends Omit<BrowserPackageQueryEvidence, "scope" | "summary"> {
  readonly scope: PackageQueryEvidenceScope;
  readonly summary: EngineWorkerPackageQueryEvidenceSummary | null;
}

interface EngineWorkerPackageQueryRow
  extends Omit<BrowserPackageQueryRow, "tier" | "evidence"> {
  readonly tier: PackageQueryFacetTier;
  readonly evidence: readonly EngineWorkerPackageQueryEvidence[];
}

interface EngineWorkerPackageQueryFailure
  extends Omit<BrowserPackageQueryFailure, "kind"> {
  readonly kind: PackageQueryFailureKind;
}

interface EngineWorkerPackageQueryProgress
  extends Omit<BrowserPackageQueryProgress, "phase"> {
  readonly phase: PackageQueryProgressPhase;
}

interface EngineWorkerPackageQueryCompletion
  extends Omit<BrowserPackageQueryCompletion, "kind"> {
  readonly kind: PackageQueryCompletionKind;
}

interface EngineWorkerPackageQueryAssessment
  extends Omit<BrowserPackageAssemblyAssessment, "disposition"> {
  readonly disposition: PackageQueryAssessmentKind;
}

interface EngineWorkerPackageQueryEventBase {
  readonly row: EngineWorkerPackageQueryRow | null;
  readonly failure: EngineWorkerPackageQueryFailure | null;
  readonly completion: EngineWorkerPackageQueryCompletion | null;
  readonly progress: EngineWorkerPackageQueryProgress | null;
  readonly assessment: EngineWorkerPackageQueryAssessment | null;
}

export type EngineWorkerPackageQueryDurableEvent =
  | EngineWorkerPackageQueryEventBase & {
      readonly kind: "Progress";
      readonly progress: EngineWorkerPackageQueryProgress;
    }
  | EngineWorkerPackageQueryEventBase & {
      readonly kind: "Match";
      readonly row: EngineWorkerPackageQueryRow;
    }
  | EngineWorkerPackageQueryEventBase & {
      readonly kind: "Failure";
      readonly failure: EngineWorkerPackageQueryFailure;
    }
  | EngineWorkerPackageQueryEventBase & {
      readonly kind: "Assessment";
      readonly assessment: EngineWorkerPackageQueryAssessment;
    };

export type EngineWorkerPackageQueryCompletionEvent =
  EngineWorkerPackageQueryEventBase & {
    readonly kind: "Completed";
    readonly completion: EngineWorkerPackageQueryCompletion;
  };

export type EngineWorkerPackageQueryInput =
  | {
      readonly kind: "query";
      readonly searchText: string;
      readonly facetIds: readonly string[];
      readonly maximumCandidates: number;
      readonly maximumMatches: number;
      readonly includePrerelease: boolean;
      readonly initialMatchCredit: number;
    }
  | {
      readonly kind: "assembly";
      readonly patternId: string;
      readonly operand: string;
      readonly packageCoordinates: readonly string[];
      readonly targetFramework: string;
      readonly initialMatchCredit: number;
    };

export interface EngineWorkerPackageQueryTerminalFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

type PackageQuerySettlement = ManagedOperationSettlement<
  EngineWorkerPackageQueryCompletionEvent,
  EngineWorkerPackageQueryTerminalFailure,
  string
>;

type PackageFacade =
  typeof import("./facades/inspect-web-package.d.ts");

export type EngineWorkerPackageQueryFacade = Pick<
  PackageFacade,
  "cancelPackageQuery"
  | "requestPackageQueryMatches"
  | "runPackageAssemblyQuery"
  | "runPackageQuery"
>;

class PackageQueryPayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

interface PayloadBudget {
  remainingCharacters: number;
  remainingItems: number;
}

function rejected(
  error: unknown,
): BoundedPayloadDecodeResult<never> {
  if (error instanceof PackageQueryPayloadError) {
    return {
      kind: "rejected",
      reason: error.reason,
      message: error.message,
      cause: error,
    };
  }
  return {
    kind: "rejected",
    reason: "invalid",
    message: error instanceof Error
      ? error.message
      : "Package Query payload validation failed.",
    cause: error,
  };
}

function dataRecord(
  value: unknown,
  names: readonly string[],
  description: string,
): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new PackageQueryPayloadError(
      `Expected ${description} data.`);
  }
  const keys = Reflect.ownKeys(value);
  if (keys.length !== names.length
    || !keys.every(key => typeof key === "string" && names.includes(key))) {
    throw new PackageQueryPayloadError(
      `Expected closed ${description} data.`);
  }
  const result: Record<string, unknown> = {};
  for (const name of names) {
    const property = Object.getOwnPropertyDescriptor(value, name);
    if (property === undefined || !("value" in property)) {
      throw new PackageQueryPayloadError(
        `Expected ${description}.${name} data.`);
    }
    result[name] = property.value;
  }
  return result;
}

function arrayItems(
  value: unknown,
  description: string,
  budget: PayloadBudget,
): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new PackageQueryPayloadError(
      `Expected ${description} array.`);
  }
  budget.remainingItems -= value.length;
  if (budget.remainingItems < 0) {
    throw new PackageQueryPayloadError(
      `Package Query payload exceeds ${maximumCollectionItems} collection items.`,
      "oversized",
    );
  }
  const keys = Reflect.ownKeys(value);
  if (keys.length !== value.length + 1
    || !keys.includes("length")) {
    throw new PackageQueryPayloadError(
      `Expected closed ${description} array.`);
  }
  return Array.from({ length: value.length }, (_, index) => {
    const property = Object.getOwnPropertyDescriptor(value, String(index));
    if (property === undefined || !("value" in property)) {
      throw new PackageQueryPayloadError(
        `Expected ${description}[${index}] data.`);
    }
    const propertyValue: unknown = property.value;
    return propertyValue;
  });
}

function text(
  value: unknown,
  description: string,
  budget: PayloadBudget,
): string {
  if (typeof value !== "string") {
    throw new PackageQueryPayloadError(
      `Expected ${description} text.`);
  }
  budget.remainingCharacters -= value.length;
  if (budget.remainingCharacters < 0) {
    throw new PackageQueryPayloadError(
      `Package Query payload exceeds its character budget.`,
      "oversized",
    );
  }
  return value;
}

function nullableText(
  value: unknown,
  description: string,
  budget: PayloadBudget,
): string | null {
  return value === null ? null : text(value, description, budget);
}

function booleanValue(value: unknown, description: string): boolean {
  if (typeof value !== "boolean") {
    throw new PackageQueryPayloadError(
      `Expected ${description} boolean.`);
  }
  return value;
}

function integer(
  value: unknown,
  description: string,
  minimum = 0,
): number {
  if (typeof value !== "number"
    || !Number.isSafeInteger(value)
    || value < minimum) {
    throw new PackageQueryPayloadError(
      `Expected ${description} safe integer of at least ${minimum}.`);
  }
  return value;
}

function nullableInteger(
  value: unknown,
  description: string,
): number | null {
  return value === null ? null : integer(value, description);
}

function nullValue(value: unknown, description: string): null {
  if (value !== null) {
    throw new PackageQueryPayloadError(
      `Expected ${description} to be null.`);
  }
  return null;
}

function literal<const TAllowed extends readonly string[]>(
  value: unknown,
  allowed: TAllowed,
  description: string,
): TAllowed[number] {
  if (typeof value === "string") {
    for (const candidate of allowed) {
      if (candidate === value) return candidate;
    }
  }
  throw new PackageQueryPayloadError(
    `Unsupported ${description} '${String(value)}'.`);
}

function stringArray(
  value: unknown,
  description: string,
  budget: PayloadBudget,
): readonly string[] {
  return arrayItems(value, description, budget).map((item, index) =>
    text(item, `${description}[${index}]`, budget));
}

function decodeInput(value: unknown): EngineWorkerPackageQueryInput {
  const kindProperty = typeof value === "object" && value !== null
    ? Object.getOwnPropertyDescriptor(value, "kind")
    : undefined;
  if (kindProperty === undefined || !("value" in kindProperty)) {
    throw new PackageQueryPayloadError(
      "Expected Package Query input kind.");
  }
  const budget = {
    remainingCharacters: maximumRequestCharacters,
    remainingItems: maximumCollectionItems,
  };
  if (kindProperty.value === "query") {
    const input = dataRecord(value, [
      "kind",
      "searchText",
      "facetIds",
      "maximumCandidates",
      "maximumMatches",
      "includePrerelease",
      "initialMatchCredit",
    ], "Package Query request");
    return {
      kind: "query",
      searchText: text(
        input.searchText,
        "Package Query search",
        budget),
      facetIds: stringArray(
        input.facetIds,
        "Package Query facets",
        budget),
      maximumCandidates: integer(
        input.maximumCandidates,
        "Package Query candidate limit",
        1),
      maximumMatches: integer(
        input.maximumMatches,
        "Package Query match limit",
        1),
      includePrerelease: booleanValue(
        input.includePrerelease,
        "Package Query prerelease selection"),
      initialMatchCredit: integer(
        input.initialMatchCredit,
        "Package Query initial match credit",
        1),
    };
  }
  if (kindProperty.value === "assembly") {
    const input = dataRecord(value, [
      "kind",
      "patternId",
      "operand",
      "packageCoordinates",
      "targetFramework",
      "initialMatchCredit",
    ], "Package Query assembly request");
    const packageCoordinates = stringArray(
      input.packageCoordinates,
      "Package Query package coordinates",
      budget);
    return {
      kind: "assembly",
      patternId: text(
        input.patternId,
        "Package Query pattern ID",
        budget),
      operand: text(
        input.operand,
        "Package Query pattern operand",
        budget),
      packageCoordinates,
      targetFramework: text(
        input.targetFramework,
        "Package Query target framework",
        budget),
      initialMatchCredit: integer(
        input.initialMatchCredit,
        "Package Query initial match credit",
        1),
    };
  }
  throw new PackageQueryPayloadError(
    `Unsupported Package Query input kind '${String(kindProperty.value)}'.`);
}

export const engineWorkerPackageQueryInput:
BoundedPayloadDecoder<EngineWorkerPackageQueryInput> = {
  decode(value) {
    try {
      return { kind: "decoded", value: decodeInput(value) };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

function parseEvidenceSummary(
  value: unknown,
  budget: PayloadBudget,
): EngineWorkerPackageQueryEvidenceSummary | null {
  if (value === null) return null;
  const summary = dataRecord(
    value,
    ["count", "preview"],
    "Package Query evidence summary",
  );
  return {
    count: integer(
      summary.count,
      "Package Query evidence count"),
    preview: stringArray(
      summary.preview,
      "Package Query evidence preview",
      budget),
  };
}

function parseEvidence(
  value: unknown,
  budget: PayloadBudget,
): EngineWorkerPackageQueryEvidence {
  const evidence = dataRecord(
    value,
    ["id", "text", "scope", "summary"],
    "Package Query evidence",
  );
  return {
    id: text(evidence.id, "Package Query evidence ID", budget),
    text: text(evidence.text, "Package Query evidence", budget),
    scope: literal(
      evidence.scope,
      ["Package", "Query"] as const,
      "Package Query evidence scope"),
    summary: parseEvidenceSummary(evidence.summary, budget),
  };
}

function parseRow(
  value: unknown,
  budget: PayloadBudget,
): EngineWorkerPackageQueryRow {
  const row = dataRecord(value, [
    "packageId",
    "version",
    "tier",
    "evidence",
    "totalDownloads",
    "verified",
    "producer",
    "description",
    "rootRequest",
  ], "Package Query row");
  const verified = row.verified === null
    ? null
    : booleanValue(row.verified, "Package Query verified value");
  return {
    packageId: text(row.packageId, "Package Query package ID", budget),
    version: text(row.version, "Package Query version", budget),
    tier: literal(
      row.tier,
      ["Nuspec", "PackageContent", "SearchMetadata", "Assembly"] as const,
      "Package Query facet tier"),
    evidence: arrayItems(
      row.evidence,
      "Package Query evidence",
      budget)
      .map(item => parseEvidence(item, budget)),
    totalDownloads: nullableInteger(
      row.totalDownloads,
      "Package Query total downloads"),
    verified,
    producer: text(row.producer, "Package Query producer", budget),
    description: nullableText(
      row.description,
      "Package Query description",
      budget),
    rootRequest: nullableText(
      row.rootRequest,
      "Package Query Root request",
      budget),
  };
}

function parseFailure(
  value: unknown,
  budget: PayloadBudget,
): EngineWorkerPackageQueryFailure {
  const failure = dataRecord(value, [
    "packageId",
    "version",
    "producer",
    "kind",
    "message",
  ], "Package Query failure");
  return {
    packageId: nullableText(
      failure.packageId,
      "Package Query failure package ID",
      budget),
    version: nullableText(
      failure.version,
      "Package Query failure version",
      budget),
    producer: text(
      failure.producer,
      "Package Query failure producer",
      budget),
    kind: literal(
      failure.kind,
      [
        "Search",
        "SearchContract",
        "ManifestAcquisition",
        "ManifestContract",
        "InvalidManifest",
        "PackageContentAcquisition",
        "PackageContentEvaluation",
        "AssemblyAcquisition",
        "AssemblyEvaluation",
      ] as const,
      "Package Query failure kind"),
    message: text(
      failure.message,
      "Package Query failure message",
      budget),
  };
}

function parseProgress(
  value: unknown,
): EngineWorkerPackageQueryProgress {
  const progress = dataRecord(value, [
    "phase",
    "completed",
    "limit",
  ], "Package Query progress");
  return {
    phase: literal(
      progress.phase,
      ["Search", "Manifest", "PackageContent", "Assembly"] as const,
      "Package Query progress phase"),
    completed: integer(
      progress.completed,
      "Package Query completed progress"),
    limit: integer(
      progress.limit,
      "Package Query progress limit"),
  };
}

function parseAssessment(
  value: unknown,
  budget: PayloadBudget,
): EngineWorkerPackageQueryAssessment {
  const assessment = dataRecord(value, [
    "packageId",
    "version",
    "disposition",
    "message",
    "assetPath",
    "rootRequest",
  ], "Package Query assessment");
  return {
    packageId: text(
      assessment.packageId,
      "Package Query assessment package ID",
      budget),
    version: text(
      assessment.version,
      "Package Query assessment version",
      budget),
    disposition: literal(
      assessment.disposition,
      ["NoMatch", "NotApplicable"] as const,
      "Package Query assessment disposition"),
    message: text(
      assessment.message,
      "Package Query assessment message",
      budget),
    assetPath: nullableText(
      assessment.assetPath,
      "Package Query assessment asset path",
      budget),
    rootRequest: text(
      assessment.rootRequest,
      "Package Query assessment Root request",
      budget),
  };
}

function parseCompletion(
  value: unknown,
  budget: PayloadBudget,
): EngineWorkerPackageQueryCompletion {
  const completion = dataRecord(value, [
    "prefix",
    "producer",
    "candidateLimit",
    "matchLimit",
    "candidates",
    "matches",
    "failures",
    "kind",
    "sourceCandidates",
    "semanticMisses",
    "notApplicable",
    "scope",
  ], "Package Query completion");
  return {
    prefix: text(
      completion.prefix,
      "Package Query completion prefix",
      budget),
    producer: text(
      completion.producer,
      "Package Query completion producer",
      budget),
    candidateLimit: integer(
      completion.candidateLimit,
      "Package Query candidate limit"),
    matchLimit: integer(
      completion.matchLimit,
      "Package Query match limit"),
    candidates: integer(
      completion.candidates,
      "Package Query candidate count"),
    matches: integer(
      completion.matches,
      "Package Query match count"),
    failures: integer(
      completion.failures,
      "Package Query failure count"),
    kind: literal(
      completion.kind,
      [
        "Exhausted",
        "MatchLimitReached",
        "CandidateLimitReached",
        "SourcePageLimitReached",
        "ClientPageLimitReached",
        "Failed",
        "ExactPackageComplete",
        "ExplicitCandidatesComplete",
      ] as const,
      "Package Query completion kind"),
    sourceCandidates: nullableInteger(
      completion.sourceCandidates,
      "Package Query source candidate count"),
    semanticMisses: nullableInteger(
      completion.semanticMisses,
      "Package Query semantic miss count"),
    notApplicable: nullableInteger(
      completion.notApplicable,
      "Package Query not-applicable count"),
    scope: nullableText(
      completion.scope,
      "Package Query completion scope",
      budget),
  };
}

function parseEvent(
  value: unknown,
): EngineWorkerPackageQueryDurableEvent
  | EngineWorkerPackageQueryCompletionEvent {
  const event = dataRecord(value, [
    "kind",
    "row",
    "failure",
    "completion",
    "progress",
    "assessment",
  ], "Package Query event");
  const budget = {
    remainingCharacters: maximumEventCharacters,
    remainingItems: maximumCollectionItems,
  };
  switch (event.kind) {
    case "Progress":
      return {
        kind: "Progress",
        row: nullValue(event.row, "Package Query progress row"),
        failure: nullValue(
          event.failure,
          "Package Query progress failure"),
        completion: nullValue(
          event.completion,
          "Package Query progress completion"),
        progress: parseProgress(event.progress),
        assessment: nullValue(
          event.assessment,
          "Package Query progress assessment"),
      };
    case "Match":
      return {
        kind: "Match",
        row: parseRow(event.row, budget),
        failure: nullValue(
          event.failure,
          "Package Query match failure"),
        completion: nullValue(
          event.completion,
          "Package Query match completion"),
        progress: nullValue(
          event.progress,
          "Package Query match progress"),
        assessment: nullValue(
          event.assessment,
          "Package Query match assessment"),
      };
    case "Failure":
      return {
        kind: "Failure",
        row: nullValue(event.row, "Package Query failure row"),
        failure: parseFailure(event.failure, budget),
        completion: nullValue(
          event.completion,
          "Package Query failure completion"),
        progress: nullValue(
          event.progress,
          "Package Query failure progress"),
        assessment: nullValue(
          event.assessment,
          "Package Query failure assessment"),
      };
    case "Assessment":
      return {
        kind: "Assessment",
        row: nullValue(event.row, "Package Query assessment row"),
        failure: nullValue(
          event.failure,
          "Package Query assessment failure"),
        completion: nullValue(
          event.completion,
          "Package Query assessment completion"),
        progress: nullValue(
          event.progress,
          "Package Query assessment progress"),
        assessment: parseAssessment(event.assessment, budget),
      };
    case "Completed":
      return {
        kind: "Completed",
        row: nullValue(event.row, "Package Query completion row"),
        failure: nullValue(
          event.failure,
          "Package Query completion failure"),
        completion: parseCompletion(event.completion, budget),
        progress: nullValue(
          event.progress,
          "Package Query completion progress"),
        assessment: nullValue(
          event.assessment,
          "Package Query completion assessment"),
      };
    default:
      throw new PackageQueryPayloadError(
        `Unsupported Package Query event kind '${String(event.kind)}'.`);
  }
}

function eventDecoder<TEvent>(
  select: (
    event: EngineWorkerPackageQueryDurableEvent
      | EngineWorkerPackageQueryCompletionEvent,
  ) => TEvent,
): BoundedPayloadDecoder<TEvent> {
  return {
    decode(value) {
      try {
        return { kind: "decoded", value: select(parseEvent(value)) };
      } catch (error: unknown) {
        return rejected(error);
      }
    },
  };
}

export const engineWorkerPackageQueryDurableEvent =
  eventDecoder<EngineWorkerPackageQueryDurableEvent>(event => {
    if (event.kind === "Completed") {
      throw new PackageQueryPayloadError(
        "Package Query completion is terminal, not durable.");
    }
    return event;
  });

export const engineWorkerPackageQueryCompletionEvent =
  eventDecoder<EngineWorkerPackageQueryCompletionEvent>(event => {
    if (event.kind !== "Completed") {
      throw new PackageQueryPayloadError(
        "Package Query settlement did not contain completion.");
    }
    return event;
  });

const packageQueryText: BoundedPayloadDecoder<string> = {
  decode(value) {
    if (typeof value !== "string") {
      return {
        kind: "rejected",
        reason: "invalid",
        message: "Expected Package Query text.",
      };
    }
    if (value.length > maximumDiagnosticCharacters) {
      return {
        kind: "rejected",
        reason: "oversized",
        message:
          `Package Query text exceeds ${maximumDiagnosticCharacters} characters.`,
      };
    }
    return { kind: "decoded", value };
  },
};

const packageQueryNoProgress: BoundedPayloadDecoder<never> = {
  decode() {
    return {
      kind: "rejected",
      reason: "invalid",
      message: "Package Query publishes nonterminal data as durable events.",
    };
  },
};

const packageQueryCredit: BoundedPayloadDecoder<number> = {
  decode(value) {
    try {
      return {
        kind: "decoded",
        value: integer(value, "Package Query match credit", 1),
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

function boundedDiagnostic(value: unknown, description: string): string {
  const decoded = packageQueryText.decode(value);
  if (decoded.kind === "rejected") {
    throw new PackageQueryPayloadError(
      decoded.message.replace("Package Query text", description),
      decoded.reason);
  }
  return decoded.value;
}

const engineWorkerPackageQueryFailure:
BoundedPayloadDecoder<EngineWorkerPackageQueryTerminalFailure> = {
  decode(value) {
    try {
      const failure = dataRecord(value, [
        "failureKind",
        "error",
        "diagnostic",
      ], "Package Query failure");
      return {
        kind: "decoded",
        value: {
          failureKind: literal(
            failure.failureKind,
            ["Expected", "Unexpected"] as const,
            "Package Query failure kind"),
          error: boundedDiagnostic(
            failure.error,
            "Package Query error"),
          diagnostic: boundedDiagnostic(
            failure.diagnostic,
            "Package Query diagnostic"),
        },
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

function packageQueryFailure(
  failureKind: EngineWorkerPackageQueryTerminalFailure["failureKind"],
  error: string,
  diagnostic: string,
): EngineWorkerPackageQueryTerminalFailure {
  return { failureKind, error, diagnostic };
}

const packageQueryBoundaryErrors = mapEngineWorkerBoundaryErrors(message =>
  packageQueryFailure("Unexpected", message, message));

function transportLimitFailure(
  diagnostic: string,
): PackageQuerySettlement {
  const error = "Package Query exceeded Worker transport limits.";
  const bounded = diagnostic.slice(0, maximumDiagnosticCharacters);
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: packageQueryFailure("Unexpected", error, bounded),
    diagnostic: bounded,
  };
}

function invalidResultFailure(error: unknown): PackageQuerySettlement {
  if (error instanceof PackageQueryPayloadError
    && error.reason === "oversized") {
    return transportLimitFailure(error.message);
  }
  const failure = "Package Query returned invalid Worker boundary data.";
  const diagnostic = error instanceof Error
    ? error.message.slice(0, maximumDiagnosticCharacters)
    : "Package Query result validation failed.";
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: packageQueryFailure("Unexpected", failure, diagnostic),
    diagnostic,
  };
}

export function mapEngineWorkerPackageQueryResult(
  value: unknown,
): PackageQuerySettlement {
  try {
    const result = dataRecord(value, [
      "version",
      "kind",
      "value",
      "failureKind",
      "error",
      "diagnostic",
      "reason",
    ], "Package Query result");
    if (result.version !== 1) {
      throw new PackageQueryPayloadError(
        "Expected a version 1 Package Query result.");
    }
    if (result.kind === "Succeeded") {
      nullValue(result.failureKind, "Package Query success failure kind");
      nullValue(result.error, "Package Query success error");
      nullValue(result.diagnostic, "Package Query success diagnostic");
      nullValue(result.reason, "Package Query success reason");
      const decoded =
        engineWorkerPackageQueryCompletionEvent.decode(result.value);
      if (decoded.kind === "rejected")
        throw new PackageQueryPayloadError(
          decoded.message,
          decoded.reason);
      return { kind: "succeeded", value: decoded.value };
    }
    if (result.kind === "Failed") {
      nullValue(result.value, "Package Query failure value");
      nullValue(result.reason, "Package Query failure reason");
      const failureKind = literal(
        result.failureKind,
        ["Expected", "Unexpected"] as const,
        "Package Query failure kind");
      const error = boundedDiagnostic(
        result.error,
        "Package Query error");
      const diagnostic = boundedDiagnostic(
        result.diagnostic,
        "Package Query diagnostic");
      return {
        kind: "failed",
        failureKind: failureKind === "Expected"
          ? "expected"
          : "unexpected",
        error: packageQueryFailure(failureKind, error, diagnostic),
        diagnostic,
      };
    }
    if (result.kind === "Canceled") {
      nullValue(result.value, "Package Query cancellation value");
      nullValue(
        result.failureKind,
        "Package Query cancellation failure kind");
      nullValue(result.error, "Package Query cancellation error");
      nullValue(
        result.diagnostic,
        "Package Query cancellation diagnostic");
      if (typeof result.reason !== "string"
        || !isWorkerOperationCancelReason(result.reason)) {
        throw new PackageQueryPayloadError(
          "Package Query cancellation reason is invalid.");
      }
      return { kind: "canceled", reason: result.reason };
    }
    throw new PackageQueryPayloadError(
      `Unsupported Package Query result kind '${String(result.kind)}'.`);
  } catch (error: unknown) {
    return invalidResultFailure(error);
  }
}

export function engineWorkerPackageQueryCancellationIsRunning(
  value: unknown,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  const result = dataRecord(
    value,
    ["kind", "reason"],
    "Package Query cancellation result",
  );
  if (result.kind === "NotActive") {
    nullValue(result.reason, "inactive Package Query cancellation reason");
    return false;
  }
  if (typeof result.reason !== "string"
    || !isWorkerOperationCancelReason(result.reason)) {
    throw new PackageQueryPayloadError(
      "Package Query cancellation reason is invalid.");
  }
  if (result.kind === "Requested") {
    if (result.reason !== requestedReason) {
      throw new PackageQueryPayloadError(
        "Package Query cancellation changed the requested reason.");
    }
    return true;
  }
  if (result.kind === "AlreadyRequested") return true;
  throw new PackageQueryPayloadError(
    `Unsupported Package Query cancellation kind '${String(result.kind)}'.`);
}

export function mapEngineWorkerPackageQueryCredit(
  value: unknown,
  requestedCredit: number,
): { readonly kind: "acknowledged"; readonly value: number }
  | { readonly kind: "not-active" } {
  const result = dataRecord(
    value,
    ["kind", "additionalMatchCredit"],
    "Package Query match-credit result",
  );
  if (result.kind === "NotActive") {
    nullValue(
      result.additionalMatchCredit,
      "inactive Package Query match credit");
    return { kind: "not-active" };
  }
  if (result.kind !== "Granted") {
    throw new PackageQueryPayloadError(
      `Unsupported Package Query match-credit kind '${String(result.kind)}'.`);
  }
  const granted = integer(
    result.additionalMatchCredit,
    "Package Query granted match credit",
    1);
  if (granted !== requestedCredit) {
    throw new PackageQueryPayloadError(
      "Package Query granted a different match-credit amount.");
  }
  return { kind: "acknowledged", value: granted };
}

function encodeQueryRequest(
  request: QueryRequest,
): BoundedPayloadDecodeResult<unknown> {
  const payload: EngineWorkerPackageQueryInput =
    request.assemblyPattern === undefined
      ? {
          kind: "query",
          searchText: request.scopeQuery,
          facetIds: request.facets.map(facet => facet.key),
          maximumCandidates: request.requestedLimit,
          maximumMatches: request.requestedMatchLimit,
          includePrerelease: request.includePrerelease,
          initialMatchCredit: PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
        }
      : {
          kind: "assembly",
          patternId: request.assemblyPattern.patternId,
          operand: request.assemblyPattern.operand,
          packageCoordinates: [...request.assemblyPattern.packageCoordinates],
          targetFramework: request.assemblyPattern.targetFramework,
          initialMatchCredit: PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
        };
  return engineWorkerPackageQueryInput.decode(payload);
}

function mapControlRequestError(
  error:
    Parameters<
      WorkerRuntimeControlledOperationRegistration<
        QueryRequest,
        EngineWorkerPackageQueryCompletionEvent,
        EngineWorkerPackageQueryTerminalFailure,
        string,
        never,
        WorkerRuntimePreparationError,
        EngineWorkerPackageQueryDurableEvent,
        number,
        number,
        string
      >["control"]["mapRequestError"]
    >[0],
): string {
  switch (error.kind) {
    case "operation-mismatch":
      return "Package Query control targeted another operation.";
    case "control-busy":
      return "Package Query control is already pending.";
    case "control-sequence-exhausted":
      return "Package Query control identity was exhausted.";
    case "payload-rejected":
      return error.message;
    case "operation-closed":
      return "Package Query Worker operation closed.";
  }
  throw new Error("Unsupported Package Query control request error.");
}

export function createEngineWorkerPackageQueryHostRegistration():
WorkerRuntimeControlledOperationRegistration<
  QueryRequest,
  EngineWorkerPackageQueryCompletionEvent,
  EngineWorkerPackageQueryTerminalFailure,
  string,
  never,
  WorkerRuntimePreparationError,
  EngineWorkerPackageQueryDurableEvent,
  number,
  number,
  string
> {
  return {
    kind: engineWorkerPackageQueryKind,
    allowance: { kind: "unbounded" },
    encodeInput: encodeQueryRequest,
    value: engineWorkerPackageQueryCompletionEvent,
    error: engineWorkerPackageQueryFailure,
    diagnostic: packageQueryText,
    progress: packageQueryNoProgress,
    durable: engineWorkerPackageQueryDurableEvent,
    mapPreparationError: error => error,
    boundaryErrors: packageQueryBoundaryErrors,
    control: {
      encodeInput: value => packageQueryCredit.decode(value),
      value: packageQueryCredit,
      mapRequestError: mapControlRequestError,
      boundaryErrors: engineWorkerBoundaryErrors,
    },
  };
}

function parseManagedDurableEvent(
  value: unknown,
): EngineWorkerPackageQueryDurableEvent {
  if (typeof value !== "string") {
    throw new PackageQueryPayloadError(
      "Package Query callback payload was not JSON text.");
  }
  if (value.length > maximumEventCharacters) {
    throw new PackageQueryPayloadError(
      `Package Query callback exceeds ${maximumEventCharacters} characters.`,
      "oversized",
    );
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch (error: unknown) {
    throw new PackageQueryPayloadError(
      error instanceof Error
        ? `Package Query callback JSON was invalid: ${error.message}`
        : "Package Query callback JSON was invalid.");
  }
  const decoded = engineWorkerPackageQueryDurableEvent.decode(parsed);
  if (decoded.kind === "rejected") {
    throw new PackageQueryPayloadError(
      decoded.message,
      decoded.reason);
  }
  return decoded.value;
}

function createManagedEventSink(
  context: WorkerOperationContext,
): Record<string, unknown> {
  const eventSink: Record<string, unknown> = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      const queryEvent = parseManagedDurableEvent(value);
      if (!context.reportEvents([{
        kind: "durable",
        payload: queryEvent,
      }])) {
        throw new Error(
          "Package Query Worker event publication is closed.");
      }
    },
  });
  return eventSink;
}

export function registerEngineWorkerPackageQueryOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerPackageQueryFacade,
): void {
  operations.register({
    kind: engineWorkerPackageQueryKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerPackageQueryInput,
    rejectInvalidPayload: failure => ({
      error: packageQueryFailure(
        "Unexpected",
        failure.message,
        failure.message,
      ),
      diagnostic: failure.message,
    }),
    invoke: async (input, context) => {
      const packageFacade = facade();
      const eventSink = createManagedEventSink(context);
      const result: BrowserPackageQueryResult =
        input.kind === "query"
          ? await packageFacade.runPackageQuery(
              context.operation.operationId,
              input.searchText,
              JSON.stringify(input.facetIds),
              input.maximumCandidates,
              input.maximumMatches,
              input.includePrerelease,
              input.initialMatchCredit,
              eventSink)
          : await packageFacade.runPackageAssemblyQuery(
              context.operation.operationId,
              input.patternId,
              input.operand,
              JSON.stringify(input.packageCoordinates),
              input.targetFramework,
              input.initialMatchCredit,
              eventSink);
      return mapEngineWorkerPackageQueryResult(result);
    },
    cancel: (operation, reason) =>
      engineWorkerPackageQueryCancellationIsRunning(
        facade().cancelPackageQuery(
          operation.operationId,
          reason),
        reason),
    control: {
      input: packageQueryCredit,
      invoke: (operation, additionalMatchCredit) =>
        mapEngineWorkerPackageQueryCredit(
          facade().requestPackageQueryMatches(
            operation.operationId,
            additionalMatchCredit),
          additionalMatchCredit),
    },
  });
}
