import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import {
  createOperationAuthorityPage,
  type OperationAuthorityPage,
  type OperationAuthorityPageOptions,
  type OperationCancelReason,
  type OperationControlResult,
  type OperationDiagnostic,
  type OperationDiagnosticObserver,
  type OperationFeatureEvent,
  type OperationFeatureObserver,
  type OperationHandle,
  type OperationId,
  type OperationIdentity,
  type OperationPreparation,
  type OperationProducerAdapter,
  type OperationProducerSink,
  type OperationSession,
  type OperationStartResult,
  type PreparedOperationProducer,
} from "../src/operation-authority.ts";

type TestEvent = OperationFeatureEvent<string, string, number>;
type TestSink = OperationProducerSink<string, string, number>;
type TestAdapter = OperationProducerAdapter<string, string, string, number, string>;
type TestSession = OperationSession<string, string, string, number, string>;
type TestStartResult = OperationStartResult<string, string, string>;
type TestHandle = OperationHandle<string, string>;

interface ProducerAttempt {
  readonly identity: OperationIdentity;
  readonly input: string;
  readonly sink: TestSink;
  readonly cancellations: OperationCancelReason[];
  activated: boolean;
  abandoned: boolean;
}

interface ProducerOptions {
  readonly reject?: string;
  readonly onPrepare?: (attempt: ProducerAttempt) => undefined;
  readonly onActivate?: (attempt: ProducerAttempt) => undefined;
  readonly onCancellation?: (
    attempt: ProducerAttempt,
    reason: OperationCancelReason,
  ) => undefined;
  readonly onAbandon?: (attempt: ProducerAttempt) => undefined;
}

interface ProducerHarness {
  readonly adapter: TestAdapter;
  readonly attempts: ProducerAttempt[];
}

interface SessionHarness {
  readonly session: TestSession;
  readonly events: TestEvent[];
  readonly diagnostics: OperationDiagnostic[];
}

interface OperationAuthorityModule {
  readonly createOperationAuthorityPage: typeof createOperationAuthorityPage;
}

function isOperationAuthorityModule(
  value: unknown,
): value is OperationAuthorityModule {
  return typeof value === "object"
    && value !== null
    && "createOperationAuthorityPage" in value
    && typeof value.createOperationAuthorityPage === "function";
}

function deterministicOptions(
  maximumSequence = Number.MAX_SAFE_INTEGER,
  createId?: () => string,
): OperationAuthorityPageOptions {
  let nextId = 1;
  return {
    allocation: {
      maximumSequence,
      createId: createId ?? (() => `operation-${nextId++}`),
    },
  };
}

function producer(options: ProducerOptions = {}): ProducerHarness {
  const attempts: ProducerAttempt[] = [];
  const adapter: TestAdapter = {
    prepare: (identity, input, sink) => {
      const attempt: ProducerAttempt = {
        identity,
        input,
        sink,
        cancellations: [],
        activated: false,
        abandoned: false,
      };
      attempts.push(attempt);
      options.onPrepare?.(attempt);
      if (options.reject !== undefined)
        return { kind: "rejected", error: options.reject };
      const binding: PreparedOperationProducer = {
        requestCancellation: reason => {
          attempt.cancellations.push(reason);
          options.onCancellation?.(attempt, reason);
          return undefined;
        },
        activate: () => {
          attempt.activated = true;
          options.onActivate?.(attempt);
          return undefined;
        },
        abandon: () => {
          attempt.abandoned = true;
          options.onAbandon?.(attempt);
          return undefined;
        },
      };
      return { kind: "prepared", binding };
    },
  };
  return { adapter, attempts };
}

function sessionHarness(
  page: OperationAuthorityPage = createOperationAuthorityPage(
    deterministicOptions(),
  ),
  publish?: (event: TestEvent) => undefined,
  report?: (diagnostic: OperationDiagnostic) => undefined,
): SessionHarness {
  const events: TestEvent[] = [];
  const diagnostics: OperationDiagnostic[] = [];
  const session = page.createSession<string, string, string, number, string>({
    feature: {
      publish: event => {
        events.push(event);
        publish?.(event);
        return undefined;
      },
    },
    diagnostic: {
      report: diagnostic => {
        diagnostics.push(diagnostic);
        report?.(diagnostic);
        return undefined;
      },
    },
  });
  return { session, events, diagnostics };
}

function started(result: TestStartResult): TestHandle {
  assert.equal(result.kind, "started");
  if (result.kind !== "started")
    throw new Error("Expected a started result.");
  return result.handle;
}

function rejected(result: TestStartResult): Exclude<TestStartResult, {
  readonly kind: "started";
}>["reason"] {
  assert.equal(result.kind, "rejected");
  if (result.kind !== "rejected")
    throw new Error("Expected a rejected result.");
  return result.reason;
}

async function promiseSettled(promise: Promise<unknown>): Promise<boolean> {
  let settled = false;
  promise.then(
    () => {
      settled = true;
      return undefined;
    },
    () => {
      settled = true;
      return undefined;
    },
  );
  await Promise.resolve();
  return settled;
}

