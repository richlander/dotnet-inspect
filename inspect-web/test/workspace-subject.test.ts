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
import { workspacePackageRemovalKey } from "../src/data.ts";
import type {
  NavigationPackagePresentationItem,
} from "../src/navigation-descriptor-presentation.ts";
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

test("Workspace navigation exposes managed activation progress and failure", () => {
  const html = renderWorkspaceSubject({
    workspaces: [{
      id: "workspace-pending",
      label: "Pending Workspace",
      packageCount: 1,
      active: false,
      status: "Activating",
      deletionDisabled: true,
    }, {
      id: "workspace-failed",
      label: "Failed Workspace",
      packageCount: 0,
      active: false,
      status: "Activation failed",
    }, {
      id: "workspace-closing",
      label: "Closing Workspace",
      packageCount: 1,
      active: true,
      status: "Closing",
      deletionDisabled: true,
    }],
    escapeHtml,
  });

  assert.match(
    html,
    /data-workspace-switch="workspace-pending"[\s\S]*disabled[\s\S]*Activating/,
  );
  assert.match(
    html,
    /data-workspace-delete="workspace-pending"[\s\S]*disabled/,
  );
  assert.match(
    html,
    /data-workspace-switch="workspace-failed"[\s\S]*Activation failed/,
  );
  assert.match(
    html,
    /data-workspace-select="workspace-closing"[\s\S]*disabled[\s\S]*Closing/,
  );
});

