import assert from "node:assert/strict";
import test from "node:test";

import {
  historyEntryId,
  isPackageQueryPath,
  isPackageQueryPredecessor,
  packageQueryHistoryState,
  readPackageQueryHistory,
  resolvePackageQueryWorkspaceSuccessor,
  validPackageQuerySearchText,
  withHistoryEntryId,
} from "../src/package-query-route.ts";

test("package query route recognizes only its canonical path", () => {
  assert.equal(isPackageQueryPath("/query"), true);
  assert.equal(isPackageQueryPath("/query/"), true);
  assert.equal(isPackageQueryPath("/packages/query"), false);
});

test("package query search validation preserves nonempty Gallery text exactly", () => {
  for (const text of [
    " Microsoft.Extensions. ",
    "Microsoft-*",
    "hosting dependency injection",
    ' tags:"web api" ',
    "a".repeat(100),
  ]) {
    assert.equal(validPackageQuerySearchText(text), text);
  }
});

test("package query blank search normalizes to browse without fabricating a wildcard", () => {
  for (const text of ["", " ", " ".repeat(100), "\u00a0\u2003"]) {
    assert.equal(validPackageQuerySearchText(text), "");
  }
});

test("package query invalid search is distinct from blank browse", () => {
  for (const text of [
    "a".repeat(101),
    " ".repeat(101),
    "contains\nnewline",
    "\t",
    "\rtext",
    "text\u0000",
    "text\u007f",
    "text\u0085",
  ]) {
    assert.equal(validPackageQuerySearchText(text), null);
  }
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
