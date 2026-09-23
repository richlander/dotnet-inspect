import type {
  BrowserInspectionDiagnostic,
  BrowserInspectionShare,
  BrowserPackageAssemblySemanticCandidateOutcome,
  BrowserPackageAssemblySemanticLibraryAssessment,
  BrowserPackageAssemblySemanticOccurrence,
  BrowserPackageAssemblySemanticResult,
  BrowserPackageAssemblySemanticSelectedAsset,
  BrowserPackageAssemblyAssessment,
  BrowserPackageQueryCompletion,
  BrowserPackageQueryEvidence,
  BrowserPackageQueryEvidenceSummary,
  BrowserPackageQueryFailure,
  BrowserPackageQueryManifest,
  BrowserPackageQueryProgress,
  BrowserPackageQueryDocument,
  BrowserPackageQueryRow,
} from "./facades/inspect-web-package.d.ts";
import type {
  QueryRequest,
} from "./package-query.ts";
import {
  isLibraryLiteralQuery,
  PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
} from "./package-query.ts";
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
// Outer wire-shape ceiling. The shared managed planner reserves its structural
// terms and owns the lower product limit for authored inspection terms.
const maximumQueryTerms = 24;
const maximumOwnerItems = 4_096;
// Match the PackageManifestFactsQuery owner limits while retaining the
// pre-existing event budget for evidence and evidence previews.
const maximumManifestPackageTypes = 128;
const maximumManifestDependencyGroups = 1_024;
const maximumManifestDependencies = 4_096;
const maximumEventCollectionItems =
  maximumCollectionItems
  + maximumOwnerItems
  + maximumManifestPackageTypes
  + maximumManifestDependencyGroups
  + maximumManifestDependencies;
// Match the Browser semantic request bound and the Analysis-owned default
// budget for each selected implementation library.
const maximumSemanticCandidates = 5;
const maximumSemanticOccurrencesPerCandidate = 10_000;
// System.Text.Json can encode one UTF-16 code unit as six JSON characters.
// Repeated contract structure is bounded separately by the maximum collection
// shape, while fixed event structure fits within the final allowance.
const maximumJsonCharactersPerTextCharacter = 6;
const maximumEventWireCharactersPerCollectionItem = 128;
const maximumEventWireFixedCharacters = 64 * 1_024;
const maximumEventWireCharacters =
  maximumEventCharacters * maximumJsonCharactersPerTextCharacter
  + maximumEventCollectionItems * maximumEventWireCharactersPerCollectionItem
  + maximumEventWireFixedCharacters;
const maximumDiagnosticCharacters = 64 * 1024;
// A bounded query can retain at most one result or failure per candidate,
// plus one source-wide failure that is not itself a candidate.
const maximumInspectionItems = 10_001;

type PackageQueryAcquisitionTier =
  Extract<BrowserPackageQueryRow["tier"], string>;
type PackageQueryEvidenceScope =
  Extract<BrowserPackageQueryEvidence["scope"], string>;
type PackageQueryFailureKind =
  Extract<BrowserPackageQueryFailure["kind"], string>;
type PackageQueryManifestFailureReason =
  Extract<BrowserPackageQueryFailure["manifestFailureReason"], string>;
type PackageQueryManifestIdentityProvenance =
  Extract<BrowserPackageQueryManifest["identityProvenance"], string>;
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
  extends Omit<BrowserPackageQueryEvidence, "scope" | "summary" | "term"> {
  readonly scope: PackageQueryEvidenceScope;
  readonly summary: EngineWorkerPackageQueryEvidenceSummary | null;
  readonly term: {
    readonly key: string;
    readonly operator: string;
    readonly value: string;
  } | null;
}

interface EngineWorkerPackageQueryRow
  extends Omit<BrowserPackageQueryRow, "tier" | "evidence"> {
  readonly tier: PackageQueryAcquisitionTier;
  readonly evidence: readonly EngineWorkerPackageQueryEvidence[];
}

