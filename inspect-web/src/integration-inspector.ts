export type IntegrationMode = "integrations" | "opportunities";

export function isIntegrationMode(value: string | undefined): value is IntegrationMode {
  return value === "integrations" || value === "opportunities";
}

export interface IntegrationInspectorContext {
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  pickerHtml: string;
  escapeHtml: (value: unknown) => string;
}

export function renderIntegrationInspector(
  context: IntegrationInspectorContext,
  mode: IntegrationMode,
  status: string,
  content: string,
): string {
  const { assemblyIdentity, assetPath, coordinate, pickerHtml, escapeHtml } = context;
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  const tabs = (["integrations", "opportunities"] as const).map(value =>
    `<button type="button" role="tab" id="integration-mode-${value}" data-integration-mode="${value}" aria-selected="${value === mode}" aria-controls="integration-results" tabindex="${value === mode ? 0 : -1}">${value === "integrations" ? "Integrations" : "Opportunities"}</button>`
  ).join("");
  return `<section class="integration-inspector library-${mode}-surface${pickerHtml ? ` library-${mode}-with-controls` : ""}" aria-labelledby="library-integrations-title">
    <header class="api-surface-head">
      <h1 id="library-integrations-title">Integrations</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
    </header>
    <div class="integration-mode-tabs" role="tablist" aria-label="Integration views">${tabs}</div>
    ${pickerHtml ? `<section class="library-${mode}-controls" aria-label="Integration scan library">${pickerHtml}</section>` : ""}
    <div id="integration-results" role="tabpanel" aria-labelledby="integration-mode-${mode}" tabindex="0" class="library-${mode}-scroll">${content}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(identity)}">${escapeHtml(identity)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}

export function bindIntegrationTabs(
  root: ParentNode,
  onSelect: (mode: IntegrationMode) => void,
): void {
  const tabs = [...root.querySelectorAll<HTMLButtonElement>("[data-integration-mode]")];
  for (const [index, tab] of tabs.entries()) {
    tab.addEventListener("click", () => {
      const mode = tab.dataset.integrationMode;
      if (isIntegrationMode(mode)) onSelect(mode);
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

export function restoreIntegrationTabFocus(root: ParentNode, mode: IntegrationMode): void {
  const tabs = root.querySelectorAll<HTMLButtonElement>("[data-integration-mode]");
  for (const tab of tabs) {
    tab.tabIndex = tab.dataset.integrationMode === mode ? 0 : -1;
    if (tab.tabIndex === 0) tab.focus({ preventScroll: true });
  }
}
