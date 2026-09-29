import type {
  BrowserLibrarySurfaceLeverage,
  BrowserLibrarySurfaceLeverageCoverage,
  BrowserLibrarySurfaceLeverageType,
} from "./facades/inspect-web-analysis.d.ts";
import type {
  OperationAuthorityPage,
  OperationDiagnostic,
  OperationFeatureEvent,
  OperationId,
  OperationProducerAdapter,
  OperationSession,
} from "./operation-authority.ts";

export type TypeLeverageFilter = "" | "sea-level" | "mountain-peak";

export interface TypeLeverageCue {
  readonly seaLevel: boolean;
  readonly mountainPeak: boolean;
  readonly seaLevelStrength: number | null;
  readonly mountainPeakStrength: number | null;
  readonly description: string;
}

export interface TypeLeveragePresentation {
  readonly byType: ReadonlyMap<string, TypeLeverageCue>;
  readonly seaLevelCount: number;
  readonly mountainPeakCount: number;
  readonly methodologyVersion: string;
  readonly disposition: string;
  readonly coverage: BrowserLibrarySurfaceLeverageCoverage;
  readonly diagnostics: readonly string[];
}

export type TypeLeverageLoadState =
  | {
      readonly status: "loading";
      readonly key: string;
    }
  | {
      readonly status: "ready";
      readonly key: string;
      readonly presentation: TypeLeveragePresentation;
    }
  | {
      readonly status: "failed";
      readonly key: string;
      readonly message: string;
    };

export interface TypeLeverageCoordinatorDependencies<TRequest> {
  readonly operationAuthority: OperationAuthorityPage;
  key(request: TRequest): string;
  query(request: TRequest): Promise<BrowserLibrarySurfaceLeverage>;
  isCurrent(request: TRequest): boolean;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  publish(state: TypeLeverageLoadState): void;
}

export interface TypeLeverageCoordinator<TRequest> {
  request(request: TRequest): void;
  retry(request: TRequest): void;
  presentation(key: string): TypeLeveragePresentation | null;
}

const categoryThreshold = 0.5;

