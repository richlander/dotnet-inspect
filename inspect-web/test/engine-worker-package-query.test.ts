import assert from "node:assert/strict";
import test from "node:test";

import {
  bindPackageQueryFacade,
  createSharedEngineOperationAuthority,
  registerEngineWorkerPackageQueryAdapter,
  type EngineWorkerPackageQueryAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerPackageQueryCancellationIsRunning,
  engineWorkerPackageQueryCompletionEvent,
  engineWorkerPackageQueryDurableEvent,
  engineWorkerPackageQueryInput,
  engineWorkerPackageQueryTerminal,
  mapEngineWorkerPackageQueryCredit,
  mapEngineWorkerPackageQueryResult,
  registerEngineWorkerPackageQueryOperation,
  type EngineWorkerPackageQueryCompletionEvent,
  type EngineWorkerPackageQueryDurableEvent,
  type EngineWorkerPackageQueryFacade,
  type EngineWorkerPackageQueryTerminal,
  type EngineWorkerPackageQueryTerminalFailure,
} from "../src/engine-worker-package-query.ts";
import type {
  BrowserPackageQueryEvent,
  BrowserPackageQueryResult,
} from "../src/facades/inspect-web-package.d.ts";
import {
  createOperationAuthorityPage,
  type OperationFeatureEvent,
  type OperationHandle,
  type OperationSession,
} from "../src/operation-authority.ts";
import {
  createQueryRequest,
  withLibraryLiteralDraft,
  type QueryRequest,
} from "../src/package-query.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
  type WorkerRuntimeSource,
  type WorkerRuntimeTransportBinding,
  type WorkerRuntimeTransportHandlers,
} from "../src/worker-runtime-core.ts";

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>(resolve => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
    },
  };
}

const progressEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Progress",
  row: null,
  failure: null,
  completion: null,
  progress: {
    phase: "DependencyTraversal",
    completed: 1,
    limit: 5,
  },
  assessment: null,
};

const matchEvent: Extract<
  EngineWorkerPackageQueryDurableEvent,
  { readonly kind: "Match" }
> = {
  kind: "Match",
  row: {
    packageId: "Contoso.Library",
    version: "1.2.3",
    tier: "Nuspec",
    answers: [{
      id: "license",
      value: "MIT",
      term: {
        key: "license",
        operator: "eq",
        value: "MIT",
      },
    }],
    evidence: [{
      id: "description",
      scope: "Package",
      summary: {
        count: 2,
        preview: ["first", "second"],
      },
      properties: [{
        name: "value",
        value: "matched package description",
      }],
      number: null,
      term: null,
    }],
    totalDownloads: 42,
    verified: true,
    producer: "nuget-gallery",
    description: "A test package.",
    rootRequest: "root1:Contoso.Library@1.2.3",
    owners: ["Contoso"],
    manifest: {
      packageId: "Contoso.Library",
      version: "1.2.3",
      manifestVersion: "nuspec",
      description: "A test package.",
      authors: "Contoso",
      repository: "https://example.test/contoso/library",
      repositoryType: "git",
      repositoryCommit: "0123456789abcdef",
      license: "MIT",
      licenseUrl: "https://example.test/licenses/mit",
      packageTypes: ["Dependency"],
      isToolPackage: false,
      readmeFile: "README.md",
      dependencyGroups: [{
        targetFramework: "net10.0",
        dependencies: [{
          id: "Contoso.Dependency",
          versionRange: "[2.0.0,3.0.0)",
        }],
        isImplicitManifestGroup: false,
      }],
      iconFile: "icon.png",
      iconUrl: "https://example.test/icon.png",
      identityProvenance: "ExpectedCoordinate",
    },
  },
  failure: null,
  completion: null,
  progress: null,
  assessment: null,
};

const failureEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Failure",
  row: null,
  failure: {
    packageId: "Contoso.Broken",
    version: "1.0.0",
    producer: "manifest",
    kind: "DependencyTraversal",
    message: "dependency traversal incomplete",
    manifestFailureReason: null,
  },
  completion: null,
  progress: null,
  assessment: null,
};

const assessmentEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Assessment",
  row: null,
  failure: null,
  completion: null,
  progress: null,
  assessment: {
    packageId: "Contoso.Other",
    version: "2.0.0",
    disposition: "NoMatch",
    message: "literal absent",
    assetPath: "lib/net10.0/Contoso.Other.dll",
    rootRequest: "root1:Contoso.Other@2.0.0",
  },
};

const completionEvent: EngineWorkerPackageQueryCompletionEvent = {
  kind: "Completed",
  row: null,
  failure: null,
  completion: {
    prefix: "Contoso.",
    producer: "nuget-gallery",
    candidateLimit: 200,
    matchLimit: 100,
    candidates: 2,
    matches: 1,
    failures: 1,
    kind: "Exhausted",
    sourceCandidates: 2,
    semanticMisses: 0,
    notApplicable: 0,
    scope: "prefix",
    occurrences: null,
    notEvaluated: null,
  },
  progress: null,
  assessment: null,
};