interface EngineWorkerPackageQueryFailure
  extends Omit<
    BrowserPackageQueryFailure,
    "kind" | "manifestFailureReason"
  > {
  readonly kind: PackageQueryFailureKind;
  readonly manifestFailureReason: PackageQueryManifestFailureReason | null;
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

interface EngineWorkerPackageQueryInspection {
  readonly content: EngineWorkerPackageQueryDocument;
  readonly share: BrowserInspectionShare;
  readonly diagnostics: readonly BrowserInspectionDiagnostic[];
}

interface EngineWorkerPackageQueryDocument
  extends Omit<
    BrowserPackageQueryDocument,
    "results" | "failures" | "completion"
  > {
  readonly results: readonly EngineWorkerPackageQueryRow[];
  readonly failures: readonly EngineWorkerPackageQueryFailure[];
  readonly completion: EngineWorkerPackageQueryCompletion;
}

export interface EngineWorkerPackageQueryTerminal {
  readonly event: EngineWorkerPackageQueryCompletionEvent | null;
  readonly inspection: EngineWorkerPackageQueryInspection | null;
}

export type EngineWorkerPackageQueryInput =
  {
    readonly kind: "query";
    readonly searchText: string;
    readonly terms: readonly {
      readonly key: string;
      readonly operator: string;
      readonly value: string;
    }[];
    readonly targetFramework: string | null;
    readonly maximumCandidates: number;
    readonly maximumMatches: number;
    readonly includePrerelease: boolean;
    readonly initialMatchCredit: number;
  };

export interface EngineWorkerPackageQueryTerminalFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

type PackageQuerySettlement = ManagedOperationSettlement<
  EngineWorkerPackageQueryTerminal,
  EngineWorkerPackageQueryTerminalFailure,
  string
>;

type PackageFacade =
  typeof import("./facades/inspect-web-package.d.ts");

export type EngineWorkerPackageQueryFacade =
  Pick<
    PackageFacade,
    "cancelPackageQuery"
    | "requestPackageQueryMatches"
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
  maximumItems = maximumCollectionItems,
): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new PackageQueryPayloadError(
      `Expected ${description} array.`);
  }
  if (value.length > maximumItems) {
    throw new PackageQueryPayloadError(
      `Package Query payload exceeds ${maximumItems} collection items.`,
      "oversized",
    );
  }
  budget.remainingItems -= value.length;
  if (budget.remainingItems < 0) {
    throw new PackageQueryPayloadError(
      "Package Query payload exceeds its aggregate collection-item budget.",
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
  maximum = Number.MAX_SAFE_INTEGER,
): number {
  if (typeof value !== "number"
    || !Number.isSafeInteger(value)
    || value < minimum
    || value > maximum) {
    throw new PackageQueryPayloadError(
      `Expected ${description} safe integer from ${minimum} through ${maximum}.`);
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
  maximumItems = maximumCollectionItems,
): readonly string[] {
  return arrayItems(value, description, budget, maximumItems).map((item, index) =>
    text(item, `${description}[${index}]`, budget));
}

function queryTerms(
  value: unknown,
  budget: PayloadBudget,
): readonly {
  readonly key: string;
  readonly operator: string;
  readonly value: string;
}[] {
  return arrayItems(
    value,
    "Package Query terms",
    budget,
    maximumQueryTerms,
  ).map((item, index) => {
    const term = dataRecord(
      item,
      ["key", "operator", "value"],
      `Package Query terms[${index}]`,
    );
    return {
      key: text(term.key, `Package Query terms[${index}].key`, budget),
      operator: text(
        term.operator,
        `Package Query terms[${index}].operator`,
        budget),
      value: text(term.value, `Package Query terms[${index}].value`, budget),
    };
  });
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
      "terms",
      "targetFramework",
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
      terms: queryTerms(input.terms, budget),
      targetFramework: nullableText(
        input.targetFramework,
        "Package Query target framework",
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
    ["id", "scope", "summary", "properties", "number", "term"],
    "Package Query evidence",
  );
  return {
    id: text(evidence.id, "Package Query evidence ID", budget),
    scope: literal(
      evidence.scope,
      ["Package", "Query"] as const,
      "Package Query evidence scope"),
    summary: parseEvidenceSummary(evidence.summary, budget),
    properties: arrayItems(
      evidence.properties,
      "Package Query evidence properties",
      budget)
      .map(propertyValue => {
        const property = dataRecord(
          propertyValue,
          ["name", "value"],
          "Package Query evidence property");
        return {
          name: text(
            property.name,
            "Package Query evidence property name",
            budget),
          value: text(
            property.value,
            "Package Query evidence property value",
            budget),
        };
      }),
    number: evidence.number === null
      ? null
      : integer(evidence.number, "Package Query evidence number"),
    term: evidence.term === null
      ? null
      : (() => {
          const term = dataRecord(
            evidence.term,
            ["key", "operator", "value"],
            "Package Query evidence term",
          );
          return {
            key: text(term.key, "Package Query evidence term key", budget),
            operator: text(
              term.operator,
              "Package Query evidence term operator",
              budget),
            value: text(
              term.value,
              "Package Query evidence term value",
              budget),
          };
        })(),
  };
}

function parseManifestDependency(
  value: unknown,
  budget: PayloadBudget,
): BrowserPackageQueryManifest["dependencyGroups"][number]["dependencies"][number] {
  const dependency = dataRecord(
    value,
    ["id", "versionRange"],
    "Package Query manifest dependency",
  );
  return {
    id: text(dependency.id, "Package Query dependency ID", budget),
    versionRange: text(
      dependency.versionRange,
      "Package Query dependency version range",
      budget),
  };
}

function parseManifestDependencyGroup(
  value: unknown,
  budget: PayloadBudget,
): BrowserPackageQueryManifest["dependencyGroups"][number] {
  const group = dataRecord(
    value,
    ["targetFramework", "dependencies", "isImplicitManifestGroup"],
    "Package Query manifest dependency group",
  );
  return {
    targetFramework: text(
      group.targetFramework,
      "Package Query dependency target framework",
      budget),
    dependencies: arrayItems(
      group.dependencies,
      "Package Query manifest dependencies",
      budget,
      maximumManifestDependencies)
      .map(item => parseManifestDependency(item, budget)),
    isImplicitManifestGroup: booleanValue(
      group.isImplicitManifestGroup,
      "Package Query implicit manifest group"),
  };
}

function parseManifest(
  value: unknown,
  budget: PayloadBudget,
): BrowserPackageQueryManifest | null {
  if (value === null) return null;
  const manifest = dataRecord(value, [
    "packageId",
    "version",
    "manifestVersion",
    "description",
    "authors",
    "repository",
    "repositoryType",
    "repositoryCommit",
    "license",
    "licenseUrl",
    "packageTypes",
    "isToolPackage",
    "readmeFile",
    "dependencyGroups",
    "iconFile",
    "iconUrl",
    "identityProvenance",
  ], "Package Query manifest");
  const identityProvenance: PackageQueryManifestIdentityProvenance = literal(
    manifest.identityProvenance,
    ["ExpectedCoordinate", "SelfAttested"] as const,
    "Package Query manifest identity provenance");
  return {
    packageId: text(
      manifest.packageId,
      "Package Query manifest package ID",
      budget),
    version: text(
      manifest.version,
      "Package Query manifest version",
      budget),
    manifestVersion: text(
      manifest.manifestVersion,
      "Package Query manifest schema version",
      budget),
    description: nullableText(
      manifest.description,
      "Package Query manifest description",
      budget),
    authors: nullableText(
      manifest.authors,
      "Package Query manifest authors",
      budget),
    repository: nullableText(
      manifest.repository,
      "Package Query manifest repository",
      budget),
    repositoryType: nullableText(
      manifest.repositoryType,
      "Package Query manifest repository type",
      budget),
    repositoryCommit: nullableText(
      manifest.repositoryCommit,
      "Package Query manifest repository commit",
      budget),
    license: nullableText(
      manifest.license,
      "Package Query manifest license",
      budget),
    licenseUrl: nullableText(
      manifest.licenseUrl,
      "Package Query manifest license URL",
      budget),
    packageTypes: stringArray(
      manifest.packageTypes,
      "Package Query manifest package types",
      budget,
      maximumManifestPackageTypes),
    isToolPackage: booleanValue(
      manifest.isToolPackage,
      "Package Query tool package value"),
    readmeFile: nullableText(
      manifest.readmeFile,
      "Package Query manifest README file",
      budget),
    dependencyGroups: arrayItems(
      manifest.dependencyGroups,
      "Package Query manifest dependency groups",
      budget,
      maximumManifestDependencyGroups)
      .map(item => parseManifestDependencyGroup(item, budget)),
    iconFile: nullableText(
      manifest.iconFile,
      "Package Query manifest icon file",
      budget),
    iconUrl: nullableText(
      manifest.iconUrl,
      "Package Query manifest icon URL",
      budget),
    identityProvenance,
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
    "answers",
    "evidence",
    "totalDownloads",
    "verified",
    "producer",
    "description",
    "rootRequest",
    "owners",
    "manifest",
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
      "Package Query preset tier"),
    answers: arrayItems(
      row.answers,
      "Package Query answers",
      budget)
      .map(item => {
        const answer = dataRecord(
          item,
          ["id", "value", "term"],
          "Package Query answer");
        return {
          id: text(answer.id, "Package Query answer ID", budget),
          value: text(answer.value, "Package Query answer value", budget),
          term: answer.term === null
            ? null
            : (() => {
                const term = dataRecord(
                  answer.term,
                  ["key", "operator", "value"],
                  "Package Query answer term");
                return {
                  key: text(
                    term.key,
                    "Package Query answer term key",
                    budget),
                  operator: text(
                    term.operator,
                    "Package Query answer term operator",
                    budget),
                  value: text(
                    term.value,
                    "Package Query answer term value",
                    budget),
                };
              })(),
        };
      }),
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
    owners: stringArray(
      row.owners,
      "Package Query owners",
      budget,
      maximumOwnerItems),
    manifest: parseManifest(row.manifest, budget),
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
    "manifestFailureReason",
  ], "Package Query failure");
  const manifestFailureReason = failure.manifestFailureReason === null
    ? null
    : literal(
      failure.manifestFailureReason,
      [
        "MalformedXml",
        "UnsupportedDocumentShape",
        "IdentityMismatch",
        "InvalidDependencyContract",
        "ConfiguredLimitExceeded",
        "InvalidIdentityContract",
      ] as const,
      "Package Query manifest failure reason");
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
        "DependencyTraversal",
        "AssemblyAcquisition",
        "AssemblyEvaluation",
        "AssemblyNotEvaluated",
      ] as const,
      "Package Query failure kind"),
    message: text(
      failure.message,
      "Package Query failure message",
      budget),
    manifestFailureReason,
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
      [
        "Search",
        "Manifest",
        "PackageContent",
        "DependencyTraversal",
        "Assembly",
      ] as const,
      "Package Query progress phase"),
    completed: integer(
      progress.completed,
      "Package Query completed progress"),
    limit: integer(
      progress.limit,
      "Package Query progress limit"),
  };
}