function category(
  rows: ReadonlyMap<string, BrowserLibrarySurfaceLeverageType>,
  order: readonly string[],
  degree: (row: BrowserLibrarySurfaceLeverageType) => number,
): ReadonlyMap<string, number> {
  const seen = new Set<string>();
  let maximum = 0;
  for (const id of order) {
    if (seen.has(id))
      throw new Error(`Type leverage order repeats '${id}'.`);
    seen.add(id);
    const row = rows.get(id);
    if (!row || !row.rankingEligible)
      throw new Error(`Type leverage order contains ineligible Type '${id}'.`);
  }
  for (const row of rows.values())
    maximum = Math.max(maximum, degree(row));
  const categoryDegrees = new Map<string, number>();
  if (maximum === 0) return categoryDegrees;
  for (const [id, row] of rows) {
    const value = degree(row);
    if (value >= maximum * categoryThreshold) {
      categoryDegrees.set(id, Math.sqrt(value / maximum));
    }
  }
  return categoryDegrees;
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

export function projectTypeLeverage(
  result: BrowserLibrarySurfaceLeverage,
): TypeLeveragePresentation {
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported Library surface-leverage schema version.");
  if (result.outcome !== "available"
    || result.methodologyVersion === null
    || result.disposition === null
    || result.coverage === null
    || result.failure !== null) {
    throw new Error(
      result.failure ?? "Library surface leverage is unavailable.",
    );
  }

  const rows = new Map<string, BrowserLibrarySurfaceLeverageType>();
  for (const row of result.types) {
    if (rows.has(row.typeDefinitionId))
      throw new Error(`Duplicate Type leverage row '${row.typeDefinitionId}'.`);
    rows.set(row.typeDefinitionId, row);
  }
  const seaLevel = category(
    rows,
    result.seaLevelOrder,
    row => row.signatureIncomingDegree,
  );
  const mountainPeak = category(
    rows,
    result.mountainPeakOrder,
    row => row.signatureOutgoingDegree,
  );
  const byType = new Map<string, TypeLeverageCue>();
  for (const [id, row] of rows) {
    const seaLevelStrength = seaLevel.get(id) ?? null;
    const mountainPeakStrength = mountainPeak.get(id) ?? null;
    if (seaLevelStrength === null && mountainPeakStrength === null) continue;
    const parts = [
      plural(row.signatureIncomingDegree, "incoming Type peer"),
      plural(row.signatureOutgoingDegree, "outgoing Type peer"),
    ];
    if (seaLevelStrength !== null) parts.push("sea-level Type");
    if (mountainPeakStrength !== null) parts.push("mountain-peak Type");
    byType.set(id, {
      seaLevel: seaLevelStrength !== null,
      mountainPeak: mountainPeakStrength !== null,
      seaLevelStrength,
      mountainPeakStrength,
      description: parts.join("; "),
    });
  }

  return {
    byType,
    seaLevelCount: seaLevel.size,
    mountainPeakCount: mountainPeak.size,
    methodologyVersion: result.methodologyVersion,
    disposition: result.disposition,
    coverage: result.coverage,
    diagnostics: result.diagnostics,
  };
}

export function typeLeverageMatchesFilter(
  cue: TypeLeverageCue | undefined,
  filter: TypeLeverageFilter,
): boolean {
  switch (filter) {
    case "":
      return true;
    case "sea-level":
      return cue?.seaLevel === true;
    case "mountain-peak":
      return cue?.mountainPeak === true;
  }
  return false;
}

export function createTypeLeverageCoordinator<TRequest>(
  dependencies: TypeLeverageCoordinatorDependencies<TRequest>,
): TypeLeverageCoordinator<TRequest> {
  interface Input {
    readonly request: TRequest;
    readonly key: string;
  }
  type FeatureEvent = OperationFeatureEvent<
    TypeLeveragePresentation,
    unknown,
    never
  >;
  type Session = OperationSession<
    Input,
    TypeLeveragePresentation,
    unknown,
    never,
    never
  >;

  const cache = new Map<string, TypeLeveragePresentation>();
  const inputs = new Map<OperationId, Input>();
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined) {
      throw new Error("Type leverage operation context is unavailable.");
    }
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const input = inputFor(event.operation.id);
        if (dependencies.isCurrent(input.request)) {
          dependencies.publish({ status: "loading", key: input.key });
        }
        break;
      }
      case "terminal": {
        const input = inputFor(event.operationId);
        if (!dependencies.isCurrent(input.request)) break;
        if (event.outcome.kind === "succeeded") {
          dependencies.publish({
            status: "ready",
            key: input.key,
            presentation: event.outcome.value,
          });
        } else {
          dependencies.publish({
            status: "failed",
            key: input.key,
            message: dependencies.describeError(event.outcome.error),
          });
        }
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
    Input,
    TypeLeveragePresentation,
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
        result: BrowserLibrarySurfaceLeverage,
      ): undefined => {
        try {
          const presentation = projectTypeLeverage(result);
          cache.set(input.key, presentation);
          sink.reportTerminal(dependencies.isCurrent(input.request)
            ? { kind: "succeeded", value: presentation }
            : { kind: "canceled", reason: "superseded" });
        } catch (error: unknown) {
          if (dependencies.isCurrent(input.request)) {
            sink.reportUnexpectedTerminal(error, error);
          } else {
            sink.reportTerminal({
              kind: "canceled",
              reason: "superseded",
            });
          }
        }
        return quiesce();
      };
      const fail = (error: unknown): undefined => {
        if (dependencies.isCurrent(input.request)) {
          sink.reportUnexpectedTerminal(error, error);
        } else {
          sink.reportTerminal({
            kind: "canceled",
            reason: "superseded",
          });
        }
        return quiesce();
      };
      return {
        kind: "prepared",
        binding: {
          requestCancellation: () => undefined,
          activate: () => {
            void dependencies.query(input.request).then(finish, fail);
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

  const request = (requestValue: TRequest): void => {
    const key = dependencies.key(requestValue);
    const cached = cache.get(key);
    if (cached !== undefined) {
      if (dependencies.isCurrent(requestValue)) {
        dependencies.publish({
          status: "ready",
          key,
          presentation: cached,
        });
      }
      return;
    }
    const result = session.start({ request: requestValue, key }, adapter);
    if (result.kind === "rejected"
        && dependencies.isCurrent(requestValue)) {
      dependencies.publish({
        status: "failed",
        key,
        message: `Type leverage could not start: ${result.reason.kind}.`,
      });
    }
  };

  return {
    request,
    retry(requestValue) {
      cache.delete(dependencies.key(requestValue));
      request(requestValue);
    },
    presentation(key) {
      return cache.get(key) ?? null;
    },
  };
}