test("page allocation is opaque, unique, and sequential across sessions", async () => {
  const page = createOperationAuthorityPage(deterministicOptions());
  const first = sessionHarness(page);
  const second = sessionHarness(page);
  const firstProducer = producer();
  const secondProducer = producer();

  const firstHandle = started(first.session.start("first", firstProducer.adapter));
  const secondHandle = started(second.session.start("second", secondProducer.adapter));
  const replacementHandle = started(first.session.start("replacement", firstProducer.adapter));

  assert.deepEqual(
    [
      firstProducer.attempts[0]?.identity,
      secondProducer.attempts[0]?.identity,
      firstProducer.attempts[1]?.identity,
    ],
    [
      { id: "operation-1", sequence: 1 },
      { id: "operation-2", sequence: 2 },
      { id: "operation-3", sequence: 3 },
    ],
  );
  assert.equal(firstHandle.id, "operation-1");
  assert.equal(secondHandle.id, "operation-2");
  assert.equal(replacementHandle.id, "operation-3");
  assert.deepEqual(await firstHandle.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
});

test("allocation limits must be non-negative safe integers", () => {
  for (const maximumSequence of [
    -1,
    1.5,
    Number.MAX_SAFE_INTEGER + 1,
  ]) {
    assert.throws(
      () => createOperationAuthorityPage(deterministicOptions(maximumSequence)),
      /maximumSequence must be a non-negative safe integer/u,
    );
  }
});

test("an identity seen by a rejecting producer is never reused", () => {
  const page = createOperationAuthorityPage(
    deterministicOptions(4, () => "reused"),
  );
  const first = sessionHarness(page);
  const rejecting = producer({ reject: "not-ready" });

  assert.deepEqual(rejected(first.session.start("first", rejecting.adapter)), {
    kind: "producer-rejected",
    error: "not-ready",
  });
  assert.equal(rejecting.attempts.length, 1);

  const sameSessionProducer = producer();
  assert.deepEqual(
    rejected(first.session.start("same-session", sameSessionProducer.adapter)),
    { kind: "identity-exhausted" },
  );
  const recreated = sessionHarness(page);
  assert.deepEqual(
    rejected(recreated.session.start("recreated", sameSessionProducer.adapter)),
    { kind: "identity-exhausted" },
  );
  assert.equal(sameSessionProducer.attempts.length, 0);
});

for (const lifecycle of ["completion", "quiescence", "disposal"] as const) {
  test(`identity reuse after ${lifecycle} is rejected before preparation`, () => {
    const page = createOperationAuthorityPage(
      deterministicOptions(3, () => "reused"),
    );
    const first = sessionHarness(page);
    const firstProducer = producer();
    started(first.session.start("first", firstProducer.adapter));
    const attempt = firstProducer.attempts[0];
    assert.ok(attempt);
    if (lifecycle === "completion")
      attempt.sink.reportTerminal({ kind: "succeeded", value: "done" });
    if (lifecycle === "quiescence") {
      attempt.sink.reportTerminal({ kind: "succeeded", value: "done" });
      attempt.sink.reportQuiesced();
    }
    if (lifecycle === "disposal")
      first.session.dispose();

    const recreated = sessionHarness(page);
    const nextProducer = producer();
    assert.deepEqual(
      rejected(recreated.session.start("next", nextProducer.adapter)),
      { kind: "identity-exhausted" },
    );
    assert.equal(nextProducer.attempts.length, 0);
  });
}

for (const transition of ["session-changed", "session-disposed"] as const) {
  test(`an identity from an abandoned ${transition} preparation is never reused`, () => {
    let idAttempt = 0;
    const page = createOperationAuthorityPage(deterministicOptions(
      transition === "session-changed" ? 3 : 2,
      () => transition === "session-changed" && ++idAttempt === 2
        ? "nested"
        : "reused",
    ));
    const outer = sessionHarness(page);
    const sameLifetimeSession = transition === "session-changed"
      ? outer
      : sessionHarness(page);
    const nested = producer();
    const outerProducer = producer({
      onPrepare: () => {
        if (transition === "session-changed")
          started(outer.session.start("nested", nested.adapter));
        else
          assert.deepEqual(outer.session.dispose(), { kind: "applied" });
        return undefined;
      },
    });

    assert.deepEqual(
      rejected(outer.session.start("outer", outerProducer.adapter)),
      {
        kind: transition === "session-changed"
          ? "session-changed"
          : "session-disposed",
      },
    );
    assert.equal(outerProducer.attempts[0]?.abandoned, true);
    assert.equal(outerProducer.attempts[0]?.activated, false);

    const recreated = sessionHarness(page);
    const reused = producer();
    assert.deepEqual(
      rejected(sameLifetimeSession.session.start("same-session-reuse", reused.adapter)),
      { kind: "identity-exhausted" },
    );
    assert.deepEqual(
      rejected(recreated.session.start("recreated-reuse", reused.adapter)),
      { kind: "identity-exhausted" },
    );
    assert.equal(reused.attempts.length, 0);
  });
}

test("the final sequence remains consumed after producer rejection", () => {
  const page = createOperationAuthorityPage(deterministicOptions(2));
  const harness = sessionHarness(page);
  const currentProducer = producer();
  const current = started(harness.session.start("current", currentProducer.adapter));
  const rejecting = producer({ reject: "busy" });

  assert.deepEqual(rejected(harness.session.start("rejected", rejecting.adapter)), {
    kind: "producer-rejected",
    error: "busy",
  });
  const neverPrepared = producer();
  assert.deepEqual(
    rejected(harness.session.start("exhausted", neverPrepared.adapter)),
    { kind: "identity-exhausted" },
  );
  assert.equal(neverPrepared.attempts.length, 0);
  assert.equal(currentProducer.attempts[0]?.cancellations.length, 0);
  assert.equal(harness.events.length, 1);
  assert.equal(current.id, "operation-1");
});

test("the final sequence remains consumed after abandoned preparation", () => {
  const page = createOperationAuthorityPage(deterministicOptions(1));
  const harness = sessionHarness(page);
  const abandoning = producer({
    onPrepare: () => {
      harness.session.dispose();
      return undefined;
    },
  });

  assert.deepEqual(
    rejected(harness.session.start("abandoned", abandoning.adapter)),
    { kind: "session-disposed" },
  );
  assert.equal(abandoning.attempts[0]?.abandoned, true);
  const recreated = sessionHarness(page);
  const neverPrepared = producer();
  assert.deepEqual(
    rejected(recreated.session.start("exhausted", neverPrepared.adapter)),
    { kind: "identity-exhausted" },
  );
  assert.equal(neverPrepared.attempts.length, 0);
});

test("preparation rejection leaves the current operation unchanged", async () => {
  const harness = sessionHarness();
  const currentProducer = producer();
  const current = started(harness.session.start("current", currentProducer.adapter));
  const rejecting = producer({ reject: "unsupported" });

  assert.deepEqual(rejected(harness.session.start("candidate", rejecting.adapter)), {
    kind: "producer-rejected",
    error: "unsupported",
  });
  assert.equal(await promiseSettled(current.outcome), false);
  assert.equal(currentProducer.attempts[0]?.cancellations.length, 0);
  assert.deepEqual(harness.events.map(event => event.kind), ["started"]);
});

test("successful preparation reentrancy cannot overwrite a nested start", async () => {
  const harness = sessionHarness();
  const nested = producer();
  let nestedHandle: TestHandle | undefined;
  const outer = producer({
    onPrepare: () => {
      nestedHandle = started(harness.session.start("nested", nested.adapter));
      return undefined;
    },
  });

  assert.deepEqual(
    rejected(harness.session.start("outer", outer.adapter)),
    { kind: "session-changed" },
  );
  assert.equal(outer.attempts[0]?.abandoned, true);
  assert.equal(outer.attempts[0]?.activated, false);
  assert.equal(nested.attempts[0]?.activated, true);
  assert.ok(nestedHandle);
  assert.equal(await promiseSettled(nestedHandle.outcome), false);
  assert.deepEqual(harness.events.map(event => event.kind), ["started"]);
});

test("successful preparation reentrancy cannot overwrite cancellation", async () => {
  const harness = sessionHarness();
  const currentProducer = producer();
  const current = started(harness.session.start("current", currentProducer.adapter));
  const outer = producer({
    onPrepare: () => {
      assert.deepEqual(harness.session.cancelCurrent(), { kind: "applied" });
      return undefined;
    },
  });

  assert.deepEqual(
    rejected(harness.session.start("outer", outer.adapter)),
    { kind: "session-changed" },
  );
  assert.equal(outer.attempts[0]?.abandoned, true);
  assert.deepEqual(await current.outcome, {
    kind: "canceled",
    reason: "user",
  });
  assert.deepEqual(currentProducer.attempts[0]?.cancellations, ["user"]);
});

test("producer rejection after preparation reentrancy returns the session transition", () => {
  const harness = sessionHarness();
  const nested = producer();
  const rejecting = producer({
    reject: "stale-error",
    onPrepare: () => {
      started(harness.session.start("nested", nested.adapter));
      return undefined;
    },
  });

  assert.deepEqual(
    rejected(harness.session.start("outer", rejecting.adapter)),
    { kind: "session-changed" },
  );
  assert.deepEqual(harness.events.map(event => event.kind), ["started"]);
  assert.deepEqual(harness.diagnostics, []);
});

test("producer rejection after preparation disposal returns disposal", () => {
  const harness = sessionHarness();
  const rejecting = producer({
    reject: "stale-error",
    onPrepare: () => {
      harness.session.dispose();
      return undefined;
    },
  });

  assert.deepEqual(
    rejected(harness.session.start("outer", rejecting.adapter)),
    { kind: "session-disposed" },
  );
  assert.deepEqual(harness.events, [{
    kind: "disposed",
    operationId: null,
  }]);
  assert.deepEqual(harness.diagnostics, []);
});

test("activation sees an installed cancellable operation after start publication", async () => {
  const order: string[] = [];
  let harness: SessionHarness;
  const activating = producer({
    onActivate: () => {
      order.push("activate");
      assert.deepEqual(harness.session.cancelCurrent(), { kind: "applied" });
      return undefined;
    },
    onCancellation: () => {
      order.push("producer-cancel");
      return undefined;
    },
  });
  harness = sessionHarness(undefined, event => {
    order.push(event.kind);
    return undefined;
  });

  const handle = started(harness.session.start("work", activating.adapter));

  assert.deepEqual(order, [
    "started",
    "activate",
    "canceled",
    "producer-cancel",
  ]);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "user",
  });
});

