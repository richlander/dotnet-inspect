import { assertNever } from "./data.ts";

export type AnalysisMode =
  | "complexity"
  | "dependencies"
  | "relationships"
  | "performance"
  | "resource-triage"
  | "unsafe"
  | "integrations";

export const defaultAnalysisMode: AnalysisMode = "relationships";

// Analysis is one inspector at Library, Type, and Member scope. Each scope
// lists only the modes it can render and has its own default. Type
// Relationships is offered only for package-backed Types, so a Type's modes
// and default depend on that context.
export type AnalysisScope = "library" | "type" | "member";
export type TypeAnalysisMode = "relationships" | "unsafe";
export type MemberAnalysisMode = "unsafe";

export interface AnalysisScopeContext {
  readonly relationshipsOffered: boolean;
}

const packageTypeContext: AnalysisScopeContext = { relationshipsOffered: true };

export const defaultMemberAnalysisMode: MemberAnalysisMode = "unsafe";

export function defaultTypeAnalysisMode(
  context: AnalysisScopeContext,
): TypeAnalysisMode {
  return context.relationshipsOffered ? "relationships" : "unsafe";
}

export function isTypeAnalysisMode(
  value: string | null | undefined,
): value is TypeAnalysisMode {
  return value === "relationships" || value === "unsafe";
}

export function isMemberAnalysisMode(
  value: string | null | undefined,
): value is MemberAnalysisMode {
  return value === "unsafe";
}

export type AnalysisSelection =
  | { scope: "library"; mode: AnalysisMode }
  | { scope: "type"; mode: TypeAnalysisMode }
  | { scope: "member"; mode: MemberAnalysisMode };

export function isAnalysisScope(value: string | undefined): value is AnalysisScope {
  return value === "library" || value === "type" || value === "member";
}

/** The scope's default when a requested mode is absent or not listed there. */
export function analysisModeForScope(scope: "library", value: string | null | undefined): AnalysisMode;
export function analysisModeForScope(scope: "type", value: string | null | undefined, context: AnalysisScopeContext): TypeAnalysisMode;
export function analysisModeForScope(scope: "member", value: string | null | undefined): MemberAnalysisMode;
export function analysisModeForScope(
  scope: AnalysisScope,
  value: string | null | undefined,
  context: AnalysisScopeContext = packageTypeContext,
): string {
  switch (scope) {
    case "library": return isAnalysisMode(value ?? undefined) ? value! : defaultAnalysisMode;
    case "type":
      return isTypeAnalysisMode(value) && analysisModeIsListed(scope, value, context)
        ? value
        : defaultTypeAnalysisMode(context);
    case "member": return isMemberAnalysisMode(value) ? value : defaultMemberAnalysisMode;
    default: return assertNever(scope, "Analysis scope");
  }
}

export function analysisModeIsListed(
  scope: AnalysisScope,
  value: string,
  context: AnalysisScopeContext = packageTypeContext,
): boolean {
  return analysisModesFor(scope, context).some(([mode]) => mode === value);
}

export function defaultAnalysisModeFor(
  scope: AnalysisScope,
  context: AnalysisScopeContext = packageTypeContext,
): string {
  switch (scope) {
    case "library": return defaultAnalysisMode;
    case "type": return defaultTypeAnalysisMode(context);
    case "member": return defaultMemberAnalysisMode;
    default: return assertNever(scope, "Analysis scope");
  }
}

export function isAnalysisMode(
  value: string | undefined,
): value is AnalysisMode {
  return value === "complexity"
    || value === "dependencies"
    || value === "relationships"
    || value === "performance"
    || value === "resource-triage"
    || value === "unsafe"
    || value === "integrations";
}

export interface AnalysisInspectorContext {
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  pickerHtml: string;
  escapeHtml: (value: unknown) => string;
}

const libraryModes = [
  [defaultAnalysisMode, "Relationships"],
  ["dependencies", "Dependencies"],
  ["complexity", "Complexity"],
  ["integrations", "Integrations"],
  ["performance", "Performance Triage"],
  ["resource-triage", "Resource Triage"],
  ["unsafe", "Unsafe"],
] as const satisfies readonly (readonly [AnalysisMode, string])[];

