import assert from "node:assert/strict";
import test from "node:test";
import {
  createFrameworkRequestTracker,
} from "../scripts/published-runtime-framework-transfer.ts";

test("interrupted preload fails after the Worker request succeeds", async () => {
  const tracker = createFrameworkRequestTracker<object>();
  const preload = {};
  const worker = {};
  tracker.observe(preload, "preload");
  tracker.observe(worker, "worker");

  tracker.fail(preload, "partial transfer");
  tracker.finish(worker);

  await assert.rejects(
    tracker.complete(1_000),
    /Framework request failed: preload: partial transfer/u,
  );
});

test("framework request completion returns every finished request", async () => {
  const tracker = createFrameworkRequestTracker<object>();
  const preload = {};
  const worker = {};
  tracker.observe(preload, "preload");
  tracker.observe(worker, "worker");
  tracker.finish(preload);
  tracker.finish(worker);

  assert.deepEqual(await tracker.complete(1_000), [preload, worker]);
});

test("framework request completion remains bounded", async () => {
  const tracker = createFrameworkRequestTracker<object>();
  tracker.observe({}, "stalled");

  await assert.rejects(
    tracker.complete(1),
    /Timed out waiting for framework network requests to complete/u,
  );
});