test("synchronous activation failure reports terminal then quiescence", async () => {
  const activating = producer({
    onActivate: attempt => {
      attempt.sink.reportTerminal({ kind: "failed", error: "activation-failed" });
      attempt.sink.reportQuiesced();
      return undefined;
    },
  });
  const harness = sessionHarness();

  const handle = started(harness.session.start("work", activating.adapter));

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "activation-failed",
  });
  await handle.quiesced;
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "terminal",
  ]);
});

test("activation replacement leaves the nested operation authoritative", async () => {
  const harness = sessionHarness();
  const replacement = producer();
  let replacementHandle: TestHandle | undefined;
  const firstProducer = producer({
    onActivate: () => {
      replacementHandle = started(
        harness.session.start("replacement", replacement.adapter),
      );
      return undefined;
    },
  });

  const first = started(harness.session.start("first", firstProducer.adapter));

  assert.ok(replacementHandle);
  assert.deepEqual(await first.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
  assert.equal(await promiseSettled(replacementHandle.outcome), false);
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "replaced",
  ]);
});

test("activation disposal returns the captured canceled handle", async () => {
  const harness = sessionHarness();
  const activating = producer({
    onActivate: () => {
      assert.deepEqual(harness.session.dispose(), { kind: "applied" });
      return undefined;
    },
  });

  const handle = started(harness.session.start("work", activating.adapter));

  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "disposed",
  });
  assert.deepEqual(rejected(harness.session.start("later", producer().adapter)), {
    kind: "session-disposed",
  });
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "disposed",
  ]);
});

test("prior cancellation runs after replacement activation and stale reports are suppressed", () => {
  const order: string[] = [];
  const harness = sessionHarness(undefined, event => {
    order.push(event.kind);
    return undefined;
  });
  const firstProducer = producer({
    onCancellation: attempt => {
      order.push("prior-cancel");
      attempt.sink.reportProgress(99);
      attempt.sink.reportTerminal({ kind: "failed", error: "late" });
      return undefined;
    },
  });
  const secondProducer = producer({
    onActivate: () => {
      order.push("replacement-activate");
      return undefined;
    },
  });
  started(harness.session.start("first", firstProducer.adapter));

  started(harness.session.start("second", secondProducer.adapter));

  assert.deepEqual(order, [
    "started",
    "replaced",
    "replacement-activate",
    "prior-cancel",
  ]);
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "replaced",
  ]);
});

test("a prior cancellation endpoint may install another replacement", async () => {
  const harness = sessionHarness();
  const thirdProducer = producer();
  let third: TestHandle | undefined;
  const firstProducer = producer({
    onCancellation: () => {
      third = started(harness.session.start("third", thirdProducer.adapter));
      return undefined;
    },
  });
  const secondProducer = producer();
  started(harness.session.start("first", firstProducer.adapter));

  const second = started(harness.session.start("second", secondProducer.adapter));

  assert.ok(third);
  assert.deepEqual(await second.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
  assert.equal(await promiseSettled(third.outcome), false);
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "replaced",
    "replaced",
  ]);
});

test("a prior cancellation endpoint may dispose the committed replacement", async () => {
  const harness = sessionHarness();
  const firstProducer = producer({
    onCancellation: () => {
      harness.session.dispose();
      return undefined;
    },
  });
  started(harness.session.start("first", firstProducer.adapter));

  const second = started(harness.session.start("second", producer().adapter));

  assert.deepEqual(await second.outcome, {
    kind: "canceled",
    reason: "disposed",
  });
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "replaced",
    "disposed",
  ]);
});

for (const route of ["handle", "session", "dispose", "supersession"] as const) {
  test(`throwing cancellation through ${route} keeps the committed outcome`, async () => {
    const cancellationError = new Error(`${route}-cancellation`);
    const harness = sessionHarness();
    const throwing = producer({
      onCancellation: () => {
        throw cancellationError;
      },
    });
    const handle = started(harness.session.start("first", throwing.adapter));

    if (route === "handle")
      assert.deepEqual(handle.cancel(), { kind: "applied" });
    if (route === "session")
      assert.deepEqual(harness.session.cancelCurrent(), { kind: "applied" });
    if (route === "dispose")
      assert.deepEqual(harness.session.dispose(), { kind: "applied" });
    if (route === "supersession")
      started(harness.session.start("second", producer().adapter));

    const outcome = await handle.outcome;
    if (route === "dispose") {
      assert.deepEqual(outcome, { kind: "canceled", reason: "disposed" });
    } else if (route === "supersession") {
      assert.deepEqual(outcome, {
        kind: "canceled",
        reason: "superseded",
      });
    } else {
      assert.deepEqual(outcome, { kind: "canceled", reason: "user" });
    }
    assert.equal(throwing.attempts[0]?.cancellations.length, 1);
    assert.equal(harness.diagnostics.length, 1);
    assert.deepEqual(harness.diagnostics[0], {
      kind: "producer-callout",
      operationId: handle.id,
      error: cancellationError,
    });
    assert.deepEqual(handle.cancel("disposed"), { kind: "no-op" });
    assert.equal(throwing.attempts[0]?.cancellations.length, 1);
  });
}

test("deferred terminal outcome and quiescence are independent", async () => {
  const harness = sessionHarness();
  const deferredProducer = producer();
  const handle = started(harness.session.start("work", deferredProducer.adapter));
  const attempt = deferredProducer.attempts[0];
  assert.ok(attempt);

  assert.equal(await promiseSettled(handle.outcome), false);
  assert.equal(await promiseSettled(handle.quiesced), false);
  attempt.sink.reportTerminal({ kind: "succeeded", value: "done" });
  assert.deepEqual(await handle.outcome, { kind: "succeeded", value: "done" });
  assert.equal(await promiseSettled(handle.quiesced), false);
  attempt.sink.reportQuiesced();
  await handle.quiesced;
});