function succeeded(
  value: EngineWorkerPackageQueryCompletionEvent = completionEvent,
): BrowserPackageQueryResult {
  return {
    version: 3,
    kind: "Succeeded",
    value,
    inspection: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function inspected(
  events: readonly BrowserPackageQueryEvent[] = [
    matchEvent,
    failureEvent,
    completionEvent,
  ],
): BrowserPackageQueryResult {
  const results = events
    .filter(event => event.kind === "Match")
    .map(event => event.row!);
  const failures = events
    .filter(event => event.kind === "Failure")
    .map(event => event.failure!);
  const completion = events
    .find(event => event.kind === "Completed")
    ?.completion;
  if (completion === null || completion === undefined) {
    throw new Error("Expected Package Query completion.");
  }
  return {
    version: 3,
    kind: "Succeeded",
    value: null,
    inspection: {
      resourcePath: "package-query",
      contentKind: "document",
      content: {
        hasPackages: results.length > 0,
        results,
        failures,
        completion: {
          ...completion,
          matches: results.length,
          failures: failures.length,
        },
        assemblySemantic: null,
      },
      portableProjection: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        location: null,
        reason: "notSupported",
        explanation: "No canonical Workspace packet.",
      },
      diagnostics: [{
        code: "package-query-note",
        severity: "Information",
        summary: "Package Query completed.",
        correspondence: null,
      }],
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function semanticInspected(): BrowserPackageQueryResult {
  const selectedAsset = {
    path: "lib/net10.0/Contoso.Library.dll",
    assemblyName: "Contoso.Library",
    targetFramework: "net10.0",
    sequence: "Implementation",
    ordinal: 0,
    unevaluatedSiblings: 0,
    rootRequest: "opaque-semantic-root",
  };
  const occurrence = {
    moduleVersionId: "00000000-0000-0000-0000-000000000001",
    methodDefinitionToken: 0x06000001,
    ilOffset: 4,
    userStringToken: 0x70000001,
    literalCharacterCount: 25,
    literalText: "shared-literal-use-marker",
  };
  const semanticResult = {
    candidateOrdinal: 1,
    packageId: "contoso.library",
    version: "1.2.3",
    producer: "nuget-gallery",
    selectedAsset,
    occurrences: [occurrence],
  };
  const semanticDocument = {
    population: {
      requestedCandidates: 1,
      candidates: 1,
      completion: "ExactPackageComplete" as const,
      isRequestedPopulationComplete: true,
      failures: [],
    },
    results: [semanticResult],
    candidateOutcomes: [{
      kind: "Matched" as const,
      candidateOrdinal: 1,
      packageId: "contoso.library",
      version: "1.2.3",
      producer: "nuget-gallery",
      result: semanticResult,
      selectedAsset,
      rootRequest: "opaque-semantic-root",
      notApplicableReason: null,
      failureKind: null,
      failureStage: null,
      nonEvaluationKind: null,
      timeoutKind: null,
      timeoutSeconds: null,
      message: null,
    }],
    candidateCount: 1,
    evaluatedCandidateCount: 1,
    notEvaluatedCount: 0,
    matchedPackageCount: 1,
    occurrenceCount: 1,
    semanticMissCount: 0,
    notApplicableCount: 0,
    failureCount: 0,
    completion: {
      population: "ExactPackageComplete" as const,
      isRequestedPopulationComplete: true,
      allCandidatesHaveTerminalOutcomes: true,
      hasFailures: false,
      isSemanticEvaluationComplete: true,
      isOperationDeadlineExpired: false,
    },
  };
  return {
    version: 3,
    kind: "Succeeded",
    value: null,
    inspection: {
      resourcePath: "package-query",
      contentKind: "document",
      content: {
        hasPackages: true,
        results: [{
          ...matchEvent.row,
          packageId: "contoso.library",
          tier: "Assembly",
          evidence: [{
            id: "selected-assembly",
            scope: "Package",
            summary: {
              count: 1,
              preview: ["Method 0x06000001, IL_0004"],
            },
            properties: [
              { name: "path", value: "lib/net10.0/Contoso.Library.dll" },
              { name: "literal-use-count", value: "1" },
              { name: "unevaluated-sibling-count", value: "0" },
            ],
            number: null,
            term: null,
          }],
          rootRequest: "opaque-semantic-root",
          owners: [],
          manifest: null,
        }],
        failures: [],
        completion: {
          prefix: "Contoso.Library",
          producer: "nuget-gallery",
          candidateLimit: 1,
          matchLimit: 1,
          candidates: 1,
          matches: 1,
          failures: 0,
          kind: "ExactPackageComplete",
          sourceCandidates: 1,
          semanticMisses: 0,
          notApplicable: 0,
          scope: "Selected primary implementation libraries only.",
          occurrences: 1,
          notEvaluated: 0,
        },
        assemblySemantic: semanticDocument,
      },
      portableProjection: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        location: null,
        reason: "notSupported",
        explanation: "No canonical Workspace packet.",
      },
      diagnostics: [],
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function semanticCandidateFailureInspected(): BrowserPackageQueryResult {
  const valid = semanticInspected();
  const inspection = valid.inspection;
  if (inspection === null) {
    throw new Error("Expected semantic Package Query inspection.");
  }
  const content = inspection.content;
  const semantic = content.assemblySemantic;
  if (semantic === null) {
    throw new Error("Expected semantic Package Query Document.");
  }
  const matched = semantic.candidateOutcomes[0]!;
  return {
    ...valid,
    inspection: {
      ...inspection,
      content: {
        ...content,
        hasPackages: false,
        results: [],
        failures: [{
          packageId: matched.packageId,
          version: matched.version,
          producer: matched.producer,
          kind: "AssemblyEvaluation",
          message: "Semantic evaluation failed.",
          manifestFailureReason: null,
        }],
        completion: {
          ...content.completion,
          matches: 0,
          failures: 1,
          occurrences: 0,
        },
        assemblySemantic: {
          ...semantic,
          results: [],
          candidateOutcomes: [{
            ...matched,
            kind: "Failure",
            result: null,
            failureKind: "Evaluation",
            failureStage: "Assembly",
            message: "Semantic evaluation failed.",
          }],
          matchedPackageCount: 0,
          occurrenceCount: 0,
          failureCount: 1,
          completion: {
            ...semantic.completion,
            hasFailures: true,
            isSemanticEvaluationComplete: false,
          },
        },
      },
    },
  };
}

function semanticZeroCandidateDeadlineInspected(): BrowserPackageQueryResult {
  const valid = semanticInspected();
  const inspection = valid.inspection;
  if (inspection === null) {
    throw new Error("Expected semantic Package Query inspection.");
  }
  const content = inspection.content;
  const semantic = content.assemblySemantic;
  if (semantic === null) {
    throw new Error("Expected semantic Package Query Document.");
  }
  const failure = {
    candidateOrdinal: null,
    packageId: null,
    version: null,
    authority: "nuget-gallery",
    kind: "Timeout",
    message: "The package source operation deadline expired.",
    timeoutKind: "Operation",
    timeoutSeconds: 25,
  };
  return {
    ...valid,
    inspection: {
      ...inspection,
      content: {
        ...content,
        hasPackages: false,
        results: [],
        failures: [{
          packageId: null,
          version: null,
          producer: "nuget-gallery",
          kind: "Search",
          message: failure.message,
          manifestFailureReason: null,
        }],
        completion: {
          ...content.completion,
          candidates: 0,
          matches: 0,
          failures: 1,
          kind: "Failed",
          sourceCandidates: 0,
          occurrences: 0,
        },
        assemblySemantic: {
          ...semantic,
          population: {
            requestedCandidates: 1,
            candidates: 0,
            completion: "SourceFailed",
            isRequestedPopulationComplete: false,
            failures: [failure],
          },
          results: [],
          candidateOutcomes: [],
          candidateCount: 0,
          evaluatedCandidateCount: 0,
          matchedPackageCount: 0,
          occurrenceCount: 0,
          completion: {
            population: "SourceFailed",
            isRequestedPopulationComplete: false,
            allCandidatesHaveTerminalOutcomes: true,
            hasFailures: true,
            isSemanticEvaluationComplete: false,
            isOperationDeadlineExpired: true,
          },
        },
      },
    },
  };
}

function semanticInspectedWithOccurrences(
  count: number,
  candidateCount = 1,
  literalCharacterCount = 25,
  literalText = "x".repeat(literalCharacterCount),
): BrowserPackageQueryResult {
  const valid = semanticInspected();
  const inspection = valid.inspection;
  if (inspection === null) {
    throw new Error("Expected semantic Package Query inspection.");
  }
  const content = inspection.content;
  const semantic = content.assemblySemantic;
  if (semantic === null) {
    throw new Error("Expected semantic Package Query Document.");
  }
  const result = semantic.results[0]!;
  const occurrence = {
    ...result.occurrences[0]!,
    literalCharacterCount,
    literalText,
  };
  const occurrences = Array.from({ length: count }, () => occurrence);
  const row = content.results[0]!;
  const evidence = row.evidence[0]!;
  const populationCompletion = candidateCount === 1
    ? "ExactPackageComplete"
    : "PrefixExhausted";
  const expandedResults = Array.from(
    { length: candidateCount },
    (_value, index) => ({
      ...result,
      candidateOrdinal: index + 1,
      packageId: `contoso.library.${index + 1}`,
      occurrences,
    }));
  const occurrenceCount = count * candidateCount;
  return {
    ...valid,
    inspection: {
      ...inspection,
      content: {
        ...content,
        results: expandedResults.map(expandedResult => ({
          ...row,
          packageId: expandedResult.packageId,
          evidence: [{
            ...evidence,
            summary: {
              count,
              preview: evidence.summary!.preview,
            },
          }],
        })),
        completion: {
          ...content.completion,
          candidateLimit: candidateCount,
          matchLimit: candidateCount,
          candidates: candidateCount,
          matches: candidateCount,
          kind: candidateCount === 1
            ? "ExactPackageComplete"
            : "Exhausted",
          sourceCandidates: candidateCount,
          occurrences: occurrenceCount,
        },
        assemblySemantic: {
          ...semantic,
          population: {
            ...semantic.population,
            requestedCandidates: candidateCount,
            candidates: candidateCount,
            completion: populationCompletion,
          },
          results: expandedResults,
          candidateOutcomes: expandedResults.map(expandedResult => ({
            ...semantic.candidateOutcomes[0]!,
            candidateOrdinal: expandedResult.candidateOrdinal,
            packageId: expandedResult.packageId,
            result: expandedResult,
          })),
          candidateCount,
          evaluatedCandidateCount: candidateCount,
          matchedPackageCount: candidateCount,
          occurrenceCount,
          completion: {
            ...semantic.completion,
            population: populationCompletion,
          },
        },
      },
    },
  };
}

function canceled(reason: string): BrowserPackageQueryResult {
  return {
    version: 3,
    kind: "Canceled",
    value: null,
    inspection: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason,
  };
}

function failed(
  failureKind: "Expected" | "Unexpected",
  error: string,
  diagnostic: string,
): BrowserPackageQueryResult {
  return {
    version: 3,
    kind: "Failed",
    value: null,
    inspection: null,
    failureKind,
    error,
    diagnostic,
    reason: null,
  };
}

function emit(
  eventSink: unknown,
  event:
    | EngineWorkerPackageQueryDurableEvent
    | EngineWorkerPackageQueryCompletionEvent,
): void {
  emitSerialized(eventSink, JSON.stringify(event));
}

function emitSerialized(eventSink: unknown, serialized: string): void {
  if (typeof eventSink !== "object" || eventSink === null) {
    throw new Error("Expected an event sink.");
  }
  if (!Reflect.set(eventSink, "event", serialized)) {
    throw new Error("Package Query event sink rejected an event.");
  }
}

function serializeLikeSystemTextJson(value: unknown): string {
  return JSON.stringify(value).replace(
    /[^\p{ASCII}]/gu,
    character =>
      `\\u${character.charCodeAt(0).toString(16).padStart(4, "0").toUpperCase()}`,
  );
}

interface Harness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly worker: FakeWorkerRuntime<string, string>;
  readonly host: WorkerRuntimeHost<string, string>;
  readonly adapter: EngineWorkerPackageQueryAdapter;
  readonly delayedControlAcknowledgmentCount: () => number;
  readonly releaseControlAcknowledgments: () => void;
}

class DelayedControlAcknowledgmentTransport
implements WorkerRuntimeTransportBinding, WorkerRuntimeSource {
  readonly source: WorkerRuntimeSource = this;
  readonly #worker: FakeWorkerRuntime<string, string>;
  readonly #pending: unknown[] = [];
  #handlers: WorkerRuntimeTransportHandlers | null = null;

  constructor(worker: FakeWorkerRuntime<string, string>) {
    this.#worker = worker;
  }

  get pendingCount(): number {
    return this.#pending.length;
  }

  bind(handlers: WorkerRuntimeTransportHandlers): () => void {
    this.#handlers = handlers;
    const unbind = this.#worker.bind({
      message: (_source, data) => {
        if (typeof data === "object"
          && data !== null
          && Object.getOwnPropertyDescriptor(data, "kind")?.value
            === "control-acknowledged") {
          this.#pending.push(data);
          return;
        }
        handlers.message(this, data);
      },
      error: (_source, diagnostic) => handlers.error(this, diagnostic),
      messageError: (_source, diagnostic) =>
        handlers.messageError(this, diagnostic),
    });
    return () => {
      if (this.#handlers === handlers) this.#handlers = null;
      unbind();
    };
  }

  send(message: unknown): void {
    this.#worker.send(message);
  }

  terminate(): void {
    this.#worker.terminate();
  }

  release(): void {
    const handlers = this.#handlers;
    if (handlers === null) return;
    for (const data of this.#pending.splice(0))
      handlers.message(this, data);
  }
}

function createHarness(
  facade: EngineWorkerPackageQueryFacade,
  options: {
    readonly delayControlAcknowledgments?: boolean;
  } = {},
): Harness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerPackageQueryOperation(operations, () => facade);
  const worker = new FakeWorkerRuntime<string, string>({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      bootstrap: () => undefined,
    },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: kind => ({
      error: `Unknown operation: ${kind}`,
      diagnostic: `Unknown operation: ${kind}`,
    }),
    operations,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  const delayedTransport = options.delayControlAcknowledgments === true
    ? new DelayedControlAcknowledgmentTransport(worker)
    : null;
  const host = new WorkerRuntimeHost<string, string>({
    transport: new QueueWorkerRuntimeTransportFactory([
      delayedTransport ?? worker,
    ]),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: engineWorkerText.decode,
      diagnostic: engineWorkerText,
    },
    diagnostic: engineWorkerText,
    callbacks: {
      failure: () => undefined,
      diagnostic: () => undefined,
      realmReleased: () => undefined,
    },
    createDiagnostic: (kind, detail) =>
      `${kind}: ${engineWorkerDiagnostic(detail)}`.slice(0, 4_096),
    idleHeartbeatIntervalMilliseconds:
      engineWorkerPolicy.idleHeartbeatIntervalMilliseconds,
    schedulingToleranceMilliseconds:
      engineWorkerPolicy.schedulingToleranceMilliseconds,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 20,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  return {
    environment,
    worker,
    host,
    adapter: registerEngineWorkerPackageQueryAdapter(host),
    delayedControlAcknowledgmentCount: () =>
      delayedTransport?.pendingCount ?? 0,
    releaseControlAcknowledgments: () => delayedTransport?.release(),
  };
}

async function startReady(harness: Harness): Promise<void> {
  assert.equal(harness.host.start("").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

type PackageQueryFeatureEvent = OperationFeatureEvent<
  EngineWorkerPackageQueryTerminal,
  EngineWorkerPackageQueryTerminalFailure,
  never,
  EngineWorkerPackageQueryDurableEvent
>;

type PackageQuerySession = OperationSession<
  QueryRequest,
  EngineWorkerPackageQueryTerminal,
  EngineWorkerPackageQueryTerminalFailure,
  never,
  WorkerRuntimePreparationError,
  EngineWorkerPackageQueryDurableEvent
>;

function createQuerySession(
  operationIds: readonly string[],
): {
  readonly events: PackageQueryFeatureEvent[];
  readonly session: PackageQuerySession;
} {
  let nextOperationId = 0;
  const events: PackageQueryFeatureEvent[] = [];
  return {
    events,
    session: createOperationAuthorityPage({
      allocation: {
        createId: () => {
          const operationId = operationIds[nextOperationId++];
          if (operationId === undefined)
            throw new Error("No Package Query operation ID remains.");
          return operationId;
        },
      },
    }).createSession<
      QueryRequest,
      EngineWorkerPackageQueryTerminal,
      EngineWorkerPackageQueryTerminalFailure,
      never,
      WorkerRuntimePreparationError,
      EngineWorkerPackageQueryDurableEvent
    >({
      feature: {
        publish: event => {
          events.push(event);
          return undefined;
        },
      },
      diagnostic: { report: () => undefined },
    }),
  };
}

function startQuery(
  adapter: EngineWorkerPackageQueryAdapter,
  request: QueryRequest = createQueryRequest("Contoso."),
  operationId = "package-query-operation",
  sessionState = createQuerySession([operationId]),
): {
  readonly handle: OperationHandle<
    EngineWorkerPackageQueryTerminal,
    EngineWorkerPackageQueryTerminalFailure
  >;
  readonly events: PackageQueryFeatureEvent[];
} {
  const { events, session } = sessionState;
  const started = session.start(request, adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started")
    throw new Error("Expected Package Query to start.");
  return { handle: started.handle, events };
}

test("Package Query Worker adapter preserves request, durable events, credit, and terminal result", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const runs: unknown[][] = [];
  const credits: unknown[][] = [];
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: (_operationId, reason) => ({
      kind: "Requested",
      reason,
    }),
    requestPackageQueryMatches: (...args) => {
      credits.push(args);
      return {
        kind: "Granted",
        additionalMatchCredit: args[1],
      };
    },
    runPackageQuery: (...args) => {
      runs.push(args);
      emit(args[7], progressEvent);
      emit(args[7], matchEvent);
      emit(args[7], failureEvent);
      emit(args[7], assessmentEvent);
      return terminal.promise;
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);

  const request: QueryRequest = {
    ...createQueryRequest("Contoso.*"),
    presets: [{
      id: "readme:eq:true",
      key: "readme",
      operator: "eq",
      value: "true",
      label: "Verified",
      tier: "nuspec",
      executionClass: "nuspec",
    }],
    terms: [{
      descriptor: {
        key: "depends",
        label: "Direct dependency",
        summary: "Matches a direct dependency in any group.",
        weight: 10,
        tier: "nuspec",
        executionClass: "nuspec",
        operators: ["eq"],
        valueKind: "package-id",
        example: "Microsoft.Extensions.Hosting",
      },
      operator: "eq",
      value: "Microsoft.Extensions.Hosting",
    }],
    includePrerelease: true,
  };
  const { handle, events } = startQuery(harness.adapter, request);
  await harness.environment.flushAsync();

  const acknowledged = harness.adapter.requestControl(handle.id, 10);
  const overlapping = harness.adapter.requestControl(handle.id, 10);
  assert.deepEqual(await overlapping, {
    kind: "failed",
    error: "Package Query control is already pending.",
  });
  await harness.environment.flushAsync();
  assert.deepEqual(await acknowledged, {
    kind: "acknowledged",
    value: 10,
  });
  assert.deepEqual(credits, [["package-query-operation", 10]]);

  terminal.resolve(inspected([
    progressEvent,
    matchEvent,
    failureEvent,
    assessmentEvent,
    completionEvent,
  ]));
  await Promise.resolve();
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: {
      event: null,
      inspection: {
        resourcePath: "package-query",
        contentKind: "document",
        content: {
          hasPackages: true,
          results: [matchEvent.row],
          failures: [failureEvent.failure],
          completion: completionEvent.completion,
          assemblySemantic: null,
        },
        portableProjection: {
          kind: "NonProjectable",
          fullUrl: null,
          packet: null,
          location: null,
          reason: "notSupported",
          explanation: "No canonical Workspace packet.",
        },
        diagnostics: [{
          code: "package-query-note",
          severity: "Information",
          summary: "Package Query completed.",
          correspondence: null,
        }],
      },
    },
  });
  await handle.quiesced;
  assert.deepEqual(
    events
      .filter(event => event.kind === "durable")
      .map(event => event.durable.value),
    [progressEvent, matchEvent, failureEvent, assessmentEvent],
  );
  assert.deepEqual(runs[0]?.slice(0, 7), [
    "package-query-operation",
    "Contoso.*",
    '[{"key":"readme","operator":"eq","value":"true"},{"key":"depends","operator":"eq","value":"Microsoft.Extensions.Hosting"}]',
    200,
    100,
    true,
    20,
  ]);
  assert.equal(runs[0]?.length, 8);

  const start = harness.worker.receivedMessages.find(message =>
    typeof message === "object"
    && message !== null
    && Object.getOwnPropertyDescriptor(message, "kind")?.value === "start");
  assert.notEqual(start, undefined);
  let payload: unknown;
  if (start !== undefined) {
    const descriptor = Object.getOwnPropertyDescriptor(start, "payload");
    payload = descriptor?.value;
  }
  assert.deepEqual(payload, {
    kind: "query",
    searchText: "Contoso.*",
    terms: [
      {
        key: "readme",
        operator: "eq",
        value: "true",
      },
      {
        key: "depends",
        operator: "eq",
        value: "Microsoft.Extensions.Hosting",
      },
    ],
    maximumCandidates: 200,
    maximumMatches: 100,
    includePrerelease: true,
    initialMatchCredit: 20,
  });
  assert.doesNotThrow(() => structuredClone(payload));
  harness.host.dispose();
});

test("Package Query Worker routes whitespace-only library literals unchanged", async () => {
  const runs: unknown[][] = [];
  const assemblyProgress: EngineWorkerPackageQueryDurableEvent = {
    ...progressEvent,
    progress: {
      phase: "Assembly",
      completed: 1,
      limit: 1,
    },
  };
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    async runPackageQuery() {
      assert.fail("Library-literal requests must use the shared semantic operation.");
    },
    async runPackageAssemblySemanticQuery(...args) {
      runs.push(args);
      emit(args[7], assemblyProgress);
      return semanticInspected();
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const request = {
    ...withLibraryLiteralDraft(
      createQueryRequest("Contoso.Library"),
      " ",
      "net10.0"),
    includePrerelease: true,
  };

  const { handle, events } = startQuery(harness.adapter, request);
  await harness.environment.flushAsync();

  const outcome = await handle.outcome;
  assert.equal(outcome.kind, "succeeded");
  if (outcome.kind !== "succeeded")
    throw new Error("Expected semantic Package Query success.");
  assert.equal(
    outcome.value.inspection?.content.assemblySemantic?.occurrenceCount,
    1);
  assert.deepEqual(
    events
      .filter(event => event.kind === "durable")
      .map(event => event.durable.value),
    [assemblyProgress],
  );
  assert.deepEqual(runs[0]?.slice(0, 7), [
    "package-query-operation",
    "Contoso.Library",
    " ",
    "net10.0",
    1,
    true,
    20,
  ]);
  assert.equal(runs[0]?.length, 8);

  const start = harness.worker.receivedMessages.find(message =>
    typeof message === "object"
    && message !== null
    && Object.getOwnPropertyDescriptor(message, "kind")?.value === "start");
  const payloadDescriptor = start === undefined
    ? undefined
    : Object.getOwnPropertyDescriptor(start, "payload");
  const payload: unknown = payloadDescriptor?.value;
  assert.deepEqual(payload, {
    kind: "library-literal",
    searchText: "Contoso.Library",
    operand: " ",
    targetFramework: "net10.0",
    maximumCandidates: 1,
    includePrerelease: true,
    initialMatchCredit: 20,
  });
  await handle.quiesced;
  harness.host.dispose();
});

test("Package Query Worker accepts escaped owner-valid manifest callbacks", async () => {
  const packageType = "\u00E9".repeat(32 * 1_024);
  const expandedMatch: EngineWorkerPackageQueryDurableEvent = {
    ...matchEvent,
    row: {
      ...matchEvent.row,
      manifest: {
        ...matchEvent.row.manifest!,
        packageTypes: Array.from({ length: 8 }, () => packageType),
      },
    },
  };
  const serialized = serializeLikeSystemTextJson(expandedMatch);
  assert.ok(serialized.length > 1_048_576);

  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageQuery(...args) {
      emitSerialized(args[7], serialized);
      return Promise.resolve(inspected([
        expandedMatch,
        failureEvent,
        completionEvent,
      ]));
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  const outcome = await handle.outcome;
  assert.equal(outcome.kind, "succeeded");
  if (outcome.kind !== "succeeded")
    throw new Error("Expected Package Query to succeed.");
  assert.equal(
    outcome.value.inspection?.content.results[0]
      ?.manifest?.packageTypes.length,
    8,
  );
  assert.equal(
    outcome.value.inspection?.content.results[0]
      ?.manifest?.packageTypes[0]?.length,
    32 * 1_024,
  );

  harness.host.dispose();
  await handle.quiesced;
});

test("Package Query Worker rejects callbacks above the encoded wire bound", async () => {
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageQuery(...args) {
      emitSerialized(args[7], " ".repeat(8 * 1_024 * 1_024));
      return Promise.resolve(succeeded());
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: {
      failureKind: "Unexpected",
      error: "Worker reported a runtime failure.",
      diagnostic: "Worker reported a runtime failure.",
    },
  });
  assert.equal(harness.host.snapshot().phase, "draining");

  harness.host.dispose();
  await handle.quiesced;
});

test("Package Query binding preserves caller identity and expected diagnostics", async () => {
  const runs: unknown[][] = [];
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageQuery: (...args) => {
      runs.push(args);
      return Promise.resolve(failed(
        "Expected",
        "query unavailable",
        "package discovery is unavailable",
      ));
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const binding = bindPackageQueryFacade(
    harness.adapter,
    () => undefined,
    createSharedEngineOperationAuthority(),
  );

  assert.deepEqual(await binding.runPackageQuery(
    "caller-package-query",
    "Contoso.",
    "[]",
    20,
    10,
    false,
    20,
    {},
  ), failed(
    "Expected",
    "query unavailable",
    "package discovery is unavailable",
  ));
  assert.equal(runs[0]?.[0], "caller-package-query");

  binding.dispose();
  harness.host.dispose();
});

test("Package Query binding preserves the inspection envelope", async () => {
  const expected = inspected([progressEvent, completionEvent]);
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageQuery: () => Promise.resolve(expected),
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const binding = bindPackageQueryFacade(
    harness.adapter,
    () => undefined,
    createSharedEngineOperationAuthority(),
  );

  const result = await binding.runPackageQuery(
    "envelope-package-query",
    "Contoso.",
    "[]",
    20,
    10,
    false,
    20,
    {});

  assert.deepEqual(result, expected);
  binding.dispose();
  harness.host.dispose();
});

test("Package Query credit reports not-active and closes after settlement", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageQuery: () => terminal.promise,
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  const inactive = harness.adapter.requestControl(handle.id, 10);
  await harness.environment.flushAsync();
  assert.deepEqual(await inactive, { kind: "not-active" });

  terminal.resolve(succeeded());
  await Promise.resolve();
  await harness.environment.flushAsync();
  await handle.quiesced;
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, 10),
    { kind: "not-active" },
  );
  harness.host.dispose();
});

test("Package Query retains an already-posted credit response across settlement", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: (_operationId, amount) => ({
      kind: "Granted",
      additionalMatchCredit: amount,
    }),
    runPackageQuery: () => terminal.promise,
  };
  const harness = createHarness(facade, {
    delayControlAcknowledgments: true,
  });
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  const pendingCredit = harness.adapter.requestControl(handle.id, 10);
  await harness.environment.flushAsync();
  assert.equal(harness.delayedControlAcknowledgmentCount(), 1);

  terminal.resolve(succeeded());
  await Promise.resolve();
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: { event: completionEvent, inspection: null },
  });
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, 10),
    { kind: "not-active" },
  );

  harness.releaseControlAcknowledgments();
  assert.deepEqual(await pendingCredit, {
    kind: "acknowledged",
    value: 10,
  });
  await handle.quiesced;
  harness.host.dispose();
});

