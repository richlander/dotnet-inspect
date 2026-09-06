import assert from "node:assert/strict";
import test from "node:test";

import type {
  OperationCancelReason,
  OperationId,
} from "../src/operation-authority.ts";
import {
  decodeBoundMainToWorkerEnvelope,
  decodeEpochFailedPayload,
  decodeEventsPayload,
  decodeInitializePayload,
  decodeProgressPayload,
  decodeRejectedPayload,
  decodeSettledPayload,
  decodeStartPayload,
  decodeStartupFailedPayload,
  decodeUnboundInitializationEnvelope,
  decodeWorkerToMainEnvelope,
  type BoundedPayloadDecodeResult,
  type BoundedPayloadDecoder,
  type InitializeMainToWorkerEnvelope,
  type MainToWorkerEnvelope,
  type ManagedOperationSettlement,
  type RawMainToWorkerEnvelope,
  type RawWorkerToMainEnvelope,
  type WorkerEnvelopeDecodeFailure,
  type WorkerEnvelopeDecodeFailureCategory,
  type WorkerEnvelopeDecodeFailureCode,
  type WorkerEnvelopeDecodeResult,
  type WorkerLivenessAllowance,
  type WorkerOperationCancelReason,
  type WorkerSettlementPayloadDecoders,
  type WorkerToMainEnvelope,
  type WorkerWireEpochToken,
  type WorkerWireOperationReference,
  WORKER_RUNTIME_PROTOCOL_VERSION,
  WORKER_RUNTIME_MAX_EVENT_BATCH_SIZE,
} from "../src/worker-runtime-protocol.ts";

const EPOCH_TOKEN = 7;
const operation = {
  operationId: "operation-7",
  operationSequence: 11,
};
const header: {
  readonly protocolVersion: typeof WORKER_RUNTIME_PROTOCOL_VERSION;
  readonly epochToken: number;
} = {
  protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
  epochToken: EPOCH_TOKEN,
};

function boundedStringDecoder(
  maximumCodeUnits = 32,
): BoundedPayloadDecoder<string> {
  return {
    decode: value => {
      if (typeof value !== "string") {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected an owner-defined string payload.",
        };
      }
      if (value.length > maximumCodeUnits) {
        return {
          kind: "rejected",
          reason: "oversized",
          message: `Owner payload exceeds ${maximumCodeUnits} code units.`,
        };
      }
      return { kind: "decoded", value };
    },
  };
}

function numberDecoder(): BoundedPayloadDecoder<number> {
  return {
    decode: value => typeof value === "number"
      ? { kind: "decoded", value }
      : {
          kind: "rejected",
          reason: "invalid",
          message: "Expected an owner-defined number payload.",
        },
  };
}

const settlementDecoders:
  WorkerSettlementPayloadDecoders<string, string, string> = {
    value: boundedStringDecoder(),
    error: boundedStringDecoder(),
    diagnostic: boundedStringDecoder(),
  };

function decodeBoundMain(
  value: unknown,
  expectedEpochToken = EPOCH_TOKEN,
): WorkerEnvelopeDecodeResult<RawMainToWorkerEnvelope> {
  return decodeBoundMainToWorkerEnvelope(value, expectedEpochToken);
}

function decodeWorker(
  value: unknown,
  expectedEpochToken = EPOCH_TOKEN,
): WorkerEnvelopeDecodeResult<RawWorkerToMainEnvelope> {
  return decodeWorkerToMainEnvelope(value, expectedEpochToken);
}

function decoded<T>(result: WorkerEnvelopeDecodeResult<T>): T {
  if (result.kind === "failure") {
    assert.fail(
      `${result.failure.code} at ${result.failure.path}: `
        + result.failure.message,
    );
  }
  return result.value;
}

function assertDecodeFailure(
  result: WorkerEnvelopeDecodeResult<unknown>,
  code: WorkerEnvelopeDecodeFailureCode,
  path: string,
): void {
  if (result.kind === "success")
    assert.fail(`Expected ${code} at ${path}, decoded successfully.`);
  assert.equal(result.failure.code, code);
  assert.equal(result.failure.path, path);
}

function hasKind<
  TEnvelope extends { readonly kind: string },
  TKind extends TEnvelope["kind"],
>(
  envelope: TEnvelope,
  kind: TKind,
): envelope is Extract<TEnvelope, { readonly kind: TKind }> {
  return envelope.kind === kind;
}

function requireKind<
  TEnvelope extends { readonly kind: string },
  TKind extends TEnvelope["kind"],
>(
  envelope: TEnvelope,
  kind: TKind,
): Extract<TEnvelope, { readonly kind: TKind }> {
  if (!hasKind(envelope, kind))
    assert.fail(`Expected ${kind}, received ${envelope.kind}.`);
  return envelope;
}

const mainToWorkerFixtures: readonly {
  readonly name: string;
  readonly envelope: unknown;
}[] = [
  {
    name: "Initialize",
    envelope: {
      ...header,
      kind: "initialize",
      bootstrap: "bootstrap",
      idleHeartbeatIntervalMilliseconds: 1_000,
      idleAllowanceMilliseconds: 1_100,
    },
  },
  {
    name: "Start",
    envelope: {
      ...header,
      kind: "start",
      operation,
      operationKind: "inspect-source",
      payload: "input",
    },
  },
  {
    name: "Cancel",
    envelope: {
      ...header,
      kind: "cancel",
      operation,
      reason: "superseded",
    },
  },
  {
    name: "Probe",
    envelope: {
      ...header,
      kind: "probe",
      probeSequence: 2,
    },
  },
];

function eventBatch(entries: unknown): unknown {
  return { ...header, kind: "events", operation, entries };
}