test("outcome and quiescence each resolve exactly once", async () => {
  const harness = sessionHarness();
  const activeProducer = producer();
  const handle = started(harness.session.start("work", activeProducer.adapter));
  let outcomeResolutions = 0;
  let quiescenceResolutions = 0;
  void handle.outcome.then(() => {
    outcomeResolutions++;
    return undefined;
  });
  void handle.quiesced.then(() => {
    quiescenceResolutions++;
    return undefined;
  });
  const sink = activeProducer.attempts[0]?.sink;
  assert.ok(sink);

  sink.reportTerminal({ kind: "succeeded", value: "first" });
  sink.reportTerminal({ kind: "failed", error: "duplicate" });
  sink.reportQuiesced();
  sink.reportQuiesced();
  await Promise.resolve();
  await Promise.resolve();

  assert.equal(outcomeResolutions, 1);
  assert.equal(quiescenceResolutions, 1);
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: "first",
  });
});

test("cancellation completes logically before producer release", async () => {
  const harness = sessionHarness();
  const activeProducer = producer();
  const handle = started(harness.session.start("work", activeProducer.adapter));

  assert.deepEqual(handle.cancel(), { kind: "applied" });
  assert.deepEqual(await handle.outcome, { kind: "canceled", reason: "user" });
  assert.equal(await promiseSettled(handle.quiesced), false);
  activeProducer.attempts[0]?.sink.reportTerminal({
    kind: "failed",
    error: "physical-cancel",
  });
  assert.equal(await promiseSettled(handle.quiesced), false);
  activeProducer.attempts[0]?.sink.reportQuiesced();
  await handle.quiesced;
});

test("cancellation normalizes the omitted reason and preserves the first reason", async () => {
  const harness = sessionHarness();
  const activeProducer = producer();
  const handle = started(harness.session.start("work", activeProducer.adapter));

  assert.deepEqual(handle.cancel(), { kind: "applied" });
  assert.deepEqual(handle.cancel("disposed"), { kind: "no-op" });
  assert.deepEqual(harness.session.cancelCurrent("feature-observer-failed"), {
    kind: "no-op",
  });
  assert.deepEqual(await handle.outcome, { kind: "canceled", reason: "user" });
  assert.deepEqual(activeProducer.attempts[0]?.cancellations, ["user"]);
});

test("exact feature events preserve transition ordering and variants", async () => {
  const harness = sessionHarness();
  const firstProducer = producer();
  const first = started(harness.session.start("first", firstProducer.adapter));
  firstProducer.attempts[0]?.sink.reportProgress(1);
  const secondProducer = producer();
  const second = started(harness.session.start("second", secondProducer.adapter));
  secondProducer.attempts[0]?.sink.reportProgress(2);
  secondProducer.attempts[0]?.sink.reportTerminal({
    kind: "succeeded",
    value: "result",
  });
  assert.deepEqual(harness.session.dispose(), { kind: "applied" });

  assert.deepEqual(harness.events, [
    {
      kind: "started",
      operation: { id: first.id, sequence: 1 },
    },
    {
      kind: "progress",
      progress: { operationId: first.id, value: 1 },
    },
    {
      kind: "replaced",
      previousOperationId: first.id,
      operation: { id: second.id, sequence: 2 },
      reason: "superseded",
    },
    {
      kind: "progress",
      progress: { operationId: second.id, value: 2 },
    },
    {
      kind: "terminal",
      operationId: second.id,
      outcome: { kind: "succeeded", value: "result" },
    },
    {
      kind: "disposed",
      operationId: second.id,
    },
  ]);
  assert.deepEqual(await first.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
});

for (const eventKind of [
  "started",
  "replaced",
  "progress",
  "terminal",
  "canceled",
  "disposed",
] as const) {
  test(`feature reentrancy is rejected first during ${eventKind}`, () => {
    const page = createOperationAuthorityPage(deterministicOptions());
    const quiet = sessionHarness(page);
    const quietProducer = producer();
    const quietHandle = started(quiet.session.start("quiet", quietProducer.adapter));
    const reentrantProducer = producer();
    const reentrantSession = sessionHarness(page).session;
    const results: {
      start?: TestStartResult;
      handle?: OperationControlResult;
      current?: OperationControlResult;
      dispose?: OperationControlResult;
    } = {};
    let target: SessionHarness;
    let armed = false;
    target = sessionHarness(page, event => {
      if (armed && event.kind === eventKind) {
        results.start = reentrantSession.start("reentrant", reentrantProducer.adapter);
        results.handle = quietHandle.cancel();
        results.current = target.session.cancelCurrent();
        results.dispose = target.session.dispose();
      }
      return undefined;
    });
    const targetProducer = producer();
    let targetHandle: TestHandle;

    if (eventKind === "started") {
      armed = true;
      targetHandle = started(target.session.start("first", targetProducer.adapter));
    } else {
      targetHandle = started(target.session.start("first", targetProducer.adapter));
      armed = true;
      if (eventKind === "replaced")
        started(target.session.start("second", producer().adapter));
      if (eventKind === "progress")
        targetProducer.attempts[0]?.sink.reportProgress(1);
      if (eventKind === "terminal")
        targetProducer.attempts[0]?.sink.reportTerminal({
          kind: "succeeded",
          value: "done",
        });
      if (eventKind === "canceled")
        targetHandle.cancel();
      if (eventKind === "disposed")
        target.session.dispose();
    }

    assert.deepEqual(rejected(results.start ?? {
      kind: "started",
      handle: targetHandle,
    }), { kind: "feature-observer-active" });
    const expected = {
      kind: "rejected",
      reason: "feature-observer-active",
    };
    assert.deepEqual(results.handle, expected);
    assert.deepEqual(results.current, expected);
    assert.deepEqual(results.dispose, expected);
    assert.equal(reentrantProducer.attempts.length, 0);
    assert.equal(quietProducer.attempts[0]?.cancellations.length, 0);
  });
}

test("nested producer publication preserves the outer feature guard", async () => {
  const page = createOperationAuthorityPage(deterministicOptions());
  const replacement = producer();
  const activeProducer = producer();
  let harness: SessionHarness;
  let handle: TestHandle;
  let startResult: TestStartResult | undefined;
  let handleResult: OperationControlResult | undefined;
  let currentResult: OperationControlResult | undefined;
  let disposeResult: OperationControlResult | undefined;
  harness = sessionHarness(page, event => {
    if (event.kind === "progress" && event.progress.value === 1) {
      activeProducer.attempts[0]?.sink.reportProgress(2);
      startResult = harness.session.start("replacement", replacement.adapter);
      handleResult = handle.cancel();
      currentResult = harness.session.cancelCurrent();
      disposeResult = harness.session.dispose();
    }
    return undefined;
  });
  handle = started(harness.session.start("first", activeProducer.adapter));

  activeProducer.attempts[0]?.sink.reportProgress(1);

  assert.deepEqual(rejected(startResult ?? {
    kind: "started",
    handle,
  }), { kind: "feature-observer-active" });
  const expectedControlResult = {
    kind: "rejected",
    reason: "feature-observer-active",
  };
  assert.deepEqual(handleResult, expectedControlResult);
  assert.deepEqual(currentResult, expectedControlResult);
  assert.deepEqual(disposeResult, expectedControlResult);
  assert.deepEqual(
    harness.events.map(event => event.kind),
    ["started", "progress", "progress"],
  );
  assert.equal(replacement.attempts.length, 0);
  assert.equal(await promiseSettled(handle.outcome), false);
});

test("nested observer failure abandons the prepared binding before activation", async () => {
  const activeProducer = producer();
  let harness: SessionHarness;
  harness = sessionHarness(undefined, event => {
    if (event.kind === "started")
      activeProducer.attempts[0]?.sink.reportProgress(1);
    if (event.kind === "progress")
      throw new Error("nested observer failed");
    return undefined;
  });

  const handle = started(
    harness.session.start("first", activeProducer.adapter),
  );

  assert.equal(activeProducer.attempts[0]?.activated, false);
  assert.equal(activeProducer.attempts[0]?.abandoned, true);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "feature-observer-failed",
  });
  await handle.quiesced;
  assert.deepEqual(
    harness.events.map(event => event.kind),
    ["started", "progress"],
  );
  assert.equal(harness.diagnostics.length, 1);
  assert.equal(harness.diagnostics[0]?.kind, "feature-observer");
});

