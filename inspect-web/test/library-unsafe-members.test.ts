import assert from "node:assert/strict";
import test from "node:test";
import {
  renderLibraryUnsafeCodeTypesSurface,
  renderMemberUnsafeMembersSurface,
  renderTypeUnsafeMembersSurface,
} from "../src/library-unsafe-members.ts";
import type {
  BrowserUnsafeCodeType,
  BrowserUnsafeCodeTypes,
  BrowserUnsafeMember,
  BrowserUnsafeMemberCounts,
  BrowserUnsafeMembers,
  BrowserUnsafeType,
} from "../src/facades/inspect-web-analysis.d.ts";

const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const census = { fresh: true, loading: false, error: "", escapeHtml };
const library = { ...census, assemblyIdentity: "Fixture", assetPath: "Fixture.dll", coordinate: "Fixture@1.0", pickerHtml: "", requireLibrary: false };
const typeScope = { ...census, context: { relationshipsOffered: true }, typeId: "Fixture.Sample", typeName: "Sample" };
const memberScope = { ...census, source: "type" as const, memberName: "Read", typeId: "Fixture.Sample", stableSelector: "Read~abc" };
const member: BrowserUnsafeMember = {
  member: "Fixture.Sample.Read(Int32)",
  metadataToken: 0x06000001,
  exposure: "Non-public",
  explicitContract: false,
  safeBoundary: "safe boundary",
  evidence: "pointer dereference ×2; explicit-contract call: System.Runtime.CompilerServices.Unsafe::Add",
  partial: false,
  typeId: "Fixture.Sample",
  stableSelector: "Read~abc",
  accessibility: "private",
  declaringType: null,
  items: [
    { role: "pointer dereference", body: "Fixture.Sample.<Read>b__0_0()", ilOffset: 7, detail: "ldind.i4" },
    { role: "explicit-contract call", body: "Fixture.Sample.Read(Int32)", ilOffset: 18, detail: "System.Runtime.CompilerServices.Unsafe::Add" },
  ],
};
const counts = (overrides: Partial<BrowserUnsafeMemberCounts> = {}): BrowserUnsafeMemberCounts => ({
  members: 1, public: 0, propagating: 0, safeBoundaries: 1, safeBoundaryUndecided: 0, ...overrides,
});
const group = (overrides: Partial<BrowserUnsafeType> = {}): BrowserUnsafeType => ({
  type: "Fixture.Sample", typeId: "Fixture.Sample", declaringType: null, counts: counts(), members: [member], ...overrides,
});
const result = (overrides: Partial<BrowserUnsafeMembers> = {}): BrowserUnsafeMembers => ({
  outcome: "available", assembly: "Fixture.dll", counts: counts(), types: [group()], limitations: [], inspectionError: null, ...overrides,
});
const codeType = (overrides: Partial<BrowserUnsafeCodeType> = {}): BrowserUnsafeCodeType => ({
  type: "Fixture.Sample", state: "unsafe", typeId: "Fixture.Sample", declaringType: null, ...overrides,
});
const codeTypes = (overrides: Partial<BrowserUnsafeCodeTypes> = {}): BrowserUnsafeCodeTypes => ({
  outcome: "available", assembly: "Fixture.dll", types: [codeType()], limitations: [], inspectionError: null, ...overrides,
});
const scopes = [
  (data: BrowserUnsafeMembers | null, overrides: Partial<typeof census> = {}) => renderTypeUnsafeMembersSurface({ ...typeScope, data, ...overrides }),
  (data: BrowserUnsafeMembers | null, overrides: Partial<typeof census> = {}) => renderMemberUnsafeMembersSurface({ ...memberScope, data, ...overrides }),
];
const renderLibrary = (data: BrowserUnsafeCodeTypes | null, overrides: Partial<typeof census> = {}) =>
  renderLibraryUnsafeCodeTypesSurface({ ...library, data, ...overrides });

test("Library scope lists one navigable row per Type, with no counts", () => {
  const html = renderLibrary(codeTypes({ types: [codeType(), codeType({ type: "Fixture.Other", typeId: "Fixture.Other" })] }));
  assert.match(html, /data-analysis-mode="unsafe" aria-selected="true"/);
  assert.match(html, /2 types with unsafe code</);
  assert.equal(html.match(/class="triage-item unsafe-type"/g)?.length, 2);
  assert.ok(html.indexOf("Fixture.Sample") < html.indexOf("Fixture.Other"), "Types keep the answer's order");
  assert.match(html, /data-unsafe-type-row="Fixture.Other" data-unsafe-assembly="Fixture.dll"/);
  // Library scope lists Types: no counts, members, evidence, or state column.
  assert.doesNotMatch(html, /unsafe-counts|perf-count|public<|propagat|safe boundar|unsafe-type-state/);
  assert.doesNotMatch(html, /data-unsafe-selector=|unsafe-evidence-table/);
});