test("event batches keep the complete nonterminal order and separate payload codecs", () => {
  const envelope = requireKind(decoded(decodeWorker(eventBatch([
    { kind: "progress", payload: 1 },
    { kind: "durable", payload: "Package.One" },
    { kind: "progress", payload: 2 },
    { kind: "durable", payload: "Package.Two failed" },
  ]))), "events");
  const result = decoded(decodeEventsPayload(
    envelope,
    numberDecoder(),
    boundedStringDecoder(),
  ));
  assert.deepEqual(result.entries, envelope.entries);
});

test("event batches accept singleton and maximum-sized batches", () => {
  for (const count of [1, WORKER_RUNTIME_MAX_EVENT_BATCH_SIZE]) {
    const entries = Array.from({ length: count }, (_, index) => ({
      kind: "durable",
      payload: `item-${index}`,
    }));
    const envelope = requireKind(
      decoded(decodeWorker(eventBatch(entries))),
      "events",
    );
    assert.equal(envelope.entries.length, count);
  }
});

test("event batch size is bounded before any entry is read", () => {
  assertDecodeFailure(decodeWorker(eventBatch([])), "invalid-integer", "$.entries.length");
  assertDecodeFailure(decodeWorker(eventBatch({})), "not-array", "$.entries");
  const entries: unknown[] = [];
  entries.length = WORKER_RUNTIME_MAX_EVENT_BATCH_SIZE + 1;
  Object.defineProperty(entries, "0", {
    get: () => assert.fail("An over-budget batch must not inspect entries."),
  });
  assertDecodeFailure(decodeWorker(eventBatch(entries)), "payload-oversized", "$.entries");
});

test("event batches require dense closed own-data entries", () => {
  const sparse: unknown[] = [];
  sparse.length = 1;
  assertDecodeFailure(
    decodeWorker(eventBatch(sparse)),
    "missing-property",
    '$.entries["0"]',
  );
  const entries: unknown[] = [null];
  Object.defineProperty(entries, "0", {
    get: () => assert.fail("Batch decoding must not invoke an entry accessor."),
  });
  assertDecodeFailure(
    decodeWorker(eventBatch(entries)),
    "accessor-property",
    '$.entries["0"]',
  );
  const extra = Object.assign([{ kind: "durable", payload: "item" }], { extra: true });
  assertDecodeFailure(
    decodeWorker(eventBatch(extra)),
    "unexpected-property",
    "$.entries.extra",
  );
  assertDecodeFailure(
    decodeWorker(eventBatch([{ kind: "durable", payload: "item", extra: true }])),
    "unexpected-property",
    "$.entries[0].extra",
  );
});

test("an event batch cannot carry semantic completion", () => {
  assertDecodeFailure(
    decodeWorker(eventBatch([{ kind: "completed", payload: "done" }])),
    "invalid-discriminator",
    "$.entries[0].kind",
  );
});

test("event payload failure rejects the complete typed batch at its indexed path", () => {
  const envelope = requireKind(decoded(decodeWorker(eventBatch([
    { kind: "durable", payload: "valid" },
    { kind: "durable", payload: "over the declared budget" },
  ]))), "events");
  assertDecodeFailure(
    decodeEventsPayload(envelope, numberDecoder(), boundedStringDecoder(5)),
    "payload-oversized",
    "$.entries[1].payload",
  );
});

test("progress-only registrations cannot decode durable entries", () => {
  const envelope = requireKind(decoded(decodeWorker(eventBatch([
    { kind: "durable", payload: "item" },
  ]))), "events");
  assertDecodeFailure(
    decodeEventsPayload(envelope, numberDecoder(), undefined),
    "payload-rejected",
    "$.entries[0].payload",
  );
});

test("structural event decoding does not inspect or reinterpret feature payloads", () => {
  const payload = Object.defineProperty({}, "featureOwned", {
    get: () => assert.fail("Only the owner may decode its feature payload."),
  });
  const source = [{ kind: "durable", payload }];
  const envelope = requireKind(decoded(decodeWorker(eventBatch(source))), "events");
  assert.equal(envelope.entries[0]?.payload, payload);
  source.length = 0;
  assert.equal(envelope.entries.length, 1);
});

for (const fixture of mainToWorkerFixtures) {
  test(`structurally decodes the bound ${fixture.name} main envelope`, () => {
    assert.deepEqual(
      decoded(decodeBoundMain(fixture.envelope)),
      fixture.envelope,
    );
  });
}

