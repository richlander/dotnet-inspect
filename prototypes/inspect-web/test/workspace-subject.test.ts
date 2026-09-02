import assert from "node:assert/strict";
import test from "node:test";
import {
  bindWorkspaceSubject,
  focusWorkspacePacket,
  renderWorkspacePacketView,
  renderWorkspaceSubject,
} from "../src/workspace-subject.ts";
import type { BrowserHomeDemoResolved } from "../src/inspect-web-engine.d.ts";
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

const packet: BrowserHomeDemoResolved = {
  id: "stj-serialize-callgraph",
  title: "Serialize call graph",
  summary: "Dense package-local STJ graph",
  workspaceMembers: [{
    kind: "package",
    id: "System.Text.Json",
    version: "10.0.0",
    framework: "net10.0",
    assembly: null,
  }],
  tabs: [{
    id: "stj",
    member: {
      kind: "package",
      id: "System.Text.Json",
      version: "10.0.0",
      framework: "net10.0",
      assembly: null,
    },
  }],
  focusTabIndex: 0,
  view: {
    library: null,
    type: "System.Text.Json.JsonSerializer",
    memberAnchor: "1dc14dd1fb",
    memberKey: "method:Serialize",
    section: "Call Graph",
  },
};

test("Workspace lists independently selectable packets", () => {
  const sibling = {
    ...packet,
    id: "stj-getdecimal-callgraph",
    title: "JsonElement.GetDecimal",
    summary: "STJ number parse path",
    view: {
      ...packet.view,
      type: "System.Text.Json.JsonElement",
      memberKey: "method:GetDecimal",
    },
  };

  const html = renderWorkspaceSubject({
    packets: [packet, sibling],
    selectedPacketId: packet.id,
    escapeHtml,
  });

  assert.match(html, /WORKSPACE PACKETS[\s\S]*2/);
  assert.match(html, /workspace-packet active/);
  assert.match(html, /Serialize call graph[\s\S]*Dense package-local STJ graph/);
  assert.match(html, /JsonElement\.GetDecimal[\s\S]*STJ number parse path/);
  assert.doesNotMatch(html, /data-workspace-open/);
});

test("Workspace packet details distinguish packet data from loaded coordinates", () => {
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

  const html = renderWorkspacePacketView({
    packet,
    packages: [platform, active],
    activePackage: active,
    escapeHtml,
    packageIdentityKey,
  });

  assert.match(html, /Workspace packet[\s\S]*Serialize call graph/);
  assert.match(html, /Packet workspace[\s\S]*System\.Text\.Json/);
  assert.match(html, /Initial view[\s\S]*Call Graph[\s\S]*method:Serialize/);
  assert.match(html, /Loaded workspace[\s\S]*Microsoft\.NETCore\.App/);
  assert.match(html, /data-workspace-open="stj-serialize-callgraph"/);
  assert.match(html, />Open workspace</);
});

test("Workspace selection, explicit Open, and Close dispatch separate identities", () => {
  const listeners = new Map<string, EventListener>();
  const select = {
    dataset: { workspacePacket: "packet-key" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`select:${name}`, listener),
  };
  const open = {
    dataset: { workspaceOpen: "open-key" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`open:${name}`, listener),
  };
  const close = {
    dataset: { workspaceClose: "close-key" },
    addEventListener: (name: string, listener: EventListener) =>
      listeners.set(`close:${name}`, listener),
  };
  const root = {
    querySelectorAll: (selector: string) =>
      selector === "[data-workspace-packet]"
        ? [select]
        : selector === "[data-workspace-open]"
          ? [open]
          : [close],
  };
  const calls: string[] = [];

  bindWorkspaceSubject(
    fakeDom.parentNode(root),
    {
      onSelect: key => calls.push(`select:${key}`),
      onOpen: key => calls.push(`open:${key}`),
      onClose: key => calls.push(`close:${key}`),
    });

  listeners.get("select:click")?.(fakeDom.event());
  listeners.get("open:click")?.(fakeDom.event());
  listeners.get("close:click")?.(fakeDom.event());
  assert.deepEqual(calls, [
    "select:packet-key",
    "open:open-key",
    "close:close-key",
  ]);
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

  const html = renderWorkspacePacketView({
    packet: null,
    packages,
    activePackage: packages[0] ?? null,
    escapeHtml,
    packageIdentityKey,
  });

  assert.match(html, /aria-label="Close Example\.Package 1\.0\.0 net8\.0"/);
  assert.match(html, /aria-label="Close Example\.Package 2\.0\.0 net10\.0"/);
});

test("Workspace packet focus survives observational selection", () => {
  let focused = "";
  const root = {
    querySelectorAll: () => [{
      dataset: { workspacePacket: "first" },
      focus: () => {
        focused = "first";
      },
    }, {
      dataset: { workspacePacket: "second" },
      focus: () => {
        focused = "second";
      },
    }],
  };

  assert.equal(
    focusWorkspacePacket(fakeDom.parentNode(root), "second"),
    true);
  assert.equal(focused, "second");
  assert.equal(
    focusWorkspacePacket(fakeDom.parentNode(root), "missing"),
    false);
});
