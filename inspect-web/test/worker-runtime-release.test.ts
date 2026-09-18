import assert from "node:assert/strict";
import test from "node:test";
import {
  createOperationAuthorityPage,
  type OperationHandle,
  type OperationProducerAdapter,
  type OperationProducerSink,
  type OperationStartResult,
  type PreparedOperationProducer,
} from "../src/operation-authority.ts";
import {
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerProbeSequenceAllocator,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
  type FakeWorkerOperationContext,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";

import {
  type TestDiagnostic,
  type TestSession,
  type TestEvent,
  type TestSettlement,
  uncanceledOperation,
  type TestHarness,
  deferred,
  terminalCallbacks,
  diagnosticDecoder,
  createHarness,
  session,
  started,
  captureIdentities,
  captureIdentity,
  preparedBinding,
  workerEnvelope,
  operationMessages,
  startReady,
} from "./worker-runtime-test-fixture.ts";

test("failed fake realm continues physical release evidence", async () => {
  const firstSettlement = deferred<TestSettlement>();
  const secondSettlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: input => input === "first"
      ? firstSettlement.promise
      : secondSettlement.promise,
  });
  await startReady(harness);
  assert.equal(
    harness.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  const page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `failed-drain-${id++}`;
      })(),
    },
  });
  const firstSession = session(harness.adapter, page);
  const secondSession = session(harness.adapter, page);
  started(firstSession.session.start("first", harness.adapter));
  const secondHandle = started(
    secondSession.session.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();

  firstSettlement.reject(new Error("managed failure"));
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "worker-declared");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.host.snapshot().activeOperations, 2);
  assert.equal(harness.host.snapshot().activeEpochWork, 1);

  secondSettlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  await secondHandle.quiesced;

  assert.equal(harness.workers[0]!.activeOperationCount, 1);
  assert.equal(harness.host.snapshot().activeOperations, 1);
  assert.equal(
    harness.workers[0]!.finishEpochWork(1),
    true,
  );
  assert.equal(harness.host.snapshot().activeEpochWork, 0);
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.deepEqual(
    harness.workers[0]!.emittedMessages.slice(-3).map(message => message.kind),
    ["epoch-failed", "settled", "epoch-work-finished"],
  );
});

test("main epoch-work unmatched and duplicate finishes fail closed", async () => {
  const unmatched = createHarness();
  await startReady(unmatched);
  unmatched.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  assert.equal(unmatched.failures[0]?.kind, "protocol");

  const duplicate = createHarness();
  await startReady(duplicate);
  duplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  duplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  duplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  assert.equal(duplicate.failures[0]?.kind, "protocol");
});

