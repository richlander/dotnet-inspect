import {
  isCompareMode,
  type CompareMode,
} from "./package-comparison-targets.ts";

// One Compare working surface at Library, Type, and Member. Diff and Clone are
// modes inside this frame rather than separate persistent inspectors; the mode
// control lives in the surface header and the Package-owned target or scope is
// explained, not edited, immediately below it. Every result state (loading,
// unavailable, failed, canceled, successful-empty) renders inside this same
// frame so the mode control and recovery action never disappear.

export type CompareSubjectKind = "library" | "type" | "member";

export interface CompareFrameOptions {
  readonly subjectKind: CompareSubjectKind;
  readonly subjectLabel: string;
  readonly mode: CompareMode;
  // The effective Package-owned Diff baseline or Clone scope, already resolved
  // to display text. Compare renders no second version or scope editor.
  readonly targetText: string;
  readonly status: string;
  readonly content: string;
  readonly escapeHtml: (value: unknown) => string;
}

const modes: readonly (readonly [CompareMode, string])[] = [
  ["diff", "Diff"],
  ["clone", "Clone"],
];

export function compareTargetLabel(mode: CompareMode): string {
  return mode === "diff" ? "Diff baseline" : "Clone scope";
}

export function renderCompareFrame(options: CompareFrameOptions): string {
  const { escapeHtml, mode } = options;
  const tabs = modes.map(([value, label]) =>
    `<button type="button" role="tab" id="compare-mode-${value}" data-compare-mode="${value}" aria-selected="${value === mode}" aria-controls="compare-panel" tabindex="${value === mode ? 0 : -1}">${label}</button>`,
  ).join("");
  return `<section class="compare-surface compare-surface-${options.subjectKind}" aria-labelledby="compare-title" data-compare-mode-active="${mode}">
    <header class="compare-head">
      <div class="compare-head-copy">
        <p class="compare-kicker">Compare · ${escapeHtml(subjectKindLabel(options.subjectKind))}</p>
        <h1 id="compare-title">${escapeHtml(options.subjectLabel)}</h1>
      </div>
      <div class="compare-mode-tabs" role="tablist" aria-label="Compare modes">${tabs}</div>
    </header>
    <div class="compare-target">
      <span class="compare-target-label">${escapeHtml(compareTargetLabel(mode))}</span>
      <span class="compare-target-value">${escapeHtml(options.targetText)}</span>
      <button type="button" class="compare-change-target" id="compare-change-target">Change target</button>
    </div>
    <p class="compare-status" role="status">${escapeHtml(options.status)}</p>
    <div id="compare-panel" class="compare-panel" role="tabpanel" aria-labelledby="compare-mode-${mode}">${options.content}</div>
  </section>`;
}

function subjectKindLabel(kind: CompareSubjectKind): string {
  switch (kind) {
    case "library": return "Library";
    case "type": return "Type";
    case "member": return "Member";
    default: {
      const exhaustive: never = kind;
      throw new Error(`Unhandled Compare subject kind: ${String(exhaustive)}`);
    }
  }
}

export function renderCompareRetry(label = "Retry comparison"): string {
  return `<button type="button" class="compare-retry" id="compare-retry">${label}</button>`;
}

export function renderCompareLoading(): string {
  return '<div class="compare-loading" aria-hidden="true"></div>';
}

export function renderCompareEmpty(
  title: string,
  detail: string,
  escapeHtml: (value: unknown) => string,
): string {
  return `<div class="compare-empty">
    <strong>${escapeHtml(title)}</strong>
    <span>${escapeHtml(detail)}</span>
  </div>`;
}

export interface CompareFrameActions {
  readonly selectMode: (mode: CompareMode) => void;
  readonly changeTarget: () => void;
  readonly retry: () => void;
}

// The tabs use manual activation: Left/Right and Home/End move focus, Enter or
// Space (the button's own click) selects, matching the Integrations inspector.
export function bindCompareFrame(
  root: ParentNode,
  actions: CompareFrameActions,
): void {
  const tabs = [
    ...root.querySelectorAll<HTMLButtonElement>("[data-compare-mode]"),
  ];
  for (const [index, tab] of tabs.entries()) {
    tab.addEventListener("click", () => {
      const mode = tab.dataset.compareMode;
      if (isCompareMode(mode)) actions.selectMode(mode);
    });
    tab.addEventListener("keydown", event => {
      const target = event.key === "Home" ? tabs[0]
        : event.key === "End" ? tabs.at(-1)
          : event.key === "ArrowRight" ? tabs[(index + 1) % tabs.length]
            : event.key === "ArrowLeft"
              ? tabs[(index + tabs.length - 1) % tabs.length]
              : null;
      if (!target) return;
      event.preventDefault();
      event.stopPropagation();
      for (const candidate of tabs)
        candidate.tabIndex = candidate === target ? 0 : -1;
      target.focus();
    });
  }
  root.querySelector("#compare-change-target")
    ?.addEventListener("click", actions.changeTarget);
  root.querySelector("#compare-retry")
    ?.addEventListener("click", actions.retry);
}

// Asynchronous result rendering replaces the surface; the selected tab keeps
// focus so a mode change never strands the keyboard user.
export function restoreCompareTabFocus(
  root: ParentNode,
  mode: CompareMode,
): void {
  const tabs = root.querySelectorAll<HTMLButtonElement>("[data-compare-mode]");
  for (const tab of tabs) {
    tab.tabIndex = tab.dataset.compareMode === mode ? 0 : -1;
    if (tab.tabIndex === 0) tab.focus({ preventScroll: true });
  }
}
