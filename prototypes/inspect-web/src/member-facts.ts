import type { MemberDetailInspectionState, MemberFacts } from "./member-detail-inspection.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

export function renderMemberFacts(
  state: Pick<
    MemberDetailInspectionState,
    "memberFacts" | "memberFactsLoading" | "memberFactsError"
  >,
) {
  if (state.memberFactsLoading) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Analyzing method…</h2><p>Decoding the selected overload and deriving method evidence and performance opportunities.</p></section>`;
  }
  if (!state.memberFacts) {
    return `<section class="document-section empty-member-section"><h2>Facts query failed</h2><p>${escapeHtml(state.memberFactsError || "No facts result was returned.")}</p></section>`;
  }

  const facts = state.memberFacts;
  const signals = facts.signals;
  const heapAllocations = facts.allocations.filter(a => a.countedAsHeap);
  const allocOffsets = heapAllocations.map(a => a.offset);
  const callOffsets = facts.calls.map(c => c.offset);
  const safetyOffsets = facts.safety
    .map(s => s.offset)
    .filter((offset): offset is string => offset != null);
  const loopAllocOffsets = heapAllocations.filter(a => a.inLoop).map(a => a.offset);
  const rows: readonly (readonly [
    label: string,
    value: string,
    evidence?: readonly string[],
  ])[] = [
    ["Allocations", String(signals.allocations), allocOffsets],
    ["Calls", String(facts.calls.length), callOffsets],
    ["Copies", String(signals.copies)],
    ["Reflection calls", String(signals.reflection)],
    ["Throws / catches / finally", `${signals.throws} / ${signals.catches} / ${signals.finallys}`],
    ["Unsafe", signals.unsafe ? "yes" : "no", signals.unsafe ? safetyOffsets : []],
    ["Allocates in loop", signals.allocatesInLoop ? "yes" : "no", signals.allocatesInLoop ? loopAllocOffsets : []],
  ];
  return `
    <section class="facts-summary" aria-labelledby="facts-summary-title">
      <header class="facts-summary-heading"><h2 id="facts-summary-title">Analysis summary</h2><span>Static analysis</span></header>
      <dl class="facts-summary-list">${rows.map(([label, value, evidence]) => `
        <div><dt>${escapeHtml(label)}</dt><dd><code class="facts-summary-value">${escapeHtml(value)}</code>${factEvidence(evidence)}</dd></div>`).join("")}
      </dl>
      <p class="facts-metadata-identity"><span>Metadata token</span><code>${escapeHtml(`0x${facts.metadataToken.toString(16).padStart(8, "0")}`)}</code></p>
    </section>
    ${renderAllocationFacts(facts.allocations)}
    ${renderFactTable("Calls", facts.calls, [
      ["IL", "offset"], ["Opcode", "opcode"], ["Callee", "callee"],
      ["Multiplicity", "multiplicity"], ["Loop", row => row.inLoop ? "yes" : ""]
    ], "No direct call sites were found in this method.")}
    ${renderFactTable("Safety facts", facts.safety, [
      ["IL", row => row.offset || ""], ["Kind", "kind"], ["Operation", "operation"],
      ["Requirement", "requirement"], ["Evidence", "evidence"]
    ], "No unsafe operations or declaration evidence were found.")}
    ${renderFactTable("Exception regions", facts.exceptionRegions, [
      ["Region", "region"], ["Clause", "clause"], ["Try", "tryRange"],
      ["Handler", "handlerRange"], ["Filter", row => row.filterRange || ""],
      ["Caught type", row => row.caughtType || ""]
    ], "No exception regions were found in this method.")}
    <section class="document-section performance-facts">
      <div class="section-title"><h2>Performance opportunities</h2><span>ranked judgments · ${facts.performanceOpportunities.length}</span></div>
      ${facts.performanceOpportunities.length
        ? facts.performanceOpportunities.map(opportunity => `
          <article class="performance-opportunity">
            <div><strong>${escapeHtml(opportunity.shape)}</strong><span class="confidence ${escapeHtml(opportunity.confidence)}">${escapeHtml(opportunity.confidence)}</span>${opportunity.offset ? `<code>${escapeHtml(opportunity.offset)}</code>` : ""}</div>
            <p>${escapeHtml(opportunity.evidence)}</p>
            <dl><dt>Possible direction</dt><dd>${escapeHtml(opportunity.fix)}</dd>${opportunity.caveat ? `<dt>Caveat</dt><dd>${escapeHtml(opportunity.caveat)}</dd>` : ""}<dt>Provenance</dt><dd>${escapeHtml([opportunity.provenance, opportunity.finding].filter(Boolean).join(" · "))}</dd></dl>
          </article>`).join("")
        : '<div class="empty-fact-group">No curated performance opportunities were found for this method.</div>'}
    </section>
    ${facts.diagnostics.length
      ? `<section class="document-section fact-group"><div class="section-title"><h2>Analysis diagnostics</h2><span>${facts.diagnostics.length}</span></div><ul>${facts.diagnostics.map(diagnostic => `<li>${escapeHtml(diagnostic)}</li>`).join("")}</ul></section>`
      : ""}`;
}

function renderAllocationFacts(allocations: MemberFacts["allocations"]) {
  return `<section class="allocation-facts" aria-labelledby="allocation-facts-title">
    <header><h2 id="allocation-facts-title">Allocation facts</h2><span>${allocations.length} ${allocations.length === 1 ? "occurrence" : "occurrences"}</span></header>
    ${allocations.length
      ? `<ol class="allocation-rows">${allocations.map(allocation => `
        <li class="allocation-row">
          <div class="allocation-location"><code>${escapeHtml(allocation.offset)}</code><span>${escapeHtml(allocation.kind)}</span></div>
          <div class="allocation-main">
            <div class="allocation-type">${allocation.type == null
              ? '<span class="allocation-unavailable">Type unavailable</span>'
              : `<code>${escapeHtml(allocation.type)}</code>`}</div>
            <dl class="allocation-properties">${[
              ["Counted as heap", allocation.countedAsHeap ? "yes" : "no"],
              ["Multiplicity", allocation.multiplicity],
              ["Path", allocation.path],
              ["Escape", allocation.escape],
              ["Loop", allocation.inLoop ? "yes" : "no"],
              ["Est. size", allocation.estimatedSizeBytes == null ? null : `${allocation.estimatedSizeBytes} B`],
            ].map(([label, value]) => `<div><dt>${escapeHtml(label)}</dt><dd>${value == null
              ? '<span class="allocation-unavailable">not available</span>'
              : `<code>${escapeHtml(value)}</code>`}</dd></div>`).join("")}</dl>
          </div>
        </li>`).join("")}</ol>`
      : '<p class="allocation-empty">No allocation occurrences were found in this method.</p>'}
  </section>`;
}

type FactTableColumn<T> =
  readonly [label: string, field: keyof T | ((row: T) => unknown)];

function renderFactTable<T extends object>(
  title: string,
  rows: readonly T[],
  columns: readonly FactTableColumn<T>[],
  emptyText: string,
) {
  return `<section class="document-section fact-group">
    <div class="section-title"><h2>${escapeHtml(title)}</h2><span>${rows.length}</span></div>
    ${rows.length
      ? `<div class="fact-table" style="--fact-columns:${columns.length}">${columns.map(([label]) => `<strong>${escapeHtml(label)}</strong>`).join("")}${rows.map(row => columns.map(([, field]) => {
          const value = typeof field === "function" ? field(row) : row[field];
          return `<code>${escapeHtml(value ?? "")}</code>`;
        }).join("")).join("")}</div>`
      : `<div class="empty-fact-group">${escapeHtml(emptyText)}</div>`}
  </section>`;
}

// The summary shows two distinct offsets; the tooltip and detail sections retain the rest.
function factEvidence(offsets?: readonly string[]) {
  const unique = [...new Set((offsets ?? []).filter(Boolean))];
  if (!unique.length) return "";
  const CAP = 2;
  const shown = unique.slice(0, CAP);
  const extra = unique.length - shown.length;
  const label = shown.join(", ") + (extra > 0 ? ` +${extra}` : "");
  return `<span class="fact-evidence" title="${escapeHtml(unique.join(", "))}">${escapeHtml(label)}</span>`;
}