test("Workspace occurrence actions are visible only in the rendered Workspace view", () => {
  const visible = {
    engineReady: true,
    scope: "workspace" as const,
    explorerOpen: false,
    creditsOpen: false,
    packageQueryOpen: false,
    packageActivityOpen: false,
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
    { packageActivityOpen: true },
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
    platform: { tfm: "net11.0", version: "11.0.0-preview.7.26381.103", includeAllLibraries: false, filter: "" },
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.doesNotMatch(html, /Demos|data-workspace-demo/);
  assert.match(
    html,
    /data-workspace-activate="opaque-action"[\s\S]*System\.Text\.Json/);
  assert.match(html, /data-workspace-platform[\s\S]*\.NET Platform/);
  assert.doesNotMatch(html, /Microsoft\.NETCore\.App/);
  assert.doesNotMatch(html, /data-workspace-close/);
});

test("Workspace details distinguish loading, empty, and failure", () => {
  const render = (
    loading: boolean,
    error = "",
  ) => renderWorkspaceView({
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

test("Workspace details render framework Libraries without a Platform component", () => {
  const html = renderWorkspaceView({
    occurrences: [],
    packages: [{
      id: "Microsoft.NETCore.App",
      version: "11.0.0",
      activeFramework: "net11.0",
      isRuntimePack: true,
    }],
    frameworkLibraries: [{
      name: "System.Text.Json",
      assembly: "System.Text.Json",
      pack: "netcore.app",
      version: "11.0.0",
      framework: "net11.0",
      source: ".NET",
    }],
    loading: false,
    error: "",
    escapeHtml,
  });

  assert.match(html, /1 loaded coordinate/);
  assert.match(
    html,
    /data-workspace-framework-library="System\.Text\.Json"[\s\S]*data-workspace-framework-pack="netcore\.app"[\s\S]*data-workspace-framework="net11\.0"[\s\S]*data-workspace-framework-version="11\.0\.0"/);
  assert.doesNotMatch(html, /data-workspace-platform|\.NET Platform/);
});

test("Workspace removal remains available while occurrence activation loads or fails", () => {
  for (const status of [{ loading: true, error: "" }, { loading: false, error: "Offline" }]) {
    const html = renderWorkspaceView({
      packages: [{ id: "Alpha", version: "1.0.0", activeFramework: "net10.0", isRuntimePack: false }],
      occurrences: [], escapeHtml, ...status,
    });
    assert.match(html, /data-workspace-remove=/);
    assert.match(html, /aria-label="Remove Alpha 1\.0\.0 net10\.0 from Workspace"/);
    assert.match(html, /class="workspace-occurrence"[^>]*disabled/);
  }
});

test("Workspace product rows do not expose compatibility mutation controls", () => {
  const packages: NavigationPackagePresentationItem[] =
    ["linux-x64", "win-x64"].map((runtimeIdentifier, order) => ({
      order,
      navigationId: `navigation-${order}`,
      subject: {
        key: `package-${order}`,
        identity: `package-${order}`,
        kind: "Package",
        label: "Alpha",
        summary: "Alpha package",
        state: "Available",
        current: order === 0,
        retained: true,
        action: null,
        localAction: null,
        evidence: null,
      },
      package: "Alpha",
      version: "1.0.0",
      framework: "net10.0",
      runtimeIdentifier,
      realization: "Ready",
      realizationFailure: null,
      detailFailure: null,
      summary: {
        selectedCompileFramework: "net10.0",
        libraryCount: 1,
        typeCount: 1,
        memberCount: 1,
        documentCount: 0,
        hasInspectionNotices: false,
      },
    }));
  const html = renderWorkspaceView({
    navigationPackages: packages,
    packages: [],
    occurrences: [],
    loading: false,
    error: "",
    escapeHtml,
  });
  assert.doesNotMatch(html, /data-workspace-remove=/);
  assert.doesNotMatch(html, /data-workspace-add-package/);
  assert.match(html, /Choose a package to inspect it\.<\/p>/);
  assert.doesNotMatch(html, /adjacent close button/);
});

test("Workspace Add is offered independently of occurrence loading and disabled until ready", () => {
  const options = {
    packages: [], occurrences: [],
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
  const removalKey = workspacePackageRemovalKey({
    id: "Alpha",
    version: "1.0.0",
    activeFramework: "net10.0",
    runtimeIdentifier: "linux-x64",
  });
  const remove = {
    dataset: { workspaceRemove: removalKey },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`remove:${name}`, listener),
  };
  const productPackage = {
    dataset: { productPackageAction: "package-navigation" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`product-package:${name}`, listener),
  };
  const productPlatform = {
    dataset: { productPlatformAction: "platform-navigation" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`product-platform:${name}`, listener),
  };
  const frameworkLibrary = {
    dataset: {
      workspaceFrameworkLibrary: "System.Text.Json",
      workspaceFrameworkPack: "netcore.app",
      workspaceFramework: "net11.0",
      workspaceFrameworkVersion: "11.0.0",
    },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`framework-library:${name}`, listener),
  };
  const root = {
    querySelector: (selector: string) =>
      selector === "[data-workspace-retry]" ? retry
        : selector === "[data-workspace-add-package]" ? add
          : selector === "[data-workspace-framework-library]"
            ? frameworkLibrary
            : null,
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-select]" ? [select]
        : selector === "[data-workspace-switch]" ? [workspaceSwitch]
        : selector === "[data-workspace-delete]" ? [workspaceDelete]
        : selector === "[data-workspace-activate]"
        ? [activate]
        : selector === "[data-product-package-action]"
          ? [productPackage]
        : selector === "[data-product-platform-action]"
          ? [productPlatform]
        : selector === "[data-workspace-demo]" ? [demo, invalidDemo]
          : selector === "[data-workspace-remove]" ? [remove] : [],
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
      onProductPackageAction: id =>
        calls.push(`product-package:${id}`),
      onProductPlatformAction: id =>
        calls.push(`product-platform:${id}`),
      onDemo: id => {
        calls.push(`demo:${id}`);
      },
      onRetry: () => {
        calls.push("retry");
      },
      onAddPackage: () => { calls.push("add"); },
      onRemove: key => { calls.push(`remove:${key}`); },
      onFrameworkLibrary: (assembly, pack, framework, version) => {
        calls.push(`framework-library:${assembly}:${pack}:${framework}:${version}`);
      },
    });

  listeners.get("select:click")?.(fakeDom.event());
  listeners.get("switch:click")?.(fakeDom.event());
  listeners.get("delete:click")?.(fakeDom.event());
  listeners.get("activate:click")?.(fakeDom.event());
  listeners.get("product-package:click")?.(fakeDom.event());
  listeners.get("product-platform:click")?.(fakeDom.event());
  listeners.get("demo:click")?.(fakeDom.event());
  listeners.get("invalid-demo:click")?.(fakeDom.event());
  listeners.get("retry:click")?.(fakeDom.event());
  listeners.get("add:click")?.(fakeDom.event());
  listeners.get("remove:click")?.(fakeDom.event());
  listeners.get("framework-library:click")?.(fakeDom.event());
  assert.deepEqual(calls, [
    "select:workspace-1",
    "switch:workspace-2",
    "delete:workspace-2",
    "activate:opaque-action",
    "product-package:package-navigation",
    "product-platform:platform-navigation",
    "demo:stj-serializer",
    "retry",
    "add",
    `remove:${removalKey}`,
    "framework-library:System.Text.Json:netcore.app:net11.0:11.0.0",
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
