import type {
  BrowserIndexedTypeFindCandidateActivation,
  BrowserIndexedTypeFindResult,
} from "./facades/inspect-web-catalog.d.ts";
import {
  diagnosticsNotice,
  type ManagedSpotlightTypeCandidate,
} from "./spotlight-type-find.ts";

interface IndexedTypeName {
  readonly namespace: string;
  readonly segments: readonly string[];
}

interface IndexedAssemblyIdentity {
  readonly name: string;
}

interface IndexedPackageCoordinate {
  readonly kind: "package";
  readonly packageCoordinate: {
    readonly packageId: string;
    readonly version: string;
  };
  readonly libraryIdentity: {
    readonly identity: IndexedAssemblyIdentity;
  };
}

interface IndexedTypeMatch {
  readonly fullName: string;
  readonly typeName: string;
  readonly namespace: string;
  readonly declarationKind: string | number;
  readonly declaration: {
    readonly source: {
      readonly coordinate: IndexedPackageCoordinate;
    };
    readonly name: IndexedTypeName;
    readonly moduleVersionId: string;
  };
}

interface IndexedTypeContent {
  readonly matches: readonly IndexedTypeMatch[];
  readonly completion: string | number;
}

interface SpotlightIndexedTypeFindPosting {
  readonly retainedDefinitionId: string;
  readonly realizationId: string;
}

interface SpotlightIndexedTypeFindOptions {
  findTypes(
    retainedDefinitionId: string,
    realizationId: string,
    resultGeneration: number,
    text: string,
    maximumRows: number,
  ): Promise<BrowserIndexedTypeFindResult>;
  schedule(callback: () => void, delay: number): number;
  cancelScheduled(handle: number): void;
  updateResults(): void;
}

export interface SpotlightIndexedTypeFindCoordinator {
  schedule(
    posting: SpotlightIndexedTypeFindPosting | null,
    query: string,
    enabled: boolean,
  ): void;
  reset(): void;
  results(): readonly ManagedSpotlightTypeCandidate[];
  loading(): boolean;
  complete(): boolean;
  error(): string;
  notice(): string;
}

interface SpotlightIndexedTypeFindProjection {
  readonly candidates: readonly ManagedSpotlightTypeCandidate[];
  readonly notice: string;
}

const maximumRows = 50;

export function composeSpotlightTypeCandidates(
  indexed: readonly ManagedSpotlightTypeCandidate[],
  resident: readonly ManagedSpotlightTypeCandidate[],
): readonly ManagedSpotlightTypeCandidate[] {
  const candidates: ManagedSpotlightTypeCandidate[] = [];
  const identities = new Set<string>();
  for (const candidate of [...indexed, ...resident]) {
    if (identities.has(candidate.identity)) continue;
    identities.add(candidate.identity);
    candidates.push(candidate);
  }
  return candidates;
}

