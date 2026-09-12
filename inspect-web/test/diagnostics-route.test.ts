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
  const marker = Object.entries(state)
    .find(([key]) => key !== "retainedWorkspaceId");

  assert.equal(state.retainedWorkspaceId, "workspace-1");
  assert.ok(marker);
  assert.equal(typeof marker[1], "string");
  assert.equal(isDiagnosticsHistoryEntry(state), true);
  assert.equal(isDiagnosticsHistoryEntry({
    ...state,
    [marker[0]]: "previous-document",
  }), false);
  assert.equal(isDiagnosticsHistoryEntry({}), false);
});
