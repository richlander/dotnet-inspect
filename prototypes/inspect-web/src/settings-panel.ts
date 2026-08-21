// The Settings page (a persistent preferences panel) and the decompiler "taste" popover it
// shares its style catalog with. Both are pure, dependency-injected render functions: `dotnet-inspect.ts`
// owns `state`, localStorage persistence, and event wiring (`setTheme`, `toggleTaste`,
// `clearTaste`, `bindSettingsEvents`), and passes each computed slice in explicitly.

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

// The decompiler style ("taste") catalog, grouped by tier, as checkbox rows. Shared by the
// detail-view taste popover and the Settings page so both stay in lockstep with the engine's
// StyleOptionCatalog (fetched once into state.styleTiers/state.styleOptions).
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

export function renderTastePopover(catalog: StyleCatalogState, escapeHtml: EscapeHtml): string {
  const groups = styleCatalogGroupsHtml(catalog, escapeHtml);
  const body = groups || '<div class="taste-empty">Style catalog unavailable.</div>';
  return `
    <div class="taste-popover" id="taste-popover" role="dialog" aria-label="Decompiler taste">
      <div class="taste-head"><strong>Taste</strong><span>decompiler style knobs</span></div>
      <div class="taste-body">${body}</div>
      <div class="taste-foot">${catalog.taste.length ? '<button id="taste-clear" type="button">reset to default</button>' : '<span>default · opcode-faithful</span>'}</div>
    </div>`;
}

export interface RenderSettingsViewOptions {
  theme: string;
  settingsReturn: string;
  styleCatalog: StyleCatalogState;
  escapeHtml: EscapeHtml;
}

// The Settings page: a persistent preferences panel. Every control here writes straight to
// localStorage (theme → inspect-theme, taste → inspect-taste) so choices survive a reload and
// future sessions. Grouped into Appearance and Decompiler style; the latter reuses the same
// style-option catalog the detail-view taste popover shows.
export function renderSettingsView(options: RenderSettingsViewOptions): string {
  const { theme, settingsReturn, styleCatalog, escapeHtml } = options;
  const catalog = styleCatalogGroupsHtml(styleCatalog, escapeHtml);
  const styleBody = catalog
    || '<div class="taste-empty">Style catalog is still loading — reopen Settings in a moment.</div>';
  const activeCount = styleCatalog.taste.length;
  return `
    <div class="settings-page">
      <header class="settings-bar">
        <a class="brand" href="/" aria-label="dotnet inspect home"><span class="brand-glyph">◇</span><span>dotnet-inspect</span></a>
        <button id="settings-close" class="settings-close">${settingsReturn === "workbench" ? "back to workbench" : "back to home"} ✕</button>
      </header>
      <main class="settings-main">
        <div class="settings-head">
          <h1>Settings</h1>
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
      </main>
    </div>`;
}