export function createSpotlightIndexedTypeFind(
  options: SpotlightIndexedTypeFindOptions,
): SpotlightIndexedTypeFindCoordinator {
  let generation = 0;
  let scheduled: number | null = null;
  let requestKey = "";
  let candidates: readonly ManagedSpotlightTypeCandidate[] = [];
  let isLoading = false;
  let phaseComplete = true;
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
    phaseComplete = true;
    failure = "";
    notice = "";
  }

  function schedule(
    posting: SpotlightIndexedTypeFindPosting | null,
    query: string,
    enabled: boolean,
  ): void {
    const text = query.trim();
    if (!enabled || posting === null || text.length === 0) {
      if (requestKey) reset();
      return;
    }

    const nextKey = JSON.stringify([
      posting.retainedDefinitionId,
      posting.realizationId,
      text,
      maximumRows,
    ]);
    if (nextKey === requestKey) return;

    requestKey = nextKey;
    const requestGeneration = ++generation;
    cancel();
    candidates = [];
    failure = "";
    notice = "";
    isLoading = true;
    phaseComplete = false;
    scheduled = options.schedule(() => {
      scheduled = null;
      void options.findTypes(
        posting.retainedDefinitionId,
        posting.realizationId,
        requestGeneration,
        text,
        maximumRows,
      ).then(result => {
        if (requestGeneration !== generation
          || requestKey !== nextKey) return undefined;
        if (result.status !== "Completed" || result.operation === null) {
          candidates = [];
          failure = result.reason
            ?? `Indexed Type Find returned '${String(result.status)}'.`;
        } else {
          const projection = projectSpotlightIndexedTypeFindResult(result);
          candidates = projection.candidates;
          notice = projection.notice;
          failure = "";
        }
        isLoading = false;
        phaseComplete = true;
        options.updateResults();
        return undefined;
      }).catch((error: unknown) => {
        if (requestGeneration !== generation
          || requestKey !== nextKey) return undefined;
        candidates = [];
        failure = error instanceof Error ? error.message : String(error);
        notice = "";
        isLoading = false;
        phaseComplete = true;
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
    complete: () => phaseComplete,
    error: () => failure,
    notice: () => notice,
  };
}

export function projectSpotlightIndexedTypeFindResult(
  result: BrowserIndexedTypeFindResult,
): SpotlightIndexedTypeFindProjection {
  if (result.status !== "Completed" || result.operation === null)
    return { candidates: [], notice: "" };

  const content = parseContent(result.operation.find.content);
  const activations = activationMap(result.operation.activations);
  const candidates = content.matches.map((match, index) => {
    const ordinal = index + 1;
    const activation = activations.get(ordinal);
    if (activation === undefined) {
      throw new TypeError(
        `Indexed Type Find omitted activation correspondence '${ordinal}'.`,
      );
    }
    activations.delete(ordinal);
    if (activation.identity === null) {
      throw new TypeError(
        `Indexed Type Find omitted stable identity '${ordinal}'.`,
      );
    }
    if (activation.status === "Available" && activation.action === null) {
      throw new TypeError(
        `Indexed Type Find marked activation '${ordinal}' available without an action.`,
      );
    }

    const coordinate = match.declaration.source.coordinate;
    return {
      identity: activation.identity,
      action: activation.status === "Available"
        ? activation.action
        : null,
      reason: activation.status === "Available"
        ? null
        : activation.reason
          ?? `Type activation is ${String(activation.status).toLowerCase()}.`,
      name: match.typeName,
      namespace: match.namespace,
      library: coordinate.libraryIdentity.identity.name,
      source: `${coordinate.packageCoordinate.packageId}@${coordinate.packageCoordinate.version}`,
      declarationKind: String(match.declarationKind),
    };
  });
  if (activations.size !== 0) {
    throw new TypeError(
      "Indexed Type Find returned activation correspondence without a match.",
    );
  }
  return {
    candidates,
    notice: [
      completionNotice(content.completion),
      diagnosticsNotice(result.operation.find.diagnostics),
    ].filter(Boolean).join(" "),
  };
}

function activationMap(
  activations: readonly BrowserIndexedTypeFindCandidateActivation[],
): Map<number, BrowserIndexedTypeFindCandidateActivation> {
  const mapped =
    new Map<number, BrowserIndexedTypeFindCandidateActivation>();
  for (const activation of activations) {
    if (!Number.isInteger(activation.matchOrdinal)
      || activation.matchOrdinal <= 0
      || mapped.has(activation.matchOrdinal)) {
      throw new TypeError(
        `Indexed Type Find returned invalid activation correspondence '${activation.matchOrdinal}'.`,
      );
    }
    mapped.set(activation.matchOrdinal, activation);
  }
  return mapped;
}

function completionNotice(completion: string | number): string {
  if (completion === "Exhausted" || completion === 0) return "";
  if (completion === "RowLimitReached" || completion === 1) {
    return "Indexed Type Find reached its result limit; later indexed matches may be omitted.";
  }
  if (completion === "Canceled" || completion === 2)
    return "Indexed Type Find was canceled before every source was evaluated.";
  return `Indexed Type Find ended with '${String(completion)}'.`;
}

function parseContent(value: unknown): IndexedTypeContent {
  if (!isRecord(value)
    || !Array.isArray(value.matches)
    || (typeof value.completion !== "string"
      && typeof value.completion !== "number")) {
    throw new TypeError("Indexed Type Find returned invalid content.");
  }
  return {
    matches: value.matches.map(parseMatch),
    completion: value.completion,
  };
}

function parseMatch(value: unknown): IndexedTypeMatch {
  if (!isRecord(value)
    || typeof value.fullName !== "string"
    || typeof value.typeName !== "string"
    || typeof value.namespace !== "string"
    || (typeof value.declarationKind !== "string"
      && typeof value.declarationKind !== "number")
    || !isRecord(value.declaration)
    || !isRecord(value.declaration.source)
    || !isRecord(value.declaration.source.coordinate)
    || value.declaration.source.coordinate.kind !== "package"
    || !isRecord(value.declaration.source.coordinate.packageCoordinate)
    || typeof value.declaration.source.coordinate.packageCoordinate.packageId
      !== "string"
    || typeof value.declaration.source.coordinate.packageCoordinate.version
      !== "string"
    || !isRecord(value.declaration.source.coordinate.libraryIdentity)
    || !isRecord(
      value.declaration.source.coordinate.libraryIdentity.identity,
    )
    || typeof value.declaration.source.coordinate.libraryIdentity.identity.name
      !== "string"
    || !isRecord(value.declaration.name)
    || typeof value.declaration.name.namespace !== "string"
    || !isStringArray(value.declaration.name.segments)
    || typeof value.declaration.moduleVersionId !== "string") {
    throw new TypeError("Indexed Type Find returned an invalid match.");
  }
  return {
    fullName: value.fullName,
    typeName: value.typeName,
    namespace: value.namespace,
    declarationKind: value.declarationKind,
    declaration: {
      source: {
        coordinate: {
          kind: "package",
          packageCoordinate: {
            packageId:
              value.declaration.source.coordinate.packageCoordinate.packageId,
            version:
              value.declaration.source.coordinate.packageCoordinate.version,
          },
          libraryIdentity: {
            identity: {
              name: value.declaration.source.coordinate.libraryIdentity
                .identity.name,
            },
          },
        },
      },
      name: {
        namespace: value.declaration.name.namespace,
        segments: value.declaration.name.segments,
      },
      moduleVersionId: value.declaration.moduleVersionId,
    },
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isStringArray(value: unknown): value is readonly string[] {
  return Array.isArray(value)
    && value.every(element => typeof element === "string");
}
