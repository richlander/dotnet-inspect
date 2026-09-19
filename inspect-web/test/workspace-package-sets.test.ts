import assert from "node:assert/strict";
import test from "node:test";
import {
  bindWorkspacePackageSetPicker,
  renderWorkspacePackageSetPicker,
  workspacePackageSets,
} from "../src/workspace-package-sets.ts";
import { fakeDom } from "./fake-dom.ts";

const catalog = {
  version: 1,
  packageSets: [{
    id: "package-set.microsoft-extensions",
    title: "Microsoft.Extensions",
    summary: "Microsoft.Extensions packages",
    order: 10,
    sourceKind: "PackageGroup",
    coordinates: Array.from({ length: 44 }, (_, index) => ({
      packageId: `Microsoft.Extensions.Package${index}`,
      version: null,
      framework: null,
      runtimeIdentifier: null,
    })),
  }, {
    id: "package-set.aspire",
    title: "Aspire",
    summary: "Aspire packages",
    order: 20,
    sourceKind: "PackageGroup",
    coordinates: Array.from({ length: 82 }, (_, index) => ({
      packageId: `Aspire.Package${index}`,
      version: null,
      framework: null,
      runtimeIdentifier: null,
    })),
  }],
} as const;

const escapeHtml = (value: unknown) => String(value);

test("Workspace package sets retain exact PackageGroup coordinates", () => {
  const packageSets = workspacePackageSets(catalog);

  assert.equal(packageSets.length, 2);
  const extensions = packageSets[0];
  assert.ok(extensions);
  assert.equal(extensions.sourceKind, "PackageGroup");
  assert.equal(extensions.coordinates.length, 44);
  const last = extensions.coordinates[43];
  assert.ok(last);
  assert.equal(
    last.packageId,
    "Microsoft.Extensions.Package43");
});

test("Workspace package-set picker enables complete fits and rejects overflow", () => {
  const html = renderWorkspacePackageSetPicker({
    open: true,
    loading: false,
    error: "",
    availableSlots: 64,
    packageSets: workspacePackageSets(catalog),
  }, escapeHtml);

  assert.match(
    html,
    /data-workspace-add-package-set="package-set\.microsoft-extensions">Add/);
  assert.match(
    html,
    /data-workspace-add-package-set="package-set\.aspire" disabled/);
  assert.match(html, /82 packages · requires 82 available slots; 64 remain/);
  assert.match(html, /Membership is never truncated/);
});

test("Workspace package-set catalog rejects non-PackageGroup declarations", () => {
  assert.throws(
    () => workspacePackageSets({
      ...catalog,
      packageSets: [{
        ...catalog.packageSets[0],
        sourceKind: "PackagePrefix",
      }],
    }),
    /invalid descriptor/);
});

test("Workspace package-set picker dispatches close and typed selection", () => {
  const listeners = new Map<string, EventListener>();
  const close = {
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`close:${name}`, listener),
  };
  const add = {
    dataset: { workspaceAddPackageSet: "package-set.microsoft-extensions" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`add:${name}`, listener),
  };
  const root = {
    querySelector: (selector: string) =>
      selector === "[data-workspace-package-set-close]" ? close : null,
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-add-package-set]" ? [add] : [],
  };
  const calls: string[] = [];

  bindWorkspacePackageSetPicker(fakeDom.parentNode(root), {
    onClose: () => calls.push("close"),
    onAdd: id => calls.push(id),
  });
  listeners.get("close:click")?.(fakeDom.event());
  listeners.get("add:click")?.(fakeDom.event());

  assert.deepEqual(calls, [
    "close",
    "package-set.microsoft-extensions",
  ]);
});
