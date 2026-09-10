import assert from "node:assert/strict";
import test from "node:test";
import {
  createTerminalEngineFailureLatch,
  type TerminalEngineFailureState,
} from "../src/terminal-engine-failure.ts";

function state(): TerminalEngineFailureState {
  return {
    loading: true,
    engineReady: true,
    engineStartupFailed: false,
    engineStatus: "Inspecting",
    errorTitle: "",
    error: "",
    errorDetail: "",
    retryAction: null,
  };
}

test("terminal Worker failure survives later feature-state mutation", () => {
  let reloads = 0;
  const reload = () => { reloads++; };
  const latch = createTerminalEngineFailureLatch(reload);
  const current = state();
  latch.fail(current, "Worker exited");

  current.loading = true;
  current.engineReady = true;
  current.engineStartupFailed = false;
  current.errorTitle = "";
  current.error = "";
  current.errorDetail = "";
  current.retryAction = null;

  assert.equal(latch.reassert(current), true);
  assert.equal(current.loading, false);
  assert.equal(current.engineReady, false);
  assert.equal(current.engineStartupFailed, true);
  assert.equal(current.errorTitle, "Inspection Worker stopped");
  assert.match(current.error, /Reload to start a new Worker/);
  assert.equal(current.errorDetail, "Worker exited");
  assert.equal(current.retryAction, reload);
  assert.equal(reloads, 0);
});

test("failure-screen package submission reloads only after terminal loss", () => {
  let reloads = 0;
  const latch = createTerminalEngineFailureLatch(() => reloads++);
  const current = state();

  assert.equal(latch.reloadIfFailed(), false);
  latch.fail(current, "Worker exited");
  assert.equal(latch.reloadIfFailed(), true);
  assert.equal(reloads, 1);
});
