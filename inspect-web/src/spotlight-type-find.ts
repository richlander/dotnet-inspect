import type {
  BrowserTypeFindCandidateActivation,
  BrowserTypeFindResult,
  InspectionDiagnostic,
} from "./facades/inspect-web-metadata.d.ts";

interface TypeFindCandidateName {
  readonly namespace: string;
  readonly segments: readonly string[];
}

interface TypeFindLibraryIdentity {
  readonly name: string;
  readonly version: string | null;
  readonly culture: string | null;
  readonly public_key_token: string | null;
}

interface TypeFindCoordinate {
  readonly kind: string;
  readonly library_identity: TypeFindLibraryIdentity;
}

interface TypeFindRealization {
  readonly kind: string;
  readonly package_id?: string;
  readonly version?: string;
  readonly producer?: string;
  readonly family?: string;
  readonly declared_name?: string;
}

interface TypeFindObservation {
  readonly context_order: number;
  readonly member_order: number;
  readonly realization: TypeFindRealization;
}

interface TypeFindCandidate {
  readonly wireIdentity: string;
  readonly coordinate: TypeFindCoordinate;
  readonly name: TypeFindCandidateName;
  readonly declaration_kind: string | number;
  readonly module_version_id: string;
  readonly observation: TypeFindObservation;
}

interface TypeFindAnswer {
  readonly identity: { readonly ordinal: number };
  readonly candidates: readonly TypeFindCandidate[];
  readonly is_complete: boolean;
}

interface TypeFindContextCoverage {
  readonly is_realized: boolean;
  readonly failures: readonly unknown[];
}

interface TypeFindMemberCoverage {
  readonly is_complete: boolean;
}

interface TypeFindEvaluatedResult {
  readonly kind: "evaluated";
  readonly contexts: readonly TypeFindContextCoverage[];
  readonly members: readonly TypeFindMemberCoverage[];
  readonly answers: readonly TypeFindAnswer[];
  readonly row_selection_failure?: unknown;
  readonly visibility_failure?: unknown;
}

interface TypeFindRejectedResult {
  readonly kind: "rejected";
  readonly rejection_kind: string | number;
}

type TypeFindResult = TypeFindEvaluatedResult | TypeFindRejectedResult;

interface ManagedSpotlightTypeCandidate {
  readonly identity: string;
  readonly action: string | null;
  readonly reason: string | null;
  readonly name: string;
  readonly namespace: string;
  readonly library: string;
  readonly source: string;
  readonly declarationKind: string;
}

interface ManagedSpotlightTypeFindProjection {
  readonly candidates: readonly ManagedSpotlightTypeCandidate[];
  readonly notice: string;
}

interface SpotlightTypeFindPosting {
  readonly retainedDefinitionId: string;
  readonly realizationId: string;
}

interface SpotlightTypeFindOptions {
  findTypes(
    retainedDefinitionId: string,
    realizationId: string,
    resultGeneration: number,
    text: string,
  ): Promise<BrowserTypeFindResult>;
  schedule(callback: () => void, delay: number): number;
  cancelScheduled(handle: number): void;
  updateResults(): void;
}

export interface SpotlightTypeFindCoordinator {
  schedule(
    posting: SpotlightTypeFindPosting | null,
    query: string,
    enabled: boolean,
  ): void;
  reset(): void;
  results(): readonly ManagedSpotlightTypeCandidate[];
  loading(): boolean;
  error(): string;
  notice(): string;
}

