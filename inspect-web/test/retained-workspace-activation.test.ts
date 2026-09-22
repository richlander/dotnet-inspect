import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceConsumerCompletionResult,
  BrowserRetainedWorkspaceDeactivationResult,
  BrowserRetainedWorkspacePosting,
  BrowserRetainedWorkspacePreparationResult,
  BrowserRetainedWorkspacePreparedPosting,
  BrowserRetainedWorkspaceSettlementResult,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  createRetainedWorkspaceActivationController,
  MAX_RETAINED_WORKSPACE_DEFINITIONS,
  type RetainedWorkspaceActivationClient,
  type RetainedWorkspacePredecessorObservation,
} from "../src/retained-workspace-activation.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });
  return { promise, resolve, reject };
}

function posting(
  retainedDefinitionId: string,
  realizationId: string,
  settlementId: string | null = null,
): BrowserRetainedWorkspacePosting {
  return {
    retainedDefinitionId,
    label: retainedDefinitionId,
    canonicalLocation: `/inspect/${retainedDefinitionId}`,
    canonicalPacket: `packet-${retainedDefinitionId}`,
    realizationId,
    publicationOrdinal: Number(realizationId.split("-").at(-1)),
    definition: {
      tabs: [],
      contexts: [],
      registrations: [],
      activeTabId: null,
      selectedContextId: null,
    },
    navigation: {
      operation: "Initialize",
      request: `request-${realizationId}`,
      snapshot: {
        generation: `generation-${realizationId}`,
        scope: {
          kind: "Current",
          runtimeFailure: null,
        },
        workspace: {
          id: `workspace-${realizationId}`,
          kind: "Workspace",
          label: retainedDefinitionId,
          summary: null,
          parent: null,
        },
        activePackage: null,
        activeSubject: {
          id: `workspace-${realizationId}`,
          kind: "Workspace",
          label: retainedDefinitionId,
          summary: null,
          parent: null,
        },
        typeInventoryLibraryContext: null,
        packages: [],
        hierarchy: [],
        libraries: [],
        types: [],
        members: [],
        lenses: [],
        lensOutcome: {
          kind: "Applied",
          basis: "Recommendation",
          subject: {
            id: `workspace-${realizationId}`,
            kind: "Workspace",
            label: retainedDefinitionId,
            summary: null,
            parent: null,
          },
          effectiveLens: null,
          request: null,
          preferredRole: null,
          policyFailure: null,
          resolution: null,
          suspension: null,
        },
        diagnostics: [],
      },
      outcome: {
        kind: "Applied",
        rejection: null,
        failureSource: null,
        message: null,
        request: null,
        resolution: null,
        scope: null,
        diagnostics: [],
        coordinateRetention: null,
      },
      synchronization: "SynchronizationRequired",
      authority: {
        session: `session-${realizationId}`,
        revision: `revision-${realizationId}`,
        intent: `intent-${realizationId}`,
        epoch: `epoch-${realizationId}`,
      },
    },
    packages: [{
      navigationId: "package-navigation",
      contextIndex: 0,
      consumerPackageSubjectId: "package-subject",
      summary: {
        selectedCompileFramework: null,
        libraryCount: 1,
        typeCount: 2,
        memberCount: 3,
        documentCount: 1,
        hasInspectionNotices: false,
      },
    }],
    platforms: [{
      navigationId: "platform-navigation",
      contextIndex: 1,
      family: "Microsoft.NETCore.App",
      runtimeIdentifier: "linux-x64",
      summary: {
        selectedCompileFramework: "net10.0",
        libraryCount: 2,
        typeCount: 4,
        memberCount: 8,
        documentCount: 0,
        hasInspectionNotices: true,
      },
    }],
    predecessor: settlementId === null
      ? null
      : {
        settlementId,
        reason: "Replaced",
      },
    cleanup: null,
  };
}

function preparedPosting(
  value: BrowserRetainedWorkspacePosting,
): BrowserRetainedWorkspacePreparedPosting {
  return {
    retainedDefinitionId: value.retainedDefinitionId,
    label: value.label,
    canonicalLocation: value.canonicalLocation,
    canonicalPacket: value.canonicalPacket,
    definition: value.definition,
    navigation: value.navigation,
    packages: value.packages,
    platforms: value.platforms,
  };
}

class ActivationClient implements RetainedWorkspaceActivationClient {
  readonly packageSourceCredentialPayloads: string[] = [];
  readonly activations: Array<{
    promise: Promise<BrowserRetainedWorkspaceActivationResult>;
    resolve(value: BrowserRetainedWorkspaceActivationResult): void;
  }> = [];
  readonly activationResults = new Map<
    string,
    BrowserRetainedWorkspaceActivationResult
  >();
  readonly commitReceipts: string[] = [];
  readonly commitResponses = new Map<
    string,
    Promise<BrowserRetainedWorkspaceActivationResult>
  >();
  readonly activationCompletions: Array<{
    receipt: string;
    succeeded: boolean;
    failure: string | null;
  }> = [];
  readonly deactivationCompletions: Array<{
    receipt: string;
    succeeded: boolean;
    failure: string | null;
  }> = [];
  readonly cancelledReceipts: string[] = [];
  readonly cancellationResponses: Array<
    Promise<BrowserRetainedWorkspaceActivationResult>
  > = [];
  cancellationResponse: BrowserRetainedWorkspaceActivationResult | null =
    null;
  activationCompletionError: Error | null = null;
  readonly settlements: string[] = [];
  readonly settlementResponses = new Map<
    string,
    Promise<BrowserRetainedWorkspaceSettlementResult>
  >();
  readonly deactivations: string[] = [];
  readonly deactivationResponses: Array<{
    promise: Promise<BrowserRetainedWorkspaceDeactivationResult>;
    resolve(value: BrowserRetainedWorkspaceDeactivationResult): void;
    reject(error: unknown): void;
  }> = [];
  readonly recordingResponses = new Map<
    string,
    string | Promise<string>
  >();
  readonly validationResponses = new Map<
    string,
    boolean | Promise<boolean>
  >();
  readonly acknowledgementResponses = new Map<
    string,
    string | Promise<string>
  >();
  readonly lifecycle: string[] = [];

