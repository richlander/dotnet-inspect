import assert from "node:assert/strict";
import test from "node:test";
import { createOperationAuthorityPage, type OperationProducerAdapter } from "../src/operation-authority.ts";
import { createMemberBodyDiff, memberBodyDestination, renderMemberBodyReader, type MemberBodyDiffContext } from "../src/member-body-diff.ts";
import type { BrowserMemberBodyDiffInventory, BrowserMemberBodyDiffDocument, BrowserMemberBodyDiffResult } from "../src/facades/inspect-web-source.d.ts";
import { fakeDom } from "./fake-dom.ts";

const inventory: BrowserMemberBodyDiffInventory = {
  id: "inventory", isComplete: true, coverage: [], types: [{
    identity: "N.T", display: "N.T", outcome: "Changed", canNavigate: true, hasApiChange: true,
    members: [{ id: "Run~123", display: "N.T.Run()", outcome: "Changed", hasApiChange: true, isAccessor: false, mechanisms: ["CSharp", "IlBody"],
      fingerprint: "123", selector: "Run~123", methodToken: 0x06000001, identityFailure: null, typeIdentity: "N.T" }],
  }], destinations: [{ id: "Run~123", display: "N.T.Run()", outcome: "Changed", hasApiChange: true, isAccessor: false, mechanisms: [],
    fingerprint: "123", selector: "Run~123", methodToken: 0x06000001, identityFailure: null, typeIdentity: "N.T" }],
};
const document: BrowserMemberBodyDiffDocument = {
  subject: "Run~123", beforeOutcome: "Present", afterOutcome: "Present", beforeDetail: null, afterDetail: null,
  media: [{ medium: "CSharp", beforeText: null, afterText: null, limit: null, diff: {
    version: 1, before: { label: "Before", lines: ["old"], finalLineTerminator: "Absent" },
    after: { label: "After", lines: ["new"], finalLineTerminator: "Absent" },
    relations: [{ kind: "Correspondence", beforeCoordinates: [0], afterCoordinates: [0], content: "Changed", placement: "Stable" }],
    statistics: { added: 0, removed: 0, changedBefore: 1, changedAfter: 1, movedBefore: 0, movedAfter: 0 },
    changes: [{ before: { start: 0, count: 1 }, after: { start: 0, count: 1 }, innerMappings: [], annotations: [] }],
  } }],
};
function context(): MemberBodyDiffContext {
  return { packageModel: {}, request: { packageId: "Example", beforeVersion: "1.0.0", afterVersion: "2.0.0",
    framework: "net10.0", compileAssetId: "asset", generation: "generation", inventoryId: null, memberId: null },
  subject: "member", subjectLabel: "Run", targetText: "1 → 2", typeIdentity: "N.T", memberFingerprint: "123",
  methodToken: 0x06000001 };
}
const flush = (): Promise<void> => new Promise(resolve => setImmediate(resolve));
const escape = (value: unknown): string => String(value).replaceAll("<", "&lt;");

test("Member entry automatically acquires the inventory and exact inline document and retains it", async () => {
  let active: MemberBodyDiffContext | null = context();
  const requests: string[] = [];
  const controller = createMemberBodyDiff({
    authority: createOperationAuthorityPage(), document: fakeDom.document({ querySelector: () => null }),
    query: async (_id, request) => {
      requests.push(request.memberId ?? "inventory");
      return { kind: "Available", inventory: request.memberId ? null : inventory,
        document: request.memberId ? document : null, detail: null, inspection: null };
    }, cancel: () => undefined, diagnostic: diagnostic => { throw new Error(JSON.stringify(diagnostic)); },
    render: () => controller.reconcile(active), escapeHtml: escape,
    activateType: () => undefined, activateMember: () => undefined,
  });
  controller.reconcile(active);
  await flush(); await flush();
  assert.deepEqual(requests, ["inventory", "Run~123"]);
  const html = controller.render();
  assert.match(html, /data-source-diff-viewer/);
  assert.match(html, /old/); assert.match(html, /new/);
  assert.doesNotMatch(html, /Show .*diff/);
  const original = active;
  active = null; controller.reconcile(active);
  active = original; controller.reconcile(active); await flush();
  assert.equal(requests.length, 2);
  assert.match(controller.render(), /data-source-diff-viewer/);
  controller.dispose();
});

