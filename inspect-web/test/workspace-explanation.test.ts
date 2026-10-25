import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  bindWorkspaceExplanation,
  createWorkspaceExplanation,
  renderWorkspaceExplanation,
} from "../src/workspace-explanation.ts";
import type { BrowserResourceExplanationResult } from "../src/facades/inspect-web-catalog.d.ts";
import { fakeDom } from "./fake-dom.ts";

const declaration = readFileSync(
  new URL("../../docs/examples/workspace-package-component.json", import.meta.url), "utf8");
const content: unknown = JSON.parse(readFileSync(
  new URL("../../docs/examples/explain-package-declaration_hal.json", import.meta.url), "utf8"));
const answer: BrowserResourceExplanationResult = {
  outcome: "Explained", error: null,
  explanation: { content, share: { kind: "nonProjectable", path: "declaration/share",
    reason: "Unavailable" }, diagnostics: [] },
};
const component = { name: "System.Text.Json",
  path: "packages/system.text.json@10.0.0/net10.0/~", declaration };

function fixture() {
  let context = 0;
  const calls: unknown[][] = [];
  const catalog = {
    explainResources: (path: string, projection: string) => {
      calls.push(["resource", path, projection]);
      return Promise.resolve(answer);
    },
    describeWorkspaceComponents: (packet: string) => {
      calls.push(["inventory", packet]);
      return Promise.resolve({ outcome: "Described", components: [component], error: null });
    },
    explainWorkspaceComponent: (json: string, parent: string | null, projection: string) => {
      calls.push(["component", json, parent, projection]);
      return Promise.resolve(answer);
    },
  };
  const controller = createWorkspaceExplanation({
    catalog,
    capturePacket: () => { calls.push(["capture"]); return Promise.resolve("owner-packet"); },
    contextKey: () => context,
    changed: () => undefined,
  });
  return { catalog, controller, calls, changeContext: () => { context++; } };
}

test("Workspace explanation has no startup reads or automatic link traversal", async () => {
  const { controller, calls } = fixture();
  assert.equal(controller.view().open, false);
  assert.deepEqual(calls, []);
  await controller.explore();
  assert.deepEqual(calls, [["resource", "root", "hal"]]);
  assert.equal(controller.view().result, answer);
  const html = renderWorkspaceExplanation(controller.view(), true, escapeHtml);
  assert.match(html, /System.Text.Json/);
  assert.match(html, /Unavailable/);
  assert.match(html, /data-explanation-resource="inspect-resource:\/workspace\/components\/package"/);
  assert.deepEqual(calls, [["resource", "root", "hal"]]);
});

test("root navigation uses the names carried by embedded resources", async () => {
  const { controller, catalog } = fixture();
  const root: unknown = JSON.parse(readFileSync(
    new URL("../../docs/examples/explain-root_hal.json", import.meta.url), "utf8"));
  catalog.explainResources = () => Promise.resolve({
    ...answer, explanation: { ...answer.explanation!, content: root },
  });
  await controller.explore();
  const html = renderWorkspaceExplanation(controller.view(), true, escapeHtml);
  assert.match(html, /data-explanation-resource="inspect-resource:\/references">References<\/button>/);
});

test("declarations pass owner JSON unchanged with only the explicitly requested parent", async () => {
  const { controller, calls } = fixture();
  await controller.declarations();
  assert.deepEqual(calls, [["capture"], ["inventory", "owner-packet"]]);
  await controller.explain(component.path, false);
  await controller.explain(component.path, true);
  assert.deepEqual(calls.slice(2), [
    ["component", declaration, null, "hal"],
    ["component", declaration, "owner-packet", "hal"],
  ]);
});

test("an older Workspace result cannot replace the current reading", async () => {
  const { controller, catalog, changeContext } = fixture();
  const pending = deferred<BrowserResourceExplanationResult>();
  catalog.explainResources = () => pending.promise;
  const request = controller.explore();
  changeContext();
  assert.equal(controller.view().open, false);
  pending.resolve(answer);
  await request;
  assert.equal(controller.view().result, null);
  assert.equal(controller.view().loading, false);
});

test("a newer explicit read supersedes an older read and closing discards pending output", async () => {
  const { controller, catalog } = fixture();
  const pending = deferred<BrowserResourceExplanationResult>();
  catalog.explainResources = () => pending.promise;
  const old = controller.explore("root");
  catalog.explainResources = () => Promise.resolve({ ...answer, outcome: "Latest" });
  await controller.explore("component-contracts");
  pending.resolve(answer);
  await old;
  assert.equal(controller.view().result?.outcome, "Latest");
  const next = deferred<BrowserResourceExplanationResult>();
  catalog.explainResources = () => next.promise;
  const closed = controller.explore();
  controller.close();
  next.resolve(answer);
  await closed;
  assert.equal(controller.view().open, false);
  assert.equal(controller.view().result, null);
});

test("packet and transport failures remain visible without activation or fallback", async () => {
  const { controller, catalog, calls } = fixture();
  catalog.describeWorkspaceComponents = () => Promise.reject(new Error("Packet unavailable"));
  await controller.declarations();
  assert.equal(controller.view().error, "Packet unavailable");
  assert.deepEqual(calls, [["capture"]]);
  const html = renderWorkspaceExplanation(controller.view(), true, escapeHtml);
  assert.match(html, /role="alert"/);
  assert.match(html, /Packet unavailable/);
});

test("owner refusals and diagnostics are escaped and the full envelope stays readable", async () => {
  const { controller, catalog } = fixture();
  catalog.explainResources = () => Promise.resolve({
    outcome: "UnsupportedContract", explanation: null, error: "Unknown <kind>",
  });
  await controller.explore();
  let html = renderWorkspaceExplanation(controller.view(), true, escapeHtml);
  assert.match(html, /UnsupportedContract: Unknown &lt;kind&gt;/);
  assert.doesNotMatch(html, /<kind>/);
  const explanation = answer.explanation!;
  catalog.explainResources = () => Promise.resolve({
    ...answer, explanation: { ...explanation, diagnostics: [{
      code: "bounded", severity: "Warning", summary: "Partial <context>", correspondence: null,
    }] },
  });
  await controller.explore();
  html = renderWorkspaceExplanation(controller.view(), true, escapeHtml);
  assert.match(html, /Warning: Partial &lt;context&gt;/);
  assert.match(html, /nonProjectable/);
  assert.match(html, /<summary>JSON<\/summary>/);
});

test("a component path absent from this captured inventory is refused locally", async () => {
  const { controller, calls } = fixture();
  await controller.explain(component.path, true);
  assert.equal(controller.view().error, "Choose a declaration from this Workspace.");
  assert.deepEqual(calls, []);
});

test("buttons invoke only their selected reading", async () => {
  const { controller, calls } = fixture();
  const listeners = new Map<string, () => void>();
  const root = fakeDom.parentNode({
    querySelector: (selector: string) => ({
      addEventListener: (_event: string, listener: () => void) => listeners.set(selector, listener),
    }),
    querySelectorAll: () => [],
  });
  bindWorkspaceExplanation(root, controller);
  listeners.get("[data-explanation-root]")!();
  await Promise.resolve();
  assert.deepEqual(calls, [["resource", "root", "hal"]]);
});

function escapeHtml(value: unknown): string {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(complete => { resolve = complete; });
  return { promise, resolve };
}
