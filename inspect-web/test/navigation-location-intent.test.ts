import assert from "node:assert/strict";
import test from "node:test";
import {
  classifyLocationEffect,
  createNavigationLocationIntentArbiter,
  type InstalledLocationAssociation,
  type LocationIntentDeclaration,
  type LocationResult,
} from "../src/navigation-location-intent.ts";

function association(
  identity: string,
  canonicalLocation = `/?w=${identity}`,
): InstalledLocationAssociation {
  return {
    identity: Symbol(identity),
    canonicalLocation,
    historyState: { workspace: identity },
  };
}

function result(
  installed: InstalledLocationAssociation,
  overrides: Partial<LocationResult> = {},
): LocationResult {
  return {
    outcome: "applied",
    synchronization: "current",
    association: installed,
    ...overrides,
  };
}

test("one location intent classifies every history effect", () => {
  const browser: LocationIntentDeclaration = {
    id: Symbol("browser"),
    source: "browser",
    selectedEntry: {
      url: "/?w=a",
      historyState: { workspace: "a" },
      retainedDefinitionId: "a",
      incumbent: association("incumbent"),
    },
  };
  const push: LocationIntentDeclaration = {
    id: Symbol("push"),
    source: "non-browser",
    policy: "push",
  };
  const replace: LocationIntentDeclaration = {
    id: Symbol("replace"),
    source: "non-browser",
    policy: "replace",
  };
  const none: LocationIntentDeclaration = {
    id: Symbol("none"),
    source: "non-browser",
    policy: "none",
  };
  const installed = association("installed");

  assert.equal(classifyLocationEffect(
    browser,
    browser.id,
    result(installed, { browserRestoration: "exact" }),
  ).kind, "adopt");
  assert.equal(classifyLocationEffect(
    browser,
    browser.id,
    result(installed, {
      synchronization: "synchronization-required",
      browserRestoration: "changed",
    }),
  ).kind, "replace");
  assert.equal(classifyLocationEffect(
    browser,
    browser.id,
    result(installed, { outcome: "failed" }),
  ).kind, "realign");
  assert.equal(
    classifyLocationEffect(push, push.id, result(installed)).kind,
    "push");
  assert.equal(
    classifyLocationEffect(replace, replace.id, result(installed)).kind,
    "replace");
  assert.equal(classifyLocationEffect(
    none,
    none.id,
    result(installed, {
      outcome: "rejected",
      synchronization: "synchronization-required",
    }),
  ).kind, "replace");
  assert.equal(classifyLocationEffect(
    none,
    none.id,
    result(installed, { outcome: "rejected" }),
  ).kind, "none");
  assert.deepEqual(
    classifyLocationEffect(push, Symbol("newer"), result(installed)),
    { kind: "none", intentId: push.id, reason: "stale" });
});

test("browser traversal owns location across irreversible Workspace completion", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const workspaceB = arbiter.admitNonBrowser("push", null, null);
  const selectedState = { workspace: "a" };
  const traversal = arbiter.selectBrowserEntry({
    url: "/?w=a",
    historyState: selectedState,
    retainedDefinitionId: "workspace-a",
    incumbent: association("workspace-a"),
  });
  selectedState.workspace = "mutated";

  assert.deepEqual(
    arbiter.classify(workspaceB, result(association("workspace-b"))),
    { kind: "none", intentId: workspaceB.id, reason: "stale" });
  const effect = arbiter.classify(
    traversal,
    result(association("workspace-a-restored"), {
      browserRestoration: "exact",
    }),
  );
  assert.equal(effect.kind, "adopt");
  assert.deepEqual(
    traversal.source === "browser"
      ? traversal.selectedEntry.historyState
      : null,
    { workspace: "a" });
});

test("new intent repairs unresolved traversal before admission", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const traversal = arbiter.selectBrowserEntry({
    url: "/?w=a",
    historyState: { workspace: "a" },
    retainedDefinitionId: "workspace-a",
    incumbent: association("workspace-a", "/same"),
  });
  const installed = association("workspace-b", "/same");
  const repaired: string[] = [];
  const history = {
    pushState() {},
    replaceState(data: unknown) {
      assert.deepEqual(data, { workspace: "workspace-b" });
      repaired.push("workspace-b");
    },
  };

  assert.throws(
    () => arbiter.admitNonBrowser("push", installed, {
      pushState() {},
      replaceState() {
        throw new Error("History blocked");
      },
    }),
    /History blocked/);
  assert.equal(arbiter.currentIntentId, traversal.id);

  const successor = arbiter.admitNonBrowser("push", installed, history);

  assert.deepEqual(repaired, ["workspace-b"]);
  assert.equal(arbiter.currentIntentId, successor.id);
  assert.equal(arbiter.unresolved, null);
});

test("failed traversal realigns only its current selected entry", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const older = arbiter.selectBrowserEntry({
    url: "/?w=older",
    historyState: { workspace: "older" },
    retainedDefinitionId: "older",
    incumbent: association("incumbent"),
  });
  const current = arbiter.selectBrowserEntry({
    url: "/?w=current",
    historyState: { workspace: "current" },
    retainedDefinitionId: "current",
    incumbent: association("incumbent"),
  });
  const incumbent = association("incumbent");

  assert.equal(arbiter.classify(
    older,
    result(incumbent, { outcome: "failed" }),
  ).kind, "none");
  const effect = arbiter.classify(
    current,
    result(incumbent, { outcome: "failed" }),
  );
  assert.equal(effect.kind, "realign");
  arbiter.settle(effect, true);
  assert.equal(arbiter.unresolved, null);
});