const workerToMainFixtures: readonly {
  readonly name: string;
  readonly envelope: unknown;
}[] = [
  {
    name: "Events",
    envelope: {
      ...header,
      kind: "events",
      operation,
      entries: [
        { kind: "progress", payload: 1 },
        { kind: "durable", payload: "Package.One" },
        { kind: "durable", payload: "Package.Two failed" },
      ],
    },
  },
  {
    name: "Ready",
    envelope: {
      ...header,
      kind: "ready",
      idleHeartbeatIntervalMilliseconds: 1_000,
    },
  },
  {
    name: "StartupFailed",
    envelope: {
      ...header,
      kind: "startup-failed",
      diagnostic: "bootstrap failed",
    },
  },
  {
    name: "Accepted with bounded allowance",
    envelope: {
      ...header,
      kind: "accepted",
      operation,
      allowance: {
        kind: "bounded",
        maxSilentActiveMilliseconds: 500,
      },
    },
  },
  {
    name: "Accepted with unbounded allowance",
    envelope: {
      ...header,
      kind: "accepted",
      operation,
      allowance: { kind: "unbounded" },
    },
  },
  {
    name: "Rejected",
    envelope: {
      ...header,
      kind: "rejected",
      operation,
      error: "invalid feature input",
      diagnostic: "input rejected",
    },
  },
  {
    name: "CancelAcknowledged",
    envelope: {
      ...header,
      kind: "cancel-acknowledged",
      operation,
      status: "not-active",
    },
  },
  {
    name: "Progress",
    envelope: {
      ...header,
      kind: "progress",
      operation,
      payload: "resolving symbols",
    },
  },
  {
    name: "Settled Succeeded",
    envelope: {
      ...header,
      kind: "settled",
      operation,
      settlement: {
        kind: "succeeded",
        value: "result",
      },
    },
  },
  {
    name: "Settled Failed Expected",
    envelope: {
      ...header,
      kind: "settled",
      operation,
      settlement: {
        kind: "failed",
        failureKind: "expected",
        error: "feature failed",
        diagnostic: "safe feature diagnostic",
      },
    },
  },
  {
    name: "Settled Failed Unexpected",
    envelope: {
      ...header,
      kind: "settled",
      operation,
      settlement: {
        kind: "failed",
        failureKind: "unexpected",
        error: "producer failed",
        diagnostic: "safe producer diagnostic",
      },
    },
  },
  {
    name: "Settled Canceled",
    envelope: {
      ...header,
      kind: "settled",
      operation,
      settlement: {
        kind: "canceled",
        reason: "worker-restarted",
      },
    },
  },
  {
    name: "Heartbeat",
    envelope: {
      ...header,
      kind: "heartbeat",
    },
  },
  {
    name: "ProbeAcknowledged",
    envelope: {
      ...header,
      kind: "probe-acknowledged",
      probeSequence: 2,
    },
  },
  {
    name: "EpochWorkStarted",
    envelope: {
      ...header,
      kind: "epoch-work-started",
      workSequence: 3,
      allowance: { kind: "unbounded" },
    },
  },
  {
    name: "EpochWorkFinished",
    envelope: {
      ...header,
      kind: "epoch-work-finished",
      workSequence: 3,
    },
  },
  {
    name: "EpochFailed",
    envelope: {
      ...header,
      kind: "epoch-failed",
      diagnostic: "managed boundary failed",
    },
  },
];

for (const fixture of workerToMainFixtures) {
  test(`structurally decodes the ${fixture.name} worker envelope`, () => {
    assert.deepEqual(decoded(decodeWorker(fixture.envelope)), fixture.envelope);
  });
}

test("unbound initialization accepts only Initialize", () => {
  const bootstrapDecoder = boundedStringDecoder();
  const initialized = decoded(decodeUnboundInitializationEnvelope({
    ...header,
    kind: "initialize",
    bootstrap: "bootstrap",
    idleHeartbeatIntervalMilliseconds: 1_000,
    idleAllowanceMilliseconds: 1_100,
  }, bootstrapDecoder));
  assert.equal(initialized.bootstrap, "bootstrap");

  for (const fixture of mainToWorkerFixtures.slice(1)) {
    assertDecodeFailure(
      decodeUnboundInitializationEnvelope(
        fixture.envelope,
        bootstrapDecoder,
      ),
      "invalid-discriminator",
      "$.kind",
    );
  }
});

test("bound main decoding checks the expected epoch for every variant", () => {
  for (const fixture of mainToWorkerFixtures) {
    assertDecodeFailure(
      decodeBoundMain(fixture.envelope, EPOCH_TOKEN + 1),
      "wrong-epoch",
      "$.epochToken",
    );
  }
});

test("worker decoding checks the expected epoch for every variant", () => {
  for (const fixture of workerToMainFixtures) {
    assertDecodeFailure(
      decodeWorker(fixture.envelope, EPOCH_TOKEN + 1),
      "wrong-epoch",
      "$.epochToken",
    );
  }
});

test("raw worker decoding does not inspect or decode payload internals", () => {
  let payloadAccessorCalls = 0;
  const payload = {
    get text(): string {
      payloadAccessorCalls++;
      return "progress";
    },
  };
  const progress = requireKind(decoded(decodeWorker({
    ...header,
    kind: "progress",
    operation,
    payload,
  })), "progress");
  assert.equal(progress.payload, payload);

  let diagnosticAccessorCalls = 0;
  const diagnostic = {
    get text(): string {
      diagnosticAccessorCalls++;
      return "diagnostic";
    },
  };
  const settled = requireKind(decoded(decodeWorker({
    ...header,
    kind: "settled",
    operation,
    settlement: {
      kind: "failed",
      failureKind: "expected",
      error: { arbitrary: true },
      diagnostic,
    },
  })), "settled");
  assert.equal(settled.settlement.kind, "failed");
  if (settled.settlement.kind === "failed")
    assert.equal(settled.settlement.diagnostic, diagnostic);
  assert.equal(payloadAccessorCalls, 0);
  assert.equal(diagnosticAccessorCalls, 0);
});

test("operation-specific codecs are selected after raw lookup", () => {
  const events: string[] = [];
  const sourceDecoder: BoundedPayloadDecoder<string> = {
    decode: value => {
      events.push("source");
      return boundedStringDecoder().decode(value);
    },
  };
  const countDecoder: BoundedPayloadDecoder<number> = {
    decode: value => {
      events.push("count");
      return numberDecoder().decode(value);
    },
  };
  const sourceOperation = {
    operationId: "source-operation",
    operationSequence: 20,
  };
  const countOperation = {
    operationId: "count-operation",
    operationSequence: 21,
  };

  const sourceStart = requireKind(decoded(decodeBoundMain({
    ...header,
    kind: "start",
    operation: sourceOperation,
    operationKind: "inspect-source",
    payload: "System.Text.Json",
  })), "start");
  const countStart = requireKind(decoded(decodeBoundMain({
    ...header,
    kind: "start",
    operation: countOperation,
    operationKind: "count-types",
    payload: 42,
  })), "start");
  assert.deepEqual(events, []);

  const sourceTyped = decoded(decodeStartPayload(
    sourceStart,
    sourceDecoder,
  ));
  const countTyped = decoded(decodeStartPayload(countStart, countDecoder));
  assert.equal(sourceTyped.payload, "System.Text.Json");
  assert.equal(countTyped.payload, 42);

  const sourceProgress = requireKind(decoded(decodeWorker({
    ...header,
    kind: "progress",
    operation: sourceOperation,
    payload: "reading PDB",
  })), "progress");
  const countProgress = requireKind(decoded(decodeWorker({
    ...header,
    kind: "progress",
    operation: countOperation,
    payload: 17,
  })), "progress");
  assert.deepEqual(events, ["source", "count"]);

  const activeProgressDecoders =
    new Map<string, BoundedPayloadDecoder<unknown>>([
    [
      sourceOperation.operationId,
      sourceDecoder,
    ],
    [
      countOperation.operationId,
      countDecoder,
    ],
  ]);
  const decodedSourceProgress = decoded(
    decodeProgressPayload(
      sourceProgress,
      activeProgressDecoders.get(
        sourceProgress.operation.operationId,
      )!,
    ),
  );
  const decodedCountProgress = decoded(
    decodeProgressPayload(
      countProgress,
      activeProgressDecoders.get(
        countProgress.operation.operationId,
      )!,
    ),
  );
  assert.equal(decodedSourceProgress.payload, "reading PDB");
  assert.equal(decodedCountProgress.payload, 17);
  assert.deepEqual(events, ["source", "count", "source", "count"]);
});

