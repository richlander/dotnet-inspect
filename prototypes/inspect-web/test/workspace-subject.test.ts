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

test("Workspace navigation always displays the singular Workspace", () => {
  const html = renderWorkspaceSubject({
    packageCount: 2,
    selected: true,
    escapeHtml,
  });

  assert.match(html, /WORKSPACE/);
  assert.match(html, /workspace-card active/);
  assert.match(html, /Workspace[\s\S]*2 loaded coordinates/);
  assert.doesNotMatch(html, /Default Workspace|WORKSPACES/);
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

test("Workspace details render exact-package editing with product activation actions", () => {
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
    /System\.Text\.Json[\s\S]*data-workspace-activate="opaque-action"/);
  assert.match(html, /data-workspace-add[\s\S]*Add package/);
  assert.match(html, /data-workspace-clear[\s\S]*Clear/);
  assert.match(
    html,
    /data-workspace-remove="system\.text\.json\|10\.0\.0\|net10\.0"/);
  assert.match(html, /data-workspace-remove-position="0"/);
  assert.match(html, /aria-label="Remove System\.Text\.Json from the Workspace"/);
  assert.match(html, /Platform[\s\S]*Microsoft\.NETCore\.App/);
  assert.doesNotMatch(html, /microsoft\.netcore\.app\|10\.0\.0\|net10\.0/);
});

test("Workspace package editing stays available while activation actions load or fail", () => {
  const packages: PackageControlPackage[] = [{
    id: "System.Text.Json",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  }];
  const render = (
    loading: boolean,
    error = "",
    demoError = "",
  ) => renderWorkspaceView({
    occurrences: [],
    packages,
    demos: [],
    demoError,
    loading,
    error,
    escapeHtml,
  });

  assert.match(render(true), /Reading package activation actions/);
  assert.match(render(true), /data-workspace-remove=/);
  assert.match(render(true), /aria-label="Inspect System\.Text\.Json when its action is ready"/);
  const refreshing = renderWorkspaceView({
    occurrences: [{
      action: "stale-action",
      package: "System.Text.Json",
      version: "10.0.0",
      framework: "net10.0",
    }],
    packages,
    demos: [],
    demoError: "",
    loading: true,
    error: "",
    escapeHtml,
  });
  assert.doesNotMatch(refreshing, /data-workspace-activate=/);
  assert.match(refreshing, /disabled aria-label="Inspect System\.Text\.Json when its action is ready"/);
  assert.match(render(false, "Acquisition failed"), /Acquisition failed/);
  assert.match(render(false, "Acquisition failed"), /Retry package actions/);
  assert.match(
    render(false, "", "Product demos are unavailable"),
    /Product demos are unavailable/);
});

test("Workspace selection, editing, and activation dispatch separate actions", () => {
  setProductHomeDemoCatalog([{
    id: "stj-serializer",
    title: "System.Text.Json",
    summary: "Browse a real package API",
  }]);
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
  const add = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`add:${name}`, listener),
  };
  const remove = {
    dataset: { workspaceRemove: "package-key" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`remove:${name}`, listener),
  };
  const clear = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`clear:${name}`, listener),
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
  const root = {
    querySelector: (selector: string) => {
      if (selector === "[data-workspace-default]") return select;
      if (selector === "[data-workspace-add]") return add;
      if (selector === "[data-workspace-clear]") return clear;
      return retry;
    },
    querySelectorAll: (selector: string) => {
      if (selector === "[data-workspace-activate]") return [activate];
      if (selector === "[data-workspace-remove]") return [remove];
      return [demo, invalidDemo];
    },
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
      onAdd: () => {
        calls.push("add");
      },
      onRemove: packageKey => {
        calls.push(`remove:${packageKey}`);
      },
      onClear: () => {
        calls.push("clear");
      },
      onDemo: id => {
        calls.push(`demo:${id}`);
      },
      onRetry: () => {
        calls.push("retry");
      },
    });

  listeners.get("select:click")?.(fakeDom.event());
  listeners.get("activate:click")?.(fakeDom.event());
  listeners.get("add:click")?.(fakeDom.event());
  listeners.get("remove:click")?.(fakeDom.event());
  listeners.get("clear:click")?.(fakeDom.event());
  listeners.get("demo:click")?.(fakeDom.event());
  listeners.get("invalid-demo:click")?.(fakeDom.event());
  listeners.get("retry:click")?.(fakeDom.event());
  assert.deepEqual(calls, [
    "select",
    "activate:opaque-action",
    "add",
    "remove:package-key",
    "clear",
    "demo:stj-serializer",
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

test("Workspace focus survives catalog rerenders by stable action identity", () => {
  setProductHomeDemoCatalog([{
    id: "stj-serializer",
    title: "System.Text.Json",
    summary: "Browse a real package API",
  }]);
  const focused: string[] = [];
  const workspace = {
    dataset: {},
    hasAttribute: (name: string) => name === "data-workspace-default",
  };
  assert.deepEqual(
    captureWorkspaceFocus(fakeDom.htmlElement({
      closest: (selector: string) =>
        selector.includes("[data-workspace-default]") ? workspace : null,
    })),
    { kind: "workspace" });

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
    focus: () => focused.push("workspace"),
  };
  const root = fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === "[data-workspace-default]"
        ? workspaceReplacement
        : null,
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-demo]" ? [replacement] : [],
  });
  assert.equal(
    restoreWorkspaceFocus(root, { kind: "workspace" }),
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

test("Workspace editing focus survives rerenders and falls back after removal", () => {
  const focused: string[] = [];
  const add = {
    dataset: {},
    hasAttribute: (name: string) => name === "data-workspace-add",
  };
  const clear = {
    dataset: {},
    hasAttribute: (name: string) => name === "data-workspace-clear",
  };
  const remove = {
    dataset: {
      workspaceRemove: "package-key",
      workspaceRemovePosition: "2",
    },
    hasAttribute: () => false,
  };

  assert.deepEqual(
    captureWorkspaceFocus(fakeDom.htmlElement({
      closest: () => add,
    })),
    { kind: "add" });
  assert.deepEqual(
    captureWorkspaceFocus(fakeDom.htmlElement({
      closest: () => clear,
    })),
    { kind: "clear" });
  assert.deepEqual(
    captureWorkspaceFocus(fakeDom.htmlElement({
      closest: () => remove,
    })),
    { kind: "remove", position: 2 });

  const priorRemove = {
    dataset: {
      workspaceRemove: "prior-key",
      workspaceRemovePosition: "1",
    },
    focus: () => focused.push("prior"),
  };
  const addReplacement = {
    focus: () => focused.push("add"),
  };
  const clearReplacement = {
    focus: () => focused.push("clear"),
  };
  const root = fakeDom.parentNode({
    querySelector: (selector: string) => {
      if (selector === "[data-workspace-add]") return addReplacement;
      if (selector === "[data-workspace-clear]:not(:disabled)")
        return clearReplacement;
      return null;
    },
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-remove]" ? [priorRemove] : [],
  });

  assert.equal(restoreWorkspaceFocus(root, { kind: "add" }), true);
  assert.equal(restoreWorkspaceFocus(root, { kind: "clear" }), true);
  assert.equal(
    restoreWorkspaceFocus(root, { kind: "remove", position: 2 }),
    true);
  assert.deepEqual(focused, ["add", "clear", "prior"]);
});

test("Empty Workspace keeps Add available and disables Clear", () => {
  const html = renderWorkspaceView({
    occurrences: [],
    packages: [],
    demos: [],
    demoError: "",
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.match(html, /data-workspace-add[\s\S]*Add package/);
  assert.match(html, /data-workspace-clear disabled/);
  assert.match(html, /No packages are loaded in this Workspace/);
});
