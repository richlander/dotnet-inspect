import assert from "node:assert/strict";
import test from "node:test";
import {
  activateLegacyWorkspaceAfterManaged,
  activateManagedWorkspace,
  activateRetainedWorkspace,
  createRetainedWorkspaceCollection,
  deleteRetainedWorkspace,
  MAX_RETAINED_WORKSPACES,
  publishLegacyWorkspaceAfterManaged,
  publishRetainedWorkspace,
  removeRetainedWorkspace,
  retainManagedWorkspace,
} from "../src/retained-workspaces.ts";

test("publishing retains the prior Workspace and activates a stable new identity", () => {
  let collection = createRetainedWorkspaceCollection<string>();
  collection = publishRetainedWorkspace(collection, null);
  collection = publishRetainedWorkspace(collection, "first");

  assert.equal(collection.activeWorkspaceId, "workspace-2");
  assert.deepEqual(collection.workspaces, [{
    id: "workspace-1",
    label: "Workspace 1",
    kind: "legacy",
    snapshot: "first",
  }, {
    id: "workspace-2",
    label: "Workspace 2",
    kind: "legacy",
    snapshot: null,
  }]);
});

test("activation exchanges only the active identity and retained snapshots", () => {
  let collection = createRetainedWorkspaceCollection<string>();
  collection = publishRetainedWorkspace(collection, null);
  collection = publishRetainedWorkspace(collection, "first");

  const transition = activateRetainedWorkspace(
    collection,
    "workspace-1",
    "second");

  assert.equal(transition.activatedSnapshot, "first");
  assert.equal(transition.collection.activeWorkspaceId, "workspace-1");
  assert.deepEqual(
    transition.collection.workspaces.map(workspace =>
      workspace.kind === "legacy" ? workspace.snapshot : null),
    [null, "second"]);
});

test("deleting the active Workspace selects next, then previous, then null", () => {
  let collection = createRetainedWorkspaceCollection<string>();
  collection = publishRetainedWorkspace(collection, null);
  collection = publishRetainedWorkspace(collection, "first");
  collection = publishRetainedWorkspace(collection, "second");
  collection = activateRetainedWorkspace(
    collection,
    "workspace-2",
    "third").collection;

  let transition = deleteRetainedWorkspace(collection, "workspace-2", "second");
  assert.equal(transition.collection.activeWorkspaceId, "workspace-3");
  assert.equal(transition.activatedSnapshot, "third");
  assert.equal(transition.removedSnapshot, "second");

  transition = deleteRetainedWorkspace(
    transition.collection,
    "workspace-3",
    "third");
  assert.equal(transition.collection.activeWorkspaceId, "workspace-1");
  assert.equal(transition.activatedSnapshot, "first");

  transition = deleteRetainedWorkspace(
    transition.collection,
    "workspace-1",
    "first");
  assert.equal(transition.collection.activeWorkspaceId, null);
  assert.equal(transition.activatedSnapshot, null);
  assert.deepEqual(transition.collection.workspaces, []);
});

test("deleting an inactive Workspace leaves the active Workspace unchanged", () => {
  let collection = createRetainedWorkspaceCollection<string>();
  collection = publishRetainedWorkspace(collection, null);
  collection = publishRetainedWorkspace(collection, "first");

  const transition = deleteRetainedWorkspace(
    collection,
    "workspace-1",
    null);
  assert.equal(transition.collection.activeWorkspaceId, "workspace-2");
  assert.equal(transition.activatedSnapshot, null);
  assert.equal(transition.removedSnapshot, "first");
});

test("the retained collection rejects publication beyond its fixed capacity", () => {
  let collection = createRetainedWorkspaceCollection<string>();
  for (let index = 0; index < MAX_RETAINED_WORKSPACES; index++) {
    collection = publishRetainedWorkspace(
      collection,
      collection.activeWorkspaceId ? `snapshot-${index}` : null);
  }
  assert.throws(
    () => publishRetainedWorkspace(collection, "last"),
    /Delete one before opening another/);
});

test("managed definitions share visible capacity and retain no snapshot", () => {
  let collection = publishRetainedWorkspace(
    createRetainedWorkspaceCollection<string>(),
    null);
  collection = retainManagedWorkspace(collection, {
    id: "managed-1",
    label: "Saved",
    packageCount: 2,
  });
  collection = activateManagedWorkspace(
    collection,
    "managed-1",
    "legacy-active");

  assert.equal(collection.activeWorkspaceId, "managed-1");
  assert.deepEqual(collection.workspaces, [
    {
      id: "workspace-1",
      label: "Workspace 1",
      kind: "legacy",
      snapshot: "legacy-active",
    },
    {
      id: "managed-1",
      label: "Saved",
      kind: "managed",
      packageCount: 2,
    },
  ]);

  collection = retainManagedWorkspace(collection, {
    id: "managed-2",
    label: "Saved 2",
    packageCount: 1,
  });
  collection = retainManagedWorkspace(collection, {
    id: "managed-3",
    label: "Saved 3",
    packageCount: 1,
  });
  assert.throws(
    () => retainManagedWorkspace(collection, {
      id: "managed-4",
      label: "Saved 4",
      packageCount: 1,
    }),
    /Delete one before retaining another/);
});

test("legacy selection after managed activation restores only its compatibility snapshot", () => {
  let collection = publishRetainedWorkspace(
    createRetainedWorkspaceCollection<string>(),
    null);
  collection = retainManagedWorkspace(collection, {
    id: "managed-1",
    label: "Saved",
    packageCount: 1,
  });
  collection = activateManagedWorkspace(
    collection,
    "managed-1",
    "legacy-active");

  const transition =
    activateLegacyWorkspaceAfterManaged(collection, "workspace-1");

  assert.equal(transition.collection.activeWorkspaceId, "workspace-1");
  assert.equal(transition.activatedSnapshot, "legacy-active");
  assert.equal(
    transition.collection.workspaces.find(
      workspace => workspace.id === "managed-1")?.kind,
    "managed");
});

test("inactive managed definitions can be removed without snapshot cleanup", () => {
  let collection = publishRetainedWorkspace(
    createRetainedWorkspaceCollection<string>(),
    null);
  collection = retainManagedWorkspace(collection, {
    id: "managed-1",
    label: "Saved",
    packageCount: 1,
  });

  const removed = removeRetainedWorkspace(collection, "managed-1");

  assert.equal(removed.removed.kind, "managed");
  assert.deepEqual(
    removed.collection.workspaces.map(workspace => workspace.id),
    ["workspace-1"]);
});

test("managed activation can publish a compatibility successor without snapshot exchange", () => {
  let collection = publishRetainedWorkspace(
    createRetainedWorkspaceCollection<string>(),
    null);
  collection = retainManagedWorkspace(collection, {
    id: "managed-1",
    label: "Saved",
    packageCount: 1,
  });
  collection = activateManagedWorkspace(
    collection,
    "managed-1",
    "legacy-active");

  collection = publishLegacyWorkspaceAfterManaged(collection);

  assert.equal(collection.activeWorkspaceId, "workspace-2");
  assert.deepEqual(collection.workspaces.at(-1), {
    id: "workspace-2",
    label: "Workspace 2",
    kind: "legacy",
    snapshot: null,
  });
  assert.equal(
    collection.workspaces.find(
      workspace => workspace.id === "managed-1")?.kind,
    "managed");
});