test("payload codecs are not invoked for malformed structural envelopes", () => {
  let decoderCalls = 0;
  const observingDecoder: BoundedPayloadDecoder<string> = {
    decode: value => {
      decoderCalls++;
      return boundedStringDecoder().decode(value);
    },
  };
  let bootstrapAccessorCalls = 0;
  assertDecodeFailure(
    decodeUnboundInitializationEnvelope({
      ...header,
      kind: "initialize",
      get bootstrap(): string {
        bootstrapAccessorCalls++;
        return "bootstrap";
      },
      idleHeartbeatIntervalMilliseconds: 1_000,
      idleAllowanceMilliseconds: 1_100,
    }, observingDecoder),
    "accessor-property",
    "$.bootstrap",
  );

  const malformedStart = decodeBoundMain({
    ...header,
    kind: "start",
    operation,
    operationKind: "inspect-source",
  });
  assertDecodeFailure(malformedStart, "missing-property", "$.payload");

  const malformedSettlement = decodeWorker({
    ...header,
    kind: "settled",
    operation,
    settlement: {
      kind: "failed",
      error: "error",
      diagnostic: "diagnostic",
    },
  });
  assertDecodeFailure(
    malformedSettlement,
    "missing-property",
    "$.settlement.failureKind",
  );
  assert.equal(bootstrapAccessorCalls, 0);
  assert.equal(decoderCalls, 0);
});

test("preserves arbitrary owner-issued operation ID and kind strings", () => {
  const longId = `owner:${"id".repeat(10_000)}`;
  const longKind = `registered:${"kind".repeat(10_000)}`;
  const decodedStart = requireKind(decoded(decodeBoundMain({
    ...header,
    kind: "start",
    operation: {
      operationId: longId,
      operationSequence: 1,
    },
    operationKind: longKind,
    payload: "input",
  })), "start");
  assert.equal(decodedStart.operation.operationId, longId);
  assert.equal(decodedStart.operationKind, longKind);

  const emptyText = requireKind(decoded(decodeBoundMain({
    ...header,
    kind: "start",
    operation: {
      operationId: "",
      operationSequence: 2,
    },
    operationKind: "",
    payload: "input",
  })), "start");
  assert.equal(emptyText.operation.operationId, "");
  assert.equal(emptyText.operationKind, "");
});

test("rejects malformed primitive, null, and array envelope roots", () => {
  const malformed: readonly unknown[] = [
    undefined,
    null,
    false,
    1,
    "heartbeat",
    [],
  ];
  for (const value of malformed) {
    assertDecodeFailure(decodeBoundMain(value), "not-record", "$");
    assertDecodeFailure(decodeWorker(value), "not-record", "$");
  }
});

test("requires every envelope field to be an own data property", () => {
  const inheritedEpoch = {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    kind: "heartbeat",
  };
  Object.setPrototypeOf(inheritedEpoch, { epochToken: EPOCH_TOKEN });
  assertDecodeFailure(
    decodeWorker(inheritedEpoch),
    "missing-property",
    "$.epochToken",
  );

  let kindAccessorCalls = 0;
  const accessorKind = {
    ...header,
    get kind(): string {
      kindAccessorCalls++;
      return "heartbeat";
    },
  };
  assertDecodeFailure(
    decodeWorker(accessorKind),
    "accessor-property",
    "$.kind",
  );
  assert.equal(kindAccessorCalls, 0);
});

test("the own-data-property guard is non-vacuous for heartbeat data", () => {
  assert.equal(
    decodeWorker({ ...header, kind: "heartbeat" }).kind,
    "success",
  );

  const inheritedKind = { ...header };
  Object.setPrototypeOf(inheritedKind, { kind: "heartbeat" });
  assertDecodeFailure(
    decodeWorker(inheritedKind),
    "missing-property",
    "$.kind",
  );

  let epochAccessorCalls = 0;
  const accessorEpoch = {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    get epochToken(): number {
      epochAccessorCalls++;
      return EPOCH_TOKEN;
    },
    kind: "heartbeat",
  };
  assertDecodeFailure(
    decodeWorker(accessorEpoch),
    "accessor-property",
    "$.epochToken",
  );
  assert.equal(epochAccessorCalls, 0);
});

test("rejects missing, extra string, and extra symbol envelope fields", () => {
  assertDecodeFailure(
    decodeWorker({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      kind: "heartbeat",
    }),
    "missing-property",
    "$.epochToken",
  );
  assertDecodeFailure(
    decodeWorker({ ...header, kind: "heartbeat", extra: true }),
    "unexpected-property",
    "$.extra",
  );
  const symbol = Symbol("extra");
  assertDecodeFailure(
    decodeWorker({ ...header, kind: "heartbeat", [symbol]: true }),
    "unexpected-property",
    "$[symbol]",
  );
});