test("Package Query terminal callback rejection fails the Worker epoch", async () => {
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    async runPackageQuery(...args) {
      emit(args[7], completionEvent);
      return succeeded();
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: {
      failureKind: "Unexpected",
      error: "Worker reported a runtime failure.",
      diagnostic: "Worker reported a runtime failure.",
    },
  });
  assert.equal(harness.host.snapshot().phase, "draining");
  harness.host.dispose();
  await handle.quiesced;
});

test("Package Query codecs reject terminal callbacks, malformed descriptors, and payload excess", () => {
  assert.equal(
    engineWorkerPackageQueryDurableEvent.decode(completionEvent).kind,
    "rejected",
  );
  assert.equal(
    engineWorkerPackageQueryCompletionEvent.decode(matchEvent).kind,
    "rejected",
  );
  assert.equal(engineWorkerPackageQueryInput.decode({
    kind: "assembly",
    patternId: "pattern",
    operand: "value",
    packageCoordinates: Array.from(
      { length: 4_097 },
      () => "Contoso.Package@1.0.0"),
    targetFramework: "net10.0",
    initialMatchCredit: 20,
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryInput.decode({
    kind: "library-literal",
    searchText: "Contoso.*",
    operand: "shared-literal-use-marker",
    targetFramework: "net10.0",
    maximumCandidates: 5,
    includePrerelease: false,
    initialMatchCredit: 20,
  }).kind, "decoded");
  assert.equal(engineWorkerPackageQueryInput.decode({
    kind: "library-literal",
    searchText: "Contoso.*",
    operand: "shared-literal-use-marker",
    targetFramework: "net10.0",
    maximumCandidates: 6,
    includePrerelease: false,
    initialMatchCredit: 20,
  }).kind, "rejected");
  const queryInput = {
    kind: "query",
    searchText: "Contoso.*",
    maximumCandidates: 200,
    maximumMatches: 100,
    includePrerelease: false,
    initialMatchCredit: 20,
  } as const;
  const terms = Array.from({ length: 24 }, (_unused, index) => ({
    key: "depends",
    operator: "eq",
    value: `Contoso.Dependency.${index}`,
  }));
  assert.equal(engineWorkerPackageQueryInput.decode({
    ...queryInput,
    terms,
  }).kind, "decoded");
  assert.equal(engineWorkerPackageQueryInput.decode({
    ...queryInput,
    terms: [...terms, terms[0]],
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryDurableEvent.decode({
    ...matchEvent,
    row: {
      ...matchEvent.row,
      description: "x".repeat(1_048_577),
    },
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryDurableEvent.decode({
    ...matchEvent,
    row: {
      ...matchEvent.row,
      owners: "Contoso",
    },
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryDurableEvent.decode({
    ...matchEvent,
    row: {
      ...matchEvent.row,
      owners: Array.from({ length: 4_097 }, () => "Contoso"),
    },
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryDurableEvent.decode({
    ...matchEvent,
    row: {
      ...matchEvent.row,
      manifest: {
        ...matchEvent.row.manifest!,
        dependencyGroups: [{
          ...matchEvent.row.manifest!.dependencyGroups[0]!,
          dependencies: [{
            id: "Contoso.Dependency",
            versionRange: 2,
          }],
        }],
      },
    },
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryDurableEvent.decode({
    ...failureEvent,
    failure: {
      ...failureEvent.failure,
      manifestFailureReason: "UnknownReason",
    },
  }).kind, "rejected");

  const accessor = { ...matchEvent };
  Object.defineProperty(accessor, "row", {
    get: () => matchEvent.row,
  });

  assert.equal(
    engineWorkerPackageQueryDurableEvent.decode(accessor).kind,
    "rejected",
  );
});

test("Package Query Worker rejects inconsistent semantic Document accounting", () => {
  const valid = semanticInspected();
  if (valid.inspection === null
      || valid.inspection.content.assemblySemantic === null) {
    throw new Error("Expected semantic Package Query inspection.");
  }
  const malformed: BrowserPackageQueryResult = {
    ...valid,
    inspection: {
      ...valid.inspection,
      content: {
        ...valid.inspection.content,
        assemblySemantic: {
          ...valid.inspection.content.assemblySemantic,
          occurrenceCount: 2,
        },
      },
    },
  };

  const settlement = mapEngineWorkerPackageQueryResult(malformed);
  assert.equal(settlement.kind, "failed");
  if (settlement.kind !== "failed")
    throw new Error("Expected malformed semantic settlement failure.");
  assert.match(
    settlement.diagnostic,
    /assembly-semantic outcome accounting is inconsistent/);
});

test("Package Query Worker accepts producer-valid semantic failure completion", () => {
  assert.equal(
    mapEngineWorkerPackageQueryResult(
      semanticCandidateFailureInspected()).kind,
    "succeeded",
  );
});

test("Package Query Worker accepts an operation deadline before candidate admission", () => {
  assert.equal(
    mapEngineWorkerPackageQueryResult(
      semanticZeroCandidateDeadlineInspected()).kind,
    "succeeded",
  );
});

test("Package Query Worker accepts full semantic occurrence, text, and inert expansion limits", () => {
  assert.equal(
    mapEngineWorkerPackageQueryResult(
      semanticInspectedWithOccurrences(
        10_000,
        5,
        400,
        "\\u202E".repeat(400))).kind,
    "succeeded",
  );
});

test("Package Query Worker rejects occurrences above the Analysis maximum", () => {
  const settlement = mapEngineWorkerPackageQueryResult(
    semanticInspectedWithOccurrences(10_001));
  assert.equal(settlement.kind, "failed");
  if (settlement.kind !== "failed")
    throw new Error("Expected oversized semantic settlement failure.");
  assert.match(settlement.diagnostic, /exceeds 10000 collection items/);
});

test("Package Query Worker accepts owner maximum manifest collections", () => {
  const result = engineWorkerPackageQueryDurableEvent.decode({
    ...matchEvent,
    row: {
      ...matchEvent.row,
      owners: Array.from({ length: 4_096 }, () => "Contoso"),
      manifest: {
        ...matchEvent.row.manifest!,
        packageTypes: Array.from({ length: 128 }, () => "Dependency"),
        dependencyGroups: Array.from(
          { length: 1_024 },
          (_group, groupIndex) => ({
            targetFramework: `net10.0-${groupIndex}`,
            dependencies: Array.from(
              { length: 4 },
              (_dependency, dependencyIndex) => ({
                id: `Dependency.${groupIndex}.${dependencyIndex}`,
                versionRange: "[1.0.0,2.0.0)",
              })),
            isImplicitManifestGroup: false,
          })),
      },
    },
  });

  assert.equal(result.kind, "decoded");
});

test("Package Query inspection accepts the maximum valid Document scale", () => {
  const content = [
    ...Array.from({ length: 4_097 }, () => failureEvent),
    completionEvent,
  ];

  assert.equal(
    mapEngineWorkerPackageQueryResult(inspected(content)).kind,
    "succeeded",
  );

  const combined = inspected([
    ...Array.from({ length: 10_001 }, () => failureEvent),
    completionEvent,
  ]);
  assert.notEqual(combined.inspection, null);
  assert.equal(mapEngineWorkerPackageQueryResult({
    ...combined,
    inspection: {
      ...combined.inspection,
      diagnostics: Array.from({ length: 4_096 }, () => ({
        code: "package-query-note",
        severity: "Information",
        summary: "Package Query completed.",
        correspondence: null,
      })),
    },
  }).kind, "succeeded");

  assert.equal(
    mapEngineWorkerPackageQueryResult(inspected([
      ...Array.from({ length: 10_002 }, () => failureEvent),
      completionEvent,
    ])).kind,
    "failed",
  );

  const result = inspected();
  assert.notEqual(result.inspection, null);
  assert.equal(mapEngineWorkerPackageQueryResult({
    ...result,
    inspection: {
      ...result.inspection,
      diagnostics: Array.from({ length: 4_097 }, () => ({
        code: "package-query-note",
        severity: "Information",
        summary: "Package Query completed.",
        correspondence: null,
      })),
    },
  }).kind, "failed");
});

test("Package Query terminal rejects duplicated completion beside inspection", () => {
  const result = inspected();
  assert.notEqual(result.inspection, null);
  assert.equal(
    engineWorkerPackageQueryTerminal.decode({
      event: {
        ...completionEvent,
        completion: {
          ...completionEvent.completion,
          matches: completionEvent.completion.matches + 1,
        },
      },
      inspection: result.inspection,
    }).kind,
    "rejected",
  );
});

test("Package Query cancellation, credit, and terminal mappers enforce exact responses", () => {
  assert.equal(engineWorkerPackageQueryCancellationIsRunning(
    { kind: "Requested", reason: "user" },
    "user",
  ), true);
  assert.equal(engineWorkerPackageQueryCancellationIsRunning(
    { kind: "AlreadyRequested", reason: "user" },
    "superseded",
  ), true);
  assert.equal(engineWorkerPackageQueryCancellationIsRunning(
    { kind: "NotActive", reason: null },
    "user",
  ), false);
  assert.throws(() => engineWorkerPackageQueryCancellationIsRunning(
    { kind: "Requested", reason: "timeout" },
    "user",
  ), /changed the requested reason/);

  assert.deepEqual(mapEngineWorkerPackageQueryCredit({
    kind: "Granted",
    additionalMatchCredit: 10,
  }, 10), { kind: "acknowledged", value: 10 });
  assert.deepEqual(mapEngineWorkerPackageQueryCredit({
    kind: "NotActive",
    additionalMatchCredit: null,
  }, 10), { kind: "not-active" });
  assert.throws(() => mapEngineWorkerPackageQueryCredit({
    kind: "Granted",
    additionalMatchCredit: 5,
  }, 10), /different match-credit amount/);

  assert.deepEqual(mapEngineWorkerPackageQueryResult({
    version: 3,
    kind: "Failed",
    value: null,
    inspection: null,
    failureKind: "Expected",
    error: "query unavailable",
    diagnostic: "expected detail",
    reason: null,
  }), {
    kind: "failed",
    failureKind: "expected",
    error: {
      failureKind: "Expected",
      error: "query unavailable",
      diagnostic: "expected detail",
    },
    diagnostic: "expected detail",
  });
  assert.deepEqual(mapEngineWorkerPackageQueryResult(canceled("timeout")), {
    kind: "canceled",
    reason: "timeout",
  });
  assert.deepEqual(mapEngineWorkerPackageQueryResult({
    version: 3,
    kind: "Failed",
    value: null,
    inspection: null,
    failureKind: "Unexpected",
    error: "query failed",
    diagnostic: "x".repeat(65_537),
    reason: null,
  }), {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "Package Query exceeded Worker transport limits.",
      diagnostic: "Package Query diagnostic exceeds 65536 characters.",
    },
    diagnostic: "Package Query diagnostic exceeds 65536 characters.",
  });
});
