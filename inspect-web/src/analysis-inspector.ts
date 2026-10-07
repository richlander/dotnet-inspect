export type AnalysisMode =
  | "complexity"
  | "dependencies"
  | "relationships"
  | "performance"
  | "resource-triage"
  | "integrations";

export const defaultAnalysisMode: AnalysisMode = "relationships";

export function isAnalysisMode(
  value: string | undefined,
): value is AnalysisMode {
  return value === "complexity"
    || value === "dependencies"
    || value === "relationships"
    || value === "performance"
    || value === "resource-triage"
    || value === "integrations";
}

export interface AnalysisInspectorContext {
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  pickerHtml: string;
  escapeHtml: (value: unknown) => string;
}

const modes = [
  [defaultAnalysisMode, "Relationships"],
  ["dependencies", "Dependencies"],
  ["complexity", "Complexity"],
  ["integrations", "Integrations"],
  ["performance", "Performance Triage"],
  ["resource-triage", "Resource Triage"],
] as const satisfies readonly (readonly [AnalysisMode, string])[];

export function renderAnalysisInspector(
  context: AnalysisInspectorContext,
  mode: AnalysisMode,
  status: string,
  content: string,
): string {
  const { pickerHtml, escapeHtml } = context;
  const tabs = modes.map(([value, label]) => {
    return `<button type="button" role="tab" id="analysis-mode-${value}" data-analysis-mode="${value}" aria-selected="${value === mode}" aria-controls="analysis-results" tabindex="${value === mode ? 0 : -1}">${label}</button>`
  }).join("");
  return `<section class="analysis-inspector library-analysis-surface library-${mode}-surface${pickerHtml ? ` library-analysis-with-controls library-${mode}-with-controls` : ""}" aria-labelledby="library-analysis-title">
    <header class="api-surface-head">
      <h1 id="library-analysis-title">Analysis</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
      <div class="analysis-mode-tabs" role="tablist" aria-label="Analysis views">${tabs}</div>
    </header>
    ${pickerHtml ? `<section class="library-analysis-controls library-${mode}-controls" aria-label="Analysis library">${pickerHtml}</section>` : ""}
    <div id="analysis-results" role="tabpanel" aria-labelledby="analysis-mode-${mode}" tabindex="0" class="library-analysis-scroll library-${mode}-scroll">${content}</div>
  </section>`;
}

export function bindAnalysisTabs(
  root: ParentNode,
  onSelect: (mode: AnalysisMode) => void,
): void {
  const tabs = [...root.querySelectorAll<HTMLButtonElement>("[data-analysis-mode]")];
  for (const [index, tab] of tabs.entries()) {
    tab.addEventListener("click", () => {
      const mode = tab.dataset.analysisMode;
      if (isAnalysisMode(mode)) onSelect(mode);
    });
    tab.addEventListener("keydown", event => {
      const target = event.key === "Home" ? tabs[0]
        : event.key === "End" ? tabs.at(-1)
          : event.key === "ArrowRight" ? tabs[(index + 1) % tabs.length]
            : event.key === "ArrowLeft" ? tabs[(index + tabs.length - 1) % tabs.length]
              : null;
      if (!target) return;
      event.preventDefault();
      event.stopPropagation();
      for (const candidate of tabs) candidate.tabIndex = candidate === target ? 0 : -1;
      target.focus();
    });
  }
}

export function restoreAnalysisTabFocus(
  root: ParentNode,
  mode: AnalysisMode,
): void {
  const tabs = root.querySelectorAll<HTMLButtonElement>("[data-analysis-mode]");
  for (const tab of tabs) {
    tab.tabIndex = tab.dataset.analysisMode === mode ? 0 : -1;
    if (tab.tabIndex === 0) tab.focus({ preventScroll: true });
  }
}
