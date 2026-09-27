import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlightTypeFind,
  projectSpotlightTypeFindResult,
} from "../src/spotlight-type-find.ts";
import type {
  BrowserTypeFindResult,
} from "../src/facades/inspect-web-metadata.d.ts";

function candidate(packageVersion = "10.0.0") {
  return {
    coordinate: {
      kind: "package",
      libraryIdentity: {
        name: "System.Text.Json",
        version: "10.0.0.0",
        culture: null,
        publicKeyToken: "cc7b13ffcd2ddd51",
      },
    },
    name: {
      namespace: "System.Text.Json",
      segments: ["JsonSerializer"],
    },
    declarationKind: "Definition",
    moduleVersionId: "mvid",
    observation: {
      contextOrder: 0,
      memberOrder: 0,
      realization: {
        kind: "package",
        packageId: "System.Text.Json",
        version: packageVersion,
        producer: "Microsoft",
      },
    },
  };
}

function result(
  action = "type-action",
  packageVersion = "10.0.0",
): BrowserTypeFindResult {
  return {
    status: "Completed",
    operation: {
      find: {
        content: {
          kind: "evaluated",
          answers: [{
            identity: { ordinal: 1 },
            candidates: [candidate(packageVersion)],
          }],
        },
        share: {
          kind: "available",
          fullUrl: "https://example.test",
          packet: "packet",
        },
        diagnostics: [],
      },
      activations: [{
        candidate: {
          answerOrdinal: 1,
          candidateOrdinal: 1,
        },
        source: "Package",
        status: "Available",
        action,
        reason: null,
      }],
    },
    reason: null,
  };
}

test("projects candidates with their exact opaque actions", () => {
  const wire = result();
  const wireCandidate = candidate();
  const [projected] = projectSpotlightTypeFindResult(wire);

  assert.deepEqual(projected, {
    identity: JSON.stringify(wireCandidate),
    action: "type-action",
    reason: null,
    name: "JsonSerializer",
    namespace: "System.Text.Json",
    library: "System.Text.Json",
    source: "System.Text.Json@10.0.0",
    declarationKind: "Definition",
  });
});

test("presentation identity does not depend on a replaceable action", () => {
  const [first] = projectSpotlightTypeFindResult(result("first"));
  const [second] = projectSpotlightTypeFindResult(result("second"));

  assert.equal(first?.identity, second?.identity);
  assert.notEqual(first?.action, second?.action);
});

test("rejects missing activation correspondence", () => {
  const value = result();
  const withoutActivation: BrowserTypeFindResult = {
    ...value,
    operation: value.operation && {
      ...value.operation,
      activations: [],
    },
  };

  assert.throws(
    () => projectSpotlightTypeFindResult(withoutActivation),
    /omitted activation correspondence/,
  );
});

test("new requests retire stale async results", async () => {
  const requests: Array<{
    readonly generation: number;
    readonly text: string;
    resolve(value: BrowserTypeFindResult): void;
  }> = [];
  const scheduled: Array<() => void> = [];
  let updates = 0;
  const coordinator = createSpotlightTypeFind({
    findTypes: (_definition, _realization, generation, text) =>
      new Promise(resolve => requests.push({ generation, text, resolve })),
    schedule: callback => {
      scheduled.push(callback);
      return scheduled.length;
    },
    cancelScheduled: () => {},
    updateResults: () => { updates++; },
  });
  const posting = {
    retainedDefinitionId: "definition",
    realizationId: "realization",
  };

  coordinator.schedule(posting, "Json", true);
  scheduled.shift()?.();
  coordinator.schedule(posting, "JsonSerializer", true);
  scheduled.shift()?.();
  assert.deepEqual(
    requests.map(request => [request.generation, request.text]),
    [[1, "Json"], [2, "JsonSerializer"]],
  );

  requests[0]?.resolve(result("stale"));
  await Promise.resolve();
  assert.deepEqual(coordinator.results(), []);
  assert.equal(updates, 0);

  requests[1]?.resolve(result("current"));
  await Promise.resolve();
  assert.equal(coordinator.results()[0]?.action, "current");
  assert.equal(coordinator.loading(), false);
  assert.equal(coordinator.error(), "");
  assert.equal(updates, 1);
});

test("empty text retires prior rows without presenting rejection as failure", async () => {
  const scheduled: Array<() => void> = [];
  const coordinator = createSpotlightTypeFind({
    findTypes: async () => ({
      status: "Rejected",
      operation: null,
      reason: "Type Find text must not be empty.",
    }),
    schedule: callback => {
      scheduled.push(callback);
      return scheduled.length;
    },
    cancelScheduled: () => {},
    updateResults: () => {},
  });

  coordinator.schedule({
    retainedDefinitionId: "definition",
    realizationId: "realization",
  }, "", true);
  scheduled.shift()?.();
  await Promise.resolve();

  assert.deepEqual(coordinator.results(), []);
  assert.equal(coordinator.loading(), false);
  assert.equal(coordinator.error(), "");
});
