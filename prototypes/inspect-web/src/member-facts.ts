import type { MemberDetailInspectionState, MemberFacts } from "./member-detail-inspection.ts";
import type {
  BrowserMemberFindingFact,
} from "./facades/inspect-web-source.d.ts";

type MemberFactsRenderState = Pick<
  MemberDetailInspectionState,
  | "memberFacts"
  | "memberFactsLoading"
  | "memberFactsError"
  | "memberAnnotatedLoading"
  | "memberAnnotatedError"
  | "memberFindingInteraction"
  | "memberFindingSelectionError"
>;

export interface MemberFactsBindingActions {
  onSelectFinding(receipt: string, instanceKey: number): void;
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

export function renderMemberFacts(
  state: MemberFactsRenderState,
) {
  return `
    ${renderAnalysisFacts(state)}
    ${renderFindingFacts(state)}`;
}

export function bindMemberFacts(
  root: ParentNode,
  actions: MemberFactsBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-finding-instance]").forEach(
    element => {
      element.addEventListener("click", event => {
        const target = event.currentTarget;
        if (!(target instanceof HTMLElement)) return;
        const receipt = target.dataset.findingReceipt;
        const instanceKey = Number(target.dataset.findingInstance);
        if (!receipt || !Number.isSafeInteger(instanceKey) || instanceKey <= 0) {
          return;
        }
        actions.onSelectFinding(receipt, instanceKey);
      });
    },
  );
}