test("rejects inherited, accessor-backed, and open operation references", () => {
  const inheritedSequence = { operationId: "operation-7" };
  Object.setPrototypeOf(inheritedSequence, { operationSequence: 11 });
  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "start",
      operation: inheritedSequence,
      operationKind: "inspect-source",
      payload: "input",
    }),
    "missing-property",
    "$.operation.operationSequence",
  );

  let idAccessorCalls = 0;
  const accessorId = {
    get operationId(): string {
      idAccessorCalls++;
      return "operation-7";
    },
    operationSequence: 11,
  };
  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "start",
      operation: accessorId,
      operationKind: "inspect-source",
      payload: "input",
    }),
    "accessor-property",
    "$.operation.operationId",
  );
  assert.equal(idAccessorCalls, 0);

  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "start",
      operation: { ...operation, extra: true },
      operationKind: "inspect-source",
      payload: "input",
    }),
    "unexpected-property",
    "$.operation.extra",
  );
  const symbol = Symbol("operation-extra");
  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "start",
      operation: { ...operation, [symbol]: true },
      operationKind: "inspect-source",
      payload: "input",
    }),
    "unexpected-property",
    "$.operation[symbol]",
  );
});

test("rejects non-positive, non-finite, fractional, or unsafe epochs", () => {
  const invalidIntegers = [
    0,
    -1,
    1.5,
    Number.NaN,
    Number.POSITIVE_INFINITY,
    Number.MAX_SAFE_INTEGER + 1,
  ];
  for (const epochToken of invalidIntegers) {
    assertDecodeFailure(
      decodeWorker({
        protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
        epochToken,
        kind: "heartbeat",
      }),
      "invalid-integer",
      "$.epochToken",
    );
  }
});

test("validates every sequence and millisecond field as a positive safe integer", () => {
  const malformedFields: readonly {
    readonly decode: () => WorkerEnvelopeDecodeResult<unknown>;
    readonly path: string;
  }[] = [
    {
      decode: () => decodeBoundMain({
        ...header,
        kind: "start",
        operation: { operationId: "operation-7", operationSequence: 0 },
        operationKind: "inspect-source",
        payload: "input",
      }),
      path: "$.operation.operationSequence",
    },
    {
      decode: () => decodeBoundMain({
        ...header,
        kind: "probe",
        probeSequence: 0,
      }),
      path: "$.probeSequence",
    },
    {
      decode: () => decodeUnboundInitializationEnvelope({
        ...header,
        kind: "initialize",
        bootstrap: "bootstrap",
        idleHeartbeatIntervalMilliseconds: 0,
        idleAllowanceMilliseconds: 1_100,
      }, boundedStringDecoder()),
      path: "$.idleHeartbeatIntervalMilliseconds",
    },
    {
      decode: () => decodeUnboundInitializationEnvelope({
        ...header,
        kind: "initialize",
        bootstrap: "bootstrap",
        idleHeartbeatIntervalMilliseconds: 1_000,
        idleAllowanceMilliseconds: 0,
      }, boundedStringDecoder()),
      path: "$.idleAllowanceMilliseconds",
    },
    {
      decode: () => decodeWorker({
        ...header,
        kind: "ready",
        idleHeartbeatIntervalMilliseconds: 0,
      }),
      path: "$.idleHeartbeatIntervalMilliseconds",
    },
    {
      decode: () => decodeWorker({
        ...header,
        kind: "accepted",
        operation,
        allowance: {
          kind: "bounded",
          maxSilentActiveMilliseconds: 0,
        },
      }),
      path: "$.allowance.maxSilentActiveMilliseconds",
    },
    {
      decode: () => decodeWorker({
        ...header,
        kind: "probe-acknowledged",
        probeSequence: 0,
      }),
      path: "$.probeSequence",
    },
    {
      decode: () => decodeWorker({
        ...header,
        kind: "epoch-work-started",
        workSequence: 0,
        allowance: { kind: "unbounded" },
      }),
      path: "$.workSequence",
    },
    {
      decode: () => decodeWorker({
        ...header,
        kind: "epoch-work-finished",
        workSequence: 0,
      }),
      path: "$.workSequence",
    },
  ];
  for (const malformed of malformedFields) {
    assertDecodeFailure(
      malformed.decode(),
      "invalid-integer",
      malformed.path,
    );
  }
});

test("requires the exact protocol version and worker expected epoch", () => {
  assertDecodeFailure(
    decodeUnboundInitializationEnvelope({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION + 1,
      epochToken: EPOCH_TOKEN,
      kind: "initialize",
      bootstrap: "bootstrap",
      idleHeartbeatIntervalMilliseconds: 1_000,
      idleAllowanceMilliseconds: 1_100,
    }, boundedStringDecoder()),
    "wrong-version",
    "$.protocolVersion",
  );
  assertDecodeFailure(
    decodeWorker({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION + 1,
      epochToken: EPOCH_TOKEN,
      kind: "heartbeat",
    }),
    "wrong-version",
    "$.protocolVersion",
  );
  assertDecodeFailure(
    decodeWorker({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: EPOCH_TOKEN + 1,
      kind: "heartbeat",
    }),
    "wrong-epoch",
    "$.epochToken",
  );
});

test("rejects unknown and non-string discriminators in both directions", () => {
  assertDecodeFailure(
    decodeBoundMain({ ...header, kind: "launch" }),
    "invalid-discriminator",
    "$.kind",
  );
  assertDecodeFailure(
    decodeWorker({ ...header, kind: "finished" }),
    "invalid-discriminator",
    "$.kind",
  );
  assertDecodeFailure(
    decodeWorker({ ...header, kind: 1 }),
    "invalid-discriminator",
    "$.kind",
  );
});