test("copied Package models retain settled inventory and exact body documents", async () => {
  const original = {};
  const copy = {};
  let active: MemberBodyDiffContext | null = { ...context(), packageModel: original };
  const requests: string[] = [];
  const controller = createMemberBodyDiff({
    authority: createOperationAuthorityPage(), document: fakeDom.document({ querySelector: () => null }),
    query: async (_id, request) => {
      requests.push(request.memberId ?? "inventory");
      return { kind: "Available", inventory: request.memberId ? null : inventory,
        document: request.memberId ? document : null, detail: null, inspection: null };
    }, cancel: () => undefined, diagnostic: diagnostic => { throw new Error(JSON.stringify(diagnostic)); },
    render: () => controller.reconcile(active), escapeHtml: escape,
    activateType: () => undefined, activateMember: () => undefined,
  });
  controller.reconcile(active);
  await flush(); await flush();
  assert.deepEqual(requests, ["inventory", "Run~123"]);

  controller.copyPackages(new Map([[original, copy]]));
  active = { ...active, packageModel: copy };
  controller.reconcile(active);
  await flush();

  assert.deepEqual(requests, ["inventory", "Run~123"]);
  assert.match(controller.render(), /data-source-diff-viewer/);
  controller.dispose();
});

test("a superseded target cannot publish its late completion", async () => {
  const first = context();
  let complete: ((value: BrowserMemberBodyDiffResult) => void) | undefined;
  const controller = createMemberBodyDiff({
    authority: createOperationAuthorityPage(), document: fakeDom.document({ querySelector: () => null }),
    query: () => new Promise(resolve => { complete = resolve; }), cancel: () => undefined,
    diagnostic: () => undefined, render: () => undefined, escapeHtml: escape,
    activateType: () => undefined, activateMember: () => undefined,
  });
  controller.reconcile(first);
  await flush();
  controller.reconcile(null);
  complete!({ kind: "Available", inventory, document: null, detail: null, inspection: null });
  await flush();
  assert.equal(controller.render(), "");
  controller.dispose();
});

test("removed and ambiguous Members have no current destination", () => {
  assert.equal(memberBodyDestination(inventory, { typeIdentity: "N.T", memberFingerprint: null, methodToken: null }), null);
  const ambiguous = { ...inventory, destinations: [...inventory.destinations, inventory.destinations[0]!] };
  assert.equal(memberBodyDestination(ambiguous, context()), null);
  const accessor = {
    ...inventory.destinations[0]!,
    id: "Value~123:1",
    selector: "Value~123:1",
    isAccessor: true,
  };
  const accessorInventory = { ...inventory, destinations: [accessor] };
  assert.equal(memberBodyDestination(accessorInventory, {
    typeIdentity: "N.T", memberFingerprint: "123", methodToken: null,
  }), null);
  assert.equal(memberBodyDestination(accessorInventory, {
    typeIdentity: "N.T", memberFingerprint: "123", methodToken: 0x06000001,
  }), accessor);
});

