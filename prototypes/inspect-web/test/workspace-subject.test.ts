import assert from "node:assert/strict";
import test from "node:test";
import {
  bindWorkspaceSubject,
  renderWorkspaceSubject,
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

test("Workspace lists package coordinates without presenting Platform as a workspace", () => {
  const active: PackageControlPackage = {
    id: "System.Text.Json",
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

  const html = renderWorkspaceSubject({
    packages: [platform, active],
    activePackage: active,
    escapeHtml,
    packageIdentityKey,
  });

  assert.match(html, /WORKSPACE[\s\S]*1 open/);
  assert.match(html, /workspace-coordinate active/);
  assert.match(html, /System\.Text\.Json[\s\S]*10\.0\.0 · net10\.0/);
  assert.doesNotMatch(html, /Microsoft\.NETCore\.App|Platform/);
});

test("Workspace activation and Close dispatch package identity keys", () => {
  const listeners = new Map<string, EventListener>();
  const activate = {
    dataset: { workspacePackage: "activate-key" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`activate:${name}`, listener),
  };
  const close = {
    dataset: { workspaceClose: "close-key" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`close:${name}`, listener),
  };
  const root = {
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-package]" ? [activate] : [close],
  };
  const calls: string[] = [];

  bindWorkspaceSubject(
    fakeDom.parentNode(root),
    {
      onActivate: key => calls.push(`activate:${key}`),
      onClose: key => calls.push(`close:${key}`),
    });

  listeners.get("activate:click")?.(fakeDom.event());
  listeners.get("close:click")?.(fakeDom.event());
  assert.deepEqual(calls, ["activate:activate-key", "close:close-key"]);
});

test("Workspace Close names distinguish matching package ids", () => {
  const packages: PackageControlPackage[] = [
    {
      id: "Example.Package",
      version: "1.0.0",
      activeFramework: "net8.0",
      isRuntimePack: false,
    },
    {
      id: "Example.Package",
      version: "2.0.0",
      activeFramework: "net10.0",
      isRuntimePack: false,
    },
  ];

  const html = renderWorkspaceSubject({
    packages,
    activePackage: packages[0] ?? null,
    escapeHtml,
    packageIdentityKey,
  });

  assert.match(html, /aria-label="Close Example\.Package 1\.0\.0 net8\.0"/);
  assert.match(html, /aria-label="Close Example\.Package 2\.0\.0 net10\.0"/);
});