  prepareRetainedWorkspaceDefinition():
  Promise<BrowserRetainedWorkspacePreparationResult> {
    let resolve!: (value: BrowserRetainedWorkspaceActivationResult) => void;
    const promise =
      new Promise<BrowserRetainedWorkspaceActivationResult>(accept => {
        resolve = accept;
      });
    const activation = { promise, resolve };
    this.activations.push(activation);
    const receipt = `receipt-${this.activations.length}`;
    return activation.promise.then(result => {
      switch (result.status) {
        case "activated": {
          if (result.posting === null) {
            throw new Error("Activated test result omitted its posting.");
          }

          this.activationResults.set(receipt, result);
          return {
            status: "prepared",
            receipt,
            preparation: preparedPosting(result.posting),
            posting: null,
            failure: null,
          };
        }
        case "noEffect":
          return {
            status: "noEffect",
            receipt: null,
            preparation: null,
            posting: result.posting,
            failure: null,
          };
        case "superseded":
          return {
            status: "superseded",
            receipt: null,
            preparation: null,
            posting: null,
            failure: null,
          };
        case "failed":
          return {
            status: "failed",
            receipt: null,
            preparation: null,
            posting: null,
            failure: result.failure,
          };
        default:
          throw new Error(`Unexpected activation status: ${result.status}`);
      }
    });
  }

  prepareRetainedWorkspaceDefinitionWithCredentials(
    _retainedDefinitionId: string,
    _label: string,
    _canonicalLocation: string,
    _canonicalPacket: string,
    packageSourceCredentialsJson: string,
  ): Promise<BrowserRetainedWorkspacePreparationResult> {
    this.packageSourceCredentialPayloads.push(
      packageSourceCredentialsJson,
    );
    return this.prepareRetainedWorkspaceDefinition();
  }