export function createSpotlightTypeFind(
  options: SpotlightTypeFindOptions,
): SpotlightTypeFindCoordinator {
  let generation = 0;
  let scheduled: number | null = null;
  let requestKey = "";
  let candidates: readonly ManagedSpotlightTypeCandidate[] = [];
  let isLoading = false;
  let failure = "";
  let notice = "";

  function cancel(): void {
    if (scheduled === null) return;
    options.cancelScheduled(scheduled);
    scheduled = null;
  }

  function reset(): void {
    generation++;
    cancel();
    requestKey = "";
    candidates = [];
    isLoading = false;
    failure = "";
    notice = "";
  }

  function schedule(
    posting: SpotlightTypeFindPosting | null,
    query: string,
    enabled: boolean,
  ): void {
    const text = query.trim();
    if (!enabled || posting === null) {
      if (requestKey) reset();
      return;
    }

    const nextKey = JSON.stringify([
      posting.retainedDefinitionId,
      posting.realizationId,
      text,
    ]);
    if (nextKey === requestKey) return;

    requestKey = nextKey;
    const requestGeneration = ++generation;
    cancel();
    candidates = [];
    failure = "";
    notice = "";
    isLoading = true;
    scheduled = options.schedule(() => {
      scheduled = null;
      void options.findTypes(
        posting.retainedDefinitionId,
        posting.realizationId,
        requestGeneration,
        text,
      ).then(result => {
          if (requestGeneration !== generation
            || requestKey !== nextKey) return undefined;
          if (result.status !== "Completed" || result.operation === null) {
            candidates = [];
            failure = text.length === 0
              ? ""
              : result.reason
                ?? `Type Find returned '${String(result.status)}'.`;
          } else {
            const projection = projectSpotlightTypeFindResult(result);
            candidates = projection.candidates;
            notice = projection.notice;
            failure = "";
          }
          isLoading = false;
          options.updateResults();
          return undefined;
        })
        .catch((error: unknown) => {
          if (requestGeneration !== generation
            || requestKey !== nextKey) return undefined;
          candidates = [];
          failure = error instanceof Error
            ? error.message
            : String(error);
          notice = "";
          isLoading = false;
          options.updateResults();
          return undefined;
        });
    }, 75);
  }

  return {
    schedule,
    reset,
    results: () => candidates,
    loading: () => isLoading,
    error: () => failure,
    notice: () => notice,
  };
}

export function projectSpotlightTypeFindResult(
  result: BrowserTypeFindResult,
): ManagedSpotlightTypeFindProjection {
  if (result.status !== "Completed" || result.operation === null) {
    return { candidates: [], notice: "" };
  }
  const content = parseResult(result.operation.find.content);
  const activations = activationMap(result.operation.activations);
  if (content.kind === "rejected") {
    if (activations.size !== 0) {
      throw new TypeError(
        "Rejected Type Find returned activation correspondence.",
      );
    }
    return {
      candidates: [],
      notice: [
        `Type Find could not evaluate this request: ${String(
          content.rejection_kind,
        )}.`,
        diagnosticsNotice(result.operation.find.diagnostics),
      ].filter(Boolean).join(" "),
    };
  }
  const projected: ManagedSpotlightTypeCandidate[] = [];

  for (const answer of content.answers) {
    answer.candidates.forEach((candidate, index) => {
      const candidateOrdinal = index + 1;
      const key = activationKey(answer.identity.ordinal, candidateOrdinal);
      const activation = activations.get(key);
      if (activation === undefined) {
        throw new TypeError(
          `Type Find omitted activation correspondence '${key}'.`,
        );
      }
      activations.delete(key);
      if (activation.status === "Available" && activation.action === null) {
        throw new TypeError(
          `Type Find marked activation correspondence '${key}' available without an action.`,
        );
      }
      const name = candidate.name.segments.join(".");
      projected.push({
        identity: candidateIdentity(candidate),
        action: activation.status === "Available"
          ? activation.action
          : null,
        reason: activation.status === "Available"
          ? null
          : activation.reason
            ?? `Type activation is ${String(activation.status).toLowerCase()}.`,
        name,
        namespace: candidate.name.namespace,
        library: candidate.coordinate.library_identity.name,
        source: realizationLabel(candidate.observation.realization),
        declarationKind: String(candidate.declaration_kind),
      });
    });
  }

  if (activations.size !== 0) {
    throw new TypeError(
      "Type Find returned activation correspondence without a candidate.",
    );
  }
  return {
    candidates: projected,
    notice: coverageNotice(content, result.operation.find.diagnostics),
  };
}

interface ManagedSpotlightSelectionCommitOptions {
  isCurrent(): boolean;
  commit(): void;
  acknowledge?: () => Promise<string>;
}

interface ManagedSpotlightSelectionCommit {
  readonly committed: boolean;
  readonly acknowledged: boolean;
  readonly current: boolean;
}