test("delayed operation retains its originating location intent", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const delayed = arbiter.admitNonBrowser("push", null, null);
  const current = arbiter.admitNonBrowser("push", null, null);

  assert.equal(arbiter.classify(
    delayed,
    result(association("delayed")),
  ).kind, "none");
  assert.equal(arbiter.classify(
    current,
    result(association("current")),
  ).kind, "push");
});

test("publication revalidates intent after classification", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const delayed = arbiter.admitNonBrowser("push", null, null);
  const effect = arbiter.classify(
    delayed,
    result(association("delayed")));
  const traversal = arbiter.selectBrowserEntry({
    url: "/?w=current",
    historyState: { workspace: "current" },
    retainedDefinitionId: "current",
    incumbent: association("incumbent"),
  });
  const writes: string[] = [];

  arbiter.publish(effect, {
    pushState() {
      writes.push("push");
    },
    replaceState() {
      writes.push("replace");
    },
  });

  assert.deepEqual(writes, []);
  assert.equal(arbiter.currentIntentId, traversal.id);
  assert.equal(arbiter.unresolved?.intentId, traversal.id);
});

test("successful publication consumes its location intent", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const intent = arbiter.admitNonBrowser("push", null, null);
  const installed = association("workspace-a");
  const effect = arbiter.classify(intent, result(installed));
  const writes: string[] = [];
  const history = {
    pushState() {
      writes.push("push");
    },
    replaceState() {
      writes.push("replace");
    },
  };

  arbiter.publish(effect, history);
  arbiter.publish(effect, history);

  assert.deepEqual(writes, ["push"]);
  assert.equal(arbiter.currentIntentId, null);
  assert.deepEqual(
    arbiter.classify(intent, result(installed)),
    { kind: "none", intentId: intent.id, reason: "stale" });
});

test("publication claims its intent before writer reentry", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const intent = arbiter.admitNonBrowser("push", null, null);
  const effect = arbiter.classify(intent, result(association("workspace-a")));
  let writes = 0;
  const nestedPublications: boolean[] = [];
  const history = {
    pushState() {
      writes++;
      nestedPublications.push(arbiter.publish(effect, history));
    },
    replaceState() {
      assert.fail("A push effect cannot replace history.");
    },
  };

  arbiter.publish(effect, history);

  assert.equal(writes, 1);
  assert.deepEqual(nestedPublications, [false]);
  assert.equal(arbiter.currentIntentId, null);
  assert.equal(arbiter.unresolved, null);
});

test("successful current no-write consumes its location intent", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const intent = arbiter.admitNonBrowser("none", null, null);
  const installed = association("workspace-a");
  const effect = arbiter.classify(intent, result(installed));

  assert.deepEqual(
    effect,
    { kind: "none", intentId: intent.id, reason: "current-no-write" });
  arbiter.publish(effect, {
    pushState() {
      assert.fail("A no-write effect cannot push history.");
    },
    replaceState() {
      assert.fail("A no-write effect cannot replace history.");
    },
  });

  assert.equal(arbiter.currentIntentId, null);
  assert.deepEqual(
    arbiter.classify(intent, result(installed, {
      outcome: "failed",
      synchronization: "synchronization-required",
    })),
    { kind: "none", intentId: intent.id, reason: "stale" });
});

test("repair failure rejects reentrant intent admission", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const incumbent = association("incumbent");
  const traversal = arbiter.selectBrowserEntry({
    url: "/?w=selected",
    historyState: { workspace: "selected" },
    retainedDefinitionId: "selected",
    incumbent,
  });

  assert.throws(
    () => arbiter.admitNonBrowser("push", incumbent, {
      pushState() {},
      replaceState() {
        assert.throws(
          () => arbiter.admitNonBrowser("replace", incumbent, {
            pushState() {},
            replaceState() {},
          }),
          /cannot be admitted while location publication is in progress/);
        throw new Error("repair failed after reentry");
      },
    }),
    /repair failed after reentry/);

  assert.equal(arbiter.currentIntentId, traversal.id);
  assert.equal(arbiter.unresolved?.intentId, traversal.id);
});

test("post-cutover history failure keeps the installed successor unresolved", () => {
  const arbiter = createNavigationLocationIntentArbiter();
  const accepted = arbiter.admitNonBrowser("push", null, null);
  const successor = association("workspace-b", "/same");
  const effect = arbiter.classify(accepted, result(successor));

  assert.equal(effect.kind, "push");
  arbiter.settle(effect, false);
  assert.equal(
    arbiter.unresolved?.association?.identity.description,
    "workspace-b");

  const repairs: unknown[] = [];
  const next = arbiter.admitNonBrowser(
    "replace",
    successor,
    {
      pushState() {},
      replaceState(data: unknown) {
        repairs.push(data);
      },
    },
  );

  assert.deepEqual(repairs, [{ workspace: "workspace-b" }]);
  assert.equal(arbiter.currentIntentId, next.id);
  assert.equal(arbiter.unresolved, null);
});