  commitRetainedWorkspaceActivation(
    receipt: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult> {
    this.commitReceipts.push(receipt);
    const result = this.activationResults.get(receipt);
    if (result === undefined) {
      throw new Error(`Unknown activation receipt: ${receipt}`);
    }
    return this.commitResponses.get(receipt) ?? Promise.resolve(result);
  }

  cancelRetainedWorkspaceActivation(
    receipt: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult> {
    this.cancelledReceipts.push(receipt);
    this.activationResults.delete(receipt);
    const queuedResponse = this.cancellationResponses.shift();
    if (queuedResponse !== undefined) return queuedResponse;
    return Promise.resolve(this.cancellationResponse ?? {
      status: "superseded",
      posting: null,
      failure: null,
    });
  }

  completeRetainedWorkspaceActivation(
    receipt: string,
    succeeded: boolean,
    failure: string | null,
  ): Promise<BrowserRetainedWorkspaceConsumerCompletionResult> {
    this.activationCompletions.push({ receipt, succeeded, failure });
    if (this.activationCompletionError !== null) {
      return Promise.reject(this.activationCompletionError);
    }
    return Promise.resolve({
      status: "completed",
      succeeded,
      failure,
      message: null,
    });
  }

  completeRetainedWorkspaceDeactivation(
    receipt: string,
    succeeded: boolean,
    failure: string | null,
  ): Promise<BrowserRetainedWorkspaceConsumerCompletionResult> {
    this.deactivationCompletions.push({ receipt, succeeded, failure });
    return Promise.resolve({
      status: "completed",
      succeeded,
      failure,
      message: null,
    });
  }

  deactivateRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceDeactivationResult> {
    this.deactivations.push(retainedDefinitionId);
    let resolve!: (
      value: BrowserRetainedWorkspaceDeactivationResult,
    ) => void;
    let reject!: (error: unknown) => void;
    const promise =
      new Promise<BrowserRetainedWorkspaceDeactivationResult>(
        (accept, fail) => {
          resolve = accept;
          reject = fail;
        },
      );
    this.deactivationResponses.push({
      promise,
      resolve,
      reject,
    });
    return promise;
  }

  observeRetainedWorkspaceSettlement(
    settlementId: string,
  ): Promise<BrowserRetainedWorkspaceSettlementResult> {
    this.settlements.push(settlementId);
    return this.settlementResponses.get(settlementId) ?? Promise.resolve({
      status: "settled",
      settlement: {
        succeeded: true,
        reason: "Replaced",
        failure: null,
      },
    });
  }

  validateRetainedWorkspaceNavigationAuthority(
    realizationId: string,
  ): boolean | Promise<boolean> {
    this.lifecycle.push(`validate:${realizationId}`);
    return this.validationResponses.get(realizationId) ?? true;
  }

  recordRetainedWorkspaceNavigationPosting(
    realizationId: string,
  ): string | Promise<string> {
    this.lifecycle.push(`record:${realizationId}`);
    return this.recordingResponses.get(realizationId) ?? "accepted";
  }

  acknowledgeRetainedWorkspaceNavigation(
    realizationId: string,
  ): string | Promise<string> {
    this.lifecycle.push(`acknowledge:${realizationId}`);
    return this.acknowledgementResponses.get(realizationId) ?? "accepted";
  }

  abandonRetainedWorkspaceNavigation(
    realizationId: string,
  ): string {
    this.lifecycle.push(`abandon:${realizationId}`);
    return "accepted";
  }
}

function createFixture(
  failPosting = false,
  failClear = false,
) {
  const client = new ActivationClient();
  const posted: BrowserRetainedWorkspacePosting[] = [];
  const presentationCurrent: boolean[] = [];
  const clearPresentationCurrent: Array<boolean | undefined> = [];
  const settled: Array<{
    observation: RetainedWorkspacePredecessorObservation;
    result: BrowserRetainedWorkspaceSettlementResult;
  }> = [];
  const observationFailures: Array<{
    observation: RetainedWorkspacePredecessorObservation;
    error: unknown;
  }> = [];
  let clears = 0;
  const controller = createRetainedWorkspaceActivationController(client, {
    post: (value, current) => {
      client.lifecycle.push(`post:${value.realizationId}`);
      if (failPosting) {
        throw new Error("Injected posting failure.");
      }
      posted.push(value);
      presentationCurrent.push(current);
    },
    clear: current => {
      if (failClear) throw new Error("Injected clear failure.");
      clearPresentationCurrent.push(current);
      clears++;
    },
    predecessorSettled: (observation, result) =>
      settled.push({ observation, result }),
    predecessorObservationFailed: (observation, error) =>
      observationFailures.push({ observation, error }),
  });
  return {
    client,
    controller,
    posted,
    presentationCurrent,
    clearPresentationCurrent,
    settled,
    observationFailures,
    clears: () => clears,
  };
}

test("posting records and acknowledges exact authority in order", async () => {
  const fixture = createFixture();
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(
    definition.id,
    undefined,
    value => {
      fixture.client.lifecycle.push(`complete:${value.realizationId}`);
    },
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  await activation;

  assert.deepEqual(fixture.client.lifecycle, [
    "validate:realization-1",
    "post:realization-1",
    "record:realization-1",
    "complete:realization-1",
    "acknowledge:realization-1",
  ]);
});

test("committed activation retains posting without replacing a newer route", async () => {
  const fixture = createFixture();
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const committed =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.commitResponses.set("receipt-1", committed.promise);
  let presentationCurrent = true;
  const completed: string[] = [];

  const activation = fixture.controller.activate(
    definition.id,
    undefined,
    value => {
      completed.push(value.realizationId);
    },
    undefined,
    undefined,
    () => presentationCurrent,
  );
  const activated = {
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  } as const;
  fixture.client.activations[0]!.resolve(activated);
  while (fixture.client.commitReceipts.length === 0) {
    await Promise.resolve();
  }
  presentationCurrent = false;
  committed.resolve(activated);
  await activation;

  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1"],
  );
  assert.deepEqual(fixture.presentationCurrent, [false]);
  assert.deepEqual(completed, ["realization-1"]);
  assert.deepEqual(fixture.client.activationCompletions, [{
    receipt: "receipt-1",
    succeeded: true,
    failure: null,
  }]);
});

test("posting failure clears retained state without replacing a newer route", async () => {
  const fixture = createFixture();
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const recording = deferred<string>();
  fixture.client.recordingResponses.set(
    "realization-1",
    recording.promise,
  );
  let presentationCurrent = true;

  const activation = fixture.controller.activate(
    definition.id,
    undefined,
    undefined,
    undefined,
    undefined,
    () => presentationCurrent,
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  while (!fixture.client.lifecycle.includes("record:realization-1")) {
    await Promise.resolve();
  }
  presentationCurrent = false;
  recording.resolve("invalidAuthority");

  await assert.rejects(
    activation,
    /Navigation posting record rejected the committed authority/,
  );
  assert.deepEqual(fixture.presentationCurrent, [true]);
  assert.deepEqual(fixture.clearPresentationCurrent, [false]);
  assert.deepEqual(fixture.client.activationCompletions, [{
    receipt: "receipt-1",
    succeeded: false,
    failure: "Navigation posting record rejected the committed authority.",
  }]);
});

test("activation passes endpoint credentials without retaining them in controller state", async () => {
  const fixture = createFixture();
  const definition = fixture.controller.retain({
    label: "Private",
    canonicalLocation: "/private",
    canonicalPacket: "packet-private",
  });
  const secret = "session-only-secret";

  const activation = fixture.controller.activate(
    definition.id,
    undefined,
    undefined,
    undefined,
    {
      "https://nuget.pkg.github.com/example/index.json": {
        username: "example-user",
        pat: secret,
      },
    },
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  await activation;

  assert.deepEqual(
    fixture.client.packageSourceCredentialPayloads,
    [
      JSON.stringify({
        "https://nuget.pkg.github.com/example/index.json": {
          username: "example-user",
          pat: secret,
        },
      }),
    ],
  );
  assert.doesNotMatch(JSON.stringify(fixture.controller.state), /session-only-secret/);
});

test("consumer rejection cancels before cutover", async () => {
  const fixture = createFixture();
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(
    definition.id,
    () => false,
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  const result = await activation;

  assert.equal(result.status, "superseded");
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.equal(fixture.controller.state.lastFailure, null);
  assert.deepEqual(fixture.posted, []);
  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-1"]);
  assert.deepEqual(fixture.client.activationCompletions, []);
});

test("unknown cancellation outcome retains deletion barrier until retry", async () => {
  const fixture = createFixture();
  const cancellation =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.cancellationResponses.push(cancellation.promise);
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(
    definition.id,
    () => false,
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  cancellation.reject(new Error("Cancellation response was lost."));

  await assert.rejects(activation, /Cancellation response was lost/);
  assert.deepEqual(
    fixture.controller.state.unsettledDefinitionIds,
    [definition.id],
  );
  await assert.rejects(
    fixture.controller.delete(definition.id),
    /cannot be deleted until its activation settles/,
  );

  assert.equal(fixture.controller.cancelPending(), true);
  await new Promise(resolve => setImmediate(resolve));

  assert.deepEqual(
    fixture.client.cancelledReceipts,
    ["receipt-1", "receipt-1"],
  );
  assert.deepEqual(fixture.controller.state.unsettledDefinitionIds, []);
  await fixture.controller.delete(definition.id);
  assert.deepEqual(fixture.controller.state.definitions, []);
});

test("overlapping unknown cancellations retain every receipt until retry", async () => {
  const fixture = createFixture();
  const firstCancellation =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  const secondCancellation =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.cancellationResponses.push(
    secondCancellation.promise,
    firstCancellation.promise,
  );
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const firstAcceptance = deferred<boolean>();

  const selectFirst = fixture.controller.activate(
    first.id,
    () => firstAcceptance.promise,
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));

  const selectSecond = fixture.controller.activate(
    second.id,
    () => false,
  );
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  secondCancellation.reject(new Error("Second cancellation response was lost."));
  await assert.rejects(selectSecond, /Second cancellation response was lost/);

  firstAcceptance.resolve(true);
  await new Promise(resolve => setImmediate(resolve));
  firstCancellation.reject(new Error("First cancellation response was lost."));
  await assert.rejects(selectFirst, /First cancellation response was lost/);

  assert.deepEqual(
    fixture.controller.state.unsettledDefinitionIds,
    [first.id, second.id],
  );
  await assert.rejects(
    fixture.controller.activate(first.id),
    /cancellation settlement is unknown/,
  );

  assert.equal(fixture.controller.cancelPending(), true);
  await new Promise(resolve => setImmediate(resolve));

  assert.deepEqual(
    fixture.client.cancelledReceipts,
    ["receipt-2", "receipt-1", "receipt-2", "receipt-1"],
  );
  assert.deepEqual(fixture.controller.state.unsettledDefinitionIds, []);
  await fixture.controller.delete(first.id);
  await fixture.controller.delete(second.id);
  assert.deepEqual(fixture.controller.state.definitions, []);
});

test("cancelling current activation also retries older uncertain receipt", async () => {
  const fixture = createFixture();
  const firstCancellation =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.cancellationResponses.push(firstCancellation.promise);
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const firstAcceptance = deferred<boolean>();
  const secondAcceptance = deferred<boolean>();

  const selectFirst = fixture.controller.activate(
    first.id,
    () => firstAcceptance.promise,
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));

  const selectSecond = fixture.controller.activate(
    second.id,
    () => secondAcceptance.promise,
  );
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));

  firstAcceptance.resolve(true);
  await new Promise(resolve => setImmediate(resolve));
  firstCancellation.reject(new Error("First cancellation response was lost."));
  await assert.rejects(selectFirst, /First cancellation response was lost/);

  assert.equal(fixture.controller.cancelPending(), true);
  secondAcceptance.resolve(true);
  const secondResult = await selectSecond;
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(secondResult.status, "superseded");
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.deepEqual(fixture.controller.state.unsettledDefinitionIds, []);
  assert.deepEqual(
    fixture.client.cancelledReceipts,
    ["receipt-1", "receipt-1", "receipt-2"],
  );
});

test("cancellation before preparation prevents cutover", async () => {
  const fixture = createFixture();
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(definition.id);
  assert.equal(fixture.controller.cancelPending(), true);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  await activation;

  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.deepEqual(fixture.posted, []);
  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-1"]);
});

test("explicit cancellation consumes its prepared receipt once", async () => {
  const fixture = createFixture();
  const acceptance = deferred<boolean>();
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(
    definition.id,
    () => acceptance.promise,
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(fixture.controller.cancelPending(), true);
  acceptance.resolve(true);
  await activation;

  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-1"]);
  assert.equal(fixture.controller.state.activeDefinitionId, null);
});

test("acceptance failure preserves candidate cleanup failure", async () => {
  const fixture = createFixture();
  fixture.client.cancellationResponse = {
    status: "failed",
    posting: null,
    failure: {
      kind: "CleanupFailed",
      message: "Injected candidate cleanup failure.",
    },
  };
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(
    definition.id,
    () => {
      throw new Error("Injected acceptance failure.");
    },
  );
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });

  await assert.rejects(
    activation,
    (error: unknown) =>
      error instanceof AggregateError
      && error.errors.some(
        candidate =>
          candidate instanceof Error
          && candidate.message === "Injected acceptance failure.",
      )
      && error.errors.some(
        candidate =>
          candidate instanceof Error
          && candidate.message === "Injected candidate cleanup failure.",
      ),
  );
  assert.equal(
    fixture.controller.state.lastFailure,
    "Injected candidate cleanup failure.",
  );
  assert.equal(fixture.controller.state.activeDefinitionId, null);
});

test("post-cutover posting failure abandons authority without rolling back active identity", async () => {
  const fixture = createFixture(true);
  const definition = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const activation = fixture.controller.activate(definition.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(definition.id, "realization-1"),
    failure: null,
  });
  await assert.rejects(activation, /Injected posting failure/);

  assert.equal(fixture.controller.state.activeDefinitionId, definition.id);
  assert.equal(
    fixture.controller.state.lastFailure,
    "Injected posting failure.",
  );
  assert.equal(fixture.controller.state.pendingDefinitionId, null);
  assert.deepEqual(fixture.controller.state.unsettledDefinitionIds, []);
  assert.deepEqual(fixture.client.lifecycle, [
    "validate:realization-1",
    "post:realization-1",
    "abandon:realization-1",
  ]);
  assert.deepEqual(fixture.client.activationCompletions, [{
    receipt: "receipt-1",
    succeeded: false,
    failure: "Injected posting failure.",
  }]);
});