interface ThrowingFeatureResult {
  readonly handle: TestHandle | null;
  readonly priorHandle: TestHandle | null;
  readonly producer: ProducerHarness;
  readonly priorProducer: ProducerHarness | null;
  readonly harness: SessionHarness;
  readonly thrown: Error;
}

function throwDuringFeatureEvent(
  eventKind: TestEvent["kind"],
): ThrowingFeatureResult {
  const thrown = new Error(`throw-${eventKind}`);
  let armed = false;
  const harness = sessionHarness(undefined, event => {
    if (armed && event.kind === eventKind) throw thrown;
    return undefined;
  });
  const activeProducer = producer();
  let handle: TestHandle | null = null;
  let priorHandle: TestHandle | null = null;
  let priorProducer: ProducerHarness | null = null;

  if (eventKind === "started") {
    armed = true;
    handle = started(harness.session.start("first", activeProducer.adapter));
  } else {
    priorHandle = started(harness.session.start("first", activeProducer.adapter));
    armed = true;
    if (eventKind === "replaced") {
      priorProducer = activeProducer;
      const replacement = producer();
      handle = started(harness.session.start("second", replacement.adapter));
      return {
        handle,
        priorHandle,
        producer: replacement,
        priorProducer,
        harness,
        thrown,
      };
    }
    handle = priorHandle;
    if (eventKind === "progress")
      activeProducer.attempts[0]?.sink.reportProgress(1);
    if (eventKind === "terminal")
      activeProducer.attempts[0]?.sink.reportTerminal({
        kind: "succeeded",
        value: "done",
      });
    if (eventKind === "canceled")
      handle.cancel();
    if (eventKind === "disposed")
      harness.session.dispose();
  }
  return {
    handle,
    priorHandle,
    producer: activeProducer,
    priorProducer,
    harness,
    thrown,
  };
}

for (const eventKind of [
  "started",
  "replaced",
  "progress",
  "terminal",
  "canceled",
  "disposed",
] as const) {
  test(`a throwing ${eventKind} observer faults without leaking`, async () => {
    const result = throwDuringFeatureEvent(eventKind);
    const { handle, harness, producer: activeProducer } = result;
    assert.ok(handle);
    assert.equal(harness.diagnostics.length, 1);
    assert.deepEqual(harness.diagnostics[0], {
      kind: "feature-observer",
      operationId: eventKind === "replaced"
        ? handle.id
        : eventKind === "started"
          ? handle.id
          : result.priorHandle?.id ?? null,
      error: result.thrown,
    });
    assert.deepEqual(rejected(harness.session.start("later", producer().adapter)), {
      kind: "session-disposed",
    });

    if (eventKind === "started" || eventKind === "replaced") {
      assert.equal(activeProducer.attempts[0]?.activated, false);
      assert.equal(activeProducer.attempts[0]?.abandoned, true);
      await handle.quiesced;
      assert.deepEqual(await handle.outcome, {
        kind: "canceled",
        reason: "feature-observer-failed",
      });
    }
    if (eventKind === "progress") {
      assert.deepEqual(await handle.outcome, {
        kind: "canceled",
        reason: "feature-observer-failed",
      });
      assert.deepEqual(activeProducer.attempts[0]?.cancellations, [
        "feature-observer-failed",
      ]);
    }
    if (eventKind === "terminal") {
      assert.deepEqual(await handle.outcome, {
        kind: "succeeded",
        value: "done",
      });
      assert.deepEqual(activeProducer.attempts[0]?.cancellations, []);
    }
    if (eventKind === "canceled") {
      assert.deepEqual(await handle.outcome, {
        kind: "canceled",
        reason: "user",
      });
      assert.deepEqual(activeProducer.attempts[0]?.cancellations, ["user"]);
    }
    if (eventKind === "disposed") {
      assert.deepEqual(await handle.outcome, {
        kind: "canceled",
        reason: "disposed",
      });
      assert.deepEqual(activeProducer.attempts[0]?.cancellations, ["disposed"]);
    }
    if (eventKind === "replaced") {
      assert.deepEqual(
        result.priorProducer?.attempts[0]?.cancellations,
        ["superseded"],
      );
    }
    const eventsBefore = harness.events.length;
    activeProducer.attempts[0]?.sink.reportProgress(99);
    assert.equal(harness.events.length, eventsBefore);
  });
}

test("diagnostic observers may reenter after state is final", async () => {
  let harness: SessionHarness;
  let nested: TestHandle | undefined;
  const nestedProducer = producer();
  const cancellationError = new Error("cancel");
  harness = sessionHarness(undefined, undefined, diagnostic => {
    assert.equal(diagnostic.kind, "producer-callout");
    nested = started(harness.session.start("nested", nestedProducer.adapter));
    return undefined;
  });
  const throwing = producer({
    onCancellation: () => {
      throw cancellationError;
    },
  });
  const first = started(harness.session.start("first", throwing.adapter));

  assert.deepEqual(first.cancel(), { kind: "applied" });

  assert.ok(nested);
  assert.deepEqual(await first.outcome, {
    kind: "canceled",
    reason: "user",
  });
  assert.equal(await promiseSettled(nested.outcome), false);
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "canceled",
    "replaced",
  ]);
});

test("throwing diagnostic observers use one non-recursive console fallback", () => {
  const original = new Error("producer");
  const observerFailure = new Error("observer");
  const fallbacks: {
    readonly diagnostic: OperationDiagnostic;
    readonly observerError: unknown;
  }[] = [];
  const page = createOperationAuthorityPage({
    ...deterministicOptions(),
    lastResortConsole: {
      report: (diagnostic, observerError) => {
        fallbacks.push({ diagnostic, observerError });
        return undefined;
      },
    },
  });
  const harness = sessionHarness(page, undefined, () => {
    throw observerFailure;
  });
  const activeProducer = producer();
  started(harness.session.start("work", activeProducer.adapter));

  activeProducer.attempts[0]?.sink.reportUnexpectedFailure(original);

  assert.equal(harness.diagnostics.length, 1);
  assert.deepEqual(fallbacks, [{
    diagnostic: {
      kind: "producer-contract",
      operationId: activeProducer.attempts[0]?.identity.id ?? null,
      error: original,
    },
    observerError: observerFailure,
  }]);
});

