import assert from "node:assert/strict";
import test from "node:test";
import {
  beginRetainedWorkspaceActivation,
  commitRetainedWorkspaceActivation,
  createRetainedWorkspaceDefinitionCollection,
  deleteRetainedWorkspaceDefinition,
  failRetainedWorkspaceActivation,
  MAX_RETAINED_WORKSPACE_DEFINITIONS,
  retainWorkspaceDefinition,
} from "../src/retained-workspace-definitions.ts";

test("retained definitions contain only inert restoration and presentation state", () => {
  const collection = retainWorkspaceDefinition(
    createRetainedWorkspaceDefinitionCollection(),
    "format-2-packet",
    "/?w=format-2-packet",
    "JSON");

  assert.deepEqual(collection.definitions, [{
    id: "workspace-1",
    label: "JSON",
    packet: "format-2-packet",
    canonicalLocation: "/?w=format-2-packet",
    failure: null,
  }]);
  assert.equal(collection.activeDefinitionId, null);
});

test("A B A activation keeps stable definitions and advances exact intent", () => {
  let collection = createRetainedWorkspaceDefinitionCollection();
  collection = retainWorkspaceDefinition(collection, "packet-a", "/?w=a", "A");
  collection = retainWorkspaceDefinition(collection, "packet-b", "/?w=b", "B");

  const firstA = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(firstA.kind, "started");
  if (firstA.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(
    firstA.collection,
    firstA.intent);

  const b = beginRetainedWorkspaceActivation(collection, "workspace-2");
  assert.equal(b.kind, "started");
  if (b.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(b.collection, b.intent);

  const secondA = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(secondA.kind, "started");
  if (secondA.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(
    secondA.collection,
    secondA.intent);

  assert.equal(collection.activeDefinitionId, "workspace-1");
  assert.equal(collection.activationGeneration, 3);
  assert.deepEqual(
    collection.definitions.map(definition => definition.packet),
    ["packet-a", "packet-b"]);
});

test("selecting the active definition is a no-effect", () => {
  let collection = retainWorkspaceDefinition(
    createRetainedWorkspaceDefinitionCollection(),
    "packet",
    "/?w=packet");
  const started = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(started.kind, "started");
  if (started.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(
    started.collection,
    started.intent);

  const repeated = beginRetainedWorkspaceActivation(
    collection,
    "workspace-1");

  assert.equal(repeated.kind, "noEffect");
  assert.equal(repeated.collection, collection);
});

test("selecting active invalidates an in-flight replacement", () => {
  let collection = createRetainedWorkspaceDefinitionCollection();
  collection = retainWorkspaceDefinition(collection, "packet-a", "/?w=a");
  collection = retainWorkspaceDefinition(collection, "packet-b", "/?w=b");
  const a = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(a.kind, "started");
  if (a.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(a.collection, a.intent);
  const b = beginRetainedWorkspaceActivation(collection, "workspace-2");
  assert.equal(b.kind, "started");
  if (b.kind !== "started") return;

  const returnToA = beginRetainedWorkspaceActivation(
    b.collection,
    "workspace-1");

  assert.equal(returnToA.kind, "noEffect");
  assert.equal(returnToA.collection.activeDefinitionId, "workspace-1");
  assert.equal(returnToA.collection.activatingDefinitionId, null);
  assert.equal(
    commitRetainedWorkspaceActivation(returnToA.collection, b.intent),
    returnToA.collection);
});

test("stale completion and failure cannot replace a newer intent", () => {
  let collection = createRetainedWorkspaceDefinitionCollection();
  collection = retainWorkspaceDefinition(collection, "packet-a", "/?w=a");
  collection = retainWorkspaceDefinition(collection, "packet-b", "/?w=b");
  const a = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(a.kind, "started");
  if (a.kind !== "started") return;
  const b = beginRetainedWorkspaceActivation(a.collection, "workspace-2");
  assert.equal(b.kind, "started");
  if (b.kind !== "started") return;

  assert.equal(
    commitRetainedWorkspaceActivation(b.collection, a.intent),
    b.collection);
  assert.equal(
    failRetainedWorkspaceActivation(b.collection, a.intent, "late"),
    b.collection);
  collection = commitRetainedWorkspaceActivation(b.collection, b.intent);
  assert.equal(collection.activeDefinitionId, "workspace-2");
});

test("failure remains attached to the attempted definition and preserves active", () => {
  let collection = createRetainedWorkspaceDefinitionCollection();
  collection = retainWorkspaceDefinition(collection, "packet-a", "/?w=a");
  collection = retainWorkspaceDefinition(collection, "packet-b", "/?w=b");
  const a = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(a.kind, "started");
  if (a.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(a.collection, a.intent);
  const b = beginRetainedWorkspaceActivation(collection, "workspace-2");
  assert.equal(b.kind, "started");
  if (b.kind !== "started") return;

  collection = failRetainedWorkspaceActivation(
    b.collection,
    b.intent,
    "Package unavailable.");

  assert.equal(collection.activeDefinitionId, "workspace-1");
  assert.equal(collection.activatingDefinitionId, null);
  assert.equal(collection.definitions[1]?.failure, "Package unavailable.");
});

test("retention is bounded independently from live realization capacity", () => {
  let collection = createRetainedWorkspaceDefinitionCollection();
  for (let index = 0; index < MAX_RETAINED_WORKSPACE_DEFINITIONS; index++) {
    collection = retainWorkspaceDefinition(
      collection,
      `packet-${index}`,
      `/?w=${index}`);
  }

  assert.throws(
    () => retainWorkspaceDefinition(collection, "extra", "/?w=extra"),
    /Delete one before opening another/);
  collection = deleteRetainedWorkspaceDefinition(collection, "workspace-2");
  assert.equal(collection.definitions.length, 3);
});

test("active and activating definitions require transactional deletion", () => {
  let collection = createRetainedWorkspaceDefinitionCollection();
  collection = retainWorkspaceDefinition(collection, "packet-a", "/?w=a");
  collection = retainWorkspaceDefinition(collection, "packet-b", "/?w=b");
  const a = beginRetainedWorkspaceActivation(collection, "workspace-1");
  assert.equal(a.kind, "started");
  if (a.kind !== "started") return;
  collection = commitRetainedWorkspaceActivation(a.collection, a.intent);
  const b = beginRetainedWorkspaceActivation(collection, "workspace-2");
  assert.equal(b.kind, "started");
  if (b.kind !== "started") return;

  assert.throws(
    () => deleteRetainedWorkspaceDefinition(b.collection, "workspace-1"),
    /Activate a successor or close the active Workspace/);
  assert.throws(
    () => deleteRetainedWorkspaceDefinition(b.collection, "workspace-2"),
    /cannot be deleted while its activation is current/);
});
