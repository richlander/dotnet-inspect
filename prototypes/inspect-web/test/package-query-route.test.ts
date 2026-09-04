import assert from "node:assert/strict";
import test from "node:test";

import {
  historyEntryId,
  isPackageQueryPath,
  isPackageQueryPredecessor,
  packageQueryHistoryState,
  readPackageQueryHistory,
  resolvePackageQueryWorkspaceSuccessor,
  validPackageQueryPrefix,
  withHistoryEntryId,
} from "../src/package-query-route.ts";

test("package query route recognizes only its canonical path", () => {
  assert.equal(isPackageQueryPath("/query"), true);
  assert.equal(isPackageQueryPath("/query/"), true);
  assert.equal(isPackageQueryPath("/packages/query"), false);
});

test("package query prefix validation trims useful input and rejects invalid shapes", () => {
  assert.equal(validPackageQueryPrefix(" Microsoft.Extensions. "), "Microsoft.Extensions.");
  assert.equal(validPackageQueryPrefix("Microsoft-*"), "Microsoft-*");
  assert.equal(validPackageQueryPrefix(""), "");
  assert.equal(validPackageQueryPrefix("contains space"), "contains space");
  assert.equal(validPackageQueryPrefix("../escape"), "../escape");
  assert.equal(validPackageQueryPrefix("a".repeat(101)), "");
});

test("package query history scopes predecessor identity to one entry", () => {
  const predecessor = withHistoryEntryId(
    { retained: "value" },
    "workspace-1");
  const query = packageQueryHistoryState(
    null,
    "query-1",
    {
      predecessorEntryId: "workspace-1",
      returnFocus: "package-search",
    });

  assert.equal(historyEntryId(predecessor), "workspace-1");
  assert.equal(historyEntryId(query), "query-1");
  assert.deepEqual(readPackageQueryHistory(query), {
    predecessorEntryId: "workspace-1",
    returnFocus: "package-search",
  });
  assert.equal(readPackageQueryHistory(predecessor), null);
  assert.equal(predecessor.retained, "value");
  assert.deepEqual(readPackageQueryHistory(packageQueryHistoryState(
    null,
    "query-2",
    {
      predecessorEntryId: "workspace-1",
      returnFocus: "application-query",
    })), {
    predecessorEntryId: "workspace-1",
    returnFocus: "application-query",
  });
  assert.deepEqual(readPackageQueryHistory(packageQueryHistoryState(
    null,
    "query-3",
    {
      predecessorEntryId: "workspace-1",
      returnFocus: "workspace-add",
    })), {
    predecessorEntryId: "workspace-1",
    returnFocus: "workspace-add",
  });
});

test("package query history rejects incomplete or unknown entry state", () => {
  assert.equal(readPackageQueryHistory(null), null);
  assert.equal(readPackageQueryHistory({
    dotnetInspectPackageQuery: {
      predecessorEntryId: "",
      returnFocus: "package-search",
    },
  }), null);
  assert.equal(readPackageQueryHistory({
    dotnetInspectPackageQuery: {
      predecessorEntryId: "workspace-1",
      returnFocus: "other",
    },
  }), null);
});

test("only the recorded predecessor entry arms query return focus", () => {
  const predecessor = withHistoryEntryId(null, "workspace-before");
  const successor = withHistoryEntryId(null, "workspace-after");

  assert.equal(
    isPackageQueryPredecessor(predecessor, "workspace-before"),
    true);
  assert.equal(
    isPackageQueryPredecessor(successor, "workspace-before"),
    false);
  assert.equal(isPackageQueryPredecessor(predecessor, null), false);
});

test("Workspace successor resolution preserves navigation when projection fails", () => {
  const retained = new URL("https://example.test/?package=A&w=retained#workspace");
  const fallback = new URL(
    "https://example.test/?package=A&version=1.0.0&framework=net10.0#workspace");
  const projectionError = new Error("Select one library");

  const projected = resolvePackageQueryWorkspaceSuccessor(
    () => retained,
    () => {
      throw new Error("fallback should not run");
    });
  assert.equal(projected.url, retained);
  assert.equal(projected.projected, true);
  assert.equal(projected.projectionError, null);

  const degraded = resolvePackageQueryWorkspaceSuccessor(
    () => {
      throw projectionError;
    },
    () => fallback);
  assert.equal(degraded.url, fallback);
  assert.equal(degraded.projected, false);
  assert.equal(degraded.projectionError, projectionError);
});
