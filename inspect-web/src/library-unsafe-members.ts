import type {
  BrowserUnsafeCodeType,
  BrowserUnsafeCodeTypes,
  BrowserUnsafeMember,
  BrowserUnsafeMemberCounts,
  BrowserUnsafeMembers,
} from "./facades/inspect-web-analysis.d.ts";
import {
  renderAnalysisInspector,
  type AnalysisInspectorContext,
  type AnalysisScopeContext,
} from "./analysis-inspector.ts";
import type { UnsafeCensusSource } from "./unsafe-members-scope.ts";

// The request state every Unsafe scope shares: one result for one exact
// Library and coordinate, and at Type and Member scope its Type or member.
interface UnsafeRequestState<T> {
  fresh: boolean;
  loading: boolean;
  error: string;
  data: T | null;
  escapeHtml: (value: unknown) => string;
}

type UnsafeCensusState = UnsafeRequestState<BrowserUnsafeMembers>;

interface UnsafeCodeTypesOptions
  extends AnalysisInspectorContext, UnsafeRequestState<BrowserUnsafeCodeTypes> {
  requireLibrary: boolean;
}

export interface TypeUnsafeMembersOptions extends UnsafeCensusState {
  // Whether the Type's Analysis also offers Relationships, which decides its
  // tabs.
  context: AnalysisScopeContext;
  typeId: string;
  typeName: string;
}

export interface MemberUnsafeMembersOptions extends UnsafeCensusState {
  // The census the data came from: a Member-scope census can't see a
  // generated body it can't authenticate to the member.
  source: UnsafeCensusSource;
  memberName: string;
  typeId: string;
  stableSelector: string;
}

function il(offset: number): string {
  return `IL_${offset.toString(16).padStart(4, "0").toUpperCase()}`;
}

function plural(count: number, singular: string, many = `${singular}s`): string {
  return `${count.toLocaleString()} ${count === 1 ? singular : many}`;
}

// One shared summary part per role: "role[ ×N][: callee, callee]". Callee names
// contain "::" but never ": ", so the first ": " separates the role from them.
function roleParts(evidence: string): { role: string; callees: string }[] {
  return evidence.split("; ").filter(Boolean).map(part => {
    const split = part.indexOf(": ");
    return split < 0
      ? { role: part, callees: "" }
      : { role: part.slice(0, split), callees: part.slice(split + 2) };
  });
}

function pill(text: string, escape: (value: unknown) => string, modifier = ""): string {
  return `<span class="perf-confidence unsafe-pill${modifier ? ` ${modifier}` : ""}">${escape(text)}</span>`;
}

function renderCounts(counts: BrowserUnsafeMemberCounts, escape: (value: unknown) => string): string {
  const items: [string, string][] = [
    [counts.members.toLocaleString(), counts.members === 1 ? "member" : "members"],
    [counts.public.toLocaleString(), "public"],
    [counts.propagating.toLocaleString(), "propagating"],
    counts.safeBoundaryUndecided === counts.members
      ? ["Safe boundaries", "legacy rules"]
      : [counts.safeBoundaries.toLocaleString(), counts.safeBoundaries === 1 ? "safe boundary" : "safe boundaries"],
  ];
  return `<dl class="unsafe-counts">${items.map(([value, label]) => `<div><dt>${escape(value)}</dt><dd>${escape(label)}</dd></div>`).join("")}</dl>`;
}

function section(title: string, detail: string, escape: (value: unknown) => string): string {
  return `<section class="document-section empty-document"><h2>${escape(title)}</h2><p>${escape(detail)}</p></section>`;
}

type RequestOutcome<T> =
  | { kind: "message"; status: string; content: string }
  | { kind: "rows"; data: T; partial: boolean; warning: string };

interface UnsafeResult {
  readonly outcome: string;
  readonly limitations: readonly string[];
  readonly inspectionError?: string | null;
}

// How a scope names its request: the census at Type and Member scope, the
// Finding Unsafe Any search at the Library.
interface RequestWording {
  subject: string;
  reading: string;
  running: string;
  operation: string;
  absent: string;
}

const censusWording: RequestWording = {
  subject: "Unsafe members",
  reading: "Reading unsafe members",
  running: "Running the unsafe member census over this library's method bodies.",
  operation: "unsafe member census",
  absent: "unsafe members",
};

const searchWording: RequestWording = {
  subject: "Unsafe code",
  reading: "Finding unsafe code",
  running: "Searching this library's method bodies until each Type's first finding.",
  operation: "unsafe code search",
  absent: "unsafe code",
};

