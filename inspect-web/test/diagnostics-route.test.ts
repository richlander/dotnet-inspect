import assert from "node:assert/strict";
import test from "node:test";
import {
  diagnosticsHistoryState,
  isDiagnosticsHistoryEntry,
  isDiagnosticsPath,
} from "../src/diagnostics-route.ts";

test("Diagnostics recognizes hosted entry paths", () => {
  assert.equal(isDiagnosticsPath("/diagnostics"), true);
  assert.equal(isDiagnosticsPath("/diagnostics/"), true);
  assert.equal(isDiagnosticsPath("/diagnostics/index.html"), false);
  assert.equal(isDiagnosticsPath("/query"), false);
});

test("Diagnostics marks in-app history without discarding retained state", () => {
  const state = diagnosticsHistoryState({ retainedWorkspaceId: "workspace-1" });

  assert.equal(state.retainedWorkspaceId, "workspace-1");
  assert.equal(isDiagnosticsHistoryEntry(state), true);
  assert.equal(isDiagnosticsHistoryEntry({}), false);
});