test("unexpected stale failure remains diagnostic without feature publication", () => {
  const harness = sessionHarness();
  const firstProducer = producer();
  started(harness.session.start("first", firstProducer.adapter));
  started(harness.session.start("second", producer().adapter));
  const eventsBefore = harness.events.length;
  const lateFailure = new Error("late-failure");

  firstProducer.attempts[0]?.sink.reportUnexpectedFailure(lateFailure);

  assert.equal(harness.events.length, eventsBefore);
  assert.deepEqual(harness.diagnostics, [{
    kind: "producer-contract",
    operationId: firstProducer.attempts[0]?.identity.id ?? null,
    error: lateFailure,
  }]);
});

test("unexpected terminal commits authority before diagnostic and publishes in order", async () => {
  const order: string[] = [];
  let handle: TestHandle;
  let cancelResult: OperationControlResult | undefined;
  const harness = sessionHarness(
    createOperationAuthorityPage(deterministicOptions()),
    event => {
      order.push(`feature:${event.kind}`);
      return undefined;
    },
    () => {
      order.push("diagnostic");
      cancelResult = handle.cancel("user");
      return undefined;
    },
  );
  const activeProducer = producer();
  handle = started(harness.session.start("work", activeProducer.adapter));

  activeProducer.attempts[0]?.sink.reportUnexpectedTerminal(
    "feature-error",
    "unexpected-failure",
  );

  assert.deepEqual(cancelResult, { kind: "no-op" });
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "feature-error",
  });
  assert.equal(await promiseSettled(handle.quiesced), false);
  assert.deepEqual(order, [
    "feature:started",
    "diagnostic",
    "feature:terminal",
  ]);
  activeProducer.attempts[0]?.sink.reportQuiesced();
  assert.equal(await promiseSettled(handle.quiesced), true);
});

test("unexpected terminal reservation survives diagnostic reentrant replacement", async () => {
  const order: string[] = [];
  const nestedProducer = producer();
  let harness: SessionHarness;
  let nestedResult: TestStartResult | undefined;
  harness = sessionHarness(
    createOperationAuthorityPage(deterministicOptions()),
    event => {
      order.push(`feature:${event.kind}`);
      return undefined;
    },
    () => {
      order.push("diagnostic");
      nestedResult = harness.session.start("nested", nestedProducer.adapter);
      return undefined;
    },
  );
  const activeProducer = producer();
  const handle = started(
    harness.session.start("original", activeProducer.adapter),
  );

  activeProducer.attempts[0]?.sink.reportUnexpectedTerminal(
    "feature-error",
    "unexpected-failure",
  );

  assert.equal(nestedResult?.kind, "started");
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "feature-error",
  });
  assert.deepEqual(order, [
    "feature:started",
    "diagnostic",
    "feature:replaced",
    "feature:terminal",
  ]);
});

test("unexpected terminal survives throwing diagnostic observation", async () => {
  const observerFailure = new Error("observer");
  const fallbacks: {
    readonly diagnostic: OperationDiagnostic;
    readonly observerError: unknown;
  }[] = [];
  const page = createOperationAuthorityPage({
    ...deterministicOptions(),
    lastResortConsole: {
      report: (diagnostic, observerError) => {
        fallbacks.push({ diagnostic, observerError });
        return undefined;
      },
    },
  });
  const harness = sessionHarness(page, undefined, () => {
    throw observerFailure;
  });
  const activeProducer = producer();
  const handle = started(
    harness.session.start("work", activeProducer.adapter),
  );

  activeProducer.attempts[0]?.sink.reportUnexpectedTerminal(
    "feature-error",
    "unexpected-failure",
  );

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "feature-error",
  });
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "terminal",
  ]);
  assert.equal(fallbacks.length, 1);
  assert.equal(fallbacks[0]?.observerError, observerFailure);
});

