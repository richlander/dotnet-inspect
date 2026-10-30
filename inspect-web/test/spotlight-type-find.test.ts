import assert from "node:assert/strict";
import test from "node:test";

import {
  commitManagedSpotlightSelection,
  createSpotlightTypeFind,
  projectSpotlightTypeFindResult,
  spotlightTypeCandidatesForScope,
} from "../src/spotlight-type-find.ts";
import type {
  BrowserTypeFindResult,
} from "../src/facades/inspect-web-metadata.d.ts";
import type {
  BrowserSpotlightTypeFindCandidate,
  BrowserSpotlightTypeFindResult,
} from "../src/facades/inspect-web-catalog.d.ts";
import { metadataInertStringFixture } from "./inert-string-fixture.ts";

function candidate(
  packageVersion = "10.0.0",
  libraryIdentity: {
    name: string;
    version?: string | null;
    culture?: string | null;
    public_key_token?: string | null;
  } = {
    name: "System.Text.Json",
    version: "10.0.0.0",
    public_key_token: "cc7b13ffcd2ddd51",
  },
) {
  return {
    coordinate: {
      kind: "package",
      library_identity: libraryIdentity,
    },
    name: {
      namespace: "System.Text.Json",
      segments: ["JsonSerializer"],
    },
    declaration_kind: "Definition",
    module_version_id: "mvid",
    observation: {
      context_order: 0,
      member_order: 0,
      realization: {
        kind: "package",
        package_id: "System.Text.Json",
        version: packageVersion,
        producer: "Microsoft",
      },
    },
  };
}