test("validation failure after deletion cutover cannot restore predecessor", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const validation = deferred<boolean>();
  fixture.client.validationResponses.set(
    "realization-2",
    validation.promise,
  );
  const deletion = fixture.controller.delete(first.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  validation.reject(new Error("Navigation validation failed."));

  await assert.rejects(deletion, /Navigation validation failed/);
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
  assert.equal(fixture.clears(), 1);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1"],
  );
  assert.deepEqual(fixture.client.activationCompletions.at(-1), {
    receipt: "receipt-2",
    succeeded: false,
    failure: "Navigation validation failed.",
  });
});

test("unknown commit outcome keeps the transaction barrier", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const commit =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.commitResponses.set("receipt-2", commit.promise);
  fixture.client.activationCompletionError =
    new Error("Completion response was lost.");

  const activation = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  commit.reject(new Error("Commit response was lost."));
  await assert.rejects(
    activation,
    /activation and completion reporting failed/,
  );

  assert.deepEqual(
    fixture.controller.state.unsettledDefinitionIds,
    [second.id],
  );
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.equal(fixture.clears(), 1);
  assert.notEqual(fixture.controller.waitForPendingCommit(), null);
  await assert.rejects(
    fixture.controller.activate(second.id),
    /awaiting consumer completion/,
  );
});

