import assert from "node:assert/strict";
import test from "node:test";
import {
  bindWorkspaceSubject,
  captureWorkspaceFocus,
  focusWorkspace,
  renderWorkspaceSubject,
  renderWorkspaceView,
  restoreWorkspaceFocus,
  workspaceOccurrenceActionsAreVisible,
} from "../src/workspace-subject.ts";
import type { PackageControlPackage } from "../src/package-controls.ts";
import { setProductHomeDemoCatalog } from "../src/product-home-demos.ts";
import { fakeDom } from "./fake-dom.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

test("Workspace navigation lists retained Workspaces and marks one active", () => {
  const html = renderWorkspaceSubject({
    workspaces: [{
      id: "workspace-1",
      label: "Workspace 1",
      packageCount: 2,
      active: false,
    }, {
      id: "workspace-2",
      label: "Workspace 2",
      packageCount: 1,
      active: true,
    }],
    escapeHtml,
  });

  assert.match(html, /WORKSPACES/);
  assert.match(html, /workspace-card active/);
  assert.match(html, /Workspace 1[\s\S]*2 loaded coordinates[\s\S]*Activate/);
  assert.match(html, /Workspace 2[\s\S]*1 loaded coordinate[\s\S]*Active/);
  assert.match(html, /data-workspace-switch="workspace-1"/);
  assert.match(html, /data-workspace-select="workspace-2"/);
  assert.match(html, /data-workspace-delete="workspace-1"/);
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
    demos: [{
      id: "stj-serializer",
      title: "System.Text.Json",
      summary: "Browse a real package API",
    }],
    demoError: "",
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.match(html, /Demos[\s\S]*System\.Text\.Json[\s\S]*Browse a real package API/);
  assert.match(html, /data-workspace-demo="stj-serializer"/);
  assert.match(html, /aria-label="Open demo System\.Text\.Json"/);
  assert.match(
    html,
    /data-workspace-activate="opaque-action"[\s\S]*System\.Text\.Json/);
  assert.match(html, /Platform[\s\S]*Microsoft\.NETCore\.App/);
  assert.doesNotMatch(html, /data-workspace-close/);
});

test("Workspace details distinguish loading, empty, and failure", () => {
  const render = (
    loading: boolean,
    error = "",
    demoError = "",
  ) => renderWorkspaceView({
    occurrences: [],
    packages: [],
    demos: [],
    demoError,
    loading,
    error,
    escapeHtml,
  });

  assert.match(render(true), /Reading Workspace package occurrences/);
  assert.match(render(false), /No packages are loaded/);
  assert.match(render(false, "Acquisition failed"), /Acquisition failed/);
  assert.match(
    render(false, "", "Product demos are unavailable"),
    /Product demos are unavailable/);
});

test("Workspace removal remains available while occurrence activation loads or fails", () => {
  for (const status of [{ loading: true, error: "" }, { loading: false, error: "Offline" }]) {
    const html = renderWorkspaceView({
      packages: [{ id: "Alpha", version: "1.0.0", activeFramework: "net10.0", isRuntimePack: false }],
      occurrences: [], demos: [], demoError: "", escapeHtml, ...status,
    });
    assert.match(html, /data-workspace-remove=/);
    assert.match(html, /aria-label="Remove Alpha 1\.0\.0 net10\.0 from Workspace"/);
    assert.match(html, /class="workspace-occurrence"[^>]*disabled/);
  }
});

test("Workspace Add is offered independently of occurrence loading and disabled until ready", () => {
  const options = {
    packages: [], occurrences: [], demos: [], demoError: "",
    loading: true, error: "", escapeHtml,
  };
  assert.match(renderWorkspaceView({ ...options, canAddPackage: true }),
    /data-workspace-add-package>Add package/);
  assert.match(renderWorkspaceView({ ...options, canAddPackage: false }),
    /data-workspace-add-package disabled/);
});