// Every scope renders the outcome the same way; only the rows differ.
function requestOutcome<T extends UnsafeResult>(
  options: UnsafeRequestState<T>,
  wording: RequestWording,
): RequestOutcome<T> {
  const { escapeHtml: escape, fresh, loading, error } = options;
  const data = fresh ? options.data : null;
  const failed = `${wording.subject} failed`;
  if (fresh && error) {
    return { kind: "message", status: failed, content: section(failed, error, escape) };
  }
  if (!data || loading) {
    return {
      kind: "message",
      status: `${wording.reading}…`,
      content: `<section class="document-section source-progress"><span class="loader"></span><h2>${escape(wording.reading)}&hellip;</h2><p>${escape(wording.running)}</p></section>`,
    };
  }
  if (data.outcome !== "available" && data.outcome !== "incomplete") {
    const status = data.outcome === "failed" || data.outcome === "rejected" ? failed : `${wording.subject} unavailable`;
    return { kind: "message", status, content: section(status, data.inspectionError || `No ${wording.operation} is available.`, escape) };
  }
  const partial = data.outcome === "incomplete";
  const warning = partial
    ? `<section class="document-section metadata-warning"><strong>The ${escape(wording.operation)} is incomplete</strong><p>${plural(data.limitations.length, "limitation")}; the rows below are the observed findings.</p><ul>${data.limitations.map(limitation => `<li>${escape(limitation)}</li>`).join("")}</ul></section>`
    : "";
  return { kind: "rows", data, partial, warning };
}

// An incomplete request with no rows cannot establish absence.
function emptySection(
  partial: boolean,
  title: string,
  escape: (value: unknown) => string,
  wording: RequestWording = censusWording,
): string {
  return partial
    ? section("No findings were observed", `The ${wording.operation} is incomplete; absence of ${wording.absent} cannot be established.`, escape)
    : section(title, "No unsafe members under the updated memory-safety rules.", escape);
}

// One row per Type, in the answer's order. Activating a row opens the Type's
// Unsafe lens; a Type without navigation identity keeps its row and renders
// without a link. The state shows only when some Type is unknown.
function renderType(
  type: BrowserUnsafeCodeType,
  assembly: string,
  showState: boolean,
  escape: (value: unknown) => string,
): string {
  const body = `
    <span class="perf-member"><span class="perf-name unsafe-type-name">${escape(type.type)}</span></span>
    <span class="perf-meta">${showState ? pill(type.state, escape, "unsafe-type-state") : ""}</span>`;
  const row = type.typeId
    ? `<button type="button" class="perf-row" data-unsafe-type-row="${escape(type.typeId)}" data-unsafe-assembly="${escape(assembly)}" title="${escape(type.type)} &mdash; open Type Unsafe">${body}</button>`
    : `<div class="perf-row unsafe-member-static" title="${escape(type.type)}">${body}</div>`;
  return `<article class="triage-item unsafe-type">${row}</article>`;
}

// One row per declared member, as the census issues it, in Performance
// Triage's row frame: no severity, risk, or recommendation. The count is the
// member's contributing evidence items, which the Member scope lists.
function renderMember(member: BrowserUnsafeMember, assembly: string, escape: (value: unknown) => string): string {
  const navigable = member.typeId && member.stableSelector;
  const parts = roleParts(member.evidence);
  const roles = parts
    .map(part => `<span class="perf-shape">${escape(part.role)}</span>`)
    .join("");
  const callees = parts
    .filter(part => part.callees)
    .map(part => escape(part.callees))
    .join("; ");
  const badges = [
    pill(member.exposure, escape, "unsafe-member-exposure"),
    member.explicitContract ? pill("propagates", escape, "unsafe-member-badge") : "",
    pill(member.safeBoundary, escape, "unsafe-member-boundary"),
    member.partial ? pill("partial", escape, "unsafe-member-partial") : "",
  ].join("");
  const body = `
    <span class="perf-count" title="Contributing evidence items">${member.items.length.toLocaleString()}</span>
    <span class="perf-member">
      <span class="perf-name unsafe-member-name">${escape(member.member)}</span>
      <span class="perf-shapes unsafe-member-evidence" title="${escape(member.evidence)}">${roles}</span>
      ${callees ? `<span class="unsafe-member-callees">${callees}</span>` : ""}
    </span>
    <span class="perf-meta">${badges}</span>`;
  const row = navigable
    ? `<button type="button" class="perf-row" data-unsafe-selector="${escape(member.stableSelector)}" data-unsafe-assembly="${escape(assembly)}" data-unsafe-type="${escape(member.typeId)}" data-unsafe-accessibility="${escape(member.accessibility ?? "")}" title="${escape(member.member)} &mdash; open member Unsafe">${body}</button>`
    : `<div class="perf-row unsafe-member-static" title="${escape(member.member)}">${body}</div>`;
  return `<article class="triage-item unsafe-member">${row}</article>`;
}