test("confirmed lost commit finalizes successor deletion", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const commit =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.commitResponses.set("receipt-2", commit.promise);
  const deletion = fixture.controller.delete(first.id, {
    successorDefinitionId: second.id,
  });
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  commit.reject(new Error("Commit response was lost."));

  await assert.rejects(deletion, /Commit response was lost/);
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
  assert.equal(fixture.clears(), 1);
  assert.equal(fixture.controller.waitForPendingCommit(), null);
  assert.deepEqual(fixture.client.activationCompletions.at(-1), {
    receipt: "receipt-2",
    succeeded: false,
    failure: "Commit response was lost.",
  });
});

test("confirmed failed completion reconciles a lost commit response", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const commit =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.commitResponses.set("receipt-2", commit.promise);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  commit.reject(new Error("Commit response was lost."));
  await assert.rejects(selectSecond, /Commit response was lost/);

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.equal(fixture.clears(), 1);
  assert.equal(fixture.controller.waitForPendingCommit(), null);
  assert.deepEqual(fixture.client.activationCompletions.at(-1), {
    receipt: "receipt-2",
    succeeded: false,
    failure: "Commit response was lost.",
  });

  const deletion = fixture.controller.delete(second.id, {
    successorDefinitionId: null,
  });
  assert.deepEqual(fixture.client.deactivations, [second.id]);
  fixture.client.deactivationResponses[0]!.resolve({
    status: "deactivated",
    completionReceipt: "deactivation-1",
    settlement: {
      succeeded: true,
      reason: "CoordinatorClosed",
      failure: null,
    },
    message: null,
  });
  await deletion;
});

test("commit barrier remains held through consumer completion", async () => {
  const fixture = createFixture();
  const staleAcknowledgement = deferred<string>();
  fixture.client.acknowledgementResponses.set(
    "realization-1",
    staleAcknowledgement.promise,
  );
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.ok(
    fixture.client.lifecycle.includes("acknowledge:realization-1"),
  );

  await assert.rejects(
    fixture.controller.activate(second.id),
    /awaiting consumer completion/,
  );
  assert.equal(fixture.client.activations.length, 1);
  staleAcknowledgement.resolve("accepted");
  await selectFirst;

  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.equal(fixture.controller.state.pendingDefinitionId, null);
  assert.equal(fixture.controller.state.lastFailure, null);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1", "realization-2"],
  );
  assert.deepEqual(
    fixture.client.activationCompletions.map(value => value.receipt),
    ["receipt-1", "receipt-2"],
  );
  assert.ok(
    !fixture.client.lifecycle.includes("abandon:realization-1"),
  );
});

test("recording holds the commit barrier through consumer completion", async () => {
  const fixture = createFixture();
  const staleRecording = deferred<string>();
  fixture.client.recordingResponses.set(
    "realization-1",
    staleRecording.promise,
  );
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.ok(fixture.client.lifecycle.includes("record:realization-1"));

  await assert.rejects(
    fixture.controller.activate(second.id),
    /awaiting consumer completion/,
  );
  assert.equal(fixture.client.activations.length, 1);
  staleRecording.resolve("accepted");
  await selectFirst;

  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.equal(fixture.controller.state.pendingDefinitionId, null);
  assert.equal(fixture.controller.state.lastFailure, null);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1", "realization-2"],
  );
  assert.ok(fixture.client.lifecycle.includes("acknowledge:realization-1"));
});