test("validates operation IDs and operation kinds as strings only", () => {
  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "start",
      operation: {
        operationId: 1,
        operationSequence: 1,
      },
      operationKind: "inspect-source",
      payload: "input",
    }),
    "invalid-string",
    "$.operation.operationId",
  );
  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "start",
      operation,
      operationKind: false,
      payload: "input",
    }),
    "invalid-string",
    "$.operationKind",
  );
});

test("rejects unknown cancellation reasons and acknowledgment statuses", () => {
  assertDecodeFailure(
    decodeBoundMain({
      ...header,
      kind: "cancel",
      operation,
      reason: "closed",
    }),
    "invalid-literal",
    "$.reason",
  );
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "cancel-acknowledged",
      operation,
      status: "queued",
    }),
    "invalid-literal",
    "$.status",
  );
});

test("accepts every operation-authority cancellation reason", () => {
  const reasons: readonly OperationCancelReason[] = [
    "user",
    "superseded",
    "disposed",
    "feature-observer-failed",
    "timeout",
    "worker-restarted",
  ];
  for (const reason of reasons) {
    const envelope = requireKind(decoded(decodeBoundMain({
      ...header,
      kind: "cancel",
      operation,
      reason,
    })), "cancel");
    assert.equal(envelope.reason, reason);
  }
});

test("accepts both cancellation acknowledgment statuses", () => {
  for (const status of ["running", "not-active"]) {
    const envelope = requireKind(decoded(decodeWorker({
      ...header,
      kind: "cancel-acknowledged",
      operation,
      status,
    })), "cancel-acknowledged");
    assert.equal(envelope.status, status);
  }
});

test("enforces exact bounded and unbounded allowance variants", () => {
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "accepted",
      operation,
      allowance: { kind: "bounded" },
    }),
    "missing-property",
    "$.allowance.maxSilentActiveMilliseconds",
  );
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "accepted",
      operation,
      allowance: {
        kind: "unbounded",
        maxSilentActiveMilliseconds: 1,
      },
    }),
    "unexpected-property",
    "$.allowance.maxSilentActiveMilliseconds",
  );
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "accepted",
      operation,
      allowance: { kind: "idle" },
    }),
    "invalid-discriminator",
    "$.allowance.kind",
  );
});

test("rejects malformed succeeded settlement variants without accessors", () => {
  const settlements: readonly {
    readonly settlement: unknown;
    readonly code: WorkerEnvelopeDecodeFailureCode;
    readonly path: string;
  }[] = [
    {
      settlement: { kind: "succeeded" },
      code: "missing-property",
      path: "$.settlement.value",
    },
    {
      settlement: { kind: "succeeded", value: "result", extra: true },
      code: "unexpected-property",
      path: "$.settlement.extra",
    },
  ];
  for (const malformed of settlements) {
    assertDecodeFailure(
      decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement: malformed.settlement,
      }),
      malformed.code,
      malformed.path,
    );
  }

  let valueAccessorCalls = 0;
  const accessorSettlement = {
    kind: "succeeded",
    get value(): string {
      valueAccessorCalls++;
      return "result";
    },
  };
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "settled",
      operation,
      settlement: accessorSettlement,
    }),
    "accessor-property",
    "$.settlement.value",
  );
  assert.equal(valueAccessorCalls, 0);
});

test("rejects every malformed failed settlement field without accessors", () => {
  const settlements: readonly {
    readonly settlement: unknown;
    readonly code: WorkerEnvelopeDecodeFailureCode;
    readonly path: string;
  }[] = [
    {
      settlement: {
        kind: "failed",
        error: "error",
        diagnostic: "diagnostic",
      },
      code: "missing-property",
      path: "$.settlement.failureKind",
    },
    {
      settlement: {
        kind: "failed",
        failureKind: "fatal",
        error: "error",
        diagnostic: "diagnostic",
      },
      code: "invalid-literal",
      path: "$.settlement.failureKind",
    },
    {
      settlement: {
        kind: "failed",
        failureKind: "expected",
        diagnostic: "diagnostic",
      },
      code: "missing-property",
      path: "$.settlement.error",
    },
    {
      settlement: {
        kind: "failed",
        failureKind: "expected",
        error: "error",
      },
      code: "missing-property",
      path: "$.settlement.diagnostic",
    },
    {
      settlement: {
        kind: "failed",
        failureKind: "expected",
        error: "error",
        diagnostic: "diagnostic",
        result: "wrong field",
      },
      code: "unexpected-property",
      path: "$.settlement.result",
    },
  ];
  for (const malformed of settlements) {
    assertDecodeFailure(
      decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement: malformed.settlement,
      }),
      malformed.code,
      malformed.path,
    );
  }

  for (const field of ["failureKind", "error", "diagnostic"] as const) {
    let accessorCalls = 0;
    const settlement = {
      kind: "failed",
      failureKind: "expected",
      error: "error",
      diagnostic: "diagnostic",
    };
    Object.defineProperty(settlement, field, {
      enumerable: true,
      get: () => {
        accessorCalls++;
        return field === "failureKind" ? "expected" : field;
      },
    });
    assertDecodeFailure(
      decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement,
      }),
      "accessor-property",
      `$.settlement.${field}`,
    );
    assert.equal(accessorCalls, 0);
  }
});