test("unexpected terminal on a stale operation remains diagnostic only", async () => {
  const harness = sessionHarness();
  const staleProducer = producer();
  const staleHandle = started(
    harness.session.start("stale", staleProducer.adapter),
  );
  started(harness.session.start("current", producer().adapter));
  const eventsBefore = harness.events.length;

  staleProducer.attempts[0]?.sink.reportUnexpectedTerminal(
    "late-error",
    "late-unexpected-failure",
  );

  assert.deepEqual(await staleHandle.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
  assert.equal(harness.events.length, eventsBefore);
  assert.deepEqual(harness.diagnostics, [{
    kind: "producer-contract",
    operationId: staleProducer.attempts[0]?.identity.id ?? null,
    error: "late-unexpected-failure",
  }]);
});

test("stale progress, success, failure, and release cannot change the current view", async () => {
  const harness = sessionHarness();
  const progressProducer = producer();
  const progressHandle = started(
    harness.session.start("progress", progressProducer.adapter),
  );
  const successProducer = producer();
  const successHandle = started(
    harness.session.start("success", successProducer.adapter),
  );
  const failureProducer = producer();
  const failureHandle = started(
    harness.session.start("failure", failureProducer.adapter),
  );
  const currentProducer = producer();
  const current = started(harness.session.start("current", currentProducer.adapter));
  const eventCount = harness.events.length;

  progressProducer.attempts[0]?.sink.reportProgress(1);
  progressProducer.attempts[0]?.sink.reportTerminal({
    kind: "failed",
    error: "stale-progress-terminal",
  });
  successProducer.attempts[0]?.sink.reportTerminal({
    kind: "succeeded",
    value: "stale-success",
  });
  failureProducer.attempts[0]?.sink.reportTerminal({
    kind: "failed",
    error: "stale-failure",
  });
  progressProducer.attempts[0]?.sink.reportQuiesced();
  successProducer.attempts[0]?.sink.reportQuiesced();
  failureProducer.attempts[0]?.sink.reportQuiesced();

  assert.equal(harness.events.length, eventCount);
  assert.equal(await promiseSettled(current.outcome), false);
  assert.equal(currentProducer.attempts[0]?.cancellations.length, 0);
  await progressHandle.quiesced;
  await successHandle.quiesced;
  await failureHandle.quiesced;
});

test("terminal, cancellation, and release races preserve their first authorities", async () => {
  const terminalFirst = sessionHarness();
  const terminalProducer = producer();
  const terminalHandle = started(
    terminalFirst.session.start("terminal-first", terminalProducer.adapter),
  );
  terminalProducer.attempts[0]?.sink.reportTerminal({
    kind: "succeeded",
    value: "done",
  });
  terminalProducer.attempts[0]?.sink.reportTerminal({
    kind: "failed",
    error: "late",
  });
  assert.deepEqual(terminalHandle.cancel(), { kind: "no-op" });
  terminalProducer.attempts[0]?.sink.reportQuiesced();
  assert.deepEqual(await terminalHandle.outcome, {
    kind: "succeeded",
    value: "done",
  });
  assert.deepEqual(
    terminalFirst.events.map(event => event.kind),
    ["started", "terminal"],
  );
  await terminalHandle.quiesced;

  const cancelFirst = sessionHarness();
  const cancelProducer = producer();
  const cancelHandle = started(
    cancelFirst.session.start("cancel-first", cancelProducer.adapter),
  );
  cancelHandle.cancel("disposed");
  cancelProducer.attempts[0]?.sink.reportTerminal({
    kind: "failed",
    error: "late",
  });
  cancelProducer.attempts[0]?.sink.reportQuiesced();
  assert.deepEqual(await cancelHandle.outcome, {
    kind: "canceled",
    reason: "disposed",
  });
  await cancelHandle.quiesced;

  const releaseFirst = sessionHarness();
  const releaseProducer = producer();
  const releaseHandle = started(
    releaseFirst.session.start("release-first", releaseProducer.adapter),
  );
  releaseProducer.attempts[0]?.sink.reportQuiesced();
  assert.equal(await promiseSettled(releaseHandle.quiesced), false);
  assert.deepEqual(releaseHandle.cancel(), { kind: "applied" });
  releaseProducer.attempts[0]?.sink.reportTerminal({
    kind: "failed",
    error: "after-release",
  });
  releaseProducer.attempts[0]?.sink.reportQuiesced();
  assert.deepEqual(await releaseHandle.outcome, {
    kind: "canceled",
    reason: "user",
  });
  await releaseHandle.quiesced;
  assert.equal(releaseFirst.diagnostics.length, 1);
  assert.equal(releaseFirst.diagnostics[0]?.kind, "producer-contract");
});

test("stale handles cannot affect replacements or settled operations", async () => {
  const harness = sessionHarness();
  const firstProducer = producer();
  const first = started(harness.session.start("first", firstProducer.adapter));
  const secondProducer = producer();
  const second = started(harness.session.start("second", secondProducer.adapter));

  assert.deepEqual(first.cancel("disposed"), { kind: "no-op" });
  assert.equal(firstProducer.attempts[0]?.cancellations.length, 1);
  assert.equal(secondProducer.attempts[0]?.cancellations.length, 0);
  assert.equal(await promiseSettled(second.outcome), false);

  secondProducer.attempts[0]?.sink.reportTerminal({
    kind: "succeeded",
    value: "done",
  });
  secondProducer.attempts[0]?.sink.reportQuiesced();
  assert.deepEqual(second.cancel(), { kind: "no-op" });
  assert.equal(secondProducer.attempts[0]?.cancellations.length, 0);
  assert.deepEqual(await second.outcome, {
    kind: "succeeded",
    value: "done",
  });
});

test("duplicate producer reports and callbacks after release are diagnostic", () => {
  const harness = sessionHarness();
  const activeProducer = producer();
  started(harness.session.start("work", activeProducer.adapter));
  const attempt = activeProducer.attempts[0];
  assert.ok(attempt);

  attempt.sink.reportTerminal({ kind: "succeeded", value: "done" });
  attempt.sink.reportTerminal({ kind: "failed", error: "duplicate" });
  attempt.sink.reportQuiesced();
  attempt.sink.reportQuiesced();
  attempt.sink.reportProgress(1);
  attempt.sink.reportTerminal({ kind: "failed", error: "after-release" });
  attempt.sink.reportUnexpectedFailure(new Error("after-release"));

  assert.equal(harness.diagnostics.length, 5);
  assert.ok(harness.diagnostics.every(
    diagnostic => diagnostic.kind === "producer-contract",
  ));
});

test("disposal before start publishes once and prevents producer activity", () => {
  const harness = sessionHarness();
  const neverPrepared = producer();

  assert.deepEqual(harness.session.dispose(), { kind: "applied" });
  assert.deepEqual(harness.session.dispose(), { kind: "no-op" });
  assert.deepEqual(
    rejected(harness.session.start("later", neverPrepared.adapter)),
    { kind: "session-disposed" },
  );
  assert.deepEqual(harness.events, [{
    kind: "disposed",
    operationId: null,
  }]);
  assert.equal(neverPrepared.attempts.length, 0);
});

test("disposal commits before endpoint callbacks and consumes reports through release", async () => {
  const harness = sessionHarness();
  const reentrant = producer();
  let reentrantResult: TestStartResult | undefined;
  const activeProducer = producer({
    onCancellation: attempt => {
      reentrantResult = harness.session.start("reentrant", reentrant.adapter);
      attempt.sink.reportProgress(1);
      attempt.sink.reportTerminal({ kind: "failed", error: "disposed-late" });
      attempt.sink.reportQuiesced();
      return undefined;
    },
  });
  const handle = started(harness.session.start("work", activeProducer.adapter));

  assert.deepEqual(harness.session.dispose(), { kind: "applied" });

  assert.deepEqual(rejected(reentrantResult ?? {
    kind: "started",
    handle,
  }), { kind: "session-disposed" });
  assert.equal(reentrant.attempts.length, 0);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "disposed",
  });
  await handle.quiesced;
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "disposed",
  ]);
});

test("removing the common publication-authority predicate admits stale progress", async (t) => {
  function staleProgressKinds(page: OperationAuthorityPage): TestEvent["kind"][] {
    const harness = sessionHarness(page);
    const firstProducer = producer();
    started(harness.session.start("first", firstProducer.adapter));
    started(harness.session.start("second", producer().adapter));
    firstProducer.attempts[0]?.sink.reportProgress(99);
    return harness.events.map(event => event.kind);
  }

  assert.deepEqual(
    staleProgressKinds(createOperationAuthorityPage(deterministicOptions())),
    ["started", "replaced"],
  );

  const sourceUrl = new URL("../src/operation-authority.ts", import.meta.url);
  const sourceText = await readFile(sourceUrl, "utf8");
  const authorityAnchor =
    "return createPage(options, standardPublicationAuthority);";
  assert.equal(sourceText.split(authorityAnchor).length - 1, 1);
  const mutatedText = sourceText.replace(
    authorityAnchor,
    "return createPage(options, session => !session.disposed);",
  );
  const temporaryDirectory = await mkdtemp(
    join(tmpdir(), "inspect-web-operation-authority-"),
  );
  t.after(async () => {
    await rm(temporaryDirectory, { recursive: true, force: true });
  });
  const mutatedPath = join(temporaryDirectory, "operation-authority.ts");
  await writeFile(mutatedPath, mutatedText, "utf8");
  const imported: unknown = await import(
    `${pathToFileURL(mutatedPath).href}?mutation=publication-authority`
  );
  assert.ok(isOperationAuthorityModule(imported));
  assert.deepEqual(
    staleProgressKinds(imported.createOperationAuthorityPage(
      deterministicOptions(),
    )),
    ["started", "replaced", "progress"],
  );
});

test("observer-failure abandonment resolves quiescence once", async () => {
  const observerError = new Error("observer");
  let abandonments = 0;
  const harness = sessionHarness(undefined, event => {
    if (event.kind === "started") throw observerError;
    return undefined;
  });
  const prepared = producer({
    onAbandon: () => {
      abandonments++;
      return undefined;
    },
  });

  const handle = started(harness.session.start("work", prepared.adapter));
  let resolutions = 0;
  void handle.quiesced.then(() => {
    resolutions++;
    return undefined;
  });
  await handle.quiesced;
  prepared.attempts[0]?.sink.reportQuiesced();
  await Promise.resolve();

  assert.equal(abandonments, 1);
  assert.equal(resolutions, 1);
  assert.equal(harness.diagnostics.length, 2);
  assert.equal(harness.diagnostics[0]?.kind, "feature-observer");
  assert.equal(harness.diagnostics[1]?.kind, "producer-contract");
});