test("superseded activation cannot post over the latest selection", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[0]!.resolve({
    status: "superseded",
    posting: null,
    failure: null,
  });
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(
      second.id,
      "realization-2",
      "settlement-1",
    ),
    failure: null,
  });
  await Promise.all([selectFirst, selectSecond]);
  await Promise.resolve();

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-2"],
  );
  assert.deepEqual(fixture.client.settlements, ["settlement-1"]);
  assert.equal(fixture.settled.length, 1);
});

test("publication order cancels a late preparation before cutover", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-2"],
  );
  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-1"]);
});

test("queued newer preparation supersedes before validation and cutover", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await Promise.all([selectFirst, selectSecond]);

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-2"],
  );
  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-1"]);
});

test("late publication still observes distinct predecessor settlement", async () => {
  const fixture = createFixture();
  const initial = fixture.controller.retain({
    label: "Initial",
    canonicalLocation: "/initial",
    canonicalPacket: "packet-initial",
  });
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectInitial = fixture.controller.activate(initial.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(initial.id, "realization-1"),
    failure: null,
  });
  await selectInitial;

  const selectFirst = fixture.controller.activate(first.id);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[2]!.resolve({
    status: "activated",
    posting: posting(
      second.id,
      "realization-3",
      "settlement-a",
    ),
    failure: null,
  });
  await selectSecond;
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(
      first.id,
      "realization-2",
      "settlement-initial",
    ),
    failure: null,
  });
  await selectFirst;
  await Promise.resolve();

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1", "realization-3"],
  );
  assert.deepEqual(
    fixture.client.settlements,
    ["settlement-a"],
  );
  assert.equal(fixture.settled.length, 1);
  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-2"]);
});

test("out-of-order predecessor outcomes preserve originating posting association", async () => {
  const fixture = createFixture();
  const firstSettlement =
    deferred<BrowserRetainedWorkspaceSettlementResult>();
  const secondSettlement =
    deferred<BrowserRetainedWorkspaceSettlementResult>();
  fixture.client.settlementResponses.set(
    "settlement-a",
    firstSettlement.promise,
  );
  fixture.client.settlementResponses.set(
    "settlement-b",
    secondSettlement.promise,
  );
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const third = fixture.controller.retain({
    label: "C",
    canonicalLocation: "/c",
    canonicalPacket: "packet-c",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(
      second.id,
      "realization-2",
      "settlement-a",
    ),
    failure: null,
  });
  await selectSecond;
  const selectThird = fixture.controller.activate(third.id);
  fixture.client.activations[2]!.resolve({
    status: "activated",
    posting: posting(
      third.id,
      "realization-3",
      "settlement-b",
    ),
    failure: null,
  });
  await selectThird;

  const secondFailure = new Error("B cleanup observation failed.");
  secondSettlement.reject(secondFailure);
  firstSettlement.resolve({
    status: "settled",
    settlement: {
      succeeded: true,
      reason: "Replaced",
      failure: null,
    },
  });
  await Promise.allSettled([
    firstSettlement.promise,
    secondSettlement.promise,
  ]);
  await Promise.resolve();

  assert.deepEqual(fixture.settled, [{
    observation: {
      retainedDefinitionId: second.id,
      realizationId: "realization-2",
      settlementId: "settlement-a",
    },
    result: {
      status: "settled",
      settlement: {
        succeeded: true,
        reason: "Replaced",
        failure: null,
      },
    },
  }]);
  assert.deepEqual(fixture.observationFailures, [{
    observation: {
      retainedDefinitionId: third.id,
      realizationId: "realization-3",
      settlementId: "settlement-b",
    },
    error: secondFailure,
  }]);
});

test("repeated no-effect evidence observes predecessor once", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const secondPosting = posting(
    second.id,
    "realization-2",
    "settlement-first",
  );
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: secondPosting,
    failure: null,
  });
  await selectSecond;
  const lifecycleAfterPosting = [...fixture.client.lifecycle];

  const selectSecondAgain = fixture.controller.activate(second.id);
  fixture.client.activations[2]!.resolve({
    status: "noEffect",
    posting: secondPosting,
    failure: null,
  });
  await selectSecondAgain;
  await Promise.resolve();

  assert.deepEqual(fixture.client.settlements, ["settlement-first"]);
  assert.equal(fixture.settled.length, 1);
  assert.deepEqual(fixture.client.lifecycle, lifecycleAfterPosting);
});

test("displaced activation blocks deletion and exposes cleanup failure", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  const selectSecond = fixture.controller.activate(second.id);

  await assert.rejects(
    fixture.controller.delete(first.id),
    /cannot be deleted until its activation settles/,
  );

  fixture.client.activations[0]!.resolve({
    status: "failed",
    posting: null,
    failure: {
      kind: "CleanupFailed",
      message: "A cleanup failed.",
    },
  });
  await selectFirst;

  assert.equal(fixture.controller.state.lastFailure, "A cleanup failed.");
  assert.deepEqual(
    fixture.controller.state.unsettledDefinitionIds,
    [second.id],
  );

  await fixture.controller.delete(first.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
  assert.equal(fixture.controller.state.pendingDefinitionId, second.id);

  fixture.client.activations[1]!.resolve({
    status: "superseded",
    posting: null,
    failure: null,
  });
  await selectSecond;
});

