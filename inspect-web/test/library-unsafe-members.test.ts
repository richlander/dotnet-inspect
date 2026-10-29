import assert from "node:assert/strict";
import test from "node:test";
import {
  renderLibraryUnsafeMembersSurface,
  renderMemberUnsafeMembersSurface,
  renderTypeUnsafeMembersSurface,
} from "../src/library-unsafe-members.ts";
import type {
  BrowserUnsafeMember,
  BrowserUnsafeMemberCounts,
  BrowserUnsafeMembers,
  BrowserUnsafeType,
} from "../src/facades/inspect-web-analysis.d.ts";

const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const census = { fresh: true, loading: false, error: "", escapeHtml };
const library = { ...census, assemblyIdentity: "Fixture", assetPath: "Fixture.dll", coordinate: "Fixture@1.0", pickerHtml: "", requireLibrary: false };
const typeScope = { ...census, typeId: "Fixture.Sample", typeName: "Sample" };
const memberScope = { ...census, typeId: "Fixture.Sample", stableSelector: "Read~abc" };
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
const scopes = [
  (data: BrowserUnsafeMembers | null, overrides: Partial<typeof census> = {}) => renderLibraryUnsafeMembersSurface({ ...library, data, ...overrides }),
  (data: BrowserUnsafeMembers | null, overrides: Partial<typeof census> = {}) => renderTypeUnsafeMembersSurface({ ...typeScope, data, ...overrides }),
  (data: BrowserUnsafeMembers | null, overrides: Partial<typeof census> = {}) => renderMemberUnsafeMembersSurface({ ...memberScope, data, ...overrides }),
];

test("Library scope shows its counts and one navigable row per declaring Type", () => {
  const second = group({ type: "Fixture.Other", typeId: "Fixture.Other", counts: counts({ members: 3, public: 2, propagating: 1, safeBoundaries: 0 }) });
  const html = renderLibraryUnsafeMembersSurface({ ...library, data: result({ counts: counts({ members: 4, public: 2, propagating: 1 }), types: [group(), second] }) });
  assert.match(html, /data-analysis-mode="unsafe" aria-selected="true"/);
  assert.match(html, /2 types · 4 unsafe members</);
  assert.match(html, /class="unsafe-counts"><div><dt>4<\/dt><dd>members<\/dd><\/div><div><dt>2<\/dt><dd>public<\/dd>/);
  assert.equal(html.match(/class="triage-item unsafe-type"/g)?.length, 2);
  assert.ok(html.indexOf("Fixture.Sample") < html.indexOf("Fixture.Other"), "Types keep the census order");
  assert.match(html, /data-unsafe-type-row="Fixture.Other" data-unsafe-assembly="Fixture.dll"/);
  assert.match(html, /class="perf-count"[^>]*>3</);
  for (const text of ["2 public", "1 propagate", "0 safe boundaries", "1 safe boundary"]) assert.ok(html.includes(`>${text}<`), text);
  // Library scope lists Types, not members or their evidence.
  assert.doesNotMatch(html, /data-unsafe-selector=|unsafe-evidence-table/);
});

test("a legacy Library presents safe boundaries as not applicable, never zero", () => {
  const legacy = counts({ safeBoundaries: 0, safeBoundaryUndecided: 1 });
  const html = renderLibraryUnsafeMembersSurface({ ...library, data: result({ counts: legacy, types: [group({ counts: legacy })] }) });
  assert.match(html, /<dd>legacy rules<\/dd>/);
  assert.match(html, /unsafe-pill">legacy rules</);
  assert.doesNotMatch(html, /0 safe boundar/);
});

test("a Type the API surface cannot resolve renders without navigation", () => {
  const html = renderLibraryUnsafeMembersSurface({ ...library, data: result({ types: [group({ typeId: null })] }) });
  assert.match(html, /Fixture.Sample/);
  assert.doesNotMatch(html, /data-unsafe-type-row=/);
  assert.match(html, /<div class="perf-row unsafe-member-static"/);
});

test("Type scope selects its Type group and shows each member's dimensions", () => {
  const other = { ...member, member: "Fixture.Other.Run()", typeId: "Fixture.Other", stableSelector: "Run~def" };
  const propagator = { ...member, member: "Fixture.Sample.Add()", stableSelector: "Add~ghi", exposure: "Public", explicitContract: true, safeBoundary: "not a safe boundary", partial: true };
  const data = result({ types: [group({ counts: counts({ members: 2, public: 1, propagating: 1 }), members: [member, propagator] }), group({ type: "Fixture.Other", typeId: "Fixture.Other", members: [other] })] });
  const html = renderTypeUnsafeMembersSurface({ ...typeScope, data });
  assert.match(html, /<h1 id="type-unsafe-title">Unsafe<\/h1>/);
  assert.match(html, /2 unsafe members<span>· Sample<\/span>/);
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

test("an incomplete census lists its limitations at every scope and keeps the observed rows", () => {
  const data = result({ outcome: "incomplete", limitations: ["BodyAnalysisFailed in <body>"] });
  for (const render of scopes) {
    const html = render(data);
    assert.match(html, /The unsafe member census is incomplete/);
    assert.match(html, /BodyAnalysisFailed in &lt;body&gt;/);
    assert.match(html, /Fixture.Sample/);
  }
  assert.match(scopes[0]!(data), /1 type · 1 unsafe member · incomplete/);
  assert.match(scopes[1]!(data), /1 unsafe member · incomplete/);
});

test("an incomplete empty census never claims absence at any scope", () => {
  const data = result({ outcome: "incomplete", counts: counts({ members: 0, safeBoundaries: 0 }), types: [], limitations: ["ReferenceAssembly"] });
  for (const render of scopes) {
    const html = render(data);
    assert.match(html, /absence of unsafe members cannot be established/);
    assert.doesNotMatch(html, /No unsafe members under the updated memory-safety rules|No unsafe members in this type|No unsafe evidence for this member/);
  }
});

test("failed, unavailable, pending, and complete empty results remain distinct at every scope", () => {
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
  assert.match(renderLibraryUnsafeMembersSurface({ ...library, requireLibrary: true, data: null }), /Pick a library/);
});

test("no scope grades its findings", () => {
  const legacy = counts({ safeBoundaries: 0, safeBoundaryUndecided: 1 });
  const data = result({ outcome: "incomplete", limitations: ["ReferenceAssembly"], types: [group({ counts: legacy, members: [{ ...member, explicitContract: true, partial: true }] })] });
  for (const render of scopes) assert.doesNotMatch(render(data), /severity|risk|recommend/i);
});
