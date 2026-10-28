import type { BrowserUnsafeMember, BrowserUnsafeMembers } from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector, type AnalysisInspectorContext } from "./analysis-inspector.ts";

interface UnsafeMembersOptions extends AnalysisInspectorContext {
  requireLibrary: boolean;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserUnsafeMembers | null;
}

function il(offset: number): string {
  return `IL_${offset.toString(16).padStart(4, "0").toUpperCase()}`;
}

// One row per declared member, as the census issues it: no severity, risk,
// or recommendation. Contributing evidence stays subordinate to its member.
function renderMember(
  member: BrowserUnsafeMember,
  assembly: string,
  escape: (value: unknown) => string,
): string {
  const navigable = member.typeId && member.stableSelector;
  const name = navigable
    ? `<button type="button" class="unsafe-member-name" data-unsafe-selector="${escape(member.stableSelector)}" data-unsafe-assembly="${escape(assembly)}" data-unsafe-type="${escape(member.typeId)}" data-unsafe-accessibility="${escape(member.accessibility ?? "")}" title="Open ${escape(member.member)}">${escape(member.member)}</button>`
    : `<span class="unsafe-member-name" title="${escape(member.member)}">${escape(member.member)}</span>`;
  const badges = [
    `<span class="unsafe-member-badge">${escape(member.exposure)}</span>`,
    member.explicitContract ? `<span class="unsafe-member-badge">explicit</span>` : "",
    member.partial ? `<span class="unsafe-member-badge unsafe-member-partial">partial</span>` : "",
  ].join("");
  const items = member.items.map(item => `<li><span class="unsafe-evidence-role">${escape(item.role)}</span> <code>${escape(item.body)}</code>${item.ilOffset === null ? "" : ` <code>${il(item.ilOffset)}</code>`}${item.detail ? ` <code>${escape(item.detail)}</code>` : ""}</li>`).join("");
  return `<article class="unsafe-member">
    <div class="unsafe-member-row">${name}<span class="unsafe-member-badges">${badges}</span></div>
    <div class="unsafe-member-evidence">${escape(member.evidence)}</div>
    <details class="unsafe-member-items"><summary>${member.items.length.toLocaleString()} evidence item${member.items.length === 1 ? "" : "s"}</summary><ul>${items}</ul></details>
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
      + (count ? `<div class="unsafe-member-list">${data.members.map(member => renderMember(member, data.assembly, escape)).join("")}</div>` : "")
      + empty;
  }
  return renderAnalysisInspector(options, "unsafe", status, content);
}