test("speculative producer lease releases while cache survives until restart", async () => {
  const speculativeContext: {
    current: FakeWorkerOperationContext | null;
  } = { current: null };
  const harness = createHarness({
    workerCount: 2,
    producerClassDefinitions: [{
      name: "speculative",
      allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
      structuralBoundMilliseconds: 30,
    }],
    invoke: (input, context) => {
      if (input === "prime") {
        speculativeContext.current = context;
        assert.equal(context.startEpochWork("speculative", 1), true);
        return { kind: "succeeded", value: "initial" };
      }
      const cached = context.cache.get("index");
      return {
        kind: "succeeded",
        value: typeof cached === "string" ? cached : "cache-miss",
      };
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `cache-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const firstHandle = started(
    first.session.start("prime", harness.adapter),
  );
  assert.deepEqual(await firstHandle.outcome, {
    kind: "succeeded",
    value: "initial",
  });
  await firstHandle.quiesced;
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  const retainedContext = speculativeContext.current;
  if (retainedContext === null)
    throw new Error("Speculative context was not retained.");
  retainedContext.cache.set("index", "cached-index");
  assert.equal(retainedContext.finishEpochWork(1), true);
  assert.equal(harness.host.snapshot().activeEpochWork, 0);

  const second = session(harness.adapter, authority);
  const secondHandle = started(
    second.session.start("consume", harness.adapter),
  );
  assert.deepEqual(await secondHandle.outcome, {
    kind: "succeeded",
    value: "cached-index",
  });

  harness.host.restart();
  assert.equal(harness.workers[0]!.cache.size, 0);
  assert.equal(harness.host.start("replacement").kind, "started");
  await harness.environment.flushAsync();
  const third = session(harness.adapter, authority);
  const thirdHandle = started(
    third.session.start("consume", harness.adapter),
  );
  assert.deepEqual(await thirdHandle.outcome, {
    kind: "succeeded",
    value: "cache-miss",
  });
});

test("hard termination revokes retained fake-worker operation contexts", async () => {
  const settlement = deferred<TestSettlement>();
  const retainedContext: {
    current: FakeWorkerOperationContext | null;
  } = { current: null };
  const harness = createHarness({
    invoke: (_input, context) => {
      retainedContext.current = context;
      return settlement.promise;
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  started(operationSession.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  const context = retainedContext.current;
  if (context === null)
    throw new Error("Expected a retained operation context.");

  harness.host.restart();

  assert.equal(
    context.startEpochWork("speculative", 1),
    false,
  );
  assert.equal(context.cache.set("late", "value"), false);
  assert.equal(harness.workers[0]!.activeEpochWorkCount, 0);
  assert.equal(harness.workers[0]!.cache.size, 0);
});

test("idle-compatible capabilities are opaque and only issued within the idle bound", () => {
  const registry = new WorkerProducerClassRegistry(10);
  const compatible = registry.register(
    "yielding",
    { kind: "bounded", maxSilentActiveMilliseconds: 10 },
    8,
  );
  const overBudget = registry.register(
    "slow",
    { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    20,
  );
  const unbounded = registry.register(
    "unbounded",
    { kind: "unbounded" },
    8,
  );
  assert.equal(compatible.kind, "idle-compatible");
  if (compatible.kind !== "idle-compatible")
    throw new Error("Expected an idle-compatible capability.");
  assert.equal(registry.acceptsCapability(compatible.capability), true);
  assert.deepEqual(overBudget, { kind: "epoch-work-required" });
  assert.deepEqual(unbounded, { kind: "epoch-work-required" });
  assert.deepEqual(registry.classify("missing"), {
    kind: "epoch-work-required",
  });
  assert.deepEqual(registry.classify("slow"), {
    kind: "epoch-work-required",
  });
});

test("producer class registration rejects evidence beyond its allowance", () => {
  const registry = new WorkerProducerClassRegistry(10);

  assert.throws(
    () => registry.register(
      "invalid",
      { kind: "bounded", maxSilentActiveMilliseconds: 5 },
      6,
    ),
    /must not exceed the producer allowance/,
  );
});

test("runtime host requires producer classes for its exact idle allowance", () => {
  const environment = new ManualWorkerRuntimeEnvironment();

  assert.throws(
    () => new WorkerRuntimeHost<string, TestDiagnostic>({
      transport: new QueueWorkerRuntimeTransportFactory([]),
      clock: environment,
      lifecycle: environment,
      bootstrap: {
        encode: bootstrap => ({ kind: "decoded", value: bootstrap }),
        diagnostic: diagnosticDecoder(),
      },
      diagnostic: diagnosticDecoder(),
      callbacks: {
        failure: () => undefined,
        diagnostic: () => undefined,
        realmReleased: () => undefined,
      },
      createDiagnostic: (kind, detail) => ({ code: kind, detail }),
      idleHeartbeatIntervalMilliseconds: 10,
      startupBudgetMilliseconds: 100,
      controlResponseGraceMilliseconds: 10,
      drainBudgetMilliseconds: 20,
      producerClasses: new WorkerProducerClassRegistry(9),
    }),
    /must use the host idle allowance/,
  );
});

test("worker startup rejects a different producer-class idle allowance", async () => {
  const harness = createHarness({
    workerProducerClassIdleAllowanceMilliseconds: 100,
  });

  assert.equal(harness.host.start("bootstrap").kind, "started");
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "startup");
  assert.equal(harness.host.snapshot().phase, "closed");
});

test("failure notification cannot reentrantly admit a new operation", async () => {
  let harness: TestHarness;
  let retry: OperationStartResult<string, string, WorkerRuntimePreparationError>
    | null = null;
  harness = createHarness({
    failure: () => {
      const operationSession = session(harness.adapter);
      retry = operationSession.session.start("retry", harness.adapter);
    },
  });
  await startReady(harness);

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(retry, {
    kind: "rejected",
    reason: {
      kind: "producer-rejected",
      error: { kind: "epoch-unavailable" },
    },
  });
  assert.equal(
    harness.workers[0]!.receivedMessages.filter(message =>
      typeof message === "object"
      && message !== null
      && Object.getOwnPropertyDescriptor(message, "kind")?.value === "start"
    ).length,
    0,
  );
});

test("operation closure precedes a reentrant failure-observer cancellation", async () => {
  const active: {
    handle: OperationHandle<string, string> | null;
  } = { handle: null };
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    failure: () => {
      active.handle?.cancel("user");
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  active.handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(await active.handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
});

test("epoch closure seals every assigned record before sink callbacks run", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const [firstIdentity, secondIdentity] = captureIdentities(2);
  const terminals: unknown[] = [];
  let secondBinding: PreparedOperationProducer | null = null;
  const firstSink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    reportDurable: () => undefined,
    reportUnexpectedTerminal: () => undefined,
    reportUnexpectedFailure: () => undefined,
    ...terminalCallbacks(outcome => {
      terminals.push(outcome);
      secondBinding?.requestCancellation("user");
      return undefined;
    }),
    reportQuiesced: () => undefined,
  };
  const secondSink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    reportDurable: () => undefined,
    reportUnexpectedTerminal: () => undefined,
    reportUnexpectedFailure: () => undefined,
    ...terminalCallbacks(outcome => {
      terminals.push(outcome);
      return undefined;
    }),
    reportQuiesced: () => undefined,
  };
  const firstBinding = preparedBinding(
    harness.adapter.prepare(
      firstIdentity!,
      "first",
      firstSink,
      uncanceledOperation,
    ),
  );
  secondBinding = preparedBinding(
    harness.adapter.prepare(
      secondIdentity!,
      "second",
      secondSink,
      uncanceledOperation,
    ),
  );
  firstBinding.activate();
  secondBinding.activate();
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(terminals, [
    { kind: "failed", error: "boundary:protocol" },
    { kind: "failed", error: "boundary:protocol" },
  ]);
  assert.deepEqual(
    operationMessages(harness.workers[0]!),
    ["initialize", "start", "start"],
  );
});

test("epoch closure commits siblings before observer failure and publishes failure before release", async () => {
  const settlement = deferred<TestSettlement>();
  const order: string[] = [];
  let secondHandle: OperationHandle<string, string> | null = null;
  let cancelResult: ReturnType<OperationHandle<string, string>["cancel"]>
    | null = null;
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlement.promise,
    failure: failure => {
      order.push(`runtime-failure:${failure.kind}`);
    },
    realmReleased: epochToken => {
      order.push(`realm-released:${epochToken}`);
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `closure-operation-${id++}`;
      })(),
    },
  });
  const firstSession = authority.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind !== "terminal") return undefined;
        order.push("first-terminal");
        throw new Error("first terminal observer failed");
      },
    },
    diagnostic: {
      report: diagnostic => {
        assert.equal(diagnostic.kind, "feature-observer");
        order.push("feature-diagnostic");
        cancelResult = secondHandle?.cancel("user") ?? null;
        harness.host.restart();
        assert.equal(harness.workers[0]!.terminated, true);
        assert.deepEqual(harness.releasedEpochs, []);
        return undefined;
      },
    },
  });
  const secondSession = authority.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind === "terminal") order.push("second-terminal");
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const firstHandle = started(
    firstSession.start("first", harness.adapter),
  );
  secondHandle = started(
    secondSession.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(cancelResult, { kind: "no-op" });
  assert.equal(harness.failures.length, 1);
  assert.deepEqual(order, [
    "first-terminal",
    "feature-diagnostic",
    "second-terminal",
    "runtime-failure:protocol",
    "realm-released:1",
  ]);
  assert.deepEqual(await firstHandle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  assert.deepEqual(await secondHandle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  await Promise.all([firstHandle.quiesced, secondHandle.quiesced]);
});

for (const physicalResponse of ["settled", "rejected"] as const) {
  for (const outstandingCancellation of [false, true] as const) {
    const cancellationCase = outstandingCancellation
      ? " and cancellation acknowledgment"
      : "";
    test(`closure publication survives reentrant sibling ${physicalResponse}${cancellationCase}`, async () => {
      const settlement = deferred<TestSettlement>();
      const order: string[] = [];
      let harness: TestHarness;
      let secondHandle: OperationHandle<string, string> | null = null;
      harness = createHarness({
        invoke: () => settlement.promise,
        failure: failure => {
          order.push(`runtime-failure:${failure.kind}`);
        },
        omitResponse: kind =>
          (outstandingCancellation && kind === "cancel-acknowledged")
          || (physicalResponse === "rejected" && kind === "accepted"),
      });
      await startReady(harness);
      const authority = createOperationAuthorityPage({
        allocation: {
          createId: (() => {
            let id = 1;
            return () => `reentrant-physical-${id++}`;
          })(),
        },
      });
      const firstSession = authority.createSession<
        string,
        string,
        string,
        string,
        WorkerRuntimePreparationError
      >({
        feature: {
          publish: event => {
            if (event.kind !== "terminal") return undefined;
            order.push("first-terminal");
            const second = secondHandle;
            assert.notEqual(second, null);
            if (second === null) return undefined;
            harness.workers[0]!.emitRaw(workerEnvelope(1,
              physicalResponse === "settled"
                ? {
                    kind: "settled",
                    operation: {
                      operationId: second.id,
                      operationSequence: 2,
                    },
                    settlement: { kind: "succeeded", value: "late" },
                  }
                : {
                    kind: "rejected",
                    operation: {
                      operationId: second.id,
                      operationSequence: 2,
                    },
                    error: "late",
                    diagnostic: { code: "late", detail: null },
                  }));
            if (outstandingCancellation) {
              harness.workers[0]!.emitRaw(workerEnvelope(1, {
                kind: "cancel-acknowledged",
                operation: {
                  operationId: second.id,
                  operationSequence: 2,
                },
                status: "not-active",
              }));
            }
            return undefined;
          },
        },
        diagnostic: { report: () => undefined },
      });
      const secondSession = authority.createSession<
        string,
        string,
        string,
        string,
        WorkerRuntimePreparationError
      >({
        feature: {
          publish: event => {
            if (event.kind === "terminal") order.push("second-terminal");
            return undefined;
          },
        },
        diagnostic: { report: () => undefined },
      });
      const firstHandle = started(
        firstSession.start("first", harness.adapter),
      );
      secondHandle = started(
        secondSession.start("second", harness.adapter),
      );
      if (outstandingCancellation) {
        assert.deepEqual(secondHandle.cancel("user"), { kind: "applied" });
      }
      let secondQuiesced = false;
      void secondHandle.quiesced.then(() => {
        secondQuiesced = true;
        return undefined;
      });
      await harness.environment.flushAsync();

      harness.workers[0]!.emitRaw({ malformed: true });
      await Promise.resolve();

      assert.deepEqual(order, outstandingCancellation
        ? [
            "first-terminal",
            "runtime-failure:protocol",
          ]
        : [
            "first-terminal",
            "second-terminal",
            "runtime-failure:protocol",
          ]);
      assert.equal(secondQuiesced, true);
      assert.equal(harness.host.snapshot().phase, "draining");

      harness.workers[0]!.emitRaw(workerEnvelope(1,
        physicalResponse === "settled"
          ? {
              kind: "settled",
              operation: {
                operationId: firstHandle.id,
                operationSequence: 1,
              },
              settlement: { kind: "succeeded", value: "late" },
            }
          : {
              kind: "rejected",
              operation: {
                operationId: firstHandle.id,
                operationSequence: 1,
              },
              error: "late",
              diagnostic: { code: "late", detail: null },
            }));
      assert.equal(harness.host.snapshot().phase, "closed");
      assert.deepEqual(harness.releasedEpochs, [1]);
      await firstHandle.quiesced;
    });
  }
}

test("synchronous fake admission cannot invoke after restart releases the realm", async () => {
  let harness: TestHarness;
  let invokeCount = 0;
  harness = createHarness({
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    workerAllowance: { kind: "bounded", maxSilentActiveMilliseconds: 19 },
    invoke: input => {
      invokeCount++;
      return { kind: "succeeded", value: input };
    },
    failure: () => {
      harness.host.restart();
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  started(operationSession.session.start("input", harness.adapter));

  await harness.environment.flushAsync();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminated, true);
  assert.equal(invokeCount, 0);
});

test("draining tracks delayed physical admission through settlement", async () => {
  const harness = createHarness();
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );

  harness.workers[0]!.emitError("worker event");
  await harness.environment.flushAsync();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.host.snapshot().activeOperations, 0);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:worker-message",
  });
  await handle.quiesced;
});

test("draining tracks delayed epoch work until its physical finish", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitError("worker event");

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: { operationId: handle.id, operationSequence: 1 },
    settlement: { kind: "succeeded", value: "physically-released" },
  }));
  assert.equal(harness.host.snapshot().phase, "draining");

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));

  assert.equal(harness.host.snapshot().phase, "closed");
  await handle.quiesced;
});

test("disposed hosts reject restart and remain quiescent", async () => {
  const harness = createHarness();
  await startReady(harness);

  harness.host.dispose();
  harness.host.dispose();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.host.start("replacement"), {
    kind: "rejected",
    reason: "host-disposed",
  });
  harness.environment.advanceActive(1_000);
  assert.equal(harness.failures.length, 0);
});

test("teardown callback failures do not interrupt mandatory shutdown", async () => {
  const clockError = new Error("clock unsubscribe failed");
  const lifecycleError = new Error("lifecycle unsubscribe failed");
  const disposing = createHarness({
    clockUnsubscribeError: clockError,
    lifecycleUnsubscribeError: lifecycleError,
  });
  await startReady(disposing);

  assert.doesNotThrow(() => disposing.host.dispose());

  assert.equal(disposing.host.snapshot().phase, "closed");
  assert.equal(disposing.workers[0]!.terminateCount, 1);
  assert.deepEqual(disposing.releasedEpochs, [1]);
  assert.deepEqual(
    disposing.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [clockError, lifecycleError],
  );

  const detachError = new Error("transport detach failed");
  const restarting = createHarness({ detachError });
  await startReady(restarting);

  assert.doesNotThrow(() => restarting.host.restart());

  assert.equal(restarting.host.snapshot().phase, "closed");
  assert.equal(restarting.workers[0]!.terminateCount, 1);
  assert.deepEqual(restarting.releasedEpochs, [1]);
  assert.deepEqual(
    restarting.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [detachError],
  );
});

test("teardown diagnostics observe closed admission and completed termination", async () => {
  const clockError = new Error("clock unsubscribe failed");
  let disposalAdmission: ReturnType<TestSession["start"]> | null = null;
  let disposalTerminated = false;
  let disposing: TestHarness;
  let disposalSession: ReturnType<typeof session>;
  disposing = createHarness({
    clockUnsubscribeError: clockError,
    diagnostic: diagnostic => {
      if (diagnostic.detail !== clockError) return;
      disposalTerminated = disposing.workers[0]!.terminated;
      disposalAdmission = disposalSession.session.start(
        "late",
        disposing.adapter,
      );
    },
  });
  await startReady(disposing);
  disposalSession = session(disposing.adapter);

  disposing.host.dispose();

  assert.equal(disposalTerminated, true);
  assert.deepEqual(disposalAdmission, {
    kind: "rejected",
    reason: {
      kind: "producer-rejected",
      error: { kind: "epoch-unavailable" },
    },
  });
  assert.deepEqual(
    operationMessages(disposing.workers[0]!),
    ["initialize"],
  );

  const reentrantClockError = new Error("reentrant clock unsubscribe failed");
  let subscriptionCleanupAfterTermination = false;
  let reentrantDisposal: TestHarness;
  reentrantDisposal = createHarness({
    detach: () => {
      reentrantDisposal.host.dispose();
    },
    clockUnsubscribeError: reentrantClockError,
    diagnostic: diagnostic => {
      if (diagnostic.detail !== reentrantClockError) return;
      subscriptionCleanupAfterTermination =
        reentrantDisposal.workers[0]!.terminated;
    },
  });
  await startReady(reentrantDisposal);

  reentrantDisposal.host.restart();

  assert.equal(subscriptionCleanupAfterTermination, true);
  assert.equal(reentrantDisposal.workers[0]!.terminateCount, 1);
  assert.deepEqual(reentrantDisposal.releasedEpochs, [1]);

  let detachRetry: ReturnType<TestHarness["host"]["start"]> | null = null;
  let terminateRetry: ReturnType<TestHarness["host"]["start"]> | null = null;
  let barrier: TestHarness;
  barrier = createHarness({
    detach: () => {
      detachRetry = barrier.host.start("during-detach");
    },
    terminate: () => {
      terminateRetry = barrier.host.start("during-terminate");
    },
  });
  await startReady(barrier);

  barrier.host.restart();

  assert.deepEqual(detachRetry, {
    kind: "rejected",
    reason: "epoch-active",
  });
  assert.deepEqual(terminateRetry, {
    kind: "rejected",
    reason: "epoch-active",
  });
  assert.equal(barrier.workers[0]!.terminateCount, 1);
  assert.deepEqual(barrier.releasedEpochs, [1]);

  const terminationError = new Error("worker termination failed");
  let terminationRetry: ReturnType<TestHarness["host"]["start"]> | null = null;
  let failedTermination: TestHarness;
  failedTermination = createHarness({
    workerCount: 2,
    terminate: () => {
      throw terminationError;
    },
    diagnostic: diagnostic => {
      if (diagnostic.detail !== terminationError) return;
      terminationRetry = failedTermination.host.start("replacement");
    },
  });
  await startReady(failedTermination);

  failedTermination.host.restart();

  assert.equal(failedTermination.workers[0]!.terminated, false);
  assert.deepEqual(failedTermination.releasedEpochs, []);
  assert.deepEqual(terminationRetry, {
    kind: "rejected",
    reason: "epoch-active",
  });
  assert.deepEqual(
    failedTermination.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [terminationError],
  );
  assert.deepEqual(failedTermination.host.start("later"), {
    kind: "rejected",
    reason: "epoch-active",
  });

  const disposalTerminationError = new Error(
    "disposal worker termination failed",
  );
  const deferredClockError = new Error("clock cleanup must remain deferred");
  const deferredLifecycleError = new Error(
    "lifecycle cleanup must remain deferred",
  );
  const failedDisposal = createHarness({
    terminate: () => {
      throw disposalTerminationError;
    },
    clockUnsubscribeError: deferredClockError,
    lifecycleUnsubscribeError: deferredLifecycleError,
  });
  await startReady(failedDisposal);

  failedDisposal.host.dispose();

  assert.equal(failedDisposal.workers[0]!.terminated, false);
  assert.deepEqual(failedDisposal.releasedEpochs, []);
  assert.deepEqual(
    failedDisposal.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [disposalTerminationError],
  );
  assert.deepEqual(failedDisposal.host.start("later"), {
    kind: "rejected",
    reason: "host-disposed",
  });

  const detachError = new Error("transport detach failed");
  let retry: ReturnType<TestHarness["host"]["start"]> | null = null;
  let oldTerminatedAtDiagnostic = false;
  let restarting: TestHarness;
  restarting = createHarness({
    workerCount: 2,
    detachError,
    diagnostic: diagnostic => {
      if (diagnostic.detail !== detachError) return;
      oldTerminatedAtDiagnostic = restarting.workers[0]!.terminated;
      retry = restarting.host.start("replacement");
    },
  });
  await startReady(restarting);

  restarting.host.restart();

  assert.equal(oldTerminatedAtDiagnostic, true);
  assert.deepEqual(retry, { kind: "started", epochToken: 2 });
  assert.deepEqual(restarting.releasedEpochs, [1]);
  await restarting.environment.flushAsync();
  assert.equal(restarting.host.snapshot().phase, "ready");
  assert.equal(restarting.host.snapshot().epochToken, 2);
});

test("first closure identity and producer outcome survive later faults and draining crash", async () => {
  const settlement = deferred<TestSettlement>();
  const firstDiagnostic = { malformed: "first" };
  const harness = createHarness({
    invoke: () => settlement.promise,
    drainBudgetMilliseconds: 100,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(firstDiagnostic);
  const closure = harness.host.snapshot().closure;
  assert.equal(closure?.kind, "unexpected-failure");
  assert.equal(
    closure?.kind === "unexpected-failure"
      ? closure.failure.kind
      : null,
    "protocol",
  );
  harness.workers[0]!.emitError("later worker message");
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-failed",
    diagnostic: { code: "later", detail: "worker declared" },
  }));
  assert.equal(harness.host.snapshot().closure, closure);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  harness.host.receiveWorkerCrash(
    harness.workers[0]!,
    "crash during draining",
  );
  assert.equal(harness.host.snapshot().closure, closure);
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.deepEqual(harness.releasedEpochs, [1]);
  await handle.quiesced;
});

test("worker crash is an exact immediate boundary and natural release closes draining early", async () => {
  const crashSettlement = deferred<TestSettlement>();
  const crash = createHarness({ invoke: () => crashSettlement.promise });
  await startReady(crash);
  const crashSession = session(crash.adapter);
  const crashHandle = started(
    crashSession.session.start("input", crash.adapter),
  );
  await crash.environment.flushAsync();
  crash.host.receiveWorkerCrash(crash.workers[0]!, "worker disappeared");
  assert.equal(crash.failures[0]?.kind, "worker-crash");
  assert.equal(crash.host.snapshot().phase, "closed");
  assert.deepEqual(await crashHandle.outcome, {
    kind: "failed",
    error: "boundary:worker-crash",
  });

  const naturalSettlement = deferred<TestSettlement>();
  const natural = createHarness({
    invoke: () => naturalSettlement.promise,
    drainBudgetMilliseconds: 100,
  });
  await startReady(natural);
  const naturalSession = session(natural.adapter);
  const naturalHandle = started(
    naturalSession.session.start("input", natural.adapter),
  );
  await natural.environment.flushAsync();
  natural.workers[0]!.emitRaw({ malformed: true });
  assert.equal(natural.host.snapshot().phase, "draining");
  natural.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: { operationId: naturalHandle.id, operationSequence: 1 },
    settlement: { kind: "succeeded", value: "physically-released" },
  }));
  assert.equal(natural.host.snapshot().phase, "closed");
  assert.equal(natural.environment.now(), 0);
  await naturalHandle.quiesced;
});

test("crash-established physical loss survives a throwing termination call", async () => {
  const terminationError = new Error("terminate after crash failed");
  const harness = createHarness({
    workerCount: 2,
    terminate: () => {
      throw terminationError;
    },
  });
  await startReady(harness);

  harness.host.receiveWorkerCrash(
    harness.transportSources[0]!,
    "physical loss",
  );

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.deepEqual(harness.releasedEpochs, [1]);
  assert.deepEqual(
    harness.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [terminationError],
  );
  assert.deepEqual(harness.host.start("replacement"), {
    kind: "started",
    epochToken: 2,
  });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
});

test("crash-established physical loss permits disposal subscription cleanup", async () => {
  const terminationError = new Error("terminate after crash failed");
  const clockError = new Error("clock cleanup failed");
  const lifecycleError = new Error("lifecycle cleanup failed");
  const harness = createHarness({
    terminate: () => {
      throw terminationError;
    },
    clockUnsubscribeError: clockError,
    lifecycleUnsubscribeError: lifecycleError,
  });
  await startReady(harness);

  harness.host.receiveWorkerCrash(
    harness.transportSources[0]!,
    "physical loss",
  );
  harness.host.dispose();

  assert.deepEqual(harness.releasedEpochs, [1]);
  assert.deepEqual(
    harness.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [terminationError, clockError, lifecycleError],
  );
  assert.deepEqual(harness.host.start("later"), {
    kind: "rejected",
    reason: "host-disposed",
  });
});

test("a crash after failed termination completes deferred realm release", async () => {
  const terminationError = new Error("termination failed before crash");
  const harness = createHarness({
    workerCount: 2,
    terminate: () => {
      throw terminationError;
    },
  });
  await startReady(harness);

  harness.host.restart();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.deepEqual(harness.releasedEpochs, []);
  assert.deepEqual(harness.host.start("blocked"), {
    kind: "rejected",
    reason: "epoch-active",
  });

  harness.host.receiveWorkerCrash(
    harness.transportSources[0]!,
    "later physical loss",
  );

  assert.deepEqual(harness.releasedEpochs, [1]);
  assert.deepEqual(harness.host.start("replacement"), {
    kind: "started",
    epochToken: 2,
  });
});

test("draining stops admission synchronously", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `drain-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  started(first.session.start("first", harness.adapter));
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw({ malformed: true });
  const second = session(harness.adapter, authority);
  const result = second.session.start("second", harness.adapter);
  assert.equal(result.kind, "rejected");
  if (result.kind === "rejected") {
    assert.deepEqual(result.reason, {
      kind: "producer-rejected",
      error: { kind: "epoch-unavailable" },
    });
  }
});

test("planned restart cancels while EpochFailed remains an unexpected boundary", async () => {
  const settlement = deferred<TestSettlement>();
  const planned = createHarness({ invoke: () => settlement.promise });
  await startReady(planned);
  const plannedSession = session(planned.adapter);
  const plannedHandle = started(
    plannedSession.session.start("input", planned.adapter),
  );
  await planned.environment.flushAsync();
  planned.host.restart();
  assert.deepEqual(await plannedHandle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });

  const unexpected = createHarness({ invoke: () => settlement.promise });
  await startReady(unexpected);
  const unexpectedSession = session(unexpected.adapter);
  const unexpectedHandle = started(
    unexpectedSession.session.start("input", unexpected.adapter),
  );
  await unexpected.environment.flushAsync();
  unexpected.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-failed",
    diagnostic: { code: "managed-boundary", detail: "failed" },
  }));
  assert.equal(unexpected.failures[0]?.kind, "worker-declared");
  assert.deepEqual(await unexpectedHandle.outcome, {
    kind: "failed",
    error: "boundary:worker-declared",
  });
});

