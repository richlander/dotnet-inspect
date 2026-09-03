import { trapModalTab } from "./shell-controls.ts";

// The Settings dialog is a dependency-injected render function with its rendered
// control bindings. `dotnet-inspect.ts` owns `state`, localStorage persistence,
// and the theme/taste effects, and passes each computed slice and action in.

export interface StyleTier {
  id: string;
  title: string;
  summary: string;
  byte_divergent?: boolean;
}

export interface StyleOption {
  id: string;
  tier: string;
  title: string;
  summary: string;
  oracle_endorsed?: boolean;
  conflict_group?: string;
}

export interface StyleCatalogState {
  styleTiers: readonly StyleTier[] | null;
  styleOptions: readonly StyleOption[] | null;
  styleCatalogError: string;
  taste: readonly string[];
}

type EscapeHtml = (value: unknown) => string;

export interface SettingsPanelBindingActions {
  onClose: () => void;
  onOpen: (from: "home" | "workbench") => void;
  onTasteClear: () => void;
  onTasteToggle: (taste: string) => void;
  onThemeSelect: (theme: "dark" | "light") => void;
}

export function bindSettingsPanel(
  root: ParentNode,
  actions: SettingsPanelBindingActions,
) {
  root.querySelector("#settings-close")?.addEventListener(
    "click",
    actions.onClose);
  root.querySelector("#home-settings")?.addEventListener(
    "click",
    () => actions.onOpen("home"));
  const backdrop = root.querySelector<HTMLElement>("#settings-backdrop");
  backdrop?.addEventListener("click", event => {
    if (event.target === backdrop) actions.onClose();
  });
  const dialog = root.querySelector<HTMLElement>("#settings-dialog");
  dialog?.addEventListener("keydown", event => {
    if (event.key === "Tab") trapModalTab(dialog, event);
  });
  root.querySelectorAll<HTMLElement>(".settings-seg[data-theme]")
    .forEach(button => button.addEventListener("click", () => {
      const theme = button.dataset.theme;
      if (theme === "dark" || theme === "light") {
        actions.onThemeSelect(theme);
      }
    }));
  root.querySelectorAll<HTMLElement>(".settings-taste [data-taste]")
    .forEach(checkbox => checkbox.addEventListener("change", () => {
      const taste = checkbox.dataset.taste;
      if (taste) actions.onTasteToggle(taste);
    }));
  root.querySelector("#settings-taste-clear")?.addEventListener(
    "click",
    actions.onTasteClear);
}

// The decompiler style ("taste") catalog, grouped by tier, as Settings checkbox
// rows kept in lockstep with the engine's StyleOptionCatalog.
export function styleCatalogGroupsHtml(catalog: StyleCatalogState, escapeHtml: EscapeHtml): string {
  const tiers = catalog.styleTiers || [];
  const options = catalog.styleOptions || [];
  if (!tiers.length || !options.length) {
    return catalog.styleCatalogError
      ? `<div class="taste-empty">Style catalog unavailable: ${escapeHtml(catalog.styleCatalogError)}</div>`
      : "";
  }
  return tiers
    .filter(tier => options.some(option => option.tier === tier.id))
    .map(tier => `
      <div class="taste-group">
        <div class="taste-group-head">
          <div class="taste-group-title">${escapeHtml(tier.title)}</div>
          ${tier.byte_divergent ? '<em class="taste-badge divergent">byte-divergent</em>' : ""}
        </div>
        <div class="taste-group-summary">${escapeHtml(tier.summary)}</div>
        ${options.filter(option => option.tier === tier.id).map(option => `
          <label class="taste-item">
            <input type="checkbox" data-taste="${escapeHtml(option.id)}" ${catalog.taste.includes(option.id) ? "checked" : ""} />
            <span class="taste-item-text">
              <span class="taste-item-title">${escapeHtml(option.title)}${option.oracle_endorsed ? '<em class="taste-badge oracle">oracle</em>' : ""}</span>
              <span class="taste-item-summary">${escapeHtml(option.summary)}</span>
            </span>
          </label>`).join("")}
      </div>`).join("");
}

export interface RenderSettingsViewOptions {
  theme: string;
  settingsReturn: string;
  styleCatalog: StyleCatalogState;
  escapeHtml: EscapeHtml;
}

// The Settings dialog: a persistent preferences panel. Every control here writes straight to
// localStorage (theme → inspect-theme, taste → inspect-taste) so choices survive a reload and
// future sessions.
export function renderSettingsView(options: RenderSettingsViewOptions): string {
  const { theme, styleCatalog, escapeHtml } = options;
  const catalog = styleCatalogGroupsHtml(styleCatalog, escapeHtml);
  const styleBody = catalog
    || '<div class="taste-empty">Style catalog is still loading — reopen Settings in a moment.</div>';
  const activeCount = styleCatalog.taste.length;
  return `
    <div id="settings-backdrop" class="modal-backdrop">
      <section id="settings-dialog" class="application-dialog settings-dialog"
        role="dialog" aria-modal="true" aria-labelledby="settings-title">
        <header class="application-dialog-head">
          <div>
            <p class="section-eyebrow">Application</p>
            <h2 id="settings-title" tabindex="-1">Settings</h2>
          </div>
          <button id="settings-close" class="settings-close">Close</button>
        </header>
        <div class="settings-main">
          <div class="settings-head">
            <p class="settings-lede">Preferences are stored locally in your browser and persist across sessions. Nothing is uploaded.</p>
          </div>

          <section class="settings-section">
            <div class="settings-section-head">
              <h2>Appearance</h2>
              <p>Choose the color theme for the whole app.</p>
            </div>
            <div class="settings-control">
              <div class="settings-segment" role="group" aria-label="Theme">
                <button type="button" class="settings-seg ${theme === "dark" ? "active" : ""}" data-theme="dark" aria-pressed="${theme === "dark"}">Dark</button>
                <button type="button" class="settings-seg ${theme === "light" ? "active" : ""}" data-theme="light" aria-pressed="${theme === "light"}">Light</button>
              </div>
            </div>
          </section>

          <section class="settings-section">
            <div class="settings-section-head">
              <h2>Decompiler style <span class="settings-badge">${activeCount ? `${activeCount} on` : "default"}</span></h2>
              <p>Tune how decompiled C# is spelled and synthesized — including <strong>readable local names</strong>. These apply to every source and call-graph view. The default is opcode-faithful.</p>
            </div>
            <div class="settings-taste">${styleBody}</div>
            <div class="settings-taste-foot">
              ${activeCount
                ? '<button id="settings-taste-clear" type="button" class="settings-reset">Reset to default</button>'
                : '<span class="settings-muted">Default · opcode-faithful</span>'}
            </div>
          </section>
        </div>
      </section>
    </div>`;
}