function result(
  action = "type-action",
  packageVersion = "10.0.0",
  libraryIdentity?: Parameters<typeof candidate>[1],
): BrowserTypeFindResult {
  return {
    status: "Completed",
    operation: {
      find: {
        content: {
          kind: "evaluated",
          contexts: [{
            context_order: 0,
            is_realized: true,
            failures: [],
          }],
          members: [{
            is_complete: true,
          }],
          answers: [{
            identity: { ordinal: 1 },
            candidates: [candidate(packageVersion, libraryIdentity)],
            is_complete: true,
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
        identity: "exact-package-type",
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
  const projection = projectSpotlightTypeFindResult(wire);
  const [projected] = projection.candidates;

  assert.deepEqual(projected, {
    identity: "exact-package-type",
    action: "type-action",
    reason: null,
    name: "JsonSerializer",
    namespace: "System.Text.Json",
    library: "System.Text.Json",
    source: "System.Text.Json@10.0.0",
    declarationKind: "Definition",
  });
  assert.equal(projection.notice, "");
});

test("accepts omitted nullable identity fields from canonical managed JSON", () => {
  const wire = result("type-action", "10.0.0", {
    name: "System.Text.Json",
  });
  const projection = projectSpotlightTypeFindResult(wire);

  assert.equal(projection.candidates.length, 1);
  assert.equal(projection.candidates[0]?.library, "System.Text.Json");
});

test("presentation identity does not depend on a replaceable action", () => {
  const [first] = projectSpotlightTypeFindResult(
    result("first"),
  ).candidates;
  const [second] = projectSpotlightTypeFindResult(
    result("second"),
  ).candidates;

  assert.equal(first?.identity, second?.identity);
  assert.notEqual(first?.action, second?.action);
});

test("Types scope keeps every managed observation reachable", () => {
  const [sample] = projectSpotlightTypeFindResult(result()).candidates;
  assert.ok(sample);
  const candidates = Array.from({ length: 51 }, (_, index) => ({
    ...sample,
    identity: `observation-${index + 1}`,
  }));

  const types = spotlightTypeCandidatesForScope(candidates, false);
  const all = spotlightTypeCandidatesForScope(candidates, true);
  const expandedAll = spotlightTypeCandidatesForScope(candidates, true, 12);

  assert.equal(types.length, 51);
  assert.equal(types.at(-1)?.identity, "observation-51");
  assert.equal(all.length, 6);
  assert.equal(expandedAll.length, 12);
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

test("projects incomplete locator coverage as a visible notice", () => {
  const value = result();
  const incomplete: BrowserTypeFindResult = {
    ...value,
    operation: value.operation && {
      ...value.operation,
      find: {
        ...value.operation.find,
        content: {
          kind: "evaluated",
          contexts: [{
            context_order: 0,
            is_realized: false,
            failures: [{ message: "Context load failed." }],
          }],
          members: [{
            is_complete: false,
          }],
          answers: [{
            identity: { ordinal: 1 },
            candidates: [],
            is_complete: false,
          }],
        },
        diagnostics: [{
          code: "type-find-incomplete",
          severity: 1,
          summary: metadataInertStringFixture(
            "Some locator evidence was unavailable.",
          ),
          correspondence: null,
        }],
      },
      activations: [],
    },
  };

  const projection = projectSpotlightTypeFindResult(incomplete);

  assert.deepEqual(projection.candidates, []);
  assert.match(
    projection.notice,
    /1 assembly could not be fully evaluated/,
  );
  assert.match(
    projection.notice,
    /1 declaration context could not be fully realized/,
  );
  assert.match(
    projection.notice,
    /Some locator evidence was unavailable/,
  );
});

test("projects locator rejection as a visible notice", () => {
  const value = result();
  const rejected: BrowserTypeFindResult = {
    ...value,
    operation: value.operation && {
      ...value.operation,
      find: {
        ...value.operation.find,
        content: {
          kind: "rejected",
          rejection_kind: "PopulationUnavailable",
        },
      },
      activations: [],
    },
  };

  const projection = projectSpotlightTypeFindResult(rejected);

  assert.deepEqual(projection.candidates, []);
  assert.match(
    projection.notice,
    /could not evaluate this request: PopulationUnavailable/,
  );
});

test("delayed acknowledgement cannot replay an older selection", async () => {
  let selected = "";
  let releaseFirstAcknowledgement: (() => void) | undefined;
  const firstAcknowledgement = new Promise<void>(resolve => {
    releaseFirstAcknowledgement = resolve;
  });
  let current = 1;

  const first = commitManagedSpotlightSelection({
    isCurrent: () => current === 1,
    commit: () => { selected = "old"; },
    acknowledge: async () => {
      await firstAcknowledgement;
      return "accepted";
    },
  });
  assert.equal(selected, "old");

  current = 2;
  const second = await commitManagedSpotlightSelection({
    isCurrent: () => current === 2,
    commit: () => { selected = "new"; },
  });
  assert.equal(second.current, true);
  assert.equal(selected, "new");

  releaseFirstAcknowledgement?.();
  const firstSettlement = await first;

  assert.equal(firstSettlement.acknowledged, true);
  assert.equal(firstSettlement.current, false);
  assert.equal(selected, "new");
});

function spotlightCandidate(
  action: string | null,
): BrowserSpotlightTypeFindCandidate {
  return {
    identity: "package-type:System.Text.Json",
    action,
    reason: null,
    name: "JsonSerializer",
    namespace: "System.Text.Json",
    library: "System.Text.Json",
    source: "System.Text.Json 10.0.0",
    declarationKind: "Class",
  };
}

function spotlightResult(action: string): BrowserSpotlightTypeFindResult {
  return {
    kind: "Succeeded",
    inspection: {
      candidates: [spotlightCandidate(action)],
      completion: "Exhausted",
      failures: [],
      unsearched: [],
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

test("new requests cancel and retire stale streamed results", async () => {
  const requests: Array<{
    readonly operationId: string;
    readonly generation: number;
    readonly text: string;
    readonly eventSink: unknown;
    resolve(value: BrowserSpotlightTypeFindResult): void;
  }> = [];
  const canceled: string[] = [];
  const scheduled: Array<() => void> = [];
  let operation = 0;
  let updates = 0;
  const coordinator = createSpotlightTypeFind({
    createOperationId: () => `find-${++operation}`,
    findTypes: (
      operationId,
      _definition,
      _realization,
      generation,
      text,
      _maximumRows,
      eventSink,
    ) => new Promise(resolve => requests.push({
      operationId,
      generation,
      text,
      eventSink,
      resolve,
    })),
    cancelTypes: operationId => { canceled.push(operationId); },
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
  assert.deepEqual(canceled, ["find-1"]);

  Reflect.set(
    requests[0]?.eventSink ?? {},
    "event",
    JSON.stringify({
      kind: "Finding",
      candidate: spotlightCandidate(null),
    }),
  );
  requests[0]?.resolve(spotlightResult("stale"));
  await Promise.resolve();
  assert.deepEqual(coordinator.results(), []);
  assert.equal(updates, 0);

  Reflect.set(
    requests[1]?.eventSink ?? {},
    "event",
    JSON.stringify({
      kind: "Finding",
      candidate: spotlightCandidate(null),
    }),
  );
  assert.equal(coordinator.results()[0]?.action, null);
  assert.equal(updates, 1);

  requests[1]?.resolve(spotlightResult("current"));
  await Promise.resolve();
  assert.equal(coordinator.results()[0]?.action, "current");
  assert.equal(coordinator.loading(), false);
  assert.equal(coordinator.error(), "");
  assert.equal(coordinator.notice(), "");
  assert.equal(updates, 2);
});

test("empty text does not start Type Find", () => {
  const scheduled: Array<() => void> = [];
  let started = false;
  const coordinator = createSpotlightTypeFind({
    createOperationId: () => "find",
    findTypes: async () => {
      started = true;
      return spotlightResult("unused");
    },
    cancelTypes: () => {},
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

  assert.equal(started, false);
  assert.deepEqual(scheduled, []);
  assert.deepEqual(coordinator.results(), []);
  assert.equal(coordinator.loading(), false);
  assert.equal(coordinator.error(), "");
});

test("managed failure becomes a visible coordinator failure", async () => {
  const scheduled: Array<() => void> = [];
  const coordinator = createSpotlightTypeFind({
    createOperationId: () => "find",
    findTypes: async () => ({
      kind: "Failed",
      inspection: null,
      failureKind: "Unexpected",
      error: "Type Find failed.",
      diagnostic: "fixture failure",
      reason: null,
    }),
    cancelTypes: () => {},
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
  }, "JsonSerializer", true);
  scheduled.shift()?.();
  await Promise.resolve();
  await Promise.resolve();

  assert.deepEqual(coordinator.results(), []);
  assert.equal(coordinator.loading(), false);
  assert.equal(coordinator.error(), "Type Find failed.");
  assert.equal(coordinator.notice(), "");
});
