import assert from "node:assert/strict";
import test from "node:test";
import {
  bindWorkspaceSubject,
  focusWorkspace,
  renderWorkspaceSubject,
  renderWorkspaceView,
  workspaceOccurrenceActionsAreVisible,
} from "../src/workspace-subject.ts";
import type { PackageControlPackage } from "../src/package-controls.ts";
import { fakeDom } from "./fake-dom.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

test("Workspace navigation always displays the Default Workspace", () => {
  const html = renderWorkspaceSubject({
    packageCount: 2,
    selected: true,
    escapeHtml,
  });

  assert.match(html, /WORKSPACES[\s\S]*1/);
  assert.match(html, /workspace-card active/);
  assert.match(html, /Default Workspace[\s\S]*2 loaded coordinates/);
});

test("Workspace occurrence actions are visible only in the rendered Workspace view", () => {
  const visible = {
    engineReady: true,
    scope: "workspace" as const,
    explorerOpen: false,
    creditsOpen: false,
    packageQueryOpen: false,
    loading: false,
    error: "",
    home: false,
    hasPackage: true,
  };

  assert.equal(workspaceOccurrenceActionsAreVisible(visible), true);
  for (const hidden of [
    { engineReady: false },
    { explorerOpen: true },
    { creditsOpen: true },
    { packageQueryOpen: true },
    { loading: true },
    { error: "failed" },
    { home: true },
    { hasPackage: false },
    { scope: "type" as const },
  ]) {
    assert.equal(
      workspaceOccurrenceActionsAreVisible({
        ...visible,
        ...hidden,
      }),
      false);
  }
});

test("Workspace details render product occurrences as opaque actions", () => {
  const packages: PackageControlPackage[] = [{
    id: "System.Text.Json",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  }, {
    id: "Microsoft.NETCore.App",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: true,
  }];

  const html = renderWorkspaceView({
    occurrences: [{
      action: "opaque-action",
      package: "system.text.json",
      version: "10.0.0",
      framework: "net10.0",
    }],
    packages,
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.match(html, /Workspace[\s\S]*Default Workspace/);
  assert.match(
    html,
    /data-workspace-activate="opaque-action"[\s\S]*system\.text\.json/);
  assert.match(html, /Platform[\s\S]*Microsoft\.NETCore\.App/);
  assert.doesNotMatch(html, /data-workspace-close/);
});

test("Workspace details distinguish loading, empty, and failure", () => {
  const render = (loading: boolean, error = "") => renderWorkspaceView({
    occurrences: [],
    packages: [],
    loading,
    error,
    escapeHtml,
  });

  assert.match(render(true), /Reading Workspace package occurrences/);
  assert.match(render(false), /No packages are loaded/);
  assert.match(render(false, "Acquisition failed"), /Acquisition failed/);
});

test("Workspace selection and activation dispatch separate actions", () => {
  const listeners = new Map<string, EventListener>();
  const select = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`select:${name}`, listener),
  };
  const activate = {
    dataset: { workspaceActivate: "opaque-action" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`activate:${name}`, listener),
  };
  const retry = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`retry:${name}`, listener),
  };
  const root = {
    querySelector: (selector: string) =>
      selector === "[data-workspace-default]" ? select : retry,
    querySelectorAll: () => [activate],
  };
  const calls: string[] = [];

  bindWorkspaceSubject(
    fakeDom.parentNode(root),
    {
      onSelect: () => {
        calls.push("select");
      },
      onActivate: action => {
        calls.push(`activate:${action}`);
      },
      onRetry: () => {
        calls.push("retry");
      },
    });

  listeners.get("select:click")?.(fakeDom.event());
  listeners.get("activate:click")?.(fakeDom.event());
  listeners.get("retry:click")?.(fakeDom.event());
  assert.deepEqual(calls, [
    "select",
    "activate:opaque-action",
    "retry",
  ]);
});

test("Workspace focus targets the always-visible Workspace", () => {
  let focused = false;
  const root = {
    querySelector: () => ({
      focus: () => {
        focused = true;
      },
    }),
  };

  assert.equal(focusWorkspace(fakeDom.parentNode(root)), true);
  assert.equal(focused, true);
});