test("rejects every malformed canceled settlement field without accessors", () => {
  const symbol = Symbol("settlement-extra");
  const settlements: readonly {
    readonly settlement: unknown;
    readonly code: WorkerEnvelopeDecodeFailureCode;
    readonly path: string;
  }[] = [
    {
      settlement: { kind: "canceled" },
      code: "missing-property",
      path: "$.settlement.reason",
    },
    {
      settlement: { kind: "canceled", reason: "closed" },
      code: "invalid-literal",
      path: "$.settlement.reason",
    },
    {
      settlement: {
        kind: "canceled",
        reason: "user",
        diagnostic: "not allowed",
      },
      code: "unexpected-property",
      path: "$.settlement.diagnostic",
    },
    {
      settlement: { kind: "canceled", reason: "user", [symbol]: true },
      code: "unexpected-property",
      path: "$.settlement[symbol]",
    },
  ];
  for (const malformed of settlements) {
    assertDecodeFailure(
      decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement: malformed.settlement,
      }),
      malformed.code,
      malformed.path,
    );
  }

  let reasonAccessorCalls = 0;
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "settled",
      operation,
      settlement: {
        kind: "canceled",
        get reason(): string {
          reasonAccessorCalls++;
          return "user";
        },
      },
    }),
    "accessor-property",
    "$.settlement.reason",
  );
  assert.equal(reasonAccessorCalls, 0);
});

test("rejects malformed settlement roots and discriminators", () => {
  for (const settlement of [null, [], "succeeded"]) {
    assertDecodeFailure(
      decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement,
      }),
      "not-record",
      "$.settlement",
    );
  }
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "settled",
      operation,
      settlement: { kind: "rejected" },
    }),
    "invalid-discriminator",
    "$.settlement.kind",
  );

  let kindAccessorCalls = 0;
  assertDecodeFailure(
    decodeWorker({
      ...header,
      kind: "settled",
      operation,
      settlement: {
        get kind(): string {
          kindAccessorCalls++;
          return "succeeded";
        },
        value: "result",
      },
    }),
    "accessor-property",
    "$.settlement.kind",
  );
  assert.equal(kindAccessorCalls, 0);
});

test("payload helpers preserve validated envelope identity", () => {
  const rawInitialize = requireKind(decoded(decodeBoundMain({
    ...header,
    kind: "initialize",
    bootstrap: "bootstrap",
    idleHeartbeatIntervalMilliseconds: 1_000,
    idleAllowanceMilliseconds: 1_100,
  })), "initialize");
  const typedInitialize = decoded(decodeInitializePayload(
    rawInitialize,
    boundedStringDecoder(),
  ));
  assert.equal(typedInitialize.epochToken, rawInitialize.epochToken);

  const rawStart = requireKind(decoded(decodeBoundMain({
    ...header,
    kind: "start",
    operation,
    operationKind: "inspect-source",
    payload: "input",
  })), "start");
  const typedStart = decoded(decodeStartPayload(
    rawStart,
    boundedStringDecoder(),
  ));
  assert.equal(typedStart.operation, rawStart.operation);
  assert.equal(typedStart.operationKind, rawStart.operationKind);

  const rawProgress = requireKind(decoded(decodeWorker({
    ...header,
    kind: "progress",
    operation,
    payload: "progress",
  })), "progress");
  const typedProgress = decoded(decodeProgressPayload(
    rawProgress,
    boundedStringDecoder(),
  ));
  assert.equal(typedProgress.operation, rawProgress.operation);

  const rawCanceled = requireKind(decoded(decodeWorker({
    ...header,
    kind: "settled",
    operation,
    settlement: { kind: "canceled", reason: "user" },
  })), "settled");
  const typedCanceled = decoded(decodeSettledPayload(
    rawCanceled,
    settlementDecoders,
  ));
  assert.equal(typedCanceled.operation, rawCanceled.operation);
  assert.equal(typedCanceled.settlement, rawCanceled.settlement);
});

test("maps codec-owned oversized failures to exact protocol paths", () => {
  const oversized = "x".repeat(33);
  const mainFailure = decodeUnboundInitializationEnvelope({
    ...header,
    kind: "initialize",
    bootstrap: oversized,
    idleHeartbeatIntervalMilliseconds: 1_000,
    idleAllowanceMilliseconds: 1_100,
  }, boundedStringDecoder());
  assertDecodeFailure(mainFailure, "payload-oversized", "$.bootstrap");
  if (mainFailure.kind === "failure")
    assert.equal(mainFailure.failure.category, "payload-budget");

  const rawProgress = requireKind(decoded(decodeWorker({
    ...header,
    kind: "progress",
    operation,
    payload: oversized,
  })), "progress");
  const workerFailure = decodeProgressPayload(
    rawProgress,
    boundedStringDecoder(),
  );
  assertDecodeFailure(workerFailure, "payload-oversized", "$.payload");
  if (workerFailure.kind === "failure")
    assert.equal(workerFailure.failure.category, "payload-budget");
});

