import assert from "node:assert/strict";
import test from "node:test";
import { rememberUnsafeEvidence, renderLibraryUnsafeMembersSurface } from "../src/library-unsafe-members.ts";
import type { BrowserUnsafeMember, BrowserUnsafeMembers } from "../src/facades/inspect-web-analysis.d.ts";

const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const options = { assemblyIdentity: "Fixture", assetPath: "Fixture.dll", coordinate: "Fixture@1.0", pickerHtml: "", requireLibrary: false, fresh: true, loading: false, error: "", escapeHtml };
const member: BrowserUnsafeMember = {
  member: "Fixture.Sample.Read(Int32)",
  metadataToken: 0x06000001,
  exposure: "Non-public",
  explicitContract: false,
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
const result = (overrides: Partial<BrowserUnsafeMembers> = {}): BrowserUnsafeMembers => ({
  outcome: "available", assembly: "Fixture.dll", members: [member], limitations: [], inspectionError: null, ...overrides,
});

test("Unsafe shows one row per declared member with its census evidence", () => {
  const html = renderLibraryUnsafeMembersSurface({ ...options, data: result() });
  assert.match(html, /data-analysis-mode="unsafe" aria-selected="true"/);
  assert.match(html, /1 unsafe member</);
  assert.equal(html.match(/class="triage-item unsafe-member"/g)?.length, 1);
  for (const text of ["Fixture.Sample.Read(Int32)", "Non-public", "pointer dereference ×2", "Unsafe::Add"]) assert.ok(html.includes(text));
  assert.match(html, /data-unsafe-selector="Read~abc"/);
  assert.match(html, /data-unsafe-assembly="Fixture.dll"/);
  assert.match(html, /data-unsafe-type="Fixture.Sample"/);
  assert.match(html, /data-unsafe-accessibility="private"/);
  assert.match(html, /2 evidence items/);
  assert.match(html, /class="perf-count"[^>]*>2</);
  assert.match(html, /<span class="perf-shape">pointer dereference ×2<\/span><span class="perf-shape">explicit-contract call<\/span>/);
  assert.match(html, /class="unsafe-member-callees">System.Runtime.CompilerServices.Unsafe::Add</);
  assert.match(html, /&lt;Read&gt;b__0_0\(\)/);
  assert.match(html, /IL_0007/);
  assert.doesNotMatch(html, /severity|risk|recommend/i);
});

test("an explicit contract and a partial member are badged, not graded", () => {
  const html = renderLibraryUnsafeMembersSurface({ ...options, data: result({ members: [{ ...member, explicitContract: true, partial: true }] }) });
  assert.match(html, /unsafe-member-badge">explicit</);
  assert.match(html, /unsafe-member-partial">partial</);
});

test("a member the API surface cannot resolve renders without navigation", () => {
  const html = renderLibraryUnsafeMembersSurface({ ...options, data: result({ members: [{ ...member, typeId: null, stableSelector: null }] }) });
  assert.match(html, /Fixture.Sample.Read\(Int32\)/);
  assert.doesNotMatch(html, /data-unsafe-selector=/);
  assert.match(html, /<div class="perf-row unsafe-member-static"/);
});

test("an incomplete census lists its limitations and keeps the observed rows", () => {
  const html = renderLibraryUnsafeMembersSurface({ ...options, data: result({ outcome: "incomplete", limitations: ["BodyAnalysisFailed in <body>"] }) });
  assert.match(html, /1 unsafe member · incomplete/);
  assert.match(html, /The unsafe member census is incomplete/);
  assert.match(html, /BodyAnalysisFailed in &lt;body&gt;/);
  assert.match(html, /Fixture.Sample.Read\(Int32\)/);
});

test("an incomplete empty census never claims there are no unsafe members", () => {
  const html = renderLibraryUnsafeMembersSurface({ ...options, data: result({ outcome: "incomplete", members: [], limitations: ["ReferenceAssembly"] }) });
  assert.match(html, /absence of unsafe members cannot be established/);
  assert.doesNotMatch(html, /No unsafe members under the updated memory-safety rules/);
});

test("failed, unavailable, pending, unselected, and complete empty results remain distinct", () => {
  for (const outcome of ["failed", "rejected", "unavailable"]) {
    const html = renderLibraryUnsafeMembersSurface({ ...options, data: result({ outcome, members: [], inspectionError: "<failure>" }) });
    assert.match(html, /&lt;failure&gt;/);
    assert.match(html, outcome === "unavailable" ? /Unsafe members unavailable/ : /Unsafe members failed/);
    assert.doesNotMatch(html, /No unsafe members under the updated memory-safety rules/);
  }
  assert.match(renderLibraryUnsafeMembersSurface({ ...options, data: result({ members: [] }) }), /No unsafe members under the updated memory-safety rules/);
  assert.match(renderLibraryUnsafeMembersSurface({ ...options, fresh: false, data: result() }), /Reading unsafe members/);
  assert.match(renderLibraryUnsafeMembersSurface({ ...options, loading: true, data: result() }), /Reading unsafe members/);
  assert.match(renderLibraryUnsafeMembersSurface({ ...options, error: "<transport>", data: null }), /&lt;transport&gt;/);
  assert.match(renderLibraryUnsafeMembersSurface({ ...options, requireLibrary: true, data: null }), /Pick a library/);
});

test("expanded evidence stays open across re-renders until it is closed", () => {
  const key = "Fixture@1.0 | Fixture.dll | Fixture.dll#100663297";
  const open = /data-unsafe-evidence="[^"]*" open/;
  assert.doesNotMatch(renderLibraryUnsafeMembersSurface({ ...options, data: result() }), open);
  rememberUnsafeEvidence(key, true);
  assert.match(renderLibraryUnsafeMembersSurface({ ...options, data: result() }), open);
  rememberUnsafeEvidence(key, false);
  assert.doesNotMatch(renderLibraryUnsafeMembersSurface({ ...options, data: result() }), open);
});

test("expanded evidence never opens the same token in another Library", () => {
  const html = (coordinate: string) => renderLibraryUnsafeMembersSurface({ ...options, coordinate, data: result() });
  const open = /data-unsafe-evidence="[^"]*" open/;
  const opened = html("net10.0 · Fixture@9.0").match(/data-unsafe-evidence="([^"]*)"/)?.[1] ?? "";
  rememberUnsafeEvidence(opened.replaceAll("&amp;", "&"), true);
  assert.match(html("net10.0 · Fixture@9.0"), open);
  assert.doesNotMatch(html("net10.0 · Fixture@10.0"), open);
  assert.doesNotMatch(html("net9.0 · Fixture@9.0"), open);
  assert.doesNotMatch(renderLibraryUnsafeMembersSurface({ ...options, coordinate: "net10.0 · Fixture@9.0", assetPath: "", data: result() }), open);
  rememberUnsafeEvidence(opened, false);
});
