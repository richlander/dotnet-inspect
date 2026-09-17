import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceDeactivationResult,
  BrowserRetainedWorkspaceInstallation,
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

function installation(
  retainedDefinitionId: string,
  realizationId: string,
  settlementId: string | null = null,
): BrowserRetainedWorkspaceInstallation {
  return {
    retainedDefinitionId,
    label: retainedDefinitionId,
    canonicalLocation: `/inspect/${retainedDefinitionId}`,
    canonicalPacket: `packet-${retainedDefinitionId}`,
    realizationId,
    publicationOrdinal: Number(realizationId.split("-").at(-1)),
    navigation: {
      activeStateIndex: null,
      states: [],
    },
    predecessor: settlementId === null
      ? null
      : {
        settlementId,
        reason: "Replaced",
      },
  };
}

class ActivationClient implements RetainedWorkspaceActivationClient {
  readonly activations: Array<{
    promise: Promise<BrowserRetainedWorkspaceActivationResult>;
    resolve(value: BrowserRetainedWorkspaceActivationResult): void;
  }> = [];
  readonly settlements: string[] = [];
  readonly settlementResponses = new Map<
    string,
    Promise<BrowserRetainedWorkspaceSettlementResult>
  >();
  readonly deactivations: string[] = [];
  readonly deactivationResponses: Array<{
    promise: Promise<BrowserRetainedWorkspaceDeactivationResult>;
    resolve(value: BrowserRetainedWorkspaceDeactivationResult): void;
  }> = [];

  activateRetainedWorkspaceDefinition():
  Promise<BrowserRetainedWorkspaceActivationResult> {
    let resolve!: (value: BrowserRetainedWorkspaceActivationResult) => void;
    const promise =
      new Promise<BrowserRetainedWorkspaceActivationResult>(accept => {
        resolve = accept;
      });
    const activation = { promise, resolve };
    this.activations.push(activation);
    return activation.promise;
  }

  deactivateRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceDeactivationResult> {
    this.deactivations.push(retainedDefinitionId);
    let resolve!: (
      value: BrowserRetainedWorkspaceDeactivationResult,
    ) => void;
    const promise =
      new Promise<BrowserRetainedWorkspaceDeactivationResult>(accept => {
        resolve = accept;
      });
    this.deactivationResponses.push({
      promise,
      resolve,
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
}

function createFixture() {
  const client = new ActivationClient();
  const installed: BrowserRetainedWorkspaceInstallation[] = [];
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
    install: value => installed.push(value),
    clear: () => clears++,
    predecessorSettled: (observation, result) =>
      settled.push({ observation, result }),
    predecessorObservationFailed: (observation, error) =>
      observationFailures.push({ observation, error }),
  });
  return {
    client,
    controller,
    installed,
    settled,
    observationFailures,
    clears: () => clears,
  };
}

test("superseded activation cannot install over the latest selection", async () => {
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
    installation: null,
    failure: null,
  });
  fixture.client.activations[1]!.resolve({
    status: "activated",
    installation: installation(
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
    fixture.installed.map(value => value.realizationId),
    ["realization-2"],
  );
  assert.deepEqual(fixture.client.settlements, ["settlement-1"]);
  assert.equal(fixture.settled.length, 1);
});

test("publication order rejects a late response from an older cutover", async () => {
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
    installation: installation(second.id, "realization-2"),
    failure: null,
  });
  await selectSecond;
  fixture.client.activations[0]!.resolve({
    status: "activated",
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.installed.map(value => value.realizationId),
    ["realization-2"],
  );
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
    installation: installation(initial.id, "realization-1"),
    failure: null,
  });
  await selectInitial;

  const selectFirst = fixture.controller.activate(first.id);
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[2]!.resolve({
    status: "activated",
    installation: installation(
      second.id,
      "realization-3",
      "settlement-a",
    ),
    failure: null,
  });
  await selectSecond;
  fixture.client.activations[1]!.resolve({
    status: "activated",
    installation: installation(
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
    fixture.installed.map(value => value.realizationId),
    ["realization-1", "realization-3"],
  );
  assert.deepEqual(
    fixture.client.settlements,
    ["settlement-a", "settlement-initial"],
  );
  assert.equal(fixture.settled.length, 2);
});

test("out-of-order predecessor outcomes preserve originating installation association", async () => {
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
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    installation: installation(
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
    installation: installation(
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
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const secondInstallation = installation(
    second.id,
    "realization-2",
    "settlement-first",
  );
  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    installation: secondInstallation,
    failure: null,
  });
  await selectSecond;

  const selectSecondAgain = fixture.controller.activate(second.id);
  fixture.client.activations[2]!.resolve({
    status: "noEffect",
    installation: secondInstallation,
    failure: null,
  });
  await selectSecondAgain;
  await Promise.resolve();

  assert.deepEqual(fixture.client.settlements, ["settlement-first"]);
  assert.equal(fixture.settled.length, 1);
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
    installation: null,
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
    installation: null,
    failure: null,
  });
  await selectSecond;
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
    installation: null,
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
    installation: null,
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
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const selectSecond = fixture.controller.activate(second.id);
  fixture.client.activations[1]!.resolve({
    status: "failed",
    installation: null,
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
    fixture.installed.map(value => value.realizationId),
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
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    installation: installation(second.id, "realization-2"),
    failure: null,
  });
  await deletion;

  assert.equal(fixture.controller.state.activeDefinitionId, second.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [second.id],
  );
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
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selectFirst;

  const deletion = fixture.controller.delete(first.id);
  const reselectFirst = fixture.controller.activate(first.id);
  fixture.client.activations[1]!.resolve({
    status: "activated",
    installation: installation(second.id, "realization-2"),
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
    installation: installation(first.id, "realization-3"),
    failure: null,
  });
  await reselectFirst;

  assert.equal(fixture.controller.state.activeDefinitionId, first.id);
  assert.deepEqual(
    fixture.controller.state.definitions.map(value => value.id),
    [first.id, second.id],
  );
  assert.deepEqual(
    fixture.installed.map(value => value.realizationId),
    ["realization-1", "realization-2", "realization-3"],
  );
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
    installation: installation(first.id, "realization-1"),
    failure: null,
  });
  await selection;

  const deletion = fixture.controller.delete(first.id);
  fixture.client.deactivationResponses[0]!.resolve({
    status: "deactivated",
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
    installation: installation(first.id, "realization-1"),
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
    installation: installation(second.id, "realization-2"),
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
