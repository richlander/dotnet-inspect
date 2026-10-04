import type {
  BrowserTypeMethodLeverage,
  BrowserTypeMethodLeverageRank,
} from "./facades/inspect-web-analysis.d.ts";
import {
  createKeyedResultCache,
  type KeyedResultCache,
} from "./implementation-profiles.ts";
import type {
  OperationAuthorityPage,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

export interface PackageTypeMethodLeverageRequest {
  readonly kind: "package";
  readonly workspaceGeneration: string;
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
  readonly assemblyName: string;
  readonly typeDefinitionId: string;
}

export interface PlatformTypeMethodLeverageRequest {
  readonly kind: "platform";
  readonly workspaceGeneration: string;
  readonly targetFramework: string;
  readonly platformVersion: string;
  readonly pack: string;
  readonly assemblyFileName: string;
  readonly typeDefinitionId: string;
}

export type TypeMethodLeverageRequest =
  | PackageTypeMethodLeverageRequest
  | PlatformTypeMethodLeverageRequest;

export interface MethodLeverageCue {
  readonly description: string;
}

export interface TypeMethodLeveragePresentation {
  readonly byStableSelector: ReadonlyMap<string, MethodLeverageCue>;
  readonly methodCount: number;
  readonly winnerCount: number;
  readonly anchoredWinnerCount: number;
  readonly anchoredMethodCount: number;
  readonly winningRank: BrowserTypeMethodLeverageRank | null;
}

export type TypeMethodLeverageState =
  | { readonly status: "idle" }
  | {
      readonly status: "loading";
      readonly request: TypeMethodLeverageRequest;
      readonly isCurrent: () => boolean;
    }
  | {
      readonly status: "ready";
      readonly request: TypeMethodLeverageRequest;
      readonly isCurrent: () => boolean;
      readonly presentation: TypeMethodLeveragePresentation;
    }
  | {
      readonly status: "failed";
      readonly request: TypeMethodLeverageRequest;
      readonly isCurrent: () => boolean;
      readonly outcome:
        | "rejected"
        | "failed"
        | "unavailable"
        | "producer-failed";
      readonly message: string;
      readonly diagnostics?: ReadonlyArray<string>;
    };

export interface TypeMethodLeverageStateHost {
  typeMethodLeverage: TypeMethodLeverageState;
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

function leverageDescription(
  rank: BrowserTypeMethodLeverageRank,
): string {
  return [
    "Top Leverage",
    plural(rank.directCallerCount, "direct caller"),
    plural(rank.rootReach, "root"),
    `fanout ${rank.fanout}`,
    plural(rank.loopCallCount, "loop call"),
  ].join("; ");
}

export function typeMethodLeverageCacheKey(
  request: TypeMethodLeverageRequest,
): string {
  return request.kind === "package"
    ? JSON.stringify([
        "type-method-leverage",
        request.workspaceGeneration,
        "package",
        request.packageId,
        request.version,
        request.targetFramework,
        request.assemblyName,
        request.typeDefinitionId,
      ])
    : JSON.stringify([
        "type-method-leverage",
        request.workspaceGeneration,
        "platform",
        request.targetFramework,
        request.platformVersion,
        request.pack,
        request.assemblyFileName,
        request.typeDefinitionId,
      ]);
}

function validateAvailable(
  result: BrowserTypeMethodLeverage,
  request: TypeMethodLeverageRequest,
): void {
  const content = result.content;
  if (result.subject === null
    || content === null
    || result.failure !== null
    || result.share === null) {
    throw new Error("Available Type method leverage is incomplete.");
  }
  if (content.typeDefinitionId !== request.typeDefinitionId)
    throw new Error("Type method leverage names another Type.");
  if (content.methodCount < 0
    || content.winnerCount < 0
    || content.winnerCount > content.methodCount
    || content.anchoredWinners.length > content.winnerCount) {
    throw new Error("Type method-leverage counts are inconsistent.");
  }
  if ((content.winnerCount === 0) !== (content.winningRank === null)) {
    throw new Error("Type method-leverage maximum is inconsistent.");
  }
  if (content.winningRank !== null
    && content.winningRank.directCallerCount <= 0) {
    throw new Error("Type method leverage has an unqualified maximum.");
  }

  const selectors = new Set<string>();
  const methodTokens = new Set<number>();
  for (const winner of content.anchoredWinners) {
    if (winner.typeDefinitionId !== request.typeDefinitionId
      || winner.stableSelector.length === 0
      || winner.methodTokens.length === 0
      || selectors.has(winner.stableSelector)) {
      throw new Error("Type method-leverage winner identity is inconsistent.");
    }
    selectors.add(winner.stableSelector);
    for (const token of winner.methodTokens) {
      if (token <= 0 || methodTokens.has(token))
        throw new Error("Type method-leverage winner token is inconsistent.");
      methodTokens.add(token);
    }
  }
  if (methodTokens.size > content.winnerCount) {
    throw new Error("Type method leverage anchors more methods than won.");
  }
}

function validateTypeMethodLeverage(
  result: BrowserTypeMethodLeverage,
  request: TypeMethodLeverageRequest,
): void {
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported Type method-leverage schema version.");
  switch (result.outcome) {
    case "available":
      validateAvailable(result, request);
      return;
    case "rejected":
    case "failed":
      if (result.content !== null || result.failure === null)
        throw new Error(`Type method-leverage ${result.outcome} result is incomplete.`);
      return;
    case "unavailable":
      if (result.subject !== null
        || result.content !== null
        || result.failure === null) {
        throw new Error("Unavailable Type method leverage is inconsistent.");
      }
      return;
    default:
      throw new Error(`Unknown Type method-leverage outcome: ${result.outcome}.`);
  }
}

export function projectTypeMethodLeverage(
  result: BrowserTypeMethodLeverage,
  request: TypeMethodLeverageRequest,
): TypeMethodLeveragePresentation {
  validateTypeMethodLeverage(result, request);
  if (result.outcome !== "available"
    || result.content === null) {
    throw new Error(
      result.failure?.detail ?? "Type method leverage is unavailable.",
    );
  }

  const rank = result.content.winningRank;
  const description =
    rank === null ? "" : leverageDescription(rank);
  return {
    byStableSelector: new Map(
      result.content.anchoredWinners.map(winner => [
        winner.stableSelector,
        { description },
      ]),
    ),
    methodCount: result.content.methodCount,
    winnerCount: result.content.winnerCount,
    anchoredWinnerCount: result.content.anchoredWinners.length,
    anchoredMethodCount: result.content.anchoredWinners.reduce(
      (count, winner) => count + winner.methodTokens.length,
      0),
    winningRank: rank,
  };
}

export function methodLeverageEmptyStateMessage(
  state: TypeMethodLeverageState,
): string {
  if (state.status === "ready") {
    if (state.presentation.winnerCount === 0) {
      return "This Type has no inbound-call Top Leverage designation.";
    }
    return state.presentation.anchoredMethodCount === 0
      ? "The true Top Leverage winner has no browsable member row."
      : "No Top Leverage member matches the current filters.";
  }
  return state.status === "failed"
    ? "Top Leverage is unavailable until retry."
    : "Loading Top Leverage…";
}

function projectState(
  result: BrowserTypeMethodLeverage,
  request: TypeMethodLeverageRequest,
  isCurrent: () => boolean,
): TypeMethodLeverageState {
  if (result.outcome !== "available" || result.content === null) {
    return {
      status: "failed",
      request,
      isCurrent,
      outcome: result.outcome === "rejected"
        || result.outcome === "failed"
        || result.outcome === "unavailable"
        ? result.outcome
        : "failed",
      message: result.failure?.detail
        ?? "Type method leverage is unavailable.",
      diagnostics: result.diagnostics.map(diagnostic =>
        `${diagnostic.code}: ${diagnostic.summary}`),
    };
  }
  return {
    status: "ready",
    request,
    isCurrent,
    presentation: projectTypeMethodLeverage(result, request),
  };
}

export function methodLeverageFor(
  state: TypeMethodLeverageState,
  stableSelector: string | null | undefined,
): MethodLeverageCue | null {
  return state.status === "ready" && stableSelector
    ? state.presentation.byStableSelector.get(stableSelector) ?? null
    : null;
}

interface MethodLeverageFilterGroup {
  readonly overloads: readonly {
    readonly stableSelector?: string | null;
  }[];
}

type MethodLeverageFilteredGroup<TGroup extends MethodLeverageFilterGroup> =
  Omit<TGroup, "overloads"> & {
    overloads: Array<TGroup["overloads"][number]>;
  };

export function filterMemberGroupsByMethodLeverage<
  TGroup extends MethodLeverageFilterGroup,
>(
  groups: readonly TGroup[],
  state: TypeMethodLeverageState,
): Array<MethodLeverageFilteredGroup<TGroup>> {
  if (state.status !== "ready") {
    return groups.map(group => ({
      ...group,
      overloads: [...group.overloads],
    }));
  }
  return groups.flatMap(group => {
    const overloads = group.overloads.filter(overload =>
      methodLeverageFor(state, overload.stableSelector) !== null);
    return overloads.length === 0
      ? []
      : [{ ...group, overloads }];
  });
}

export interface TypeMethodLeverageCoordinatorDependencies {
  readonly state: TypeMethodLeverageStateHost;
  readonly operationAuthority: OperationAuthorityPage;
  readonly cache?: KeyedResultCache<
    TypeMethodLeverageRequest,
    BrowserTypeMethodLeverage
  >;
  query(
    request: TypeMethodLeverageRequest,
  ): Promise<BrowserTypeMethodLeverage>;
  whenWorkerIdle(): Promise<void>;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface TypeMethodLeverageCoordinator {
  request(
    request: TypeMethodLeverageRequest,
    isCurrent: () => boolean,
  ): void;
  retry(
    request: TypeMethodLeverageRequest,
    isCurrent: () => boolean,
  ): void;
}

interface TypeMethodLeverageInput {
  readonly request: TypeMethodLeverageRequest;
  readonly isCurrent: () => boolean;
}

export function createTypeMethodLeverageCoordinator(
  dependencies: TypeMethodLeverageCoordinatorDependencies,
): TypeMethodLeverageCoordinator {
  type FeatureEvent = OperationFeatureEvent<
    TypeMethodLeverageState,
    unknown,
    never
  >;
  type Session = OperationSession<
    TypeMethodLeverageInput,
    TypeMethodLeverageState,
    unknown,
    never,
    never
  >;

  const cache = dependencies.cache
    ?? createKeyedResultCache(
      typeMethodLeverageCacheKey,
      validateTypeMethodLeverage,
    );
  const inputs = new Map<OperationId, TypeMethodLeverageInput>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined) {
      throw new Error(
        "Type method-leverage operation context is unavailable.",
      );
    }
    return input;
  };
  const releaseLoading = (input: TypeMethodLeverageInput) => {
    const published = dependencies.state.typeMethodLeverage;
    if (published.status === "loading"
      && published.request === input.request) {
      dependencies.state.typeMethodLeverage = { status: "idle" };
    }
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const input = inputFor(event.operation.id);
        if (!input.isCurrent()) break;
        dependencies.state.typeMethodLeverage = {
          status: "loading",
          request: input.request,
          isCurrent: input.isCurrent,
        };
        dependencies.render();
        break;
      }
      case "terminal": {
        const input = inputFor(event.operationId);
        if (!input.isCurrent()) {
          releaseLoading(input);
          break;
        }
        dependencies.state.typeMethodLeverage =
          event.outcome.kind === "succeeded"
            ? event.outcome.value
            : {
                status: "failed",
                request: input.request,
                isCurrent: input.isCurrent,
                outcome: "producer-failed",
                message: dependencies.describeError(event.outcome.error),
              };
        dependencies.render();
        break;
      }
      case "canceled":
      case "disposed":
      case "progress":
        break;
    }
    return undefined;
  };
  const session: Session = dependencies.operationAuthority.createSession({
    feature: { publish },
    diagnostic: {
      report: diagnostic =>
        dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    TypeMethodLeverageInput,
    TypeMethodLeverageState,
    unknown,
    never,
    never
  > = {
    prepare: (identity, input, sink) => {
      inputs.set(identity.id, input);
      let quiesced = false;
      const quiesce = (): undefined => {
        if (quiesced) return undefined;
        quiesced = true;
        inputs.delete(identity.id);
        sink.reportQuiesced();
        return undefined;
      };
      const finish = (
        result: BrowserTypeMethodLeverage,
      ): undefined => {
        if (!input.isCurrent()) {
          releaseLoading(input);
          sink.reportTerminal({
            kind: "canceled",
            reason: "superseded",
          });
          return quiesce();
        }
        try {
          sink.reportTerminal({
            kind: "succeeded",
            value: projectState(
              result,
              input.request,
              input.isCurrent,
            ),
          });
        } catch (error: unknown) {
          sink.reportUnexpectedTerminal(error, error);
        }
        return quiesce();
      };
      const fail = (error: unknown): undefined => {
        if (!input.isCurrent()) {
          releaseLoading(input);
          sink.reportTerminal({
            kind: "canceled",
            reason: "superseded",
          });
          return quiesce();
        }
        sink.reportUnexpectedTerminal(error, error);
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: () => undefined,
          activate: () => {
            let result: Promise<BrowserTypeMethodLeverage>;
            try {
              result = cache.load(
                input.request,
                request => dependencies.query(request),
              );
            } catch (error: unknown) {
              return fail(error);
            }
            void result.then(finish, fail);
            return undefined;
          },
          abandon: () => {
            inputs.delete(identity.id);
            return undefined;
          },
        },
      };
    },
  };

  const start = (input: TypeMethodLeverageInput): Promise<void> => {
    const result = session.start(input, adapter);
    if (result.kind === "rejected") {
      if (input.isCurrent()) {
        dependencies.state.typeMethodLeverage = {
          status: "failed",
          request: input.request,
          isCurrent: input.isCurrent,
          outcome: "producer-failed",
          message:
            "Type method leverage could not start: "
            + `${result.reason.kind}.`,
        };
        dependencies.render();
      }
      return Promise.resolve();
    }
    return result.handle.quiesced;
  };

  let running = false;
  let queued: TypeMethodLeverageInput | null = null;
  const pump = async (): Promise<void> => {
    if (running) return;
    const next = queued;
    if (next === null) return;
    queued = null;
    if (!next.isCurrent()) {
      releaseLoading(next);
      return pump();
    }
    running = true;
    try {
      try {
        await dependencies.whenWorkerIdle();
      } catch {
        // Idle tracking failure must not strand the request.
      }
      if (next.isCurrent()) await start(next);
      else releaseLoading(next);
    } finally {
      running = false;
    }
    return pump();
  };

  const requestLeverage = (
    request: TypeMethodLeverageRequest,
    isCurrent: () => boolean,
  ) => {
    const input = { request, isCurrent };
    if (cache.status(request) !== "missing") {
      void start(input);
      return;
    }
    if (queued !== null) releaseLoading(queued);
    queued = input;
    if (isCurrent()) {
      dependencies.state.typeMethodLeverage = {
        status: "loading",
        request,
        isCurrent,
      };
      dependencies.render();
    }
    void pump();
  };

  return {
    request: requestLeverage,
    retry(request, isCurrent) {
      cache.retry(request, result => result.outcome !== "available");
      requestLeverage(request, isCurrent);
    },
  };
}