export async function commitManagedSpotlightSelection(
  options: ManagedSpotlightSelectionCommitOptions,
): Promise<ManagedSpotlightSelectionCommit> {
  if (!options.isCurrent()) {
    return {
      committed: false,
      acknowledged: false,
      current: false,
    };
  }

  options.commit();
  if (options.acknowledge === undefined) {
    return {
      committed: true,
      acknowledged: false,
      current: options.isCurrent(),
    };
  }

  const acknowledged = await options.acknowledge();
  if (acknowledged !== "accepted") {
    throw new Error(
      `Managed Navigation acknowledgement returned '${acknowledged}'.`,
    );
  }
  return {
    committed: true,
    acknowledged: true,
    current: options.isCurrent(),
  };
}

function activationMap(
  activations: readonly BrowserTypeFindCandidateActivation[],
): Map<string, BrowserTypeFindCandidateActivation> {
  const mapped = new Map<string, BrowserTypeFindCandidateActivation>();
  for (const activation of activations) {
    const key = activationKey(
      activation.candidate.answerOrdinal,
      activation.candidate.candidateOrdinal,
    );
    if (mapped.has(key)) {
      throw new TypeError(
        `Type Find returned duplicate activation correspondence '${key}'.`,
      );
    }
    mapped.set(key, activation);
  }
  return mapped;
}

function activationKey(answerOrdinal: number, candidateOrdinal: number): string {
  return `${answerOrdinal}:${candidateOrdinal}`;
}

function candidateIdentity(candidate: TypeFindCandidate): string {
  return candidate.wireIdentity;
}

function realizationLabel(realization: TypeFindRealization): string {
  switch (realization.kind) {
    case "package":
      return realization.package_id && realization.version
        ? `${realization.package_id}@${realization.version}`
        : realization.package_id ?? "Package";
    case "platform": {
      const product = realization.family?.toLowerCase().includes("aspnet")
        ? "ASP.NET Core"
        : ".NET";
      return realization.version
        ? `${product} ${realization.version}`
        : product;
    }
    case "embedded":
      return realization.declared_name ?? "Embedded";
    case "platform-reference":
      return ".NET";
    default:
      return realization.kind;
  }
}

function parseResult(value: unknown): TypeFindResult {
  if (!isRecord(value)) {
    throw new TypeError("Type Find returned an invalid locator result.");
  }
  if (value.kind === "rejected") {
    if (typeof value.rejection_kind !== "string"
      && typeof value.rejection_kind !== "number") {
      throw new TypeError("Type Find returned an invalid rejection.");
    }
    return {
      kind: "rejected",
      rejection_kind: value.rejection_kind,
    };
  }
  if (value.kind !== "evaluated"
    || !Array.isArray(value.contexts)
    || !Array.isArray(value.members)
    || !Array.isArray(value.answers)) {
    throw new TypeError("Type Find returned an invalid locator result.");
  }

  const answers = value.answers.map(parseAnswer);
  return {
    kind: "evaluated",
    contexts: value.contexts.map(parseContextCoverage),
    members: value.members.map(parseMemberCoverage),
    answers,
    ...("row_selection_failure" in value
      ? { row_selection_failure: value.row_selection_failure }
      : {}),
    ...("visibility_failure" in value
      ? { visibility_failure: value.visibility_failure }
      : {}),
  };
}

function parseAnswer(value: unknown): TypeFindAnswer {
  if (!isRecord(value)
    || !isRecord(value.identity)
    || typeof value.identity.ordinal !== "number"
    || !Number.isInteger(value.identity.ordinal)
    || !Array.isArray(value.candidates)
    || typeof value.is_complete !== "boolean") {
    throw new TypeError("Type Find returned an invalid answer.");
  }
  return {
    identity: { ordinal: value.identity.ordinal },
    candidates: value.candidates.map(parseCandidate),
    is_complete: value.is_complete,
  };
}