const packageTypeModes = [
  ["relationships", "Relationships"],
  ["unsafe", "Unsafe"],
] as const satisfies readonly (readonly [TypeAnalysisMode, string])[];

const platformTypeModes = [
  ["unsafe", "Unsafe"],
] as const satisfies readonly (readonly [TypeAnalysisMode, string])[];

const memberModes = [
  [defaultMemberAnalysisMode, "Unsafe"],
] as const satisfies readonly (readonly [MemberAnalysisMode, string])[];

export function analysisModesFor(
  scope: AnalysisScope,
  context: AnalysisScopeContext = packageTypeContext,
): readonly (readonly [string, string])[] {
  switch (scope) {
    case "library": return libraryModes;
    case "type": return context.relationshipsOffered ? packageTypeModes : platformTypeModes;
    case "member": return memberModes;
    default: return assertNever(scope, "Analysis scope");
  }
}

export function renderAnalysisInspector(
  context: Pick<AnalysisInspectorContext, "escapeHtml"> & Partial<AnalysisInspectorContext>,
  mode: AnalysisMode,
  status: string,
  content: string,
  scope: AnalysisScope = "library",
  scopeContext: AnalysisScopeContext = packageTypeContext,
): string {
  const { pickerHtml, escapeHtml } = context;
  const modes = analysisModesFor(scope, scopeContext);
  // A scope with one mode shows no tab strip; its content is the panel.
  const tabbed = modes.length > 1;
  const tabs = modes.map(([value, label]) => {
    return `<button type="button" role="tab" id="analysis-mode-${value}" data-analysis-scope="${scope}" data-analysis-mode="${value}" aria-selected="${value === mode}" aria-controls="analysis-results" tabindex="${value === mode ? 0 : -1}">${label}</button>`
  }).join("");
  return `<section class="analysis-inspector library-analysis-surface${scope === "library" ? "" : ` ${scope}-analysis-surface`} ${scope}-${mode}-surface${pickerHtml ? ` library-analysis-with-controls library-${mode}-with-controls` : ""}" data-analysis-inspector="${scope}" aria-labelledby="${scope}-analysis-title">
    <header class="api-surface-head">
      <h1 id="${scope}-analysis-title">Analysis</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
      ${tabbed ? `<div class="analysis-mode-tabs" role="tablist" aria-label="Analysis views">${tabs}</div>` : ""}
    </header>
    ${pickerHtml ? `<section class="library-analysis-controls library-${mode}-controls" aria-label="Analysis library">${pickerHtml}</section>` : ""}
    <div id="analysis-results" ${tabbed ? `role="tabpanel" aria-labelledby="analysis-mode-${mode}"` : `role="region" aria-labelledby="${scope}-analysis-title"`} tabindex="0" data-analysis-mode-panel="${mode}" class="library-analysis-scroll library-${mode}-scroll">${content}</div>
  </section>`;
}

// A tab's selection, validated against its own scope's mode list.
function tabSelection(tab: HTMLElement): AnalysisSelection | null {
  const mode = tab.dataset.analysisMode;
  switch (tab.dataset.analysisScope ?? "library") {
    case "library": return isAnalysisMode(mode) ? { scope: "library", mode } : null;
    case "type": return isTypeAnalysisMode(mode) ? { scope: "type", mode } : null;
    case "member": return isMemberAnalysisMode(mode) ? { scope: "member", mode } : null;
    default: return null;
  }
}

export function bindAnalysisTabs(
  root: ParentNode,
  onSelect: (selection: AnalysisSelection) => void,
): void {
  const tabs = [...root.querySelectorAll<HTMLButtonElement>("[data-analysis-mode]")];
  for (const [index, tab] of tabs.entries()) {
    tab.addEventListener("click", () => {
      const selection = tabSelection(tab);
      if (selection) onSelect(selection);
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