// One declared body's finding at Member scope: its dimensions, kept separate,
// and every contributing evidence item.
function renderFinding(member: BrowserUnsafeMember, escape: (value: unknown) => string): string {
  const dimensions: [string, string][] = [
    ["Evidence", member.evidence],
    ["Exposure", member.exposure],
    ["Propagation", member.explicitContract ? "propagates (explicit caller-unsafe contract)" : "does not propagate"],
    ["Safe boundary", member.safeBoundary],
    ["Completeness", member.partial ? "partial evidence" : "complete evidence"],
  ];
  const items = member.items.map(item => `<tr><td>${escape(item.role)}</td><td><code>${escape(item.body)}</code></td><td><code>${item.ilOffset === null ? "" : il(item.ilOffset)}</code></td><td><code>${escape(item.detail ?? "")}</code></td></tr>`).join("");
  return `<section class="document-section unsafe-member-finding">
    <div class="section-title"><h2>${escape(member.member)}</h2><span>${plural(member.items.length, "evidence item")}</span></div>
    <dl class="fact-rows unsafe-member-dimensions">${dimensions.map(([key, value]) => `<div><dt>${escape(key)}</dt><dd>${escape(value)}</dd></div>`).join("")}</dl>
    <div class="unsafe-evidence-wrap"><table class="unsafe-evidence-table"><thead><tr><th>Role</th><th>Body</th><th>IL</th><th>Detail</th></tr></thead><tbody>${items}</tbody></table></div>
  </section>`;
}

/** Library scope: one row per Type that has unsafe code, as Finding Unsafe Any settles it. */
export function renderLibraryUnsafeCodeTypesSurface(options: UnsafeCodeTypesOptions): string {
  const escape = options.escapeHtml;
  if (options.requireLibrary) {
    return renderAnalysisInspector(options, "unsafe", "Select a library",
      `<section class="document-section empty-document"><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to inspect its unsafe code.</p></section>`);
  }
  const outcome = requestOutcome(options, searchWording);
  if (outcome.kind === "message") return renderAnalysisInspector(options, "unsafe", outcome.status, outcome.content);
  const { data, partial, warning } = outcome;
  const showState = data.types.some(type => type.state !== "unsafe");
  const status = `${plural(data.types.length, "type")} with unsafe code${partial ? " · incomplete" : ""}`;
  const content = warning
    + (data.types.length
      ? `<p class="library-analysis-note">Types with unsafe code under the updated memory-safety rules, each settled at its first finding. The search reports evidence, not a verdict. Select a linked Type for its members.</p><div class="perf-list unsafe-type-list">${data.types.map(type => renderType(type, data.assembly, showState, escape)).join("")}</div>`
      : emptySection(partial, "No unsafe code", escape, searchWording));
  return renderAnalysisInspector(options, "unsafe", status, content);
}

/** Type scope: the Type Analysis Unsafe tab, with the Type's counts and one row per member with a finding. */
export function renderTypeUnsafeMembersSurface(options: TypeUnsafeMembersOptions): string {
  const escape = options.escapeHtml;
  const outcome = requestOutcome(options, censusWording);
  let status: string;
  let content: string;
  if (outcome.kind === "message") {
    ({ status, content } = outcome);
  } else {
    const { data, partial, warning } = outcome;
    const type = data.types.find(candidate => candidate.typeId === options.typeId);
    status = `${plural(type?.counts.members ?? 0, "unsafe member")}${partial ? " · incomplete" : ""}`;
    content = warning
      + (type
        ? `<p class="library-analysis-note">Members with unsafe evidence under the updated memory-safety rules, one row per declared member. The census reports evidence, not a verdict. Select a linked member for its evidence.</p>${renderCounts(type.counts, escape)}<div class="perf-list unsafe-member-list">${type.members.map(member => renderMember(member, data.assembly, escape)).join("")}</div>`
        : emptySection(partial, "No unsafe members in this type", escape));
  }
  return renderAnalysisInspector(options, "unsafe", `${options.typeName} · ${status}`, content, "type", options.context);
}

/** Member scope: the Member Analysis Unsafe tab, with every finding anchored to the member. */
export function renderMemberUnsafeMembersSurface(options: MemberUnsafeMembersOptions): string {
  const escape = options.escapeHtml;
  const outcome = requestOutcome(options, censusWording);
  if (outcome.kind === "message") {
    return renderAnalysisInspector(options, "unsafe", `${options.memberName} · ${outcome.status}`, outcome.content, "member");
  }
  const { data, partial, warning } = outcome;
  // Several declared bodies, such as property accessors, can anchor to one
  // member; each keeps its own finding.
  const findings = options.stableSelector
    ? data.types.find(candidate => candidate.typeId === options.typeId)?.members
      .filter(member => member.stableSelector === options.stableSelector) ?? []
    : [];
  const status = `${options.memberName} · ${plural(findings.length, "finding")}${partial ? " · incomplete" : ""}`;
  const empty = options.source === "member"
    ? section("No unsafe evidence was attributed to this member", "A generated body that can't be authenticated to this member is reported at Type and Library scope.", escape)
    : emptySection(partial, "No unsafe evidence for this member", escape);
  return renderAnalysisInspector(options, "unsafe", status, warning + (findings.length
    ? findings.map(member => renderFinding(member, escape)).join("")
    : empty), "member");
}
