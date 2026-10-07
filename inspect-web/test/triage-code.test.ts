import assert from "node:assert/strict";
import test from "node:test";
import { inertStringFixture } from "./inert-string-fixture.ts";
import { bindTriageCode, renderTriageCode, triageIssueLine } from "../src/triage-code.ts";
import type { BrowserMemberSourceResult } from "../src/facades/inspect-web-source.d.ts";

class Preview extends EventTarget {
  open = false;
  isConnected = true;
  dataset = { triageAssembly: "Fixture.dll", triageType: "Fixture.Private", triageMember: "Read", triageSelector: "triage", triageToken: "100663297" };
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
  assert.match(html, /data-triage-assembly="&quot;&lt;Fixture>"/);
  assert.match(html, /<summary>Code<\/summary>/);
  assert.doesNotMatch(html, /data-(?:type|member|selector|assembly|token)=/);
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


const mappedDocument = {
  text: "void Run() {\n    object boxed = 42;\n    Send(boxed);\n}",
  nodes: [
    { id: 0, kind: "Statement", medium: "CSharp", spans: [{ start: 17, length: 18 }], provenance: { il_offsets: [3, 4] } },
    { id: 1, kind: "Statement", medium: "CSharp", spans: [{ start: 40, length: 12 }], provenance: { il_offsets: [8] } },
    { id: 2, kind: "Body", medium: "CSharp", spans: [{ start: 11, length: 43 }], provenance: { il_offsets: [3, 4, 8] } },
  ], regions: [], facts: [], targets: [],
};

test("one attributed line replaces the whole method, including multiple offsets on that line", () => {
  assert.equal(triageIssueLine(mappedDocument, [3, 4]), "object boxed = 42;");
});

test("multi-line and unmapped issues retain the code fallback", () => {
  assert.equal(triageIssueLine(mappedDocument, [3, 8]), null);
  assert.equal(triageIssueLine(mappedDocument, [99]), null);
  assert.equal(triageIssueLine(mappedDocument, []), null);
});

test("ambiguous nearest provenance does not pick a line arbitrarily", () => {
  const document = { ...mappedDocument, nodes: [
    ...mappedDocument.nodes,
    { id: 3, kind: "Statement", medium: "CSharp", spans: [{ start: 36, length: 18 }], provenance: { il_offsets: [3] } },
  ] };
  assert.equal(triageIssueLine(document, [3]), null);
});

test("an attributed line is published as inert text", async () => {
  const preview = new Preview();
  bindTriageCode(root(preview), async () => ({ kind: "line", text: "Send(<inert>);" }));
  preview.toggle(true);
  await settle();
  assert.equal(preview.code.textContent, "Send(<inert>);");
});
