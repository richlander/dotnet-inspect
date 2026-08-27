// DOM bindings for library and accessibility filters plus .NET platform
// library selectors. The application root owns all resulting state and work.

export type PlatformLibraryLens =
  | "integrations"
  | "opportunities"
  | "analysis"
  | "metadata";

export interface LibraryControlBindingActions {
  onAccessibilityChipSelect: (accessibility: string) => void;
  onLibraryChipSelect: (library: string) => void;
  onLibraryJump: (library: string) => void;
  onPlatformLibrarySelect: (name: string, pack: string) => void;
  onPlatformLensLibrarySelect: (
    lens: PlatformLibraryLens,
    name: string,
    pack: string | undefined,
  ) => void;
}

const platformLensSelectors:
  readonly [selector: string, lens: PlatformLibraryLens][] = [
    ["[data-platform-integrations-library]", "integrations"],
    ["[data-platform-opportunities-library]", "opportunities"],
    ["[data-platform-analysis-library]", "analysis"],
    ["[data-platform-metadata-library]", "metadata"],
  ];

export function bindLibraryControls(
  root: ParentNode,
  actions: LibraryControlBindingActions,
) {
  root.querySelectorAll<HTMLElement>("[data-library-chip]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onLibraryChipSelect(button.dataset.libraryChip ?? "")));
  root.querySelectorAll<HTMLElement>("[data-access-chip]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onAccessibilityChipSelect(
        button.dataset.accessChip ?? "")));

  const libraryJump =
    root.querySelector<HTMLSelectElement>("#library-jump");
  libraryJump?.addEventListener(
    "change",
    () => actions.onLibraryJump(libraryJump.value));

  root.querySelectorAll<HTMLSelectElement>("[data-platform-library-select]")
    .forEach(select =>
      select.addEventListener("change", () => {
        const name = select.value;
        if (!name) return;
        const pack =
          select.selectedOptions[0]?.dataset.pack || "netcore.app";
        actions.onPlatformLibrarySelect(name, pack);
      }));

  for (const [selector, lens] of platformLensSelectors) {
    root.querySelectorAll<HTMLSelectElement>(selector).forEach(select =>
      select.addEventListener("change", () => {
        const name = select.value;
        if (!name) return;
        actions.onPlatformLensLibrarySelect(
          lens,
          name,
          select.selectedOptions[0]?.dataset.pack);
      }));
  }
}