test("inventory rows show qualified names and category chips without count summaries", async () => {
  const controller = createMemberBodyDiff({
    authority: createOperationAuthorityPage(), document: fakeDom.document({ querySelector: () => null }),
    query: async () => ({ kind: "Available", inventory, document: null, detail: null, inspection: null }),
    cancel: () => undefined, diagnostic: diagnostic => { throw new Error(JSON.stringify(diagnostic)); },
    render: () => undefined, escapeHtml: escape,
    activateType: () => undefined, activateMember: () => undefined,
  });
  controller.reconcile({ ...context(), subject: "type" });
  await flush(); await flush();
  const html = controller.render();
  assert.match(html, /N\.T\.Run\(\)/);
  assert.match(html, /member-body-category">API</);
  assert.match(html, /member-body-category">C#/);
  assert.match(html, /member-body-category">IL</);
  assert.doesNotMatch(html, /\d+ changed Members|exact|unavailable|incomplete|failed/i);
  controller.dispose();
});

test("removed Type rows retain their Before-side Member count and names without navigation", async () => {
  const removed = {
    ...inventory,
    types: [{
      ...inventory.types[0]!,
      identity: "N.Removed",
      display: "N.Removed",
      outcome: "Removed",
      canNavigate: false,
      members: [{
        ...inventory.types[0]!.members[0]!,
        id: "Old~123",
        display: "N.Removed.Old()",
        outcome: "Removed",
        fingerprint: null,
        selector: null,
        methodToken: null,
        identityFailure: "Removed",
        typeIdentity: "N.Removed",
      }],
    }],
    destinations: [],
  };
  const controller = createMemberBodyDiff({
    authority: createOperationAuthorityPage(), document: fakeDom.document({ querySelector: () => null }),
    query: async () => ({ kind: "Available", inventory: removed, document: null, detail: null, inspection: null }),
    cancel: () => undefined, diagnostic: diagnostic => { throw new Error(JSON.stringify(diagnostic)); },
    render: () => undefined, escapeHtml: escape,
    activateType: () => undefined, activateMember: () => undefined,
  });
  controller.reconcile({ ...context(), subject: "library" });
  await flush(); await flush();
  const html = controller.render();
  assert.match(html, /1 Member: N\.Removed\.Old\(\)/);
  assert.match(html, /aria-disabled="true"/);
  assert.doesNotMatch(html, /data-member-body-type="N\.Removed"/);
  controller.dispose();
});

test("reader omits the absence banner and preserves unavailable and size-limit outcomes", () => {
  const reader = { document, medium: "CSharp" as const, mode: "unified" as const };
  assert.doesNotMatch(renderMemberBodyReader({ ...reader, document: { ...document, beforeOutcome: "Absent" } }, escape), /Not present on this side/);
  assert.match(renderMemberBodyReader({ ...reader, document: { ...document, media: [{ medium: "CSharp", beforeText: null, afterText: null, diff: null, limit: "Too complex: Lines" }] } }, escape), /Too complex: Lines/);
  assert.match(renderMemberBodyReader({ ...reader, document: { ...document, beforeOutcome: "Unavailable", beforeDetail: "Exact body unavailable", media: [{ medium: "CSharp", beforeText: null, afterText: null, diff: null, limit: null }] } }, escape), /Exact body unavailable/);
});


test("Member auto-loading defers admission until the current page publication finishes", async () => {
  const authority = createOperationAuthorityPage();
  const requests: string[] = [];
  const controller = createMemberBodyDiff({
    authority, document: fakeDom.document({ querySelector: () => null }),
    query: async (_id, request) => {
      requests.push(request.memberId ?? "inventory");
      return { kind: "Available", inventory: request.memberId ? null : inventory,
        document: request.memberId ? document : null, detail: null, inspection: null };
    }, cancel: () => undefined, diagnostic: diagnostic => { throw new Error(JSON.stringify(diagnostic)); },
    render: () => controller.reconcile(active), escapeHtml: escape,
    activateType: () => undefined, activateMember: () => undefined,
  });
  const active = context();
  const navigation = authority.createSession<string, string, string, never, never>({
    feature: { publish(event) { if (event.kind === "terminal") controller.reconcile(active); return undefined; } },
    diagnostic: { report() { return undefined; } },
  });
  const producer: OperationProducerAdapter<string, string, string, never, never> = {
    prepare(_identity, _input, sink) {
      return { kind: "prepared", binding: {
        activate() { sink.reportTerminal({ kind: "succeeded", value: "Member" }); sink.reportQuiesced(); return undefined; },
        requestCancellation() { return undefined; }, abandon() { return undefined; },
      } };
    },
  };
  navigation.start("navigate", producer);
  await flush(); await flush();
  assert.deepEqual(requests, ["inventory", "Run~123"]);
  assert.match(controller.render(), /data-source-diff-viewer/);
  controller.dispose(); navigation.dispose();
});

test("restored body selector joins the issued destination without using a foreign metadata token", () => {
  const destination = { ...inventory.destinations[0]!, selector: "Run~123:1" };
  const restored = { ...context(), methodToken: 0x06000099, bodySelector: "Run~123:1" };
  assert.equal(memberBodyDestination({ ...inventory, destinations: [destination] }, restored), destination);
  assert.equal(memberBodyDestination({ ...inventory, destinations: [destination] }, { ...restored, bodySelector: "Run~123:2" }), null);
});