function parseAssemblySemanticSelectedAsset(
  value: unknown,
  budget: PayloadBudget,
): BrowserPackageAssemblySemanticSelectedAsset {
  const selected = dataRecord(value, [
    "path",
    "assemblyName",
    "targetFramework",
    "sequence",
    "ordinal",
    "unevaluatedSiblings",
    "rootRequest",
  ], "Package Query assembly-semantic selected asset");
  return {
    path: text(selected.path, "selected asset path", budget),
    assemblyName: text(
      selected.assemblyName,
      "selected asset assembly name",
      budget),
    targetFramework: text(
      selected.targetFramework,
      "selected asset target framework",
      budget),
    sequence: text(
      selected.sequence,
      "selected asset sequence",
      budget),
    ordinal: integer(
      selected.ordinal,
      "selected asset ordinal"),
    unevaluatedSiblings: integer(
      selected.unevaluatedSiblings,
      "selected asset unevaluated sibling count"),
    rootRequest: text(
      selected.rootRequest,
      "selected asset Root request",
      budget),
  };
}

function parseAssemblySemanticLibraryAssessment(
  value: unknown,
  budget: PayloadBudget,
): BrowserPackageAssemblySemanticLibraryAssessment {
  const assessment = dataRecord(value, [
    "selectedAsset",
    "kind",
    "occurrences",
    "failureStage",
    "message",
  ], "Package Query assembly-semantic Library assessment");
  return {
    selectedAsset: parseAssemblySemanticSelectedAsset(
      assessment.selectedAsset,
      budget),
    kind: literal(
      assessment.kind,
      ["Matched", "NoMatch", "Failure"] as const,
      "assembly-semantic Library assessment kind"),
    occurrences: integer(
      assessment.occurrences,
      "assembly-semantic Library occurrence count",
      0),
    failureStage: assessment.failureStage === null
      ? null
      : text(
          assessment.failureStage,
          "assembly-semantic Library failure stage",
          budget),
    message: assessment.message === null
      ? null
      : text(
          assessment.message,
          "assembly-semantic Library assessment message",
          budget),
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
    "libraries",
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
      ["Matched", "NoMatch", "NotApplicable", "Failure", "NotEvaluated"] as const,
      "Package Query assessment disposition"),
    message: text(
      assessment.message,
      "Package Query assessment message",
      budget),
    assetPath: nullableText(
      assessment.assetPath,
      "Package Query assessment asset path",
      budget),
    rootRequest: nullableText(
      assessment.rootRequest,
      "Package Query assessment Root request",
      budget),
    libraries: arrayItems(
      assessment.libraries,
      "Package Query assessment Libraries",
      budget).map(item =>
        parseAssemblySemanticLibraryAssessment(item, budget)),
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
    "occurrences",
    "notEvaluated",
    "evaluatedCandidates",
    "semanticMatches",
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
    occurrences: nullableInteger(
      completion.occurrences,
      "Package Query occurrence count"),
    notEvaluated: nullableInteger(
      completion.notEvaluated,
      "Package Query not-evaluated count"),
    evaluatedCandidates: nullableInteger(
      completion.evaluatedCandidates,
      "Package Query evaluated candidate count"),
    semanticMatches: nullableInteger(
      completion.semanticMatches,
      "Package Query semantic match count"),
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
    remainingItems: maximumEventCollectionItems,
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

function parseInspection(
  inspectionValue: unknown,
): EngineWorkerPackageQueryInspection {
  const inspection = dataRecord(inspectionValue, [
    "content",
    "share",
    "diagnostics",
  ], "Package Query inspection");
  const documentRecord = dataRecord(
    inspection.content,
    [
      "results",
      "failures",
      "completion",
      "hasPackages",
      "libraryLiteralAssessments",
    ],
    "Package Query Document");
  const documentBudget = {
    remainingCharacters: maximumEventCharacters,
    remainingItems: maximumInspectionItems,
  };
  const content: EngineWorkerPackageQueryDocument = {
    hasPackages: booleanValue(
      documentRecord.hasPackages,
      "Package Query Document hasPackages"),
    results: arrayItems(
      documentRecord.results,
      "Package Query Document results",
      documentBudget,
      maximumInspectionItems).map(row =>
        parseRow(row, {
          remainingCharacters: maximumEventCharacters,
          remainingItems: maximumEventCollectionItems,
        })),
    failures: arrayItems(
      documentRecord.failures,
      "Package Query Document failures",
      documentBudget,
      maximumInspectionItems).map(failure =>
        parseFailure(failure, {
          remainingCharacters: maximumEventCharacters,
          remainingItems: maximumEventCollectionItems,
        })),
    completion: parseCompletion(
      documentRecord.completion,
      documentBudget),
    libraryLiteralAssessments: arrayItems(
      documentRecord.libraryLiteralAssessments,
      "Package Query library-literal assessments",
      documentBudget,
      maximumSemanticCandidates).map(item =>
        parseAssemblySemanticOutcome(item, {
          remainingCharacters: maximumEventCharacters,
          remainingItems: maximumEventCollectionItems,
        })),
  };
  if (content.completion.matches !== content.results.length
      || content.completion.failures !== content.failures.length
      || content.hasPackages !== (content.results.length > 0)) {
    throw new PackageQueryPayloadError(
      "Package Query Document does not match its terminal accounting.");
  }
  if (content.completion.semanticMatches === null) {
    if (content.completion.occurrences !== null
        || content.completion.notEvaluated !== null
        || content.libraryLiteralAssessments.length !== 0) {
      throw new PackageQueryPayloadError(
        "An ordinary Package Query Document carried semantic accounting.");
    }
  } else {
    if (content.completion.evaluatedCandidates === null
        || content.completion.semanticMisses === null
        || content.completion.notApplicable === null
        || content.completion.occurrences === null
        || content.completion.notEvaluated === null
        || content.completion.evaluatedCandidates
          + content.completion.notEvaluated
          !== content.libraryLiteralAssessments.length) {
      throw new PackageQueryPayloadError(
        "Package Query semantic accounting does not match its candidate assessments.");
    }
  }

  function finiteNonnegative(
    value: unknown,
    description: string,
  ): number {
    if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
      throw new PackageQueryPayloadError(
        `Expected ${description} non-negative finite number.`);
    }
    return value;
  }

  function nullableFiniteNonnegative(
    value: unknown,
    description: string,
  ): number | null {
    return value === null ? null : finiteNonnegative(value, description);
  }

  function parseAssemblySemanticOccurrence(
    value: unknown,
    budget: PayloadBudget,
  ): BrowserPackageAssemblySemanticOccurrence {
    const occurrence = dataRecord(value, [
      "libraryPath",
      "moduleVersionId",
      "methodDefinitionToken",
      "ilOffset",
      "userStringToken",
      "literalCharacterCount",
      "literalText",
    ], "Package Query assembly-semantic occurrence");
    return {
      libraryPath: text(
        occurrence.libraryPath,
        "occurrence library path",
        budget),
      moduleVersionId: text(
        occurrence.moduleVersionId,
        "occurrence module version ID",
        budget),
      methodDefinitionToken: integer(
        occurrence.methodDefinitionToken,
        "occurrence MethodDef token"),
      ilOffset: integer(occurrence.ilOffset, "occurrence IL offset"),
      userStringToken: integer(
        occurrence.userStringToken,
        "occurrence user-string token"),
      literalCharacterCount: integer(
        occurrence.literalCharacterCount,
        "occurrence literal character count"),
      literalText: text(
        occurrence.literalText,
        "occurrence literal text",
        budget),
    };
  }

  function parseAssemblySemanticResult(
    value: unknown,
    budget: PayloadBudget,
  ): BrowserPackageAssemblySemanticResult {
    const result = dataRecord(value, [
      "candidateOrdinal",
      "packageId",
      "version",
      "producer",
      "selectedAsset",
      "occurrences",
    ], "Package Query assembly-semantic Result");
    return {
      candidateOrdinal: integer(
        result.candidateOrdinal,
        "assembly-semantic Result candidate ordinal",
        1),
      packageId: text(
        result.packageId,
        "assembly-semantic Result package ID",
        budget),
      version: text(
        result.version,
        "assembly-semantic Result version",
        budget),
      producer: text(
        result.producer,
        "assembly-semantic Result producer",
        budget),
      selectedAsset: parseAssemblySemanticSelectedAsset(
        result.selectedAsset,
        budget),
      occurrences: arrayItems(
        result.occurrences,
        "assembly-semantic Result occurrences",
        budget,
        maximumSemanticOccurrencesPerCandidate)
        .map(item => parseAssemblySemanticOccurrence(item, budget)),
    };
  }

  function parseAssemblySemanticOutcome(
    value: unknown,
    budget: PayloadBudget,
  ): BrowserPackageAssemblySemanticCandidateOutcome {
    const outcome = dataRecord(value, [
      "kind",
      "candidateOrdinal",
      "packageId",
      "version",
      "producer",
      "result",
      "selectedAsset",
      "rootRequest",
      "notApplicableReason",
      "failureKind",
      "failureStage",
      "nonEvaluationKind",
      "timeoutKind",
      "timeoutSeconds",
      "message",
      "libraries",
    ], "Package Query assembly-semantic candidate outcome");
    const kind = literal(
      outcome.kind,
      ["Matched", "NoMatch", "NotApplicable", "Failure", "NotEvaluated"] as const,
      "assembly-semantic candidate outcome kind");
    const result = outcome.result === null
      ? null
      : parseAssemblySemanticResult(outcome.result, budget);
    const selectedAsset = outcome.selectedAsset === null
      ? null
      : parseAssemblySemanticSelectedAsset(outcome.selectedAsset, budget);
    const projected: BrowserPackageAssemblySemanticCandidateOutcome = {
      kind,
      candidateOrdinal: integer(
        outcome.candidateOrdinal,
        "assembly-semantic candidate ordinal",
        1),
      packageId: text(
        outcome.packageId,
        "assembly-semantic candidate package ID",
        budget),
      version: text(
        outcome.version,
        "assembly-semantic candidate version",
        budget),
      producer: text(
        outcome.producer,
        "assembly-semantic candidate producer",
        budget),
      result,
      selectedAsset,
      rootRequest: outcome.rootRequest === null
        ? null
        : text(outcome.rootRequest, "candidate Root request", budget),
      notApplicableReason: outcome.notApplicableReason === null
        ? null
        : literal(
            outcome.notApplicableReason,
            [
              "NoCompileAssets",
              "NoMatchingTargetFramework",
              "EmptyCompileGroup",
              "NoImplementationCounterpart",
            ] as const,
            "candidate not-applicable reason"),
      failureKind: outcome.failureKind === null
        ? null
        : literal(
            outcome.failureKind,
            ["Acquisition", "Evaluation"] as const,
            "candidate failure kind"),
      failureStage: outcome.failureStage === null
        ? null
        : text(outcome.failureStage, "candidate failure stage", budget),
      nonEvaluationKind: outcome.nonEvaluationKind === null
        ? null
        : literal(
            outcome.nonEvaluationKind,
            ["OperationDeadline"] as const,
            "candidate non-evaluation kind"),
      timeoutKind: outcome.timeoutKind === null
        ? null
        : text(outcome.timeoutKind, "candidate timeout kind", budget),
      timeoutSeconds: nullableFiniteNonnegative(
        outcome.timeoutSeconds,
        "candidate timeout seconds"),
      message: outcome.message === null
        ? null
        : text(outcome.message, "candidate outcome message", budget),
      libraries: arrayItems(
        outcome.libraries,
        "assembly-semantic candidate Library assessments",
        budget).map(item =>
          parseAssemblySemanticLibraryAssessment(item, budget)),
    };
    if (kind === "Matched" && result === null) {
      throw new PackageQueryPayloadError(
        "A matched assembly-semantic candidate omitted its Result.");
    }
    if (kind !== "Matched" && result !== null) {
      throw new PackageQueryPayloadError(
        "A non-match assembly-semantic candidate carried a Result.");
    }
    return projected;
  }

  const metadataBudget = {
    remainingCharacters: maximumEventCharacters,
    remainingItems: maximumCollectionItems,
  };
  const share = dataRecord(
    inspection.share,
    ["kind", "fullUrl", "packet", "path", "reason"],
    "Package Query inspection Share");
  const kind = literal(
    share.kind,
    ["Available", "NonProjectable"] as const,
    "Package Query inspection Share kind");
  const projectedShare: BrowserInspectionShare = {
    kind,
    fullUrl: nullableText(
      share.fullUrl,
      "Package Query inspection Share URL",
      metadataBudget),
    packet: nullableText(
      share.packet,
      "Package Query inspection Share packet",
      metadataBudget),
    path: nullableText(
      share.path,
      "Package Query inspection Share path",
      metadataBudget),
    reason: nullableText(
      share.reason,
      "Package Query inspection Share reason",
      metadataBudget),
  };
  if (kind === "Available") {
    if (projectedShare.fullUrl === null
        || projectedShare.packet === null
        || projectedShare.path !== null
        || projectedShare.reason !== null) {
      throw new PackageQueryPayloadError(
        "Available Package Query Share data is malformed.");
    }
  } else if (projectedShare.fullUrl !== null
      || projectedShare.packet !== null
      || projectedShare.path === null
      || projectedShare.reason === null) {
    throw new PackageQueryPayloadError(
      "Non-projectable Package Query Share data is malformed.");
  }

  const diagnostics = arrayItems(
    inspection.diagnostics,
    "Package Query inspection diagnostics",
    metadataBudget).map(item => {
      const diagnostic = dataRecord(item, [
        "code",
        "severity",
        "summary",
        "correspondence",
      ], "Package Query inspection diagnostic");
      return {
        code: text(
          diagnostic.code,
          "Package Query diagnostic code",
          metadataBudget),
        severity: text(
          diagnostic.severity,
          "Package Query diagnostic severity",
          metadataBudget),
        summary: text(
          diagnostic.summary,
          "Package Query diagnostic summary",
          metadataBudget),
        correspondence: nullableText(
          diagnostic.correspondence,
          "Package Query diagnostic correspondence",
          metadataBudget),
      };
    });

  return {
    content,
    share: projectedShare,
    diagnostics,
  };
}

