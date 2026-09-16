import assert from "node:assert/strict";
import test from "node:test";

import {
  isPackageActivityPath,
  isPackageActivityPredecessor,
  packageActivityHistoryState,
  readPackageActivityHistory,
} from "../src/package-activity-route.ts";
import { historyEntryId, withHistoryEntryId } from "../src/package-query-route.ts";

test("Package Activity recognizes only its canonical route", () => {
  assert.equal(isPackageActivityPath("/activity"), true);
  assert.equal(isPackageActivityPath("/activity/"), true);
  assert.equal(isPackageActivityPath("/query/activity"), false);
});

test("Package Activity history preserves predecessor and return focus", () => {
  const predecessor = withHistoryEntryId(
    { retained: "value" },
    "workspace-1");
  const activity = packageActivityHistoryState(
    null,
    "activity-1",
    {
      predecessorEntryId: "workspace-1",
      returnFocus: "package-search",
    });

  assert.equal(historyEntryId(predecessor), "workspace-1");
  assert.equal(historyEntryId(activity), "activity-1");
  assert.deepEqual(readPackageActivityHistory(activity), {
    predecessorEntryId: "workspace-1",
    returnFocus: "package-search",
  });
  assert.equal(readPackageActivityHistory(predecessor), null);
  assert.equal(predecessor.retained, "value");
  assert.deepEqual(readPackageActivityHistory(packageActivityHistoryState(
    null,
    "activity-2",
    {
      predecessorEntryId: "workspace-1",
      returnFocus: "application-activity",
    })), {
    predecessorEntryId: "workspace-1",
    returnFocus: "application-activity",
  });
});

test("Package Activity history rejects incomplete or unknown state", () => {
  assert.equal(readPackageActivityHistory(null), null);
  assert.equal(readPackageActivityHistory({
    dotnetInspectPackageActivity: {
      predecessorEntryId: "",
      returnFocus: "package-search",
    },
  }), null);
  assert.equal(readPackageActivityHistory({
    dotnetInspectPackageActivity: {
      predecessorEntryId: "workspace-1",
      returnFocus: "other",
    },
  }), null);
});

test("only the recorded predecessor arms Package Activity return focus", () => {
  const predecessor = withHistoryEntryId(null, "workspace-before");
  const successor = withHistoryEntryId(null, "workspace-after");

  assert.equal(
    isPackageActivityPredecessor(predecessor, "workspace-before"),
    true);
  assert.equal(
    isPackageActivityPredecessor(successor, "workspace-before"),
    false);
  assert.equal(isPackageActivityPredecessor(predecessor, null), false);
});
