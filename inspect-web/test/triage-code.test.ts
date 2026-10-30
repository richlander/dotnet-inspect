import assert from "node:assert/strict";
import test from "node:test";
import { bindTriageCode, renderTriageCode, triageIssueLine, triageIssuePreview } from "../src/triage-code.ts";
import Prism from "prismjs";
import "prismjs/components/prism-clike.js";
import "prismjs/components/prism-csharp.js";

class Preview {
  hidden = true;
  isConnected = true;
  dataset = { triageAssembly: "Fixture.dll", triageType: "Fixture.Private", triageMember: "Read", triageSelector: "triage", triageToken: "100663297", triageOffsets: "7", triageBound: "" };
  code = { innerHTML: "" };
  querySelector() { return this.code; }
}
// oxlint-disable-next-line typescript/no-unsafe-type-assertion -- Minimal DOM stand-in for interaction tests.
const root = (...previews: Preview[]) => ({ querySelectorAll: () => previews }) as unknown as ParentNode;
const highlight = (text: string) => Prism.highlight(text, Prism.languages.csharp!, "csharp");
const settle = async () => { await Promise.resolve(); await Promise.resolve(); };

test("an attributed code line appears automatically with escaped Prism syntax", async () => {
  const preview = new Preview();
  bindTriageCode(root(preview), async target => {
    assert.deepEqual(target, { assembly: "Fixture.dll", typeId: "Fixture.Private", memberName: "Read", selector: "triage", methodToken: 0x06000001, issueOffsets: [7] });
    return { code: '    string value = "<inert>";', carets: ["//     ^^^^^"] };
  }, highlight);
  await settle();
  assert.equal(preview.hidden, false);
  assert.match(preview.code.innerHTML, /token keyword/);
  assert.match(preview.code.innerHTML, /&lt;inert>/);
  assert.doesNotMatch(preview.code.innerHTML, /<inert>/);
});

test("an obsolete line cannot publish a late result", async () => {
  const preview = new Preview();
  let complete!: (value: { code: string; carets: string[] } | null) => void;
  bindTriageCode(root(preview), () => new Promise(resolve => { complete = resolve; }), highlight);
  preview.isConnected = false;
  complete({ code: "obsolete source", carets: [] });
  await settle();
  assert.equal(preview.hidden, true);
  assert.equal(preview.code.innerHTML, "");
});

test("unattributed and multi-line results show no code", async () => {
  for (const value of [null, "one\ntwo", "one\rtwo"]) {
    const preview = new Preview();
    bindTriageCode(root(preview), async () => value === null ? null : ({ code: value, carets: [] }), highlight);
    await settle();
    assert.equal(preview.hidden, true);
    assert.equal(preview.code.innerHTML, "");
  }
});

test("automatic code acquisition permits at most two concurrent requests", async () => {
  const previews = [new Preview(), new Preview(), new Preview()];
  const completions: ((value: { code: string; carets: string[] } | null) => void)[] = [];
  bindTriageCode(root(...previews), () => new Promise(resolve => completions.push(resolve)), highlight);
  assert.equal(completions.length, 2);
  completions[0]!(null);
  await settle();
  assert.equal(completions.length, 3);
  completions[1]!(null);
  completions[2]!(null);
  await settle();
});

test("triage line slots have no disclosure control and require issue coordinates", () => {
  const target = { assembly: '"<Fixture>', typeId: "Private", memberName: "Read", selector: "triage", methodToken: 1, issueOffsets: [7] };
  const html = renderTriageCode(target, value => value.replaceAll('"', "&quot;").replaceAll("<", "&lt;"));
  assert.match(html, /data-triage-assembly="&quot;&lt;Fixture>"/);
  assert.doesNotMatch(html, /details|summary|data-(?:type|member|selector|assembly|token)=/);
  assert.equal(renderTriageCode({ ...target, issueOffsets: null }, String), "");
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


test("focus extents remain aligned when source indentation is removed", () => {
  const preview = triageIssuePreview(mappedDocument, [3, 4]);
  assert.ok(preview);
  assert.equal(preview.code, "    object boxed = 42;");
  assert.deepEqual(preview.focus, [{ column: 4, length: 18 }]);
});

test("different nearest spans on the same line cannot fabricate a precise caret", () => {
  const text = "    First(); Second();";
  const document = { text, nodes: [
    { id: 0, kind: "Call", medium: "CSharp", spans: [{ start: 4, length: 7 }], provenance: { il_offsets: [7] } },
    { id: 1, kind: "Call", medium: "CSharp", spans: [{ start: 13, length: 7 }], provenance: { il_offsets: [7] } },
  ], regions: [], facts: [], targets: [] };
  assert.equal(triageIssuePreview(document, [7]), null);
});
