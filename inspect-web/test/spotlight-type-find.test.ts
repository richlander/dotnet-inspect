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
  const projection = projectSpotlightTypeFindResult(wire);
  const [projected] = projection.candidates;

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

  assert.equal(types.length, 51);
  assert.equal(types.at(-1)?.identity, "observation-51");
  assert.equal(all.length, 6);
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
  assert.equal(coordinator.notice(), "");
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

test("malformed managed content becomes a visible coordinator failure", async () => {
  const scheduled: Array<() => void> = [];
  const value = result();
  const coordinator = createSpotlightTypeFind({
    findTypes: async () => ({
      ...value,
      operation: value.operation && {
        ...value.operation,
        find: {
          ...value.operation.find,
          content: { kind: "evaluated" },
        },
      },
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
  }, "JsonSerializer", true);
  scheduled.shift()?.();
  await Promise.resolve();
  await Promise.resolve();

  assert.deepEqual(coordinator.results(), []);
  assert.equal(coordinator.loading(), false);
  assert.match(coordinator.error(), /invalid locator result/);
  assert.equal(coordinator.notice(), "");
});
