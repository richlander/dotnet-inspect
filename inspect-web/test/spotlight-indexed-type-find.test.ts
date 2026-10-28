import assert from "node:assert/strict";
import test from "node:test";

import {
  composeSpotlightTypeCandidates,
  createSpotlightIndexedTypeFind,
  projectSpotlightIndexedTypeFindResult,
} from "../src/spotlight-indexed-type-find.ts";
import type {
  BrowserIndexedTypeFindResult,
} from "../src/facades/inspect-web-catalog.d.ts";
import type {
  ManagedSpotlightTypeCandidate,
} from "../src/spotlight-type-find.ts";

function result(
  action = "indexed-action",
  identity = "exact-package-type",
  completion: string | number = "Exhausted",
): BrowserIndexedTypeFindResult {
  return {
    status: "Completed",
    operation: {
      find: {
        content: {
          matches: [{
            fullName: "Aspire.Hosting.ApplicationModel.RedisResource",
            typeName: "RedisResource",
            namespace: "Aspire.Hosting.ApplicationModel",
            declarationKind: "Definition",
            declaration: {
              source: {
                coordinate: {
                  kind: "package",
                  packageCoordinate: {
                    packageId: "aspire.hosting.redis",
                    version: "13.6.3",
                  },
                  libraryIdentity: {
                    identity: {
                      name: "Aspire.Hosting.Redis",
                    },
                  },
                },
              },
              name: {
                namespace: "Aspire.Hosting.ApplicationModel",
                segments: ["RedisResource"],
              },
              moduleVersionId: "mvid",
            },
          }],
          completion,
        },
        share: {
          kind: "available",
          fullUrl: "https://example.test/find",
          packet: "find-packet",
        },
        diagnostics: [],
      },
      activations: [{
        matchOrdinal: 1,
        identity,
        status: "Available",
        action,
        reason: null,
      }],
    },
    reason: null,
  };
}

function candidate(
  identity: string,
  action: string,
  source: string,
): ManagedSpotlightTypeCandidate {
  return {
    identity,
    action,
    reason: null,
    name: "RedisResource",
    namespace: "Aspire.Hosting.ApplicationModel",
    library: "Aspire.Hosting.Redis",
    source,
    declarationKind: "Definition",
  };
}

test("projects exact indexed Type identity and opaque action", () => {
  const projection = projectSpotlightIndexedTypeFindResult(result());

  assert.deepEqual(projection.candidates, [{
    identity: "exact-package-type",
    action: "indexed-action",
    reason: null,
    name: "RedisResource",
    namespace: "Aspire.Hosting.ApplicationModel",
    library: "Aspire.Hosting.Redis",
    source: "aspire.hosting.redis@13.6.3",
    declarationKind: "Definition",
  }]);
  assert.equal(projection.notice, "");
});

test("qualifies row-limited indexed results visibly", () => {
  const projection = projectSpotlightIndexedTypeFindResult(
    result("indexed-action", "exact-package-type", "RowLimitReached"),
  );

  assert.match(projection.notice, /reached its result limit/);
});

test("rejects missing activation correspondence", () => {
  const value = result();
  const malformed = {
    ...value,
    operation: value.operation && {
      ...value.operation,
      activations: [],
    },
  };

  assert.throws(
    () => projectSpotlightIndexedTypeFindResult(malformed),
    /omitted activation correspondence/,
  );
});

test("indexed phase keeps first exact identity and distinct sources", () => {
  const indexed = candidate(
    "aspire-redis-resource",
    "indexed",
    "aspire.hosting.redis@13.6.3",
  );
  const residentDuplicate = candidate(
    "aspire-redis-resource",
    "resident",
    "aspire.hosting.redis@13.6.3",
  );
  const sameSpellingElsewhere = candidate(
    "other-redis-resource",
    "other",
    "other.redis@1.0.0",
  );

  assert.deepEqual(
    composeSpotlightTypeCandidates(
      [indexed],
      [residentDuplicate, sameSpellingElsewhere],
    ),
    [indexed, sameSpellingElsewhere],
  );
});

test("new indexed requests retire stale completions", async () => {
  const requests: Array<{
    readonly generation: number;
    resolve(value: BrowserIndexedTypeFindResult): void;
  }> = [];
  const scheduled: Array<() => void> = [];
  let updates = 0;
  const coordinator = createSpotlightIndexedTypeFind({
    findTypes: (_definition, _realization, generation) =>
      new Promise(resolve => requests.push({ generation, resolve })),
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

  coordinator.schedule(posting, "Redis", true);
  scheduled.shift()?.();
  coordinator.schedule(posting, "RedisResource", true);
  scheduled.shift()?.();
  assert.equal(coordinator.complete(), false);
  assert.deepEqual(
    requests.map(request => request.generation),
    [1, 2],
  );

  requests[0]?.resolve(result("stale"));
  await Promise.resolve();
  assert.deepEqual(coordinator.results(), []);
  assert.equal(updates, 0);

  requests[1]?.resolve(result("current"));
  await Promise.resolve();
  assert.equal(coordinator.results()[0]?.action, "current");
  assert.equal(coordinator.complete(), true);
  assert.equal(coordinator.loading(), false);
  assert.equal(updates, 1);
});