test("the Library state shows only when some Type is unknown", () => {
  const html = renderLibrary(codeTypes({
    outcome: "incomplete",
    limitations: ["BodyAnalysisFailed in Fixture.Other.Run()"],
    types: [codeType(), codeType({ type: "Fixture.Other", typeId: "Fixture.Other", state: "unknown" })],
  }));
  assert.match(html, /unsafe-type-state">unsafe</);
  assert.match(html, /unsafe-type-state">unknown</);
  assert.match(html, /2 types with unsafe code · incomplete/);
  assert.match(html, /The unsafe code search is incomplete/);
});

test("a Type the API surface cannot resolve renders without navigation", () => {
  const html = renderLibrary(codeTypes({ types: [codeType({ typeId: null })] }));
  assert.match(html, /Fixture.Sample/);
  assert.doesNotMatch(html, /data-unsafe-type-row=/);
  assert.match(html, /<div class="perf-row unsafe-member-static"/);
});

test("Library outcomes remain distinct and an incomplete empty answer never claims absence", () => {
  for (const outcome of ["failed", "rejected", "unavailable"]) {
    const html = renderLibrary(codeTypes({ outcome, types: [], inspectionError: "<failure>" }));
    assert.match(html, /&lt;failure&gt;/);
    assert.match(html, outcome === "unavailable" ? /Unsafe code unavailable/ : /Unsafe code failed/);
  }
  assert.match(renderLibrary(codeTypes({ types: [] })), /No unsafe code/);
  const partial = renderLibrary(codeTypes({ outcome: "incomplete", types: [], limitations: ["ReferenceAssembly"] }));
  assert.match(partial, /absence of unsafe code cannot be established/);
  assert.doesNotMatch(partial, /No unsafe members under the updated memory-safety rules/);
  assert.match(renderLibrary(codeTypes(), { fresh: false }), /Finding unsafe code/);
  assert.match(renderLibrary(codeTypes(), { loading: true }), /Finding unsafe code/);
  assert.match(renderLibrary(null, { error: "<transport>" }), /&lt;transport&gt;/);
  assert.match(renderLibraryUnsafeCodeTypesSurface({ ...library, requireLibrary: true, data: null }), /Pick a library/);
  assert.doesNotMatch(renderLibrary(codeTypes()), /severity|risk|recommend/i);
});

test("Type scope selects its Type group and shows each member's dimensions", () => {
  const other = { ...member, member: "Fixture.Other.Run()", typeId: "Fixture.Other", stableSelector: "Run~def" };
  const propagator = { ...member, member: "Fixture.Sample.Add()", stableSelector: "Add~ghi", exposure: "Public", explicitContract: true, safeBoundary: "not a safe boundary", partial: true };
  const data = result({ types: [group({ counts: counts({ members: 2, public: 1, propagating: 1 }), members: [member, propagator] }), group({ type: "Fixture.Other", typeId: "Fixture.Other", members: [other] })] });
  const html = renderTypeUnsafeMembersSurface({ ...typeScope, data });
  assert.match(html, /<h1 id="type-analysis-title">Analysis<\/h1>/);
  assert.match(html, /<p title="Sample · 2 unsafe members">/);
  assert.match(html, /<dt>2<\/dt><dd>members<\/dd>/);
  assert.equal(html.match(/class="triage-item unsafe-member"/g)?.length, 2);
  assert.doesNotMatch(html, /Fixture.Other.Run/);
  assert.ok(html.indexOf("Fixture.Sample.Read") < html.indexOf("Fixture.Sample.Add"), "members keep the census order");
  assert.match(html, /data-unsafe-selector="Read~abc" data-unsafe-assembly="Fixture.dll" data-unsafe-type="Fixture.Sample" data-unsafe-accessibility="private"/);
  assert.match(html, /<span class="perf-shape">pointer dereference ×2<\/span><span class="perf-shape">explicit-contract call<\/span>/);
  assert.match(html, /class="unsafe-member-callees">System.Runtime.CompilerServices.Unsafe::Add</);
  assert.match(html, /class="perf-count"[^>]*>2</);
  assert.match(html, /unsafe-member-exposure">Non-public</);
  assert.match(html, /unsafe-member-boundary">safe boundary</);
  assert.match(html, /unsafe-member-badge">propagates</);
  assert.match(html, /unsafe-member-boundary">not a safe boundary</);
  assert.match(html, /unsafe-member-partial">partial</);
  assert.equal(html.match(/unsafe-member-badge">propagates</g)?.length, 1);
  assert.doesNotMatch(html, /unsafe-evidence-table/);
});

test("a legacy Type presents safe boundaries as not applicable, never zero", () => {
  const legacy = counts({ safeBoundaries: 0, safeBoundaryUndecided: 1 });
  const html = renderTypeUnsafeMembersSurface({ ...typeScope, data: result({ counts: legacy, types: [group({ counts: legacy })] }) });
  assert.match(html, /<dd>legacy rules<\/dd>/);
  assert.doesNotMatch(html, /0 safe boundar/);
});

test("a member the API surface cannot resolve renders without navigation", () => {
  const html = renderTypeUnsafeMembersSurface({ ...typeScope, data: result({ types: [group({ members: [{ ...member, stableSelector: null }] })] }) });
  assert.match(html, /Fixture.Sample.Read\(Int32\)/);
  assert.doesNotMatch(html, /data-unsafe-selector=/);
  assert.match(html, /<div class="perf-row unsafe-member-static"/);
});

test("a Type absent from a complete census says it has no unsafe members", () => {
  const html = renderTypeUnsafeMembersSurface({ ...typeScope, typeId: "Fixture.Clean", data: result() });
  assert.match(html, /No unsafe members in this type/);
  assert.match(html, /0 unsafe members/);
  assert.doesNotMatch(html, /Fixture.Sample.Read/);
});

test("Type and Member scopes are the Unsafe mode of their own Analysis inspector", () => {
  // A package Type's Analysis also offers Relationships, so Unsafe is a tab.
  const packageType = renderTypeUnsafeMembersSurface({ ...typeScope, data: result() });
  assert.match(packageType, /data-analysis-inspector="type"/);
  assert.equal(packageType.match(/role="tab"/g)?.length, 2);
  assert.match(packageType, />Relationships<\/button>/);
  assert.match(packageType, /id="analysis-mode-unsafe" data-analysis-scope="type" data-analysis-mode="unsafe" aria-selected="true"/);
  assert.match(packageType, /role="tabpanel" aria-labelledby="analysis-mode-unsafe"/);
  // A Platform Type and a member have only Unsafe, so no tab strip.
  for (const [scope, html] of [
    ["type", renderTypeUnsafeMembersSurface({ ...typeScope, context: { relationshipsOffered: false }, data: result() })],
    ["member", renderMemberUnsafeMembersSurface({ ...memberScope, data: result() })],
  ] as const) {
    assert.match(html, new RegExp(`data-analysis-inspector="${scope}"`));
    assert.doesNotMatch(html, /role="tab"|Relationships|Performance Triage/);
    assert.match(html, /data-analysis-mode-panel="unsafe"/);
  }
  assert.match(renderMemberUnsafeMembersSurface({ ...memberScope, data: result() }), /<p title="Read · 1 finding">/);
});

test("Member scope selects every finding anchored to the member, with its evidence", () => {
  const getter = { ...member, member: "Fixture.Sample.get_Value()", stableSelector: "Value~p", metadataToken: 0x06000002, safeBoundary: "legacy rules" };
  const setter = { ...getter, member: "Fixture.Sample.set_Value(Int32)", metadataToken: 0x06000003, explicitContract: true, partial: true, items: [{ role: "pointer dereference", body: "Fixture.Sample.set_Value(Int32)", ilOffset: null, detail: "" }] };
  const data = result({ types: [group({ members: [member, getter, setter] })] });
  const html = renderMemberUnsafeMembersSurface({ ...memberScope, stableSelector: "Value~p", data });
  assert.equal(html.match(/class="document-section unsafe-member-finding"/g)?.length, 2);
  assert.doesNotMatch(html, /<h2>Fixture.Sample.Read\(Int32\)/);
  assert.match(html, /get_Value\(\)/);
  assert.match(html, /set_Value\(Int32\)/);
  assert.match(html, /<dt>Exposure<\/dt><dd>Non-public<\/dd>/);
  assert.match(html, /<dt>Safe boundary<\/dt><dd>legacy rules<\/dd>/);
  assert.match(html, /<dt>Propagation<\/dt><dd>does not propagate<\/dd>/);
  assert.match(html, /<dt>Propagation<\/dt><dd>propagates \(explicit caller-unsafe contract\)<\/dd>/);
  assert.match(html, /<dt>Completeness<\/dt><dd>partial evidence<\/dd>/);
  assert.equal(html.match(/<table class="unsafe-evidence-table"><thead><tr><th>Role<\/th><th>Body<\/th><th>IL<\/th><th>Detail<\/th>/g)?.length, 2);
  assert.match(html, /&lt;Read&gt;b__0_0\(\)/);
  assert.match(html, /IL_0007/);
  assert.match(html, /IL_0012/);
  assert.doesNotMatch(html, /<details/);
});

test("a member absent from a complete census says it has no unsafe evidence", () => {
  const html = renderMemberUnsafeMembersSurface({ ...memberScope, stableSelector: "Other~x", data: result() });
  assert.match(html, /No unsafe evidence for this member/);
  assert.match(html, /No unsafe members under the updated memory-safety rules/);
});

test("a member without findings from its own Member census reads as unattributed, not absent", () => {
  const empty = result({ counts: counts({ members: 0, safeBoundaries: 0 }), types: [group({ counts: counts({ members: 0, safeBoundaries: 0 }), members: [] })] });
  for (const data of [empty, result({ ...empty, outcome: "incomplete", limitations: ["ReferenceAssembly"] })]) {
    const html = renderMemberUnsafeMembersSurface({ ...memberScope, source: "member", data });
    assert.match(html, /No unsafe evidence was attributed to this member/);
    assert.match(html, /generated body that can&#39;t be authenticated|generated body that can't be authenticated/);
    assert.doesNotMatch(html, /No unsafe members under the updated memory-safety rules|No unsafe evidence for this member/);
  }
  // Selected from a Type census, the member keeps that census's wording.
  const html = renderMemberUnsafeMembersSurface({ ...memberScope, source: "type", stableSelector: "Other~x", data: result() });
  assert.match(html, /No unsafe evidence for this member/);
  assert.doesNotMatch(html, /attributed/);
});

test("an incomplete census lists its limitations at Type and Member scope and keeps the observed rows", () => {
  const data = result({ outcome: "incomplete", limitations: ["BodyAnalysisFailed in <body>"] });
  for (const render of scopes) {
    const html = render(data);
    assert.match(html, /The unsafe member census is incomplete/);
    assert.match(html, /BodyAnalysisFailed in &lt;body&gt;/);
    assert.match(html, /Fixture.Sample/);
  }
  assert.match(scopes[0]!(data), /1 unsafe member · incomplete/);
});

test("an incomplete empty census never claims absence at Type or Member scope", () => {
  const data = result({ outcome: "incomplete", counts: counts({ members: 0, safeBoundaries: 0 }), types: [], limitations: ["ReferenceAssembly"] });
  for (const render of scopes) {
    const html = render(data);
    assert.match(html, /absence of unsafe members cannot be established/);
    assert.doesNotMatch(html, /No unsafe members under the updated memory-safety rules|No unsafe members in this type|No unsafe evidence for this member/);
  }
});

test("failed, unavailable, pending, and complete empty census results remain distinct", () => {
  for (const render of scopes) {
    for (const outcome of ["failed", "rejected", "unavailable"]) {
      const html = render(result({ outcome, types: [], inspectionError: "<failure>" }));
      assert.match(html, /&lt;failure&gt;/);
      assert.match(html, outcome === "unavailable" ? /Unsafe members unavailable/ : /Unsafe members failed/);
      assert.doesNotMatch(html, /No unsafe members under the updated memory-safety rules/);
    }
    assert.match(render(result({ types: [] })), /No unsafe members under the updated memory-safety rules/);
    assert.match(render(result(), { fresh: false }), /Reading unsafe members/);
    assert.match(render(result(), { loading: true }), /Reading unsafe members/);
    assert.match(render(null, { error: "<transport>" }), /&lt;transport&gt;/);
  }
});

test("no scope grades its findings", () => {
  const legacy = counts({ safeBoundaries: 0, safeBoundaryUndecided: 1 });
  const data = result({ outcome: "incomplete", limitations: ["ReferenceAssembly"], types: [group({ counts: legacy, members: [{ ...member, explicitContract: true, partial: true }] })] });
  for (const render of scopes) assert.doesNotMatch(render(data), /severity|risk|recommend/i);
});
