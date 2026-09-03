import assert from "node:assert/strict";
import test from "node:test";
import {
  bindWorkspaceSubject,
  focusWorkspace,
  renderWorkspaceSubject,
  renderWorkspaceView,
  type WorkspaceSummary,
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

function packageIdentityKey(pkg: PackageControlPackage) {
  return `${pkg.id}@${pkg.version}::${pkg.activeFramework}`;
}

const stj: PackageControlPackage = {
  id: "System.Text.Json",
  version: "10.0.0",
  activeFramework: "net10.0",
  isRuntimePack: false,
};
const extensions: PackageControlPackage = {
  id: "Microsoft.Extensions.DependencyInjection",
  version: "10.0.0",
  activeFramework: "net10.0",
  isRuntimePack: false,
};
const platform: PackageControlPackage = {
  id: "Microsoft.NETCore.App",
  version: "10.0.0",
  activeFramework: "net10.0",
  isRuntimePack: true,
};

function workspace(
  overrides: Partial<WorkspaceSummary<PackageControlPackage>> = {},
): WorkspaceSummary<PackageControlPackage> {
  return {
    id: "default",
    name: "Default",
    isDefault: true,
    packages: [stj],
    activePackageKey: packageIdentityKey(stj),
    ...overrides,
  };
}

test("Workspace inventory lists every live workspace and inferred packages", () => {
  const html = renderWorkspaceSubject({
    workspaces: [
      workspace(),
      workspace({
        id: "extensions",
        name: "Workspace 2",
        isDefault: false,
        packages: [extensions, platform],
        activePackageKey: packageIdentityKey(extensions),
      }),
    ],
    selectedWorkspaceId: "extensions",
    maximumWorkspaces: 4,
    escapeHtml,
  });

  assert.match(html, /WORKSPACES[\s\S]*2/);
  assert.match(html, /data-workspace="default"[\s\S]*Default[\s\S]*System\.Text\.Json/);
  assert.match(html, /workspace-card active[\s\S]*Workspace 2/);
  assert.match(html, /Microsoft\.Extensions\.DependencyInjection \+ 1/);
  assert.match(html, /data-workspace-create/);
  assert.doesNotMatch(html, /workspace packet/i);
});

test("Workspace inventory disables creation at the session limit", () => {
  const html = renderWorkspaceSubject({
    workspaces: [
      workspace(),
      workspace({ id: "two", name: "Workspace 2", isDefault: false }),
      workspace({ id: "three", name: "Workspace 3", isDefault: false }),
      workspace({ id: "four", name: "Workspace 4", isDefault: false }),
    ],
    selectedWorkspaceId: "default",
    maximumWorkspaces: 4,
    escapeHtml,
  });

  assert.match(html, /data-workspace-create disabled/);
});

test("Workspace details show live coordinates and remove only non-default workspaces", () => {
  const nonDefault = workspace({
    id: "extensions",
    name: "Workspace 2",
    isDefault: false,
    packages: [platform, extensions],
    activePackageKey: packageIdentityKey(extensions),
  });
  const html = renderWorkspaceView({
    workspace: nonDefault,
    escapeHtml,
    packageIdentityKey,
  });
  const defaultHtml = renderWorkspaceView({
    workspace: workspace(),
    escapeHtml,
    packageIdentityKey,
  });

  assert.match(html, /Inspection workspace[\s\S]*Workspace 2/);
  assert.match(html, /Microsoft\.NETCore\.App[\s\S]*Microsoft\.Extensions\.DependencyInjection/);
  assert.match(html, /data-workspace-remove="extensions"/);
  assert.match(html, /aria-label="Close Microsoft\.Extensions\.DependencyInjection 10\.0\.0 net10\.0"/);
  assert.doesNotMatch(html, /Close Microsoft\.NETCore\.App/);
  assert.doesNotMatch(defaultHtml, /data-workspace-remove/);
});

test("Empty workspace remains a visible analysis destination", () => {
  const html = renderWorkspaceView({
    workspace: workspace({
      id: "empty",
      name: "Workspace 2",
      isDefault: false,
      packages: [],
      activePackageKey: null,
    }),
    escapeHtml,
    packageIdentityKey,
  });

  assert.match(html, /Workspace 2[\s\S]*No packages loaded/);
  assert.match(html, /ready for packages/);
  assert.match(html, /No packages are loaded in this workspace/);
});

test("Workspace selection, creation, removal, and package close dispatch separately", () => {
  const listeners = new Map<string, EventListener>();
  const elements = {
    select: {
      dataset: { workspace: "workspace-key" },
      addEventListener: (name: string, listener: EventListener) =>
        listeners.set(`select:${name}`, listener),
    },
    create: {
      dataset: {},
      addEventListener: (name: string, listener: EventListener) =>
        listeners.set(`create:${name}`, listener),
    },
    remove: {
      dataset: { workspaceRemove: "remove-key" },
      addEventListener: (name: string, listener: EventListener) =>
        listeners.set(`remove:${name}`, listener),
    },
    close: {
      dataset: { workspacePackageClose: "package-key" },
      addEventListener: (name: string, listener: EventListener) =>
        listeners.set(`close:${name}`, listener),
    },
  };
  const root = {
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace]"
        ? [elements.select]
        : selector === "[data-workspace-create]"
          ? [elements.create]
          : selector === "[data-workspace-remove]"
            ? [elements.remove]
            : [elements.close],
  };
  const calls: string[] = [];

  bindWorkspaceSubject(fakeDom.parentNode(root), {
    onSelect: key => calls.push(`select:${key}`),
    onCreate: () => calls.push("create"),
    onRemove: key => calls.push(`remove:${key}`),
    onClosePackage: key => calls.push(`close:${key}`),
  });

  listeners.get("select:click")?.(fakeDom.event());
  listeners.get("create:click")?.(fakeDom.event());
  listeners.get("remove:click")?.(fakeDom.event());
  listeners.get("close:click")?.(fakeDom.event());
  assert.deepEqual(calls, [
    "select:workspace-key",
    "create",
    "remove:remove-key",
    "close:package-key",
  ]);
});

test("Workspace focus follows selection", () => {
  let focused = "";
  const root = {
    querySelectorAll: () => [{
      dataset: { workspace: "first" },
      focus: () => {
        focused = "first";
      },
    }, {
      dataset: { workspace: "second" },
      focus: () => {
        focused = "second";
      },
    }],
  };

  assert.equal(focusWorkspace(fakeDom.parentNode(root), "second"), true);
  assert.equal(focused, "second");
  assert.equal(focusWorkspace(fakeDom.parentNode(root), "missing"), false);
});
