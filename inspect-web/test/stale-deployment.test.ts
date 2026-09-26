import assert from "node:assert/strict";
import test from "node:test";
import {
  installStaleDeploymentDetection,
  resetStaleDeploymentDetectionForTest,
  retainSuccessfulImport,
  staleDeploymentDetected,
} from "../src/stale-deployment.ts";

test("a Vite preload failure marks the deployment stale without preventing it", () => {
  resetStaleDeploymentDetectionForTest();
  const target = new EventTarget();
  installStaleDeploymentDetection(target);
  assert.equal(staleDeploymentDetected(), false);

  const event = new Event("vite:preloadError", { cancelable: true });
  target.dispatchEvent(event);

  assert.equal(staleDeploymentDetected(), true);
  assert.equal(event.defaultPrevented, false);
  resetStaleDeploymentDetectionForTest();
});

test("a failed lazy import is re-requested instead of replayed", async () => {
  let calls = 0;
  const load = retainSuccessfulImport(async () => {
    calls++;
    if (calls === 1)
      throw new TypeError("Failed to fetch dynamically imported module");
    return "module";
  });

  await assert.rejects(load(), /Failed to fetch dynamically imported module/);
  assert.equal(await load(), "module");
  assert.equal(await load(), "module");
  assert.equal(calls, 2);
});
