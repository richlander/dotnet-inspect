import assert from "node:assert/strict";
import test from "node:test";

import {
  createEngineWorkerSpotlightTypeFindHostRegistration,
  engineWorkerSpotlightTypeFindInput,
} from "../src/engine-worker-spotlight-type-find.ts";

const request = {
  retainedDefinitionId: "definition",
  realizationId: "realization",
  resultGeneration: 3,
  text: "JsonSerializer",
  maximumRows: 25,
};

const candidate = {
  identity: "package-type:System.Text.Json",
  action: null,
  reason: null,
  name: "JsonSerializer",
  namespace: "System.Text.Json",
  library: "System.Text.Json",
  source: "System.Text.Json@10.0.0",
  declarationKind: "Class",
};

test("Spotlight Type Find Worker accepts one bounded generic request", () => {
  assert.deepEqual(
    engineWorkerSpotlightTypeFindInput.decode(request),
    { kind: "decoded", value: request },
  );
  assert.equal(
    engineWorkerSpotlightTypeFindInput.decode({
      ...request,
      maximumRows: 0,
    }).kind,
    "rejected",
  );
  assert.equal(
    engineWorkerSpotlightTypeFindInput.decode({
      ...request,
      indexed: true,
    }).kind,
    "rejected",
  );
});

test("Spotlight Type Find Worker validates durable finding events", () => {
  const registration =
    createEngineWorkerSpotlightTypeFindHostRegistration();
  assert.ok(registration.durable);
  assert.deepEqual(
    registration.durable.decode({
      kind: "Finding",
      candidate,
    }),
    {
      kind: "decoded",
      value: {
        kind: "Finding",
        candidate,
      },
    },
  );
  assert.equal(
    registration.durable.decode({
      kind: "IndexedFinding",
      candidate,
    }).kind,
    "rejected",
  );
  assert.equal(
    registration.durable.decode({
      kind: "Finding",
      candidate: { ...candidate, phase: "indexed" },
    }).kind,
    "rejected",
  );
});
