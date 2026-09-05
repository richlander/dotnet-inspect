import assert from "node:assert/strict";
import test from "node:test";
import { renderMemberFacts } from "../src/member-facts.ts";
import type { MemberFacts } from "../src/member-detail-inspection.ts";
import { memberFactsFixture } from "./member-facts-fixture.ts";

function render(facts: MemberFacts = memberFactsFixture()) {
  return renderMemberFacts({
    memberFacts: facts,
    memberFactsLoading: false,
    memberFactsError: "",
  });
}

function summaryRow(html: string, label: string) {
  const row = html.split(`<dt>${label}</dt>`)[1]?.split("</dd>")[0];
  assert.ok(row, `Summary row ${label} is missing.`);
  return row;
}

test("member Facts summary preserves all signals without repeating subject identity", () => {
  const html = render();
  assert.match(html, /<h2 id="facts-summary-title">Analysis summary<\/h2>/);
  for (const [label, value] of [
    ["Allocations", "1"], ["Calls", "3"], ["Copies", "0"],
    ["Reflection calls", "0"], ["Throws / catches / finally", "1 / 0 / 0"],
    ["Unsafe", "no"], ["Allocates in loop", "no"],
  ] as const) {
    assert.ok(summaryRow(html, label).includes(`>${value}</code>`));
  }
  assert.doesNotMatch(html, /<dt>Overload|<dt>Kind|<dt>Declaring type/);
  assert.match(html, /<\/dl>\s*<p class="facts-metadata-identity"><span>Metadata token<\/span><code>0x06000125<\/code>/);
});

test("member Facts evidence retains heap filtering, deduplication, and the full tooltip", () => {
  const facts = memberFactsFixture();
  const allocation = facts.allocations[0]!;
  const html = render({
    ...facts,
    signals: { ...facts.signals, unsafe: true, allocatesInLoop: true },
    allocations: [
      { ...allocation, inLoop: true },
      { ...allocation, offset: "IL_0030", countedAsHeap: false, inLoop: true },
    ],
    calls: [...facts.calls, facts.calls[0]!],
    safety: [
      { kind: "declaration", offset: null, operation: "unsafe", requirement: "", evidence: "" },
      { kind: "instruction", offset: "IL_0040", operation: "localloc", requirement: "", evidence: "" },
    ],
  });
  assert.match(summaryRow(html, "Allocations"), /IL_0020/);
  assert.doesNotMatch(summaryRow(html, "Allocations"), /IL_0030/);
  assert.match(summaryRow(html, "Calls"), />4<\/code>/);
  assert.match(summaryRow(html, "Calls"), /title="IL_0008, IL_0014, IL_0020">IL_0008, IL_0014 \+1<\/span>/);
  assert.match(summaryRow(html, "Unsafe"), />yes<\/code>.*IL_0040/);
  assert.match(summaryRow(html, "Allocates in loop"), />yes<\/code>.*IL_0020/);
  assert.doesNotMatch(summaryRow(html, "Allocates in loop"), /IL_0030/);
  assert.match(html, /<code>IL_0030<\/code>/);
});

test("member Facts keeps explicit zero results distinct from loading and failure", () => {
  const html = render(memberFactsFixture("zero"));
  for (const label of ["Allocations", "Calls", "Copies", "Reflection calls"]) {
    assert.match(summaryRow(html, label), />0<\/code>/);
  }
  assert.match(summaryRow(html, "Throws / catches / finally"), />0 \/ 0 \/ 0<\/code>/);
  assert.match(summaryRow(html, "Unsafe"), />no<\/code>/);
  assert.doesNotMatch(html, /class="fact-evidence"/);
  assert.match(html, /No direct call sites were found/);

  const loading = renderMemberFacts({
    memberFacts: memberFactsFixture(),
    memberFactsLoading: true,
    memberFactsError: "",
  });
  assert.match(loading, /Analyzing method/);
  assert.doesNotMatch(loading, /facts-summary|Metadata token/);

  const failure = renderMemberFacts({
    memberFacts: null,
    memberFactsLoading: false,
    memberFactsError: "Could not decode <method>.",
  });
  assert.match(failure, /Facts query failed/);
  assert.match(failure, /Could not decode &lt;method&gt;\./);
  assert.doesNotMatch(failure, /facts-summary|No direct call sites/);
  assert.match(renderMemberFacts({
    memberFacts: null,
    memberFactsLoading: false,
    memberFactsError: "",
  }), /No facts result was returned/);
});

test("member Facts escapes summary evidence and all relocated detail sections", () => {
  const facts = memberFactsFixture();
  const html = render({
    ...facts,
    calls: [{ ...facts.calls[0]!, offset: "<offset>\"", callee: "<callee>" }],
    allocations: [{ ...facts.allocations[0]!, type: "<type>" }],
    safety: [{ kind: "<kind>", offset: null, operation: "<operation>", requirement: "<requirement>", evidence: "<evidence>" }],
    exceptionRegions: [{ region: 1, clause: "<clause>", tryRange: "<try>", handlerRange: "<handler>", filterRange: "<filter>", caughtType: "<caught>" }],
    performanceOpportunities: [{ shape: "<shape>", evidence: "<evidence>", fix: "<fix>", confidence: "<confidence>", offset: "<offset>", inLoop: false, caveat: "<caveat>", finding: "<finding>", provenance: "<provenance>" }],
    diagnostics: ["<diagnostic>"],
  });
  for (const value of [
    "offset", "callee", "type", "kind", "operation", "requirement", "evidence",
    "clause", "try", "handler", "filter", "caught", "shape", "fix",
    "confidence", "caveat", "finding", "provenance", "diagnostic",
  ]) {
    assert.ok(html.includes(`&lt;${value}&gt;`));
    assert.ok(!html.includes(`<${value}>`));
  }
  assert.match(html, /title="&lt;offset&gt;&quot;"/);
  assert.match(html, /Analysis diagnostics/);
});
