import type {
  BrowserTypeFindCandidateActivation,
  BrowserTypeFindResult,
} from "./facades/inspect-web-metadata.d.ts";

interface TypeFindCandidateName {
  readonly namespace: string;
  readonly segments: readonly string[];
}

interface TypeFindLibraryIdentity {
  readonly name: string;
  readonly version: string | null;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
}

interface TypeFindCoordinate {
  readonly kind: string;
  readonly libraryIdentity: TypeFindLibraryIdentity;
}

interface TypeFindRealization {
  readonly kind: string;
  readonly packageId?: string;
  readonly version?: string;
  readonly producer?: string;
  readonly family?: string;
  readonly declaredName?: string;
}

interface TypeFindObservation {
  readonly contextOrder: number;
  readonly memberOrder: number;
  readonly realization: TypeFindRealization;
}

interface TypeFindCandidate {
  readonly wireIdentity: string;
  readonly coordinate: TypeFindCoordinate;
  readonly name: TypeFindCandidateName;
  readonly declarationKind: string | number;
  readonly moduleVersionId: string;
  readonly observation: TypeFindObservation;
}

interface TypeFindAnswer {
  readonly identity: { readonly ordinal: number };
  readonly candidates: readonly TypeFindCandidate[];
}

interface TypeFindEvaluatedResult {
  readonly kind: "evaluated";
  readonly answers: readonly TypeFindAnswer[];
}

export interface ManagedSpotlightTypeCandidate {
  readonly identity: string;
  readonly action: string | null;
  readonly reason: string | null;
  readonly name: string;
  readonly namespace: string;
  readonly library: string;
  readonly source: string;
  readonly declarationKind: string;
}

export interface SpotlightTypeFindPosting {
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
    isLoading = true;
    scheduled = options.schedule(() => {
      scheduled = null;
      void options.findTypes(
        posting.retainedDefinitionId,
        posting.realizationId,
        requestGeneration,
        text,
      ).then(
        result => {
          if (requestGeneration !== generation
            || requestKey !== nextKey) return undefined;
          if (result.status !== "Completed" || result.operation === null) {
            candidates = [];
            failure = text.length === 0
              ? ""
              : result.reason
                ?? `Type Find returned '${String(result.status)}'.`;
          } else {
            candidates = projectSpotlightTypeFindResult(result);
            failure = "";
          }
          isLoading = false;
          options.updateResults();
          return undefined;
        },
        (error: unknown) => {
          if (requestGeneration !== generation
            || requestKey !== nextKey) return undefined;
          candidates = [];
          failure = error instanceof Error
            ? error.message
            : String(error);
          isLoading = false;
          options.updateResults();
          return undefined;
        },
      );
    }, 75);
  }

  return {
    schedule,
    reset,
    results: () => candidates,
    loading: () => isLoading,
    error: () => failure,
  };
}

export function projectSpotlightTypeFindResult(
  result: BrowserTypeFindResult,
): readonly ManagedSpotlightTypeCandidate[] {
  if (result.status !== "Completed" || result.operation === null) return [];
  const content = parseEvaluatedResult(result.operation.find.content);
  const activations = activationMap(result.operation.activations);
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
        library: candidate.coordinate.libraryIdentity.name,
        source: realizationLabel(candidate.observation.realization),
        declarationKind: String(candidate.declarationKind),
      });
    });
  }

  if (activations.size !== 0) {
    throw new TypeError(
      "Type Find returned activation correspondence without a candidate.",
    );
  }
  return projected;
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
      return realization.packageId && realization.version
        ? `${realization.packageId}@${realization.version}`
        : realization.packageId ?? "Package";
    case "platform": {
      const product = realization.family?.toLowerCase().includes("aspnet")
        ? "ASP.NET Core"
        : ".NET";
      return realization.version
        ? `${product} ${realization.version}`
        : product;
    }
    case "embedded":
      return realization.declaredName ?? "Embedded";
    case "platform-reference":
      return ".NET";
    default:
      return realization.kind;
  }
}

function parseEvaluatedResult(value: unknown): TypeFindEvaluatedResult {
  if (!isRecord(value)
    || value.kind !== "evaluated"
    || !Array.isArray(value.answers)) {
    throw new TypeError("Type Find returned an invalid locator result.");
  }

  const answers = value.answers.map(parseAnswer);
  return { kind: "evaluated", answers };
}

function parseAnswer(value: unknown): TypeFindAnswer {
  if (!isRecord(value)
    || !isRecord(value.identity)
    || typeof value.identity.ordinal !== "number"
    || !Number.isInteger(value.identity.ordinal)
    || !Array.isArray(value.candidates)) {
    throw new TypeError("Type Find returned an invalid answer.");
  }
  return {
    identity: { ordinal: value.identity.ordinal },
    candidates: value.candidates.map(parseCandidate),
  };
}

function parseCandidate(value: unknown): TypeFindCandidate {
  if (!isRecord(value)
    || !isCoordinate(value.coordinate)
    || !isCandidateName(value.name)
    || (typeof value.declarationKind !== "string"
      && typeof value.declarationKind !== "number")
    || typeof value.moduleVersionId !== "string"
    || !isObservation(value.observation)) {
    throw new TypeError("Type Find returned an invalid candidate.");
  }
  return {
    wireIdentity: JSON.stringify(value),
    coordinate: value.coordinate,
    name: value.name,
    declarationKind: value.declarationKind,
    moduleVersionId: value.moduleVersionId,
    observation: value.observation,
  };
}

function isCoordinate(value: unknown): value is TypeFindCoordinate {
  return isRecord(value)
    && typeof value.kind === "string"
    && isRecord(value.libraryIdentity)
    && typeof value.libraryIdentity.name === "string"
    && (typeof value.libraryIdentity.version === "string"
      || value.libraryIdentity.version === null)
    && (typeof value.libraryIdentity.culture === "string"
      || value.libraryIdentity.culture === null)
    && (typeof value.libraryIdentity.publicKeyToken === "string"
      || value.libraryIdentity.publicKeyToken === null);
}

function isCandidateName(value: unknown): value is TypeFindCandidateName {
  return isRecord(value)
    && typeof value.namespace === "string"
    && Array.isArray(value.segments)
    && value.segments.every(segment => typeof segment === "string");
}

function isObservation(value: unknown): value is TypeFindObservation {
  return isRecord(value)
    && Number.isInteger(value.contextOrder)
    && Number.isInteger(value.memberOrder)
    && isRealization(value.realization);
}

function isRealization(value: unknown): value is TypeFindRealization {
  return isRecord(value)
    && typeof value.kind === "string"
    && optionalString(value.packageId)
    && optionalString(value.version)
    && optionalString(value.producer)
    && optionalString(value.family)
    && optionalString(value.declaredName);
}

function optionalString(value: unknown): boolean {
  return value === undefined || typeof value === "string";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