test("Workspace selection, switching, deletion, and occurrence activation dispatch separate actions", () => {
  setProductHomeDemoCatalog([{
    id: "stj-serializer",
    title: "System.Text.Json",
    summary: "Browse a real package API",
  }]);
  const listeners = new Map<string, EventListener>();
  const select = {
    dataset: { workspaceSelect: "workspace-1" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`select:${name}`, listener),
  };
  const workspaceSwitch = {
    dataset: { workspaceSwitch: "workspace-2" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`switch:${name}`, listener),
  };
  const workspaceDelete = {
    dataset: { workspaceDelete: "workspace-2" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`delete:${name}`, listener),
  };
  const activate = {
    dataset: { workspaceActivate: "opaque-action" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`activate:${name}`, listener),
  };
  const demo = {
    dataset: { workspaceDemo: "stj-serializer" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`demo:${name}`, listener),
  };
  const invalidDemo = {
    dataset: { workspaceDemo: "not-a-demo" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`invalid-demo:${name}`, listener),
  };
  const retry = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`retry:${name}`, listener),
  };
  const add = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`add:${name}`, listener),
  };
  const root = {
    querySelector: (selector: string) =>
      selector === "[data-workspace-retry]" ? retry
        : selector === "[data-workspace-add-package]" ? add : null,
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-select]" ? [select]
        : selector === "[data-workspace-switch]" ? [workspaceSwitch]
        : selector === "[data-workspace-delete]" ? [workspaceDelete]
        : selector === "[data-workspace-activate]"
        ? [activate]
        : selector === "[data-workspace-demo]" ? [demo, invalidDemo] : [],
  };
  const calls: string[] = [];

  bindWorkspaceSubject(
    fakeDom.parentNode(root),
    {
      onSelect: id => {
        calls.push(`select:${id}`);
      },
      onActivateWorkspace: id => calls.push(`switch:${id}`),
      onDeleteWorkspace: id => calls.push(`delete:${id}`),
      onActivate: action => {
        calls.push(`activate:${action}`);
      },
      onDemo: id => {
        calls.push(`demo:${id}`);
      },
      onRetry: () => {
        calls.push("retry");
      },
      onAddPackage: () => { calls.push("add"); },
    });

  listeners.get("select:click")?.(fakeDom.event());
  listeners.get("switch:click")?.(fakeDom.event());
  listeners.get("delete:click")?.(fakeDom.event());
  listeners.get("activate:click")?.(fakeDom.event());
  listeners.get("demo:click")?.(fakeDom.event());
  listeners.get("invalid-demo:click")?.(fakeDom.event());
  listeners.get("retry:click")?.(fakeDom.event());
  listeners.get("add:click")?.(fakeDom.event());
  assert.deepEqual(calls, [
    "select:workspace-1",
    "switch:workspace-2",
    "delete:workspace-2",
    "activate:opaque-action",
    "demo:stj-serializer",
    "retry",
    "add",
  ]);
});

test("Workspace focus targets the active or first retained Workspace", () => {
  const selectors: string[] = [];
  const focused: string[] = [];
  const root = {
    querySelector: (selector: string) => {
      selectors.push(selector);
      return selector === "[data-workspace-select]"
        ? { focus: () => focused.push("active") }
        : { focus: () => focused.push("inactive") };
    },
  };

  assert.equal(focusWorkspace(fakeDom.parentNode(root)), true);
  assert.deepEqual(selectors, ["[data-workspace-select]"]);
  assert.deepEqual(focused, ["active"]);

  selectors.length = 0;
  focused.length = 0;
  const fallbackRoot = {
    querySelector: (selector: string) => {
      selectors.push(selector);
      return selector === "[data-workspace-switch]"
        ? { focus: () => focused.push("inactive") }
        : null;
    },
  };
  assert.equal(focusWorkspace(fakeDom.parentNode(fallbackRoot)), true);
  assert.deepEqual(selectors, [
    "[data-workspace-select]",
    "[data-workspace-switch]",
  ]);
  assert.deepEqual(focused, ["inactive"]);
});

test("Workspace focus survives catalog rerenders by stable Workspace identity", () => {
  setProductHomeDemoCatalog([{
    id: "stj-serializer",
    title: "System.Text.Json",
    summary: "Browse a real package API",
  }]);
  const focused: string[] = [];
  const workspace = {
    dataset: { workspaceSelect: "workspace-1" },
    hasAttribute: () => false,
  };
  assert.deepEqual(
    captureWorkspaceFocus(fakeDom.htmlElement({
      closest: (selector: string) =>
        selector.includes("[data-workspace-select]") ? workspace : null,
    })),
    { kind: "workspace", id: "workspace-1" });

  const demo = {
    dataset: { workspaceDemo: "stj-serializer" },
    hasAttribute: () => false,
    focus: () => focused.push("demo"),
  };
  const active = fakeDom.htmlElement({
    closest: (selector: string) =>
      selector.includes("[data-workspace-demo]") ? demo : null,
  });
  const captured = captureWorkspaceFocus(active);
  assert.deepEqual(captured, { kind: "demo", id: "stj-serializer" });

  const replacement = {
    dataset: { workspaceDemo: "stj-serializer" },
    focus: () => focused.push("replacement"),
  };
  const workspaceReplacement = {
    dataset: { workspaceSelect: "workspace-1" },
    focus: () => focused.push("workspace"),
  };
  const root = fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === "[data-workspace-select], [data-workspace-switch]"
        ? workspaceReplacement
        : null,
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-demo]" ? [replacement]
        : selector === "[data-workspace-select], [data-workspace-switch]"
          ? [workspaceReplacement] : [],
  });
  assert.equal(
    restoreWorkspaceFocus(root, { kind: "workspace", id: "workspace-1" }),
    true);
  assert.equal(
    captured && restoreWorkspaceFocus(root, captured),
    true);
  assert.deepEqual(focused, ["workspace", "replacement"]);

  assert.equal(
    captureWorkspaceFocus(fakeDom.htmlElement({
      closest: () => null,
    })),
    null);
});