test("sole-active deletion cannot revive retired authority", async () => {
  const fixture = createFixture();
  const completion = deferred<void>();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id, {
    successorDefinitionId: null,
    completeDeactivation: () => completion.promise,
  });
  fixture.client.deactivationResponses[0]!.resolve({
    status: "deactivated",
    completionReceipt: "deactivation-1",
    settlement: {
      succeeded: true,
      reason: "CoordinatorClosed",
      failure: null,
    },
    message: null,
  });
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.deepEqual(fixture.controller.state.definitions, []);
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  await assert.rejects(
    fixture.controller.activate(second.id),
    /being deactivated/,
  );
  await assert.rejects(
    fixture.controller.activate(first.id),
    /Unknown retained Workspace definition/,
  );
  assert.deepEqual(fixture.client.deactivationCompletions, []);

  completion.resolve();
  await deletion;
  assert.deepEqual(fixture.client.deactivationCompletions, [{
    receipt: "deactivation-1",
    succeeded: true,
    failure: null,
  }]);
  assert.equal(fixture.controller.state.activeDefinitionId, null);

  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
});

test("overlapping activations retain deletion barrier until all settle", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });

  const firstSelection = fixture.controller.activate(first.id);
  const secondSelection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "superseded",
    posting: null,
    failure: null,
  });
  await firstSelection;

  assert.deepEqual(
    fixture.controller.state.unsettledDefinitionIds,
    [first.id],
  );
  await assert.rejects(
    fixture.controller.delete(first.id),
    /cannot be deleted until its activation settles/,
  );

  fixture.client.activations[1]!.resolve({
    status: "failed",
    posting: null,
    failure: {
      kind: "InvalidPacket",
      message: "Invalid packet.",
    },
  });
  await secondSelection;

  assert.deepEqual(fixture.controller.state.unsettledDefinitionIds, []);
  await fixture.controller.delete(first.id);
  assert.deepEqual(fixture.controller.state.definitions, []);
});

test("failed activation preserves the incumbent definition", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "failed",
    posting: null,
    failure: {
      kind: "InvalidPacket",
      message: "Packet format 1 is unsupported.",
    },
  });
  await selectSecond;

  assert.equal(fixture.controller.state.activeDefinitionId, first.id);
  assert.equal(
    fixture.controller.state.lastFailure,
    "Packet format 1 is unsupported.",
  );
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1"],
  );
});

test("active deletion activates the next definition before removal", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await deletion;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
});

test("waiting activation cannot revoke committed deletion", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const successorCompletion = deferred<void>();
  const deletion = fixture.controller.delete(first.id, {
    completeSuccessor: () => successorCompletion.promise,
  });
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));

  const barrier = fixture.controller.waitForPendingCommit();
  assert.notEqual(barrier, null);
  const reselectDeleted = barrier!.then(
    () => fixture.controller.activate(first.id),
  );
  successorCompletion.resolve();
  await deletion;

  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
  await assert.rejects(reselectDeleted, /Unknown retained Workspace definition/);
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
});

test("active deletion removes retired definition after cutover failure", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id, {
    completeSuccessor: () => {
      throw new Error("Successor presentation failed.");
    },
  });
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });

  await assert.rejects(deletion, /Successor presentation failed/);
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
  assert.deepEqual(fixture.client.activationCompletions.at(-1), {
    receipt: "receipt-2",
    succeeded: false,
    failure: "Successor presentation failed.",
  });
});

test("active deletion cannot overwrite an in-flight commit barrier", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const delayedCommit =
    deferred<BrowserRetainedWorkspaceActivationResult>();
  fixture.client.commitResponses.set("receipt-2", delayedCommit.promise);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await new Promise(resolve => setImmediate(resolve));

  await assert.rejects(
    fixture.controller.delete(first.id, {
      successorDefinitionId: null,
    }),
    /awaiting consumer completion/,
  );
  delayedCommit.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;

  assert.equal(fixture.controller.waitForPendingCommit(), null);
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
});

test("active deletion cannot overlap another prepared activation", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const selectSecond = fixture.controller.activate(second.id);
  await assert.rejects(
    fixture.controller.delete(first.id, {
      successorDefinitionId: null,
    }),
    /until pending activations settle/,
  );
  assert.deepEqual(fixture.client.deactivations, []);

  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;
  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
});

test("active definition cannot be its own deletion successor", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  await assert.rejects(
    fixture.controller.delete(first.id, {
      successorDefinitionId: first.id,
    }),
    /cannot be its own deletion successor/,
  );
  assert.equal(fixture.controller.state.activeDefinitionId, first.id);
  assert.deepEqual(fixture.controller.state.definitions, [first]);
  assert.equal(fixture.client.activations.length, 1);
});

test("newer selection cancels successor deletion commit", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id);
  const reselectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await deletion;

  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [first.id, second.id],
  );
  assert.deepEqual(
    fixture.controller.state.unsettledDefinitionIds,
    [first.id],
  );

  fixture.client.activations[2]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-3"),
    failure: null,
  });
  await reselectFirst;

  assert.equal(fixture.controller.state.activeDefinitionId, first.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [first.id, second.id],
  );
  assert.deepEqual(
    fixture.posted.map(value => value.realizationId),
    ["realization-1", "realization-3"],
  );
  assert.deepEqual(fixture.client.cancelledReceipts, ["receipt-2"]);
});

test("deleting the sole active definition drains managed state", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.deactivationResponses[0]!.resolve({
    status: "deactivated",
    completionReceipt: "deactivation-1",
    settlement: {
      succeeded: true,
      reason: "CoordinatorClosed",
      failure: null,
    },
    message: null,
  });
  await deletion;

  assert.deepEqual(fixture.client.deactivations, [first.id]);
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.deepEqual(fixture.controller.state.definitions, []);
  assert.equal(fixture.clears(), 1);
});