test("callback errors remain failure-complete and realm release is reported once", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const identity = captureIdentity();
  const calls: string[] = [];
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    reportDurable: () => undefined,
    reportUnexpectedTerminal: () => undefined,
    reportUnexpectedFailure: () => {
      calls.push("unexpected");
      throw new Error("unexpected callback failed");
    },
    ...terminalCallbacks(() => {
      calls.push("terminal");
      throw new Error("terminal callback failed");
    }),
    reportQuiesced: () => {
      calls.push("quiesced");
      throw new Error("quiescence callback failed");
    },
  };
  preparedBinding(
    harness.adapter.prepare(identity, "input", sink, uncanceledOperation),
  ).activate();
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw({ malformed: true });
  harness.environment.advanceActive(20);
  assert.deepEqual(calls, ["terminal", "quiesced"]);
  assert.equal(
    harness.runtimeDiagnostics.filter(
      diagnostic => diagnostic.code === "callback-error",
    ).length,
    2,
  );
  harness.host.receiveWorkerCrash(harness.workers[0]!, "late crash");
  assert.deepEqual(harness.releasedEpochs, [1]);
});

test("callbacks and messages after realm release cannot deliver", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.host.restart();
  const failureCount = harness.failures.length;
  settlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "heartbeat",
  }));
  assert.equal(harness.failures.length, failureCount);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });
  await handle.quiesced;
});