export const engineWorkerPackageQueryTerminal:
BoundedPayloadDecoder<EngineWorkerPackageQueryTerminal> = {
  decode(value) {
    try {
      const terminal = dataRecord(
        value,
        ["event", "inspection"],
        "Package Query terminal result");
      if (terminal.inspection === null) {
        const decoded =
          engineWorkerPackageQueryCompletionEvent.decode(terminal.event);
        if (decoded.kind === "rejected") {
          throw new PackageQueryPayloadError(
            decoded.message,
            decoded.reason);
        }
        return {
          kind: "decoded",
          value: { event: decoded.value, inspection: null },
        };
      }
      nullValue(
        terminal.event,
        "Package Query inspection terminal event");
      const inspection = parseInspection(terminal.inspection);
      return {
        kind: "decoded",
        value: {
          event: null,
          inspection,
        },
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

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
      "inspection",
      "failureKind",
      "error",
      "diagnostic",
      "reason",
    ], "Package Query result");
    if (result.version !== 3) {
      throw new PackageQueryPayloadError(
        "Expected a version 3 Package Query result.");
    }
    if (result.kind === "Succeeded") {
      nullValue(result.failureKind, "Package Query success failure kind");
      nullValue(result.error, "Package Query success error");
      nullValue(result.diagnostic, "Package Query success diagnostic");
      nullValue(result.reason, "Package Query success reason");
      if (result.inspection !== null) {
        nullValue(result.value, "Package Query inspection success value");
        const inspection = parseInspection(result.inspection);
        return {
          kind: "succeeded",
          value: {
            event: null,
            inspection,
          },
        };
      }
      const decoded =
        engineWorkerPackageQueryCompletionEvent.decode(result.value);
      if (decoded.kind === "rejected") {
        throw new PackageQueryPayloadError(
          decoded.message,
          decoded.reason);
      }
      return {
        kind: "succeeded",
        value: { event: decoded.value, inspection: null },
      };
    }
    if (result.kind === "Failed") {
      nullValue(result.value, "Package Query failure value");
      nullValue(result.inspection, "Package Query failure inspection");
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
      nullValue(result.inspection, "Package Query cancellation inspection");
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
    {
          kind: "query",
          searchText: request.scopeQuery,
          terms: [
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
          targetFramework: isLibraryLiteralQuery(request)
            ? request.targetFramework
            : null,
          maximumCandidates: request.requestedLimit,
          maximumMatches: request.requestedMatchLimit,
          includePrerelease: request.includePrerelease,
          initialMatchCredit: PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
        };
  return engineWorkerPackageQueryInput.decode(payload);
}

function mapControlRequestError(
  error:
    Parameters<
      WorkerRuntimeControlledOperationRegistration<
        QueryRequest,
        EngineWorkerPackageQueryTerminal,
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
  EngineWorkerPackageQueryTerminal,
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
    value: engineWorkerPackageQueryTerminal,
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
  if (value.length > maximumEventWireCharacters) {
    throw new PackageQueryPayloadError(
      `Package Query callback exceeds ${maximumEventWireCharacters} characters.`,
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
      const result = await packageFacade.runPackageQuery(
          context.operation.operationId,
          input.searchText,
        input.terms,
          input.targetFramework,
          input.maximumCandidates,
          input.maximumMatches,
          input.includePrerelease,
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
