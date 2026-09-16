export type QueryMode = "changes" | "packages";

export function renderQueryModeSelector(mode: QueryMode): string {
  const tab = (value: QueryMode, label: string) =>
    `<button type="button" role="tab" id="query-mode-${value}" data-query-mode="${value}" aria-selected="${mode === value}" tabindex="${mode === value ? "0" : "-1"}">${label}</button>`;
  return `
    <div class="query-mode-selector" role="tablist" aria-label="Query mode">
      ${tab("packages", "Packages")}
      ${tab("changes", "Activity")}
    </div>`;
}

export function bindQueryModeSelector(
  root: ParentNode,
  onChange: (mode: QueryMode) => void,
): void {
  const tabs = [
    ...root.querySelectorAll<HTMLButtonElement>("[data-query-mode]"),
  ];
  function activate(tab: HTMLButtonElement): void {
    const mode = tab.dataset.queryMode;
    if (mode !== "packages" && mode !== "changes") return;
    onChange(mode);
  }
  for (const tab of tabs) {
    tab.addEventListener("click", () => activate(tab));
    tab.addEventListener("keydown", event => {
      if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
        return;
      }
      event.preventDefault();
      const index = tabs.indexOf(tab);
      const nextIndex = event.key === "Home"
        ? 0
        : event.key === "End"
          ? tabs.length - 1
          : event.key === "ArrowLeft"
            ? (index - 1 + tabs.length) % tabs.length
            : (index + 1) % tabs.length;
      const next = tabs[nextIndex];
      if (!next) return;
      activate(next);
      queueMicrotask(() =>
        root.querySelector<HTMLElement>(
          `[data-query-mode="${next.dataset.queryMode}"]`)?.focus());
    });
  }
}