test("operation authority remains usable by a neighboring browser-native producer", async () => {
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "browser-native" },
  });
  const events: TestEvent[] = [];
  const browserSession = page.createSession<
    string,
    string,
    string,
    string,
    string
  >({
    feature: {
      publish: event => {
        events.push(event);
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const nativeAdapter: OperationProducerAdapter<
    string,
    string,
    string,
    string,
    string
  > = {
    prepare: (_identity, input, sink) => ({
      kind: "prepared",
      binding: {
        requestCancellation: () => undefined,
        abandon: () => undefined,
        activate: () => {
          sink.reportTerminal({ kind: "succeeded", value: input });
          sink.reportQuiesced();
        },
      },
    }),
  };
  const handle = started(browserSession.start("native", nativeAdapter));
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: "native",
  });
  await handle.quiesced;
  assert.deepEqual(events.map(event => event.kind), ["started", "terminal"]);
});

test("sequence allocators start at one, never wrap, and report exhaustion", () => {
  const probes = new WorkerProbeSequenceAllocator(
    Number.MAX_SAFE_INTEGER,
  );
  assert.deepEqual(probes.allocate(), {
    kind: "allocated",
    sequence: Number.MAX_SAFE_INTEGER,
  });
  assert.deepEqual(probes.allocate(), { kind: "exhausted" });

  const normal = new WorkerProbeSequenceAllocator();
  assert.deepEqual(normal.allocate(), { kind: "allocated", sequence: 1 });
  assert.deepEqual(normal.allocate(), { kind: "allocated", sequence: 2 });
});
