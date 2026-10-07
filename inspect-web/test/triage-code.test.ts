import assert from "node:assert/strict";
import test from "node:test";
import { inertStringFixture } from "./inert-string-fixture.ts";
import { bindTriageCode, renderTriageCode } from "../src/triage-code.ts";
import type { BrowserMemberSourceResult } from "../src/facades/inspect-web-source.d.ts";

class Preview extends EventTarget {
  open = false;
  isConnected = true;
  dataset = { assembly: "Fixture.dll", type: "Fixture.Private", member: "Read", selector: "triage", token: "100663297" };
  code = { textContent: "" };
  querySelector() { return this.code; }
  toggle(open: boolean) { this.open = open; this.dispatchEvent(new Event("toggle")); }
}
// oxlint-disable-next-line typescript/no-unsafe-type-assertion -- Minimal DOM stand-in for the interaction test.
const root = (preview: Preview) => ({ querySelectorAll: () => [preview] }) as unknown as ParentNode;
const success = (text: string): BrowserMemberSourceResult => ({ value: {
  source: { provider: "decompiled", provenance: inertStringFixture("fixture"), url: null, pdbSourceLimitation: null, text },
  parts: [], diagnostics: [],
}, error: null, diagnostics: [] });
const settle = async () => { await Promise.resolve(); await Promise.resolve(); };

test("triage decompilation is explicit and targets a private implementation method", async () => {
  const preview = new Preview();
  let calls = 0;
  bindTriageCode(root(preview), async target => {
    calls++;
    assert.deepEqual(target, { assembly: "Fixture.dll", typeId: "Fixture.Private", memberName: "Read", selector: "triage", methodToken: 0x06000001 });
    return success("void Read() { /* <inert> */ }");
  });
  preview.toggle(false);
  assert.equal(calls, 0);
  preview.toggle(true);
  await settle();
  assert.equal(preview.code.textContent, "void Read() { /* <inert> */ }");
  preview.toggle(false);
  preview.toggle(true);
  assert.equal(calls, 1);
});

test("an obsolete preview cannot publish a late decompilation", async () => {
  const preview = new Preview();
  let complete!: (result: BrowserMemberSourceResult) => void;
  bindTriageCode(root(preview), () => new Promise(resolve => { complete = resolve; }));
  preview.toggle(true);
  preview.isConnected = false;
  complete(success("obsolete source"));
  await settle();
  assert.equal(preview.code.textContent, "Decompiling…");
});

test("source failures stay visible and can be retried by expanding again", async () => {
  const preview = new Preview();
  let calls = 0;
  bindTriageCode(root(preview), async () => ++calls === 1
    ? { value: null, error: "Unavailable", diagnostics: [] } : success("retry source"));
  preview.toggle(true);
  await settle();
  assert.equal(preview.code.textContent, "Unavailable");
  preview.toggle(false);
  preview.toggle(true);
  await settle();
  assert.equal(preview.code.textContent, "retry source");
});

test("triage source controls escape their metadata attributes", () => {
  const html = renderTriageCode({ assembly: '"<Fixture>', typeId: "Private", memberName: "Read", selector: "triage", methodToken: 1 }, value => value.replaceAll('"', "&quot;").replaceAll("<", "&lt;"));
  assert.match(html, /data-assembly="&quot;&lt;Fixture>"/);
  assert.match(html, /Decompiled method/);
});

test("the preview uses owner-issued member spans rather than neighboring declarations", async () => {
  const preview = new Preview();
  bindTriageCode(root(preview), async () => {
    const result = success("prefixRead()suffix");
    return { ...result, value: { ...result.value!, parts: [{ kind: "Member", spans: [
      { start: 6, length: 6, end: 12, startLine: 1, endLine: 1, leadingIndentation: "" },
    ] }] } };
  });
  preview.toggle(true);
  await settle();
  assert.equal(preview.code.textContent, "Read()");
});
