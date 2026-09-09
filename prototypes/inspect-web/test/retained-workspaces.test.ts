import assert from "node:assert/strict";
import test from "node:test";
import {
  activateRetainedWorkspace,
  createRetainedWorkspaceCollection,
  deleteRetainedWorkspace,
  MAX_RETAINED_WORKSPACES,
  publishRetainedWorkspace,
} from "../src/retained-workspaces.ts";

test("publishing retains the prior Workspace and activates a stable new identity", () => {
  let collection = createRetainedWorkspaceCollection<string>();
  collection = publishRetainedWorkspace(collection, null);
  collection = publishRetainedWorkspace(collection, "first");

  assert.equal(collection.activeWorkspaceId, "workspace-2");
  assert.deepEqual(collection.workspaces, [{
    id: "workspace-1",
    label: "Workspace 1",
    snapshot: "first",
  }, {
    id: "workspace-2",
    label: "Workspace 2",
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
    transition.collection.workspaces.map(workspace => workspace.snapshot),
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