test("failed sole-active cleanup clears unavailable presentation and preserves evidence", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.deactivationResponses[0]!.resolve({
    status: "cleanupFailed",
    completionReceipt: "deactivation-1",
    settlement: {
      succeeded: false,
      reason: "CoordinatorClosed",
      failure: "Injected cleanup failure.",
    },
    message: "The active Workspace could not be settled.",
  });
  await deletion;

  assert.deepEqual(fixture.controller.state.definitions, []);
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.equal(
    fixture.controller.state.lastFailure,
    "Injected cleanup failure.",
  );
  assert.equal(fixture.clears(), 1);
});

test("sole-active cleanup and clear failures remain visible together", async () => {
  const fixture = createFixture(false, true);
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.deactivationResponses[0]!.resolve({
    status: "cleanupFailed",
    completionReceipt: "deactivation-1",
    settlement: {
      succeeded: false,
      reason: "CoordinatorClosed",
      failure: "Injected managed cleanup failure.",
    },
    message: "The active Workspace could not be settled.",
  });
  await assert.rejects(
    deletion,
    (error: unknown) =>
      error instanceof AggregateError
      && error.errors.some(
        candidate =>
          candidate instanceof Error
          && candidate.message === "Injected managed cleanup failure.",
      )
      && error.errors.some(
        candidate =>
          candidate instanceof Error
          && candidate.message === "Injected clear failure.",
      ),
  );

  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.deepEqual(fixture.controller.state.definitions, []);
  assert.equal(
    fixture.controller.state.lastFailure,
    "Injected managed cleanup failure. Consumer completion failed: "
      + "Injected clear failure.",
  );
  assert.deepEqual(fixture.client.deactivationCompletions, [{
    receipt: "deactivation-1",
    succeeded: false,
    failure: "Injected managed cleanup failure. Consumer completion failed: "
      + "Injected clear failure.",
  }]);
});

test("pre-close deactivation rejection preserves active presentation", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.deactivationResponses[0]!.resolve({
    status: "rejected",
    completionReceipt: null,
    settlement: null,
    message: "Deactivation did not begin.",
  });
  await deletion;

  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [first.id],
  );
  assert.equal(fixture.controller.state.activeDefinitionId, first.id);
  assert.equal(
    fixture.controller.state.lastFailure,
    "Deactivation did not begin.",
  );
  assert.equal(fixture.clears(), 0);
});

test("unknown deactivation outcome keeps the transition barrier", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selection = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  const deletion = fixture.controller.delete(first.id, {
    successorDefinitionId: null,
  });
  fixture.client.deactivationResponses[0]!.reject(
    new Error("Deactivation response was lost."),
  );
  await assert.rejects(deletion, /Deactivation response was lost/);

  assert.equal(
    fixture.controller.state.lastFailure,
    "Deactivation response was lost.",
  );
  assert.equal(
    fixture.controller.state.deactivatingDefinitionId,
    first.id,
  );
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.equal(fixture.clears(), 1);
  assert.notEqual(fixture.controller.waitForPendingCommit(), null);
  await assert.rejects(
    fixture.controller.activate(first.id),
    /being deactivated/,
  );
  await assert.rejects(
    fixture.controller.delete(first.id),
    /already being deactivated/,
  );
});

test("sole active deactivation blocks activation and preserves new definitions", async () => {
  const fixture = createFixture();
  const first = fixture.controller.retain({
    label: "A",
    canonicalLocation: "/a",
    canonicalPacket: "packet-a",
  });
  const selectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[0]!.resolve({
    status: "activated",
    posting: posting(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id);
  const second = fixture.controller.retain({
    label: "B",
    canonicalLocation: "/b",
    canonicalPacket: "packet-b",
  });

  assert.equal(
    fixture.controller.state.deactivatingDefinitionId,
    first.id,
  );
  await assert.rejects(
    fixture.controller.delete(first.id),
    /already being deactivated/,
  );
  await assert.rejects(
    fixture.controller.activate(second.id),
    /cannot be activated while the active Workspace is being deactivated/,
  );
  assert.equal(fixture.client.activations.length, 1);

  fixture.client.deactivationResponses[0]!.resolve({
    status: "deactivated",
    completionReceipt: "deactivation-1",
    settlement: {
      succeeded: true,
      reason: "CoordinatorClosed",
      failure: null,
    },
    message: null,
  });
  await deletion;

  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
  assert.equal(fixture.controller.state.activeDefinitionId, null);
  assert.equal(fixture.controller.state.deactivatingDefinitionId, null);
  assert.equal(fixture.clears(), 1);

  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    posting: posting(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
});

test("retained definitions are resource-free, bounded, and stable", () => {
  const fixture = createFixture();
  const ids: string[] = [];
  for (
    let index = 0;
    index < MAX_RETAINED_WORKSPACE_DEFINITIONS;
    index++
  ) {
    ids.push(fixture.controller.retain({
      label: `Workspace ${index}`,
      canonicalLocation: `/workspace/${index}`,
      canonicalPacket: `packet-${index}`,
    }).id);
  }

  assert.deepEqual(ids, [
    "workspace-definition-1",
    "workspace-definition-2",
    "workspace-definition-3",
    "workspace-definition-4",
  ]);
  assert.throws(
    () => fixture.controller.retain({
      label: "Workspace 5",
      canonicalLocation: "/workspace/5",
      canonicalPacket: "packet-5",
    }),
    /at most 4 Workspace definitions/,
  );
});
