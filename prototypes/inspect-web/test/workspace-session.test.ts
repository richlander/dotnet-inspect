import assert from "node:assert/strict";
import test from "node:test";

import { createNavigationSequence } from "../src/workspace-navigation.ts";
import {
  createWorkspaceProjectionTransactionController,
  createLiveWorkspace,
  createLiveWorkspaceSession,
  defaultLiveWorkspace,
  rememberedLiveWorkspaceHref,
  removeLiveWorkspace,
  selectLiveWorkspace,
  selectedLiveWorkspace,
  updateSelectedLiveWorkspace,
  withWorkspaceHistoryId,
  workspaceForHistory,
  workspaceHistoryId,
  workspaceHistoryMembershipStatus,
  workspaceOperationIsCurrent,
} from "../src/workspace-session.ts";

test("live Workspace session starts with one Default workspace", () => {
  const session = createLiveWorkspaceSession<string>("default-id");

  assert.equal(session.workspaces.length, 1);
  assert.equal(selectedLiveWorkspace(session).name, "Default");
  assert.equal(defaultLiveWorkspace(session).id, "default-id");
});

test("created Workspaces retain isolated projections and can be removed", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  updateSelectedLiveWorkspace(session, {
    packages: ["System.Text.Json"],
    activePackageKey: "System.Text.Json",
    shareBasis: null,
    navigation: { stack: [], index: -1 },
  });
  const created = createLiveWorkspace(session, "extensions-id");
  assert.equal(created?.name, "Workspace 2");
  updateSelectedLiveWorkspace(session, {
    packages: ["Microsoft.Extensions.DependencyInjection"],
    activePackageKey: "Microsoft.Extensions.DependencyInjection",
    shareBasis: null,
    navigation: { stack: [], index: -1 },
  });

  assert.deepEqual(defaultLiveWorkspace(session).packages, ["System.Text.Json"]);
  assert.deepEqual(selectedLiveWorkspace(session).packages, [
    "Microsoft.Extensions.DependencyInjection",
  ]);
  assert.equal(removeLiveWorkspace(session, "extensions-id")?.id, "extensions-id");
  assert.equal(selectedLiveWorkspace(session).id, "default-id");
  assert.equal(removeLiveWorkspace(session, "default-id"), null);
});

test("session allows at most four live Workspaces", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  assert.ok(createLiveWorkspace(session, "two"));
  assert.ok(createLiveWorkspace(session, "three"));
  assert.ok(createLiveWorkspace(session, "four"));
  assert.equal(createLiveWorkspace(session, "five"), null);
});

test("browser history preserves Workspace association without dropping other state", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  createLiveWorkspace(session, "second-id");
  const state = withWorkspaceHistoryId({ entry: "history-id" }, "workspace-id");

  assert.deepEqual(state, {
    entry: "history-id",
    dotnetInspectWorkspaceId: "workspace-id",
  });
  assert.equal(workspaceHistoryId(state), "workspace-id");
  assert.equal(workspaceHistoryId({ dotnetInspectWorkspaceId: "" }), null);
  assert.equal(
    workspaceForHistory(
      session,
      withWorkspaceHistoryId(null, "second-id")).id,
    "second-id");
  assert.equal(workspaceForHistory(session, null).id, "default-id");
  assert.equal(
    workspaceForHistory(
      session,
      withWorkspaceHistoryId(null, "unknown-id")).id,
    "default-id");
});

test("an empty Workspace retains its session-only canonical return route", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  const canonicalHrefs = new Map([
    ["default-id", "https://example.test/#workspace"],
  ]);

  assert.equal(
    rememberedLiveWorkspaceHref(session, canonicalHrefs),
    "https://example.test/#workspace");
  assert.equal(
    rememberedLiveWorkspaceHref(session, new Map()),
    null);
});

test("history cannot restore stale membership into an associated live Workspace", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  updateSelectedLiveWorkspace(session, {
    packages: ["Package.A", "Package.B"],
    activePackageKey: "Package.A",
    shareBasis: null,
    navigation: { stack: [], index: -1 },
  });
  const associated = withWorkspaceHistoryId(null, "default-id");

  assert.equal(
    workspaceHistoryMembershipStatus(
      session,
      associated,
      packages => packages.includes("Package.A")),
    "current");
  assert.equal(
    workspaceHistoryMembershipStatus(
      session,
      associated,
      packages =>
        packages.length === 1
        && packages[0] === "Package.A"),
    "stale");
  assert.equal(
    workspaceHistoryMembershipStatus(
      session,
      associated,
      packages => packages.includes("Package.Closed")),
    "stale");
  assert.equal(
    workspaceHistoryMembershipStatus(
      session,
      withWorkspaceHistoryId(null, "unknown"),
      packages => packages.includes("Package.Closed")),
    "unassociated");
});