test("wraps owner codec rejection with exact field context and cause", () => {
  const codecCause = new Error("owner detail");
  const rejectingDecoder: BoundedPayloadDecoder<string> = {
    decode: () => ({
      kind: "rejected",
      reason: "invalid",
      message: "Owner codec rejected the value.",
      cause: codecCause,
    }),
  };
  const cases: readonly {
    readonly result: WorkerEnvelopeDecodeResult<unknown>;
    readonly path: string;
  }[] = [
    {
      result: decodeUnboundInitializationEnvelope({
        ...header,
        kind: "initialize",
        bootstrap: "bootstrap",
        idleHeartbeatIntervalMilliseconds: 1_000,
        idleAllowanceMilliseconds: 1_100,
      }, rejectingDecoder),
      path: "$.bootstrap",
    },
    {
      result: decodeStartPayload(requireKind(decoded(decodeBoundMain({
        ...header,
        kind: "start",
        operation,
        operationKind: "inspect-source",
        payload: "input",
      })), "start"), rejectingDecoder),
      path: "$.payload",
    },
    {
      result: decodeProgressPayload(requireKind(decoded(decodeWorker({
        ...header,
        kind: "progress",
        operation,
        payload: "progress",
      })), "progress"), rejectingDecoder),
      path: "$.payload",
    },
    {
      result: decodeSettledPayload(requireKind(decoded(decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement: { kind: "succeeded", value: "result" },
      })), "settled"), {
        ...settlementDecoders,
        value: rejectingDecoder,
      }),
      path: "$.settlement.value",
    },
    {
      result: decodeRejectedPayload(requireKind(decoded(decodeWorker({
        ...header,
        kind: "rejected",
        operation,
        error: "error",
        diagnostic: "diagnostic",
      })), "rejected"), rejectingDecoder, boundedStringDecoder()),
      path: "$.error",
    },
    {
      result: decodeStartupFailedPayload(requireKind(decoded(decodeWorker({
        ...header,
        kind: "startup-failed",
        diagnostic: "diagnostic",
      })), "startup-failed"), rejectingDecoder),
      path: "$.diagnostic",
    },
    {
      result: decodeSettledPayload(requireKind(decoded(decodeWorker({
        ...header,
        kind: "settled",
        operation,
        settlement: {
          kind: "failed",
          failureKind: "expected",
          error: "error",
          diagnostic: "diagnostic",
        },
      })), "settled"), {
        ...settlementDecoders,
        diagnostic: rejectingDecoder,
      }),
      path: "$.settlement.diagnostic",
    },
    {
      result: decodeEpochFailedPayload(requireKind(decoded(decodeWorker({
        ...header,
        kind: "epoch-failed",
        diagnostic: "diagnostic",
      })), "epoch-failed"), rejectingDecoder),
      path: "$.diagnostic",
    },
  ];
  for (const fixture of cases) {
    assertDecodeFailure(fixture.result, "payload-rejected", fixture.path);
    if (fixture.result.kind === "failure") {
      assert.equal(fixture.result.failure.category, "payload-codec");
      assert.equal(fixture.result.failure.cause, codecCause);
    }
  }
});

test("does not reinterpret a throwing owner decoder as wire failure", () => {
  const codecError = new Error("codec bug");
  const throwingDecoder: BoundedPayloadDecoder<string> = {
    decode: () => {
      throw codecError;
    },
  };
  assert.throws(
    () => decodeUnboundInitializationEnvelope({
      ...header,
      kind: "initialize",
      bootstrap: "bootstrap",
      idleHeartbeatIntervalMilliseconds: 1_000,
      idleAllowanceMilliseconds: 1_100,
    }, throwingDecoder),
    error => error === codecError,
  );
});

type EqualTypes<TLeft, TRight> =
  [TLeft] extends [TRight]
    ? [TRight] extends [TLeft]
      ? true
      : false
    : false;
type ExpectTrue<TValue extends true> = TValue;

function compileTimeEnvelopeContracts(): void {
  type CancellationReasonMatchesAuthority = ExpectTrue<
    EqualTypes<WorkerOperationCancelReason, OperationCancelReason>
  >;

  const cancellationTypeEvidence:
    CancellationReasonMatchesAuthority = true;
  const wireEpochToken: WorkerWireEpochToken = 1;
  const cancellationReason: WorkerOperationCancelReason = "user";
  const allowance: WorkerLivenessAllowance = { kind: "unbounded" };
  const payloadResult: BoundedPayloadDecodeResult<string> = {
    kind: "decoded",
    value: "payload",
  };
  const failureCategory: WorkerEnvelopeDecodeFailureCategory = "property";
  const publicFailure: WorkerEnvelopeDecodeFailure = {
    category: failureCategory,
    code: "missing-property",
    path: "$.field",
    message: "Missing field.",
    cause: undefined,
  };
  const wireOperation: WorkerWireOperationReference = {
    operationId: "decoded-wire-value",
    operationSequence: 1,
  };
  // @ts-expect-error Decoded wire text is not allocator-issued authority.
  const allocatorIssuedId: OperationId = wireOperation.operationId;

  // @ts-expect-error Operation references always carry ID and sequence.
  const incompleteOperation: WorkerWireOperationReference = {
    operationId: "missing-sequence",
  };

  // @ts-expect-error A bound decode always requires the expected epoch.
  const missingExpectedEpoch = decodeBoundMainToWorkerEnvelope({
    ...header,
    kind: "probe",
    probeSequence: 1,
  });

  const undefinedExpectedEpoch = decodeBoundMainToWorkerEnvelope({
    ...header,
    kind: "probe",
    probeSequence: 1,
  },
  // @ts-expect-error A bound decode cannot skip epoch comparison.
  undefined);

  // @ts-expect-error Start cannot drift from its required payload fields.
  const incompleteStart: MainToWorkerEnvelope<string, string> = {
    ...header,
    kind: "start",
    operation: wireOperation,
    operationKind: "inspect-source",
  };

  // @ts-expect-error Failed settlement always carries a diagnostic.
  const incompleteFailure: ManagedOperationSettlement<string, string, string> = {
    kind: "failed",
    failureKind: "expected",
    error: "error",
  };

  const canceledSettlement:
    ManagedOperationSettlement<string, string, string> = {
      kind: "canceled",
      reason: "user",
      // @ts-expect-error Canceled settlement cannot carry failure fields.
      error: "not allowed",
    };

  const initialize: InitializeMainToWorkerEnvelope<string> = {
    ...header,
    kind: "initialize",
    bootstrap: "bootstrap",
    idleHeartbeatIntervalMilliseconds: 1_000,
    idleAllowanceMilliseconds: 1_100,
  };
  const heartbeat: WorkerToMainEnvelope<string, string, string, string> = {
    ...header,
    kind: "heartbeat",
  };
  void cancellationTypeEvidence;
  void wireEpochToken;
  void cancellationReason;
  void allowance;
  void payloadResult;
  void publicFailure;
  void allocatorIssuedId;
  void incompleteOperation;
  void missingExpectedEpoch;
  void undefinedExpectedEpoch;
  void incompleteStart;
  void incompleteFailure;
  void canceledSettlement;
  void initialize;
  void heartbeat;
}
void compileTimeEnvelopeContracts;
