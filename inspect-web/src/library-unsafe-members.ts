import type { BrowserUnsafeMember, BrowserUnsafeMembers } from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector, type AnalysisInspectorContext } from "./analysis-inspector.ts";

interface UnsafeMembersOptions extends AnalysisInspectorContext {
  requireLibrary: boolean;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserUnsafeMembers | null;
}

// Expanded evidence is presentation state: it survives the app's re-renders,
// keyed by the row's Library assembly and member token.
const expandedEvidence = new Set<string>();

export function rememberUnsafeEvidence(key: string, open: boolean): void {
  if (!key) return;
  if (open) expandedEvidence.add(key);
  else expandedEvidence.delete(key);
}

function il(offset: number): string {
  return `IL_${offset.toString(16).padStart(4, "0").toUpperCase()}`;
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

// One row per declared member, as the census issues it, in Performance
// Triage's row frame: no severity, risk, or recommendation. The count is the
// member's contributing evidence items, which stay subordinate to the row.
function renderMember(
  member: BrowserUnsafeMember,
  assembly: string,
  escape: (value: unknown) => string,
): string {
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
    member.partial ? `<span class="perf-confidence unsafe-member-partial">partial</span>` : "",
    member.explicitContract ? `<span class="perf-confidence unsafe-member-badge">explicit</span>` : "",
    `<span class="perf-confidence unsafe-member-exposure">${escape(member.exposure)}</span>`,
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
    ? `<button type="button" class="perf-row" data-unsafe-selector="${escape(member.stableSelector)}" data-unsafe-assembly="${escape(assembly)}" data-unsafe-type="${escape(member.typeId)}" data-unsafe-accessibility="${escape(member.accessibility ?? "")}" title="${escape(member.member)} &mdash; open member">${body}</button>`
    : `<div class="perf-row unsafe-member-static" title="${escape(member.member)}">${body}</div>`;
  const items = member.items.map(item => `<tr><td>${escape(item.role)}</td><td><code>${escape(item.body)}</code></td><td><code>${item.ilOffset === null ? "" : il(item.ilOffset)}</code></td><td><code>${escape(item.detail ?? "")}</code></td></tr>`).join("");
  const count = member.items.length;
  const key = `${assembly}#${member.metadataToken}`;
  return `<article class="triage-item unsafe-member">${row}
    <details class="unsafe-member-items" data-unsafe-evidence="${escape(key)}"${expandedEvidence.has(key) ? " open" : ""}><summary>${count.toLocaleString()} evidence item${count === 1 ? "" : "s"}</summary>
      <div class="unsafe-evidence-wrap"><table class="unsafe-evidence-table"><thead><tr><th>Role</th><th>Body</th><th>IL</th><th>Detail</th></tr></thead><tbody>${items}</tbody></table></div>
    </details>
  </article>`;
}

export function renderLibraryUnsafeMembersSurface(options: UnsafeMembersOptions): string {
  const { escapeHtml: escape, fresh, loading, error } = options;
  const data = fresh ? options.data : null;
  let status: string;
  let content: string;
  if (options.requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><h2>Pick a library to analyze</h2><p>Choose a .NET platform library above to inspect its unsafe members.</p></section>`;
  } else if (fresh && error) {
    status = "Unsafe members failed";
    content = `<section class="document-section empty-document"><h2>Unsafe members failed</h2><p>${escape(error)}</p></section>`;
  } else if (!data || loading) {
    status = "Reading unsafe members…";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Reading unsafe members&hellip;</h2><p>Running the unsafe member census over this library's method bodies.</p></section>`;
  } else if (data.outcome !== "available" && data.outcome !== "incomplete") {
    status = data.outcome === "failed" || data.outcome === "rejected" ? "Unsafe members failed" : "Unsafe members unavailable";
    content = `<section class="document-section empty-document"><h2>${status}</h2><p>${escape(data.inspectionError || "No unsafe member census is available.")}</p></section>`;
  } else {
    const partial = data.outcome === "incomplete";
    const count = data.members.length;
    status = `${count.toLocaleString()} unsafe member${count === 1 ? "" : "s"}${partial ? " · incomplete" : ""}`;
    const warning = partial
      ? `<section class="document-section metadata-warning"><strong>The unsafe member census is incomplete</strong><p>${data.limitations.length.toLocaleString()} limitation${data.limitations.length === 1 ? "" : "s"}; the rows below are the observed findings.</p><ul>${data.limitations.map(limitation => `<li>${escape(limitation)}</li>`).join("")}</ul></section>`
      : "";
    const empty = count
      ? ""
      : partial
        ? `<section class="document-section empty-document"><h2>No findings were observed</h2><p>The census is incomplete; absence of unsafe members cannot be established.</p></section>`
        : `<section class="document-section empty-document"><h2>No unsafe members</h2><p>No unsafe members under the updated memory-safety rules.</p></section>`;
    content = warning
      + (count ? `<p class="library-analysis-note">Members with unsafe evidence under the updated memory-safety rules, one row per declared member. The census reports evidence, not a verdict. Select a member to open it; expand a row for its contributing evidence.</p><div class="perf-list unsafe-member-list">${data.members.map(member => renderMember(member, data.assembly, escape)).join("")}</div>` : "")
      + empty;
  }
  return renderAnalysisInspector(options, "unsafe", status, content);
}
