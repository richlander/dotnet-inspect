import assert from "node:assert/strict";
import test from "node:test";
import {
  createPackageQueryAnnouncementTracker,
} from "../src/package-query-announcements.ts";

test("persistent query failures are announced only when introduced", () => {
  const tracker = createPackageQueryAnnouncementTracker();
  const initial = {
    catalogError: "Catalog failed.",
    navigationError: "",
    failures: ["Feed A failed."],
  };

  assert.equal(
    tracker.take(initial),
    "Catalog failed. Feed A failed.");
  assert.equal(tracker.take(initial), "");
  assert.equal(
    tracker.take({
      ...initial,
      navigationError: "Workspace handoff failed.",
      failures: [...initial.failures, "Feed B failed."],
    }),
    "Workspace handoff failed. Feed B failed.");
});

test("cleared and reset query failures can be announced again", () => {
  const tracker = createPackageQueryAnnouncementTracker();
  const failed = {
    catalogError: "",
    navigationError: "",
    failures: ["Feed failed."],
  };

  assert.equal(tracker.take(failed), "Feed failed.");
  assert.equal(tracker.take({ ...failed, failures: [] }), "");
  assert.equal(tracker.take(failed), "Feed failed.");
  tracker.reset();
  assert.equal(tracker.take(failed), "Feed failed.");
});