function parseCandidate(value: unknown): TypeFindCandidate {
  if (!isRecord(value)
    || !isCoordinate(value.coordinate)
    || !isCandidateName(value.name)
    || (typeof value.declaration_kind !== "string"
      && typeof value.declaration_kind !== "number")
    || typeof value.module_version_id !== "string"
    || !isObservation(value.observation)) {
    throw new TypeError("Type Find returned an invalid candidate.");
  }
  return {
    wireIdentity: JSON.stringify(value),
    coordinate: value.coordinate,
    name: value.name,
    declaration_kind: value.declaration_kind,
    module_version_id: value.module_version_id,
    observation: value.observation,
  };
}

function isCoordinate(value: unknown): value is TypeFindCoordinate {
  return isRecord(value)
    && typeof value.kind === "string"
    && isRecord(value.library_identity)
    && typeof value.library_identity.name === "string"
    && (typeof value.library_identity.version === "string"
      || value.library_identity.version === null)
    && (typeof value.library_identity.culture === "string"
      || value.library_identity.culture === null)
    && (typeof value.library_identity.public_key_token === "string"
      || value.library_identity.public_key_token === null);
}

function isCandidateName(value: unknown): value is TypeFindCandidateName {
  return isRecord(value)
    && typeof value.namespace === "string"
    && Array.isArray(value.segments)
    && value.segments.every(segment => typeof segment === "string");
}

function isObservation(value: unknown): value is TypeFindObservation {
  return isRecord(value)
    && Number.isInteger(value.context_order)
    && Number.isInteger(value.member_order)
    && isRealization(value.realization);
}

function isRealization(value: unknown): value is TypeFindRealization {
  return isRecord(value)
    && typeof value.kind === "string"
    && optionalString(value.package_id)
    && optionalString(value.version)
    && optionalString(value.producer)
    && optionalString(value.family)
    && optionalString(value.declared_name);
}

function parseContextCoverage(value: unknown): TypeFindContextCoverage {
  if (!isRecord(value)
    || typeof value.is_realized !== "boolean"
    || !Array.isArray(value.failures)) {
    throw new TypeError("Type Find returned invalid context coverage.");
  }
  return {
    is_realized: value.is_realized,
    failures: value.failures,
  };
}

function parseMemberCoverage(value: unknown): TypeFindMemberCoverage {
  if (!isRecord(value) || typeof value.is_complete !== "boolean") {
    throw new TypeError("Type Find returned invalid member coverage.");
  }
  return { is_complete: value.is_complete };
}

function coverageNotice(
  content: TypeFindEvaluatedResult,
  diagnostics: readonly InspectionDiagnostic[],
): string {
  const incompleteMembers = content.members.filter(
    member => !member.is_complete,
  ).length;
  const incompleteContexts = content.contexts.filter(
    context => !context.is_realized || context.failures.length > 0,
  ).length;
  const incompleteAnswers = content.answers.filter(
    answer => !answer.is_complete,
  ).length;
  const messages: string[] = [];
  if (incompleteMembers > 0) {
    messages.push(
      `${incompleteMembers} assembly${incompleteMembers === 1 ? "" : "s"} `
        + "could not be fully evaluated.",
    );
  }
  if (incompleteContexts > 0) {
    messages.push(
      `${incompleteContexts} declaration context`
        + `${incompleteContexts === 1 ? "" : "s"} could not be fully realized.`,
    );
  }
  if (incompleteAnswers > 0
    && incompleteMembers === 0
    && incompleteContexts === 0) {
    messages.push(
      `${incompleteAnswers} Type search request`
        + `${incompleteAnswers === 1 ? " has" : "s have"} incomplete evidence.`,
    );
  }
  if (content.row_selection_failure !== undefined) {
    messages.push("Type result row selection could not be completed.");
  }
  if (content.visibility_failure !== undefined) {
    messages.push("Type visibility selection could not be completed.");
  }
  for (const diagnostic of diagnostics) {
    const summary = diagnosticsNotice([diagnostic]);
    if (summary) messages.push(summary);
  }
  return messages.join(" ");
}

function diagnosticsNotice(
  diagnostics: readonly InspectionDiagnostic[],
): string {
  return diagnostics
    .map(diagnostic => String(diagnostic.summary).trim())
    .filter(Boolean)
    .join(" ");
}

function optionalString(value: unknown): boolean {
  return value === undefined || typeof value === "string";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