interface FetchRequest {
  readonly signal: AbortSignal;
  readonly promise: Promise<string>;
  readonly resolve: (value: string) => void;
}

function fakeFetchRequests(): {
  readonly fetch: (input: string, signal: AbortSignal) => Promise<string>;
  readonly requests: FetchRequest[];
} {
  const requests: FetchRequest[] = [];
  return {
    requests,
    fetch: (_input, signal) => {
      let resolveRequest: ((value: string) => void) | undefined;
      const promise = new Promise<string>((resolve) => {
        resolveRequest = resolve;
      });
      requests.push({
        signal,
        promise,
        resolve: value => resolveRequest?.(value),
      });
      return promise;
    },
  };
}

function browserFetchAdapter(
  fetchText: (input: string, signal: AbortSignal) => Promise<string>,
): TestAdapter {
  return {
    prepare: (_identity, input, sink) => {
      const controller = new AbortController();
      let activated = false;
      const binding: PreparedOperationProducer = {
        requestCancellation: () => {
          controller.abort();
          return undefined;
        },
        activate: () => {
          activated = true;
          void fetchText(input, controller.signal).then(
            value => {
              sink.reportTerminal({ kind: "succeeded", value });
              sink.reportQuiesced();
              return undefined;
            },
            (error: unknown) => {
              sink.reportTerminal({
                kind: "failed",
                error: error instanceof Error ? error.message : String(error),
              });
              sink.reportQuiesced();
              return undefined;
            },
          );
          return undefined;
        },
        abandon: () => {
          assert.equal(activated, false);
          controller.abort();
          return undefined;
        },
      };
      return { kind: "prepared", binding };
    },
  };
}

test("a browser fetch adapter uses the same placement-independent authority", async () => {
  const fake = fakeFetchRequests();
  const adapter = browserFetchAdapter(fake.fetch);
  const harness = sessionHarness();
  const first = started(harness.session.start("/first", adapter));
  const firstRequest = fake.requests[0];
  assert.ok(firstRequest);
  const second = started(harness.session.start("/second", adapter));
  const secondRequest = fake.requests[1];
  assert.ok(secondRequest);

  assert.equal(firstRequest.signal.aborted, true);
  firstRequest.resolve("stale");
  await firstRequest.promise;
  await first.quiesced;
  assert.deepEqual(await first.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "replaced",
  ]);

  secondRequest.resolve("current");
  await secondRequest.promise;
  assert.deepEqual(await second.outcome, {
    kind: "succeeded",
    value: "current",
  });
  await second.quiesced;
  assert.deepEqual(harness.events.map(event => event.kind), [
    "started",
    "replaced",
    "terminal",
  ]);
});

function compileTimeCallbackContracts(): void {
  // @ts-expect-error Operation IDs can only be constructed by the page allocator.
  const forgedOperationId: OperationId = "forged";
  const validFeature: OperationFeatureObserver<string, string, number> = {
    publish: _event => undefined,
  };
  const validDiagnostic: OperationDiagnosticObserver = {
    report: _diagnostic => undefined,
  };
  const validSink: TestSink = {
    reportProgress: _value => undefined,
    reportTerminal: _outcome => undefined,
    reportUnexpectedTerminal: (_error, _diagnostic) => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: _error => undefined,
  };
  const validBinding: PreparedOperationProducer = {
    requestCancellation: _reason => undefined,
    activate: () => undefined,
    abandon: () => undefined,
  };
  const validAdapter: TestAdapter = {
    prepare: () => ({ kind: "prepared", binding: validBinding }),
  };
  void validFeature;
  void validDiagnostic;
  void validSink;
  void validAdapter;

  const promiseFeature: OperationFeatureObserver<string, string, number> = {
    // @ts-expect-error Feature publication is synchronous and returns exactly undefined.
    publish: async _event => {},
  };
  const narrowedFeature: OperationFeatureObserver<string, string, number> = {
    // @ts-expect-error The feature callback must accept every owner-issued event.
    publish: (_event: Extract<TestEvent, { readonly kind: "started" }>) => undefined,
  };
  const promiseDiagnostic: OperationDiagnosticObserver = {
    // @ts-expect-error Diagnostic delivery is synchronous and returns exactly undefined.
    report: async _diagnostic => {},
  };
  const narrowedDiagnostic: OperationDiagnosticObserver = {
    // @ts-expect-error The diagnostic callback must accept every diagnostic category.
    report: (_diagnostic: OperationDiagnostic & {
      readonly kind: "producer-contract";
    }) => undefined,
  };
  const promiseSink: TestSink = {
    // @ts-expect-error Producer sink callbacks never return Promises.
    reportProgress: async _value => {},
    reportTerminal: _outcome => undefined,
    reportUnexpectedTerminal: (_error, _diagnostic) => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: _error => undefined,
  };
  const narrowedSink: TestSink = {
    reportProgress: _value => undefined,
    // @ts-expect-error A terminal callback cannot narrow the owner-issued outcome.
    reportTerminal: (
      _outcome: { readonly kind: "succeeded"; readonly value: string },
    ) => undefined,
    reportUnexpectedTerminal: (_error, _diagnostic) => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: _error => undefined,
  };
  const promiseBinding: PreparedOperationProducer = {
    requestCancellation: _reason => undefined,
    // @ts-expect-error Activation is synchronous and returns exactly undefined.
    activate: async () => {},
    // @ts-expect-error Abandonment is synchronous and returns exactly undefined.
    abandon: async () => {},
  };
  const narrowedBinding: PreparedOperationProducer = {
    // @ts-expect-error Cancellation must accept every owner-issued reason.
    requestCancellation: (_reason: "user") => undefined,
    activate: () => undefined,
    abandon: () => undefined,
  };
  const promiseAdapter: TestAdapter = {
    // @ts-expect-error Preparation is synchronous and returns a typed preparation result.
    prepare: async () => ({ kind: "prepared", binding: validBinding }),
  };
  const narrowedAdapter: TestAdapter = {
    // @ts-expect-error Preparation cannot narrow the feature-owned input.
    prepare: (
      _identity: OperationIdentity,
      _input: "only-this-input",
      _sink: TestSink,
    ): OperationPreparation<string> => ({
      kind: "prepared",
      binding: validBinding,
    }),
  };
  const narrowedSinkAdapter: TestAdapter = {
    // @ts-expect-error Preparation cannot require a narrower sink shape.
    prepare: (
      _identity: OperationIdentity,
      _input: string,
      _sink: TestSink & { readonly producerOnly: true },
    ): OperationPreparation<string> => ({
      kind: "prepared",
      binding: validBinding,
    }),
  };
  const sequenceCoupledIdentitySource: OperationAuthorityPageOptions = {
    allocation: {
      // @ts-expect-error Opaque ID construction cannot depend on the sequence.
      createId: (_sequence: number) => "sequence-coupled",
    },
  };
  void forgedOperationId;
  void promiseFeature;
  void narrowedFeature;
  void promiseDiagnostic;
  void narrowedDiagnostic;
  void promiseSink;
  void narrowedSink;
  void promiseBinding;
  void narrowedBinding;
  void promiseAdapter;
  void narrowedAdapter;
  void narrowedSinkAdapter;
  void sequenceCoupledIdentitySource;
}
void compileTimeCallbackContracts;