function renderAnalysisFacts(state: MemberFactsRenderState): string {
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
    ${renderCallFacts(facts.calls)}
    ${renderSafetyFacts(facts.safety)}
    ${renderExceptionRegions(facts.exceptionRegions)}
    ${renderPerformanceOpportunities(facts.performanceOpportunities)}
    ${renderAnalysisDiagnostics(facts.diagnostics)}`;
}

function renderFindingFacts(state: MemberFactsRenderState): string {
  if (state.memberAnnotatedLoading) {
    return `<section class="document-section source-progress finding-facts-progress"><span class="loader"></span><h2>Collecting Findings…</h2><p>Projecting one identity-preserving Finding census for Facts and Annotated Source.</p></section>`;
  }
  const interaction = state.memberFindingInteraction;
  if (!interaction) {
    return `<section class="document-section finding-facts-failure" role="alert"><h2>Finding census failed</h2><p>${escapeHtml(state.memberAnnotatedError || "No Finding census result was returned.")}</p></section>`;
  }

  const facts = interaction.census.facts;
  const receipt = interaction.census.factCensusReceipt;
  const selectionError = state.memberFindingSelectionError
    ? `<p class="finding-selection-error" role="alert">${escapeHtml(state.memberFindingSelectionError)}</p>`
    : "";
  return `
    <section class="finding-facts" aria-labelledby="finding-facts-title">
      <header><h2 id="finding-facts-title">Findings</h2><span>${facts.length} ${facts.length === 1 ? "finding" : "findings"}</span></header>
      <p class="finding-context">Research observations for this member. Body findings can open their exact occurrence in Annotated Source.</p>
      ${selectionError}
      ${facts.length
        ? `<ol class="finding-rows">${facts.map(fact =>
            renderFindingFact(
              fact,
              receipt,
              interaction.selectedInstanceKey,
            )).join("")}</ol>`
        : '<p class="finding-empty">No Research findings were reported for this member.</p>'}
    </section>`;
}

function renderFindingFact(
  fact: BrowserMemberFindingFact,
  receipt: string,
  selectedInstanceKey: number | null,
): string {
  const selected =
    fact.instanceKey !== null && fact.instanceKey === selectedInstanceKey;
  const content = `
    <span class="finding-location">
      ${fact.ilOffset === null
        ? '<span>Member</span>'
        : `<code>${escapeHtml(`IL_${fact.ilOffset.toString(16).padStart(4, "0").toUpperCase()}`)}</code>`}
      ${fact.cSharpLine === null
        ? ""
        : `<span>line ${escapeHtml(fact.cSharpLine)}</span>`}
    </span>
    <span class="finding-main">
      <code class="finding-id">${escapeHtml(fact.id)}</code>
      ${fact.detail
        ? `<span class="finding-detail">${escapeHtml(fact.detail)}</span>`
        : ""}
      <span class="finding-properties">
        <span class="finding-property"><span class="finding-property-label">Category</span><code>${escapeHtml(fact.category)}</code></span>
        <span class="finding-property"><span class="finding-property-label">Conditionality</span><code>${escapeHtml(fact.conditionality)}</code></span>
        <span class="finding-property"><span class="finding-property-label">Anchor</span><code>${escapeHtml(fact.anchor)}</code></span>
      </span>
    </span>
    ${fact.instanceKey === null
      ? '<span class="finding-source-unavailable">No annotated source target</span>'
      : `<span class="finding-source-action">${selected
          ? '<span class="finding-selected-label">Selected</span><span aria-hidden="true">·</span>'
          : ""}<span>Annotated source</span><span aria-hidden="true">→</span></span>`}`;
  return fact.instanceKey === null
    ? `<li class="finding-row finding-row-unkeyed"><div>${content}</div></li>`
    : `<li class="finding-row${selected ? " selected" : ""}">
        <button type="button"
          data-finding-instance="${fact.instanceKey}"
          data-finding-receipt="${escapeHtml(receipt)}"
          aria-pressed="${selected}">
          ${content}
        </button>
      </li>`;
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

function renderCallFacts(calls: MemberFacts["calls"]) {
  return `<section class="call-facts" aria-labelledby="call-facts-title">
    <header><h2 id="call-facts-title">Calls</h2><span>${calls.length} ${calls.length === 1 ? "call site" : "call sites"}</span></header>
    ${calls.length
      ? `<ol class="call-rows">${calls.map(call => `
        <li class="call-row">
          <div class="call-location"><code>${escapeHtml(call.offset)}</code><code>${escapeHtml(call.opcode)}</code></div>
          <div class="call-main">
            <div class="call-callee"><code>${escapeHtml(call.callee)}</code></div>
            <dl class="call-properties">
              <div><dt>Multiplicity</dt><dd><code>${escapeHtml(call.multiplicity)}</code></dd></div>
              <div><dt>Loop</dt><dd><code>${call.inLoop ? "yes" : "no"}</code></dd></div>
            </dl>
          </div>
        </li>`).join("")}</ol>`
      : '<p class="call-empty">No direct call sites were found in this method.</p>'}
  </section>`;
}

function renderSafetyFacts(safety: MemberFacts["safety"]) {
  return `<section class="safety-facts" aria-labelledby="safety-facts-title">
    <header><h2 id="safety-facts-title">Safety facts</h2><span>${safety.length} ${safety.length === 1 ? "fact" : "facts"}</span></header>
    ${safety.length
      ? `<ol class="safety-rows">${safety.map(fact => `
        <li class="safety-row">
          <div class="safety-location">${fact.offset == null
            ? '<span class="safety-no-offset">No IL offset</span>'
            : `<code>${escapeHtml(fact.offset)}</code>`}<span class="safety-kind">${escapeHtml(fact.kind)}</span></div>
          <div class="safety-main">
            <div class="safety-operation"><code>${escapeHtml(fact.operation)}</code></div>
            <dl class="safety-properties">
              <div><dt>Requirement</dt><dd><code>${escapeHtml(fact.requirement)}</code></dd></div>
              <div><dt>Evidence</dt><dd><code>${escapeHtml(fact.evidence)}</code></dd></div>
            </dl>
          </div>
        </li>`).join("")}</ol>`
      : '<p class="safety-empty">No unsafe operations or declaration evidence were found.</p>'}
  </section>`;
}

function renderExceptionRegions(regions: MemberFacts["exceptionRegions"]) {
  return `<section class="exception-regions" aria-labelledby="exception-regions-title">
    <header><h2 id="exception-regions-title">Exception regions</h2><span>${regions.length} ${regions.length === 1 ? "region" : "regions"}</span></header>
    ${regions.length
      ? `<ol class="exception-rows">${regions.map(region => `
        <li class="exception-row">
          <div class="exception-identity"><span>Region <code>${escapeHtml(region.region)}</code></span><code class="exception-clause">${escapeHtml(region.clause)}</code></div>
          <div class="exception-main">
            <dl class="exception-type"><div><dt>Caught type</dt><dd>${region.caughtType == null
              ? '<span class="exception-unavailable">not supplied</span>'
              : `<code>${escapeHtml(region.caughtType)}</code>`}</dd></div></dl>
            <dl class="exception-ranges">${[
              ["Try", region.tryRange],
              ["Handler", region.handlerRange],
              ["Filter", region.filterRange],
            ].map(([label, value]) => `<div><dt>${escapeHtml(label)}</dt><dd>${value == null
              ? '<span class="exception-unavailable">not supplied</span>'
              : `<code>${escapeHtml(value)}</code>`}</dd></div>`).join("")}</dl>
          </div>
        </li>`).join("")}</ol>`
      : '<p class="exception-empty">No exception regions were found in this method.</p>'}
  </section>`;
}

function renderPerformanceOpportunities(
  opportunities: MemberFacts["performanceOpportunities"],
) {
  return `<section class="performance-facts" aria-labelledby="performance-facts-title">
    <header><h2 id="performance-facts-title">Performance opportunities</h2><span>${opportunities.length} ${opportunities.length === 1 ? "opportunity" : "opportunities"}</span></header>
    ${opportunities.length
      ? `<ol class="performance-rows">${opportunities.map(opportunity => `
        <li class="performance-row">
          <div class="performance-identity">${opportunity.offset == null
            ? '<span class="performance-no-offset">No IL offset</span>'
            : `<code class="performance-offset">${escapeHtml(opportunity.offset)}</code>`}<code class="performance-shape">${escapeHtml(opportunity.shape)}</code></div>
          <div class="performance-main">
            <p class="performance-evidence">${escapeHtml(opportunity.evidence)}</p>
            <dl class="performance-properties">
              <div><dt>Confidence</dt><dd><code>${escapeHtml(opportunity.confidence)}</code></dd></div>
              <div><dt>In loop</dt><dd><code>${opportunity.inLoop ? "yes" : "no"}</code></dd></div>
              <div><dt>Provenance</dt><dd><code>${escapeHtml(opportunity.provenance)}</code></dd></div>
              <div><dt>Finding</dt><dd>${opportunity.finding == null
                ? '<span class="performance-unavailable">not supplied</span>'
                : `<code>${escapeHtml(opportunity.finding)}</code>`}</dd></div>
            </dl>
            <dl class="performance-guidance">
              <div><dt>Possible direction</dt><dd>${escapeHtml(opportunity.fix)}</dd></div>
              <div><dt>Caveat</dt><dd>${opportunity.caveat == null
                ? '<span class="performance-unavailable">not supplied</span>'
                : escapeHtml(opportunity.caveat)}</dd></div>
            </dl>
          </div>
        </li>`).join("")}</ol>`
      : '<p class="performance-empty">No curated performance opportunities were found for this method.</p>'}
  </section>`;
}

function renderAnalysisDiagnostics(
  diagnostics: MemberFacts["diagnostics"],
) {
  if (!diagnostics.length) return "";
  return `<section class="analysis-diagnostics" aria-labelledby="analysis-diagnostics-title">
    <header><h2 id="analysis-diagnostics-title">Analysis diagnostics</h2><span>${diagnostics.length} ${diagnostics.length === 1 ? "diagnostic" : "diagnostics"}</span></header>
    <p class="analysis-diagnostics-context">Some method analysis could not complete. Available evidence remains shown above.</p>
    <ol class="analysis-diagnostic-rows">${diagnostics.map((diagnostic, index) => `
      <li class="analysis-diagnostic-row">
        <span class="analysis-diagnostic-label">Diagnostic ${index + 1}</span>
        <code class="analysis-diagnostic-value">${escapeHtml(diagnostic)}</code>
      </li>`).join("")}</ol>
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
