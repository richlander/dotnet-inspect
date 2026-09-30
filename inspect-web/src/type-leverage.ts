import type {
  BrowserLibraryNamespaceLeverage,
  BrowserLibrarySignatureUseCoverage,
  BrowserLibraryTypeLeverageRow,
  BrowserLibraryTypeLeverageShard,
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

interface NamespaceLeverageCue {
  readonly topLeverage: boolean;
  readonly externalIncomingSourceTypeCount: number;
  readonly description: string;
}

export interface TypeLeveragePresentation {
  readonly byType: ReadonlyMap<string, TypeLeverageCue>;
  readonly byNamespace: ReadonlyMap<string, NamespaceLeverageCue>;
  readonly seaLevelCount: number;
  readonly mountainPeakCount: number;
  readonly methodologyVersion: string;
  readonly evidenceMode: string;
  readonly disposition: string;
  readonly coverage: BrowserLibrarySignatureUseCoverage;
  readonly diagnostics: readonly string[];
  readonly loadedNamespaces: readonly string[];
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
  libraryKey(request: TRequest): string;
  queryIndex(request: TRequest): Promise<BrowserLibraryNamespaceLeverage>;
  selectNamespaces(
    request: TRequest,
    index: BrowserLibraryNamespaceLeverage,
  ): readonly string[];
  queryShard(
    request: TRequest,
    exactNamespace: string,
  ): Promise<BrowserLibraryTypeLeverageShard>;
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

function availableIndex(result: BrowserLibraryNamespaceLeverage): void {
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported namespace-leverage schema version.");
  if (result.outcome !== "available"
    || result.methodologyVersion === null
    || result.evidenceMode === null
    || result.disposition === null
    || result.coverage === null
    || result.failure !== null) {
    throw new Error(result.failure ?? "Namespace leverage is unavailable.");
  }
}

function availableShard(result: BrowserLibraryTypeLeverageShard): void {
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported Type-leverage shard schema version.");
  if (result.outcome !== "available"
    || result.methodologyVersion === null
    || result.evidenceMode === null
    || result.namespace === null
    || result.disposition === null
    || result.coverage === null
    || result.failure !== null) {
    throw new Error(result.failure ?? "Namespace Type leverage is unavailable.");
  }
}

function validateOrder(
  rows: ReadonlyMap<string, BrowserLibraryTypeLeverageRow>,
  order: readonly string[],
  designation: (row: BrowserLibraryTypeLeverageRow) => boolean,
  name: string,
): void {
  const seen = new Set<string>();
  for (const id of order) {
    if (seen.has(id))
      throw new Error(`${name} order repeats '${id}'.`);
    seen.add(id);
    const row = rows.get(id);
    if (!row || !row.designationEligible)
      throw new Error(`${name} order contains ineligible Type '${id}'.`);
  }
  for (const [id, row] of rows) {
    if (designation(row) && !seen.has(id))
      throw new Error(`${name} designation is missing from its order: '${id}'.`);
  }
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

function sumCoverage(
  values: readonly BrowserLibrarySignatureUseCoverage[],
): BrowserLibrarySignatureUseCoverage {
  return values.reduce(
    (sum, value) => ({
      considered: sum.considered + value.considered,
      examined: sum.examined + value.examined,
      unavailable: sum.unavailable + value.unavailable,
      limited: sum.limited + value.limited,
    }),
    { considered: 0, examined: 0, unavailable: 0, limited: 0 },
  );
}

export function projectTypeLeverage(
  index: BrowserLibraryNamespaceLeverage,
  shards: readonly BrowserLibraryTypeLeverageShard[],
): TypeLeveragePresentation {
  availableIndex(index);
  const methodologyVersion = index.methodologyVersion!;
  const evidenceMode = index.evidenceMode!;
  const byNamespace = new Map<string, NamespaceLeverageCue>();
  for (const row of index.namespaces) {
    if (byNamespace.has(row.namespace))
      throw new Error(`Duplicate namespace leverage row '${row.namespace}'.`);
    byNamespace.set(row.namespace, {
      topLeverage: row.topLeverage,
      externalIncomingSourceTypeCount:
        row.externalIncomingSourceTypeCount,
      description: row.topLeverage
        ? `${plural(row.externalIncomingSourceTypeCount, "external source Type")}; top-leverage namespace`
        : plural(row.externalIncomingSourceTypeCount, "external source Type"),
    });
  }

  const byType = new Map<string, TypeLeverageCue>();
  const seenTypeIds = new Set<string>();
  const loadedNamespaces = new Set<string>();
  const diagnostics = [...index.diagnostics];
  const coverages = [index.coverage!];
  let disposition = index.disposition!;
  let seaLevelCount = 0;
  let mountainPeakCount = 0;
  for (const shard of shards) {
    availableShard(shard);
    if (shard.methodologyVersion !== methodologyVersion
      || shard.evidenceMode !== evidenceMode) {
      throw new Error(
        `Type-leverage shard '${shard.namespace}' does not match its namespace index.`,
      );
    }
    if (!byNamespace.has(shard.namespace!))
      throw new Error(`Unknown Type-leverage namespace '${shard.namespace}'.`);
    if (loadedNamespaces.has(shard.namespace!))
      throw new Error(`Duplicate Type-leverage shard '${shard.namespace}'.`);
    loadedNamespaces.add(shard.namespace!);
    if (shard.disposition!.toLowerCase() !== "complete")
      disposition = shard.disposition!;
    coverages.push(shard.coverage!);
    diagnostics.push(...shard.diagnostics);

    const rows = new Map<string, BrowserLibraryTypeLeverageRow>();
    for (const row of shard.types) {
      if (rows.has(row.typeDefinitionId)
        || seenTypeIds.has(row.typeDefinitionId)) {
        throw new Error(
          `Duplicate Type leverage row '${row.typeDefinitionId}'.`,
        );
      }
      rows.set(row.typeDefinitionId, row);
      seenTypeIds.add(row.typeDefinitionId);
    }
    validateOrder(
      rows,
      shard.seaLevelOrder,
      row => row.seaLevel,
      "Sea-level",
    );
    validateOrder(
      rows,
      shard.mountainPeakOrder,
      row => row.mountainPeak,
      "Mountain-peak",
    );
    for (const [id, row] of rows) {
      if (!row.seaLevel && !row.mountainPeak) continue;
      const parts = [
        plural(row.signatureIncomingDegree, "incoming Type peer"),
        plural(row.signatureOutgoingDegree, "outgoing Type peer"),
      ];
      if (row.seaLevel) {
        parts.push("sea-level Type");
        seaLevelCount++;
      }
      if (row.mountainPeak) {
        parts.push("mountain-peak Type");
        mountainPeakCount++;
      }
      byType.set(id, {
        seaLevel: row.seaLevel,
        mountainPeak: row.mountainPeak,
        seaLevelStrength: row.seaLevel ? 1 : null,
        mountainPeakStrength: row.mountainPeak ? 1 : null,
        description: parts.join("; "),
      });
    }
  }

  return {
    byType,
    byNamespace,
    seaLevelCount,
    mountainPeakCount,
    methodologyVersion,
    evidenceMode,
    disposition,
    coverage: sumCoverage(coverages),
    diagnostics,
    loadedNamespaces: [...loadedNamespaces].sort(),
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
    readonly libraryKey: string;
    readonly cacheGeneration: number;
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

  const presentations = new Map<string, TypeLeveragePresentation>();
  const presentationLibraries = new Map<string, string>();
  const indexes = new Map<string, BrowserLibraryNamespaceLeverage>();
  const shards = new Map<string, BrowserLibraryTypeLeverageShard>();
  const cacheGenerations = new Map<string, number>();
  const inputs = new Map<OperationId, Input>();
  const shardKey = (libraryKey: string, exactNamespace: string) =>
    JSON.stringify([libraryKey, exactNamespace]);
  const cacheGeneration = (libraryKey: string) =>
    cacheGenerations.get(libraryKey) ?? 0;
  const ownsCacheGeneration = (input: Input) =>
    input.cacheGeneration === cacheGeneration(input.libraryKey);
  const inputFor = (operationId: OperationId) => {
    const input = inputs.get(operationId);
    if (input === undefined)
      throw new Error("Type leverage operation context is unavailable.");
    return input;
  };
  const publish = (event: FeatureEvent): undefined => {
    switch (event.kind) {
      case "started":
      case "replaced": {
        const input = inputFor(event.operation.id);
        if (dependencies.isCurrent(input.request))
          dependencies.publish({ status: "loading", key: input.key });
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
      const finish = (value: TypeLeveragePresentation): undefined => {
        if (ownsCacheGeneration(input)) {
          presentations.set(input.key, value);
          presentationLibraries.set(input.key, input.libraryKey);
        }
        sink.reportTerminal(
          ownsCacheGeneration(input) && dependencies.isCurrent(input.request)
          ? { kind: "succeeded", value }
          : { kind: "canceled", reason: "superseded" },
        );
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
            void (async () => {
              let index = indexes.get(input.libraryKey);
              if (index === undefined) {
                index = await dependencies.queryIndex(input.request);
                availableIndex(index);
                if (ownsCacheGeneration(input))
                  indexes.set(input.libraryKey, index);
              }
              const requested = [
                ...new Set(
                  dependencies.selectNamespaces(input.request, index),
                ),
              ].sort();
              const selectedShards = await Promise.all(
                requested.map(async exactNamespace => {
                  const key = shardKey(input.libraryKey, exactNamespace);
                  let shard = shards.get(key);
                  if (shard === undefined) {
                    shard = await dependencies.queryShard(
                      input.request,
                      exactNamespace,
                    );
                    availableShard(shard);
                    if (ownsCacheGeneration(input))
                      shards.set(key, shard);
                  }
                  return shard;
                }),
              );
              return projectTypeLeverage(index, selectedShards);
            })().then(finish, fail);
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
    const cached = presentations.get(key);
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
    const result = session.start({
      request: requestValue,
      key,
      libraryKey: dependencies.libraryKey(requestValue),
      cacheGeneration: cacheGeneration(
        dependencies.libraryKey(requestValue),
      ),
    }, adapter);
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
      const libraryKey = dependencies.libraryKey(requestValue);
      cacheGenerations.set(
        libraryKey,
        cacheGeneration(libraryKey) + 1,
      );
      for (const [key, cachedLibrary] of presentationLibraries) {
        if (cachedLibrary === libraryKey) {
          presentations.delete(key);
          presentationLibraries.delete(key);
        }
      }
      indexes.delete(libraryKey);
      for (const key of shards.keys()) {
        if (key.startsWith(`[${JSON.stringify(libraryKey)},`))
          shards.delete(key);
      }
      request(requestValue);
    },
    presentation(key) {
      return presentations.get(key) ?? null;
    },
  };
}