test("superseded restoration abandons its transient Workspace projection", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  updateSelectedLiveWorkspace(session, {
    packages: ["Stable.Package"],
    activePackageKey: "Stable.Package",
    shareBasis: null,
    navigation: { stack: [], index: -1 },
  });
  let visiblePackages = ["Stable.Package"];
  const released: string[] = [];
  const transactions = createWorkspaceProjectionTransactionController(
    () => session,
    {
      currentPackages: () => visiblePackages,
      synchronize: () => {
        updateSelectedLiveWorkspace(session, {
          packages: visiblePackages,
          activePackageKey: visiblePackages[0] ?? null,
          shareBasis: null,
          navigation: { stack: [], index: -1 },
        });
      },
      restore: workspace => {
        visiblePackages = [...workspace.packages];
      },
      release: packageModel => released.push(packageModel),
    });
  const sequence = createNavigationSequence(() => transactions.abandon());
  const restorationSequence = sequence.begin();
  const owner = {
    workspaceId: session.selectedWorkspaceId,
    navigationSequence: restorationSequence,
  };
  transactions.begin(owner);
  visiblePackages = ["Partially.Loaded"];

  assert.equal(transactions.blocksSelectedWorkspaceSynchronization(), true);
  sequence.begin();

  assert.deepEqual(visiblePackages, ["Stable.Package"]);
  assert.deepEqual(selectedLiveWorkspace(session).packages, ["Stable.Package"]);
  assert.deepEqual(released, ["Partially.Loaded"]);
  assert.equal(transactions.commit(owner), false);
});

test("restoration transactions follow a replaced Workspace session", () => {
  let session = createLiveWorkspaceSession<string>("original-default");
  let visiblePackages = ["Original.Package"];
  const released: string[] = [];
  const transactions = createWorkspaceProjectionTransactionController(
    () => session,
    {
      currentPackages: () => visiblePackages,
      synchronize: () => {
        updateSelectedLiveWorkspace(session, {
          packages: visiblePackages,
          activePackageKey: visiblePackages[0] ?? null,
          shareBasis: null,
          navigation: { stack: [], index: -1 },
        });
      },
      restore: workspace => {
        visiblePackages = [...workspace.packages];
      },
      release: packageModel => released.push(packageModel),
    });

  session = createLiveWorkspaceSession<string>("restored-default");
  updateSelectedLiveWorkspace(session, {
    packages: ["Restored.Package"],
    activePackageKey: "Restored.Package",
    shareBasis: null,
    navigation: { stack: [], index: -1 },
  });
  const owner = {
    workspaceId: session.selectedWorkspaceId,
    navigationSequence: 4,
  };
  transactions.begin(owner);
  visiblePackages = ["Partially.Loaded"];

  assert.equal(transactions.blocksSelectedWorkspaceSynchronization(), true);
  transactions.abandon();

  assert.deepEqual(visiblePackages, ["Restored.Package"]);
  assert.deepEqual(released, ["Partially.Loaded"]);
});

test("committed restoration releases the replaced Workspace projection", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  updateSelectedLiveWorkspace(session, {
    packages: ["Replaced.Package"],
    activePackageKey: "Replaced.Package",
    shareBasis: null,
    navigation: { stack: [], index: -1 },
  });
  let visiblePackages = ["Replacement.Package"];
  const released: string[] = [];
  const transactions = createWorkspaceProjectionTransactionController(
    () => session,
    {
      currentPackages: () => visiblePackages,
      synchronize: () => {
        updateSelectedLiveWorkspace(session, {
          packages: visiblePackages,
          activePackageKey: visiblePackages[0] ?? null,
          shareBasis: null,
          navigation: { stack: [], index: -1 },
        });
      },
      restore: workspace => {
        visiblePackages = [...workspace.packages];
      },
      release: packageModel => released.push(packageModel),
    });
  const owner = {
    workspaceId: session.selectedWorkspaceId,
    navigationSequence: 3,
  };
  transactions.begin(owner);

  assert.equal(transactions.commit(owner), true);
  assert.deepEqual(
    selectedLiveWorkspace(session).packages,
    ["Replacement.Package"]);
  assert.deepEqual(released, ["Replaced.Package"]);
});

test("late operations cannot mutate a newly selected Workspace", () => {
  const session = createLiveWorkspaceSession<string>("default-id");
  const owner = {
    workspaceId: session.selectedWorkspaceId,
    navigationSequence: 7,
  };
  createLiveWorkspace(session, "extensions-id");

  assert.equal(workspaceOperationIsCurrent(session, owner, 7), false);
  assert.equal(selectLiveWorkspace(session, "default-id")?.id, "default-id");
  assert.equal(workspaceOperationIsCurrent(session, owner, 8), false);
  assert.equal(workspaceOperationIsCurrent(session, owner, 7), true);
});
