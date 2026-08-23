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
        // A missing payload used to be rewritten to a *different, specific* runtime pack,
        // which is a silent substitution rather than a default: the user would be shown
        // another pack's library without anything reporting it. There is no correct pack to
        // guess, so the selection is not made.
        const pack = select.selectedOptions[0]?.dataset.pack;
        if (!pack) return;
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
