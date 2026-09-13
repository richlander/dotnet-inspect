import assert from "node:assert/strict";
import test from "node:test";
import {
  createPackageQueryLiveAnnouncer,
  createPackageQueryAnnouncementTracker,
} from "../src/package-query-announcements.ts";

test("persistent query failures are announced only when introduced", () => {
  const tracker = createPackageQueryAnnouncementTracker();
  const initial = {
    catalogError: "Catalog failed.",
    navigationError: "",
    failures: ["Feed A failed."],
    terminalFailure: "",
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
    terminalFailure: "",
  };

  assert.equal(tracker.take(failed), "Feed failed.");
  assert.equal(tracker.take({ ...failed, failures: [] }), "");
  assert.equal(tracker.take(failed), "Feed failed.");
  tracker.reset();
  assert.equal(tracker.take(failed), "Feed failed.");
});

test("a whole-query failure is announced once", () => {
  const tracker = createPackageQueryAnnouncementTracker();
  const failed = {
    catalogError: "",
    navigationError: "",
    failures: [],
    terminalFailure: "The query timed out.",
  };

  assert.equal(tracker.take(failed), "The query timed out.");
  assert.equal(tracker.take(failed), "");
  assert.equal(
    tracker.take({ ...failed, terminalFailure: "" }),
    "");
  assert.equal(tracker.take(failed), "The query timed out.");
});

test("a new navigation attempt can repeat the same failure announcement", () => {
  const tracker = createPackageQueryAnnouncementTracker();
  const failed = {
    catalogError: "",
    navigationError: "Workspace acquisition failed.",
    failures: [],
    terminalFailure: "",
  };

  assert.equal(tracker.take(failed), "Workspace acquisition failed.");
  assert.equal(tracker.take(failed), "");
  tracker.beginNavigationAttempt();
  assert.equal(tracker.take(failed), "Workspace acquisition failed.");
});

test("the live announcer batches same-turn deltas into a stable region", () => {
  const target = { textContent: "Earlier announcement." };
  const scheduled: Array<() => void> = [];
  const announcer = createPackageQueryLiveAnnouncer(
    () => target,
    action => scheduled.push(action));

  announcer.enqueue("nuget.org: HTTP 503.");
  announcer.enqueue("The package source failed.");
  assert.equal(scheduled.length, 1);
  assert.equal(target.textContent, "Earlier announcement.");

  scheduled.shift()?.();
  assert.equal(target.textContent, "");
  assert.equal(scheduled.length, 1);

  scheduled.shift()?.();
  assert.equal(
    target.textContent,
    "nuget.org: HTTP 503. The package source failed.");
});

test("reset prevents an older scheduled announcement from publishing", () => {
  const target = { textContent: "" };
  const scheduled: Array<() => void> = [];
  const announcer = createPackageQueryLiveAnnouncer(
    () => target,
    action => scheduled.push(action));

  announcer.enqueue("Stale failure.");
  announcer.reset();
  announcer.enqueue("Current failure.");

  scheduled.shift()?.();
  assert.equal(target.textContent, "");
  scheduled.shift()?.();
  assert.equal(target.textContent, "");
  scheduled.shift()?.();
  assert.equal(target.textContent, "Current failure.");
});

test("a temporarily missing live region does not consume announcements", () => {
  let target: { textContent: string | null } | null = null;
  const scheduled: Array<() => void> = [];
  const announcer = createPackageQueryLiveAnnouncer(
    () => target,
    action => scheduled.push(action));

  announcer.enqueue("First failure.");
  scheduled.shift()?.();
  assert.equal(scheduled.length, 0);

  target = { textContent: "" };
  announcer.enqueue("Second failure.");
  scheduled.shift()?.();
  scheduled.shift()?.();
  assert.equal(
    target.textContent,
    "First failure. Second failure.");
});
