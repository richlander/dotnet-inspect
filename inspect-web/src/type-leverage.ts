import type {
  BrowserLibraryNamespaceLeverageIndex,
  BrowserLibraryNamespaceLeverageRow,
  BrowserLibrarySignatureUseCoverage,
  BrowserLibraryStructuralSalience,
  BrowserLibraryTypeLeverageRow,
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
export type TypeLeveragePole = Exclude<TypeLeverageFilter, "">;

export interface TypeLeverageCue {
  readonly pole: TypeLeveragePole;
  readonly description: string;
}

interface NamespaceLeverageCue {
  readonly topLeverage: boolean;
  readonly externalIncomingSourceTypeCount: number;
  readonly description: string;
}

export interface TypeLeverageShardPresentation {
  readonly namespace: string;
  readonly seaLevelOrder: readonly BrowserLibraryTypeLeverageRow[];
  readonly mountainPeakOrder: readonly BrowserLibraryTypeLeverageRow[];
}

export interface TypeLeveragePresentation {
  readonly byType: ReadonlyMap<string, TypeLeverageCue>;
  readonly byNamespace: ReadonlyMap<string, NamespaceLeverageCue>;
  readonly namespaceOrder: readonly BrowserLibraryNamespaceLeverageRow[];
  readonly shardsByNamespace:
    ReadonlyMap<string, TypeLeverageShardPresentation>;
  readonly seaLevelCount: number;
  readonly mountainPeakCount: number;
  readonly methodologyVersion: string;
  readonly evidenceMode: string;
  readonly disposition: string;
  readonly coverage: BrowserLibrarySignatureUseCoverage;
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
  libraryKey(request: TRequest): string;
  queryDocument(request: TRequest): Promise<BrowserLibraryStructuralSalience>;
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

function availableDocument(
  result: BrowserLibraryStructuralSalience,
): asserts result is BrowserLibraryStructuralSalience & {
  readonly methodologyVersion: string;
  readonly evidenceMode: string;
  readonly namespaceIndex: BrowserLibraryNamespaceLeverageIndex;
} {
  if (result.schemaVersion !== 1)
    throw new Error("Unsupported structural-salience schema version.");
  if (result.outcome !== "available"
    || result.methodologyVersion === null
    || result.evidenceMode === null
    || result.namespaceIndex === null
    || result.failure !== null) {
    throw new Error(result.failure ?? "Structural salience is unavailable.");
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

export function typeLeveragePole(
  value: BrowserLibraryTypeLeverageRow["pole"],
): TypeLeveragePole | null {
  switch (value) {
    case null:
      return null;
    case "SeaLevel":
      return "sea-level";
    case "MountainPeak":
      return "mountain-peak";
    default:
      throw new Error(`Unknown structural Type pole '${value}'.`);
  }
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
  document: BrowserLibraryStructuralSalience,
): TypeLeveragePresentation {
  availableDocument(document);
  const methodologyVersion = document.methodologyVersion;
  const evidenceMode = document.evidenceMode;
  const index = document.namespaceIndex;
  const shards = document.typeLeverageShards;
  if (shards.length !== index.namespaces.length)
    throw new Error("Structural salience does not cover every namespace.");
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
  const shardsByNamespace =
    new Map<string, TypeLeverageShardPresentation>();
  const seenTypeIds = new Set<string>();
  const diagnostics = [...index.diagnostics];
  const coverages = [index.coverage];
  let disposition = index.disposition;
  let seaLevelCount = 0;
  let mountainPeakCount = 0;
  for (let indexPosition = 0; indexPosition < shards.length; indexPosition++) {
    const shard = shards[indexPosition]!;
    const expectedNamespace = index.namespaces[indexPosition]!.namespace;
    if (shard.namespace !== expectedNamespace) {
      throw new Error(
        `Expected Type-leverage shard '${expectedNamespace}' at index ${indexPosition}; received '${shard.namespace}'.`,
      );
    }
    if (!byNamespace.has(shard.namespace))
      throw new Error(`Unknown Type-leverage namespace '${shard.namespace}'.`);
    if (shard.disposition.toLowerCase() !== "complete")
      disposition = shard.disposition;
    coverages.push(shard.coverage);
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
      row => row.pole === "SeaLevel",
      "Sea-level",
    );
    validateOrder(
      rows,
      shard.mountainPeakOrder,
      row => row.pole === "MountainPeak",
      "Mountain-peak",
    );
    shardsByNamespace.set(shard.namespace, {
      namespace: shard.namespace,
      seaLevelOrder: shard.seaLevelOrder.map(id => rows.get(id)!),
      mountainPeakOrder: shard.mountainPeakOrder.map(id => rows.get(id)!),
    });
    for (const [id, row] of rows) {
      const pole = typeLeveragePole(row.pole);
      if (pole === null) continue;
      const parts = [
        plural(row.signatureIncomingDegree, "incoming Type peer"),
        plural(row.signatureOutgoingDegree, "outgoing Type peer"),
        pole === "sea-level" ? "sea-level Type" : "mountain-peak Type",
      ];
      if (pole === "sea-level")
        seaLevelCount++;
      else
        mountainPeakCount++;
      byType.set(id, {
        pole,
        description: parts.join("; "),
      });
    }
  }

  return {
    byType,
    byNamespace,
    namespaceOrder: [...index.namespaces],
    shardsByNamespace,
    seaLevelCount,
    mountainPeakCount,
    methodologyVersion,
    evidenceMode,
    disposition,
    coverage: sumCoverage(coverages),
    diagnostics,
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
      return cue?.pole === "sea-level";
    case "mountain-peak":
      return cue?.pole === "mountain-peak";
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
  const documents = new Map<string, BrowserLibraryStructuralSalience>();
  const cacheGenerations = new Map<string, number>();
  const inputs = new Map<OperationId, Input>();
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
              let document = documents.get(input.libraryKey);
              if (document === undefined) {
                document = await dependencies.queryDocument(input.request);
                availableDocument(document);
                if (ownsCacheGeneration(input))
                  documents.set(input.libraryKey, document);
              }
              return projectTypeLeverage(document);
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
      documents.delete(libraryKey);
      request(requestValue);
    },
    presentation(key) {
      return presentations.get(key) ?? null;
    },
  };
}
