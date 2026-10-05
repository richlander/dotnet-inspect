import type {
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "./facades/inspect-web-analysis.d.ts";
import { renderAnalysisInspector } from "./analysis-inspector.ts";
import { renderOpportunityRow } from "./package-opportunities.ts";

export interface LibraryIntegrationsOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  integrationsFresh: boolean;
  integrationsLoading: boolean;
  integrationsError: string;
  integrationsData: BrowserPackageIntegrations | null;
  suggestionsFresh: boolean;
  suggestionsLoading: boolean;
  suggestionsError: string;
  suggestionsData: BrowserPackageOpportunities | null;
  escapeHtml: (value: unknown) => string;
}

type IntegrationSignal =
  BrowserPackageIntegrations["categories"][number]["signals"][number];
type IntegrationSuggestion =
  BrowserPackageOpportunities["categories"][number]["items"][number];

interface IntegrationCategory {
  name: string;
  signals: IntegrationSignal[];
  suggestions: IntegrationSuggestion[];
}

export function renderLibraryIntegrationsSurface(options: LibraryIntegrationsOptions): string {
  const {
    libraryName, requireLibrary, integrationsFresh, integrationsLoading,
    integrationsError, integrationsData, suggestionsFresh, suggestionsLoading,
    suggestionsError, suggestionsData, escapeHtml,
  } = options;
  const integrations = integrationsFresh ? integrationsData : null;
  const suggestions = suggestionsFresh ? suggestionsData : null;
  const loading = (integrationsFresh && integrationsLoading)
    || (suggestionsFresh && suggestionsLoading);
  const errors = [
    integrationsFresh && integrationsError
      ? `Detected integrations: ${integrationsError}`
      : "",
    suggestionsFresh && suggestionsError
      ? `Suggested integrations: ${suggestionsError}`
      : "",
  ].filter(Boolean);
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25C8;</span><h2>Pick a library to scan</h2><p>Choose a .NET platform library above to find detected and suggested ecosystem integrations.</p></section>`;
  } else if (!integrations && !suggestions && loading) {
    status = "Scanning integrations\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Scanning integrations&hellip;</h2><p>Reading the public surface of ${escapeHtml(libraryName)} for detected and suggested ecosystem integrations.</p></section>`;
  } else if (!integrations && !suggestions && errors.length) {
    status = "Scan failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25C8;</span><h2>Integration scan failed</h2><ul>${errors.map(error => `<li>${escapeHtml(error)}</li>`).join("")}</ul></section>`;
  } else if (!integrations && !suggestions) {
    status = "Loading\u2026";
    content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
  } else {
    const categories = mergeCategories(integrations, suggestions);
    const totalSignals = integrations?.totalSignals ?? 0;
    const totalSuggestions = suggestions?.totalOpportunities ?? 0;
    const diagnostics = [
      ...errors,
      integrations && (!integrations.isComplete || integrations.inspectionError)
        ? integrations.inspectionError
          || "Detected integrations could not be scanned completely."
        : "",
      suggestions && (!suggestions.isComplete || suggestions.inspectionError)
        ? suggestions.inspectionError
          || "Suggested integrations could not be scanned completely."
        : "",
    ].filter(Boolean);
    const partial = loading || diagnostics.length > 0;
    status = `${categories.length.toLocaleString()} categor${categories.length === 1 ? "y" : "ies"} \u00b7 ${totalSignals.toLocaleString()} detected \u00b7 ${totalSuggestions.toLocaleString()} suggested${loading ? " \u00b7 scanning\u2026" : partial ? " \u00b7 partial" : ""}`;
    const warning = partial
      ? `<section class="document-section metadata-warning"><strong>&#x26A0; ${loading ? "The integration scan is still running" : "This library could not be scanned completely"}</strong>${diagnostics.length ? `<ul>${diagnostics.map(diagnostic => `<li><code>${escapeHtml(diagnostic)}</code></li>`).join("")}</ul>` : ""}</section>`
      : "";
    const note = totalSuggestions > 0
      ? `<p class="library-integrations-note">Detected entries describe current API signals. Suggested entries identify ecosystem integrations the library may support; their types, packages, and concrete APIs are interactive.</p>`
      : "";
    const blocks = categories.map((category, index) => {
      const signals = [...category.signals].sort((a, b) => {
        const rank = (shape: string) => /type/i.test(shape) ? 0 : 1;
        return rank(a.shape) - rank(b.shape) || a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name);
      });
      const typeCount = signals.filter(signal =>
        /type/i.test(signal.shape)).length;
      const apiCount = signals.length - typeCount;
      const rows = signals.map(signal => {
        const isType = /type/i.test(signal.shape);
        const { short, qualifier } = splitSignalName(signal.name);
        return `<div class="signal-row" role="listitem" title="${escapeHtml(signal.name)} &middot; ${escapeHtml(signal.shape)} &middot; ${escapeHtml(signal.kind)}">
          <span class="signal-badge signal-${isType ? "type" : "api"}">${isType ? "T" : "&#402;"}</span>
          <span class="signal-body"><span class="signal-name">${escapeHtml(short)}</span>${qualifier ? `<span class="signal-ns">${escapeHtml(qualifier)}</span>` : ""}</span>
          <span class="signal-kind">${escapeHtml(signal.kind)}</span>
        </div>`;
      }).join("");
      const suggestedRows = category.suggestions
        .map(suggestion => renderOpportunityRow(suggestion, escapeHtml))
        .join("");
      const counts = [
        signals.length
          ? `${typeCount} type${typeCount === 1 ? "" : "s"}`
          : "",
        signals.length
          ? `${apiCount} API${apiCount === 1 ? "" : "s"}`
          : "",
        category.suggestions.length
          ? `${category.suggestions.length} suggested`
          : "",
      ].filter(Boolean).join(" &middot; ");
      return `<section class="integration-category" aria-labelledby="integration-category-${index}">
        <div class="section-title"><h2 id="integration-category-${index}">${escapeHtml(category.name)}</h2><span>${counts}</span></div>
        <div class="integration-list" role="list">${rows}${suggestedRows}</div>
      </section>`;
    }).join("");
    const empty = partial
      ? `<section class="document-section empty-document"><h2>Integration scan incomplete</h2><p>No integration results are available from this incomplete scan.</p></section>`
      : `<section class="document-section empty-document"><span class="large-glyph">&#x25C7;</span><h2>No ecosystem integrations found</h2><p>The public surface of ${escapeHtml(libraryName)} shows no detected or suggested ecosystem integrations.</p></section>`;
    content = `${warning}${note}${categories.length ? blocks : empty}`;
  }
  return renderAnalysisInspector(options, "integrations", status, content);
}

function mergeCategories(
  integrations: BrowserPackageIntegrations | null,
  suggestions: BrowserPackageOpportunities | null,
): IntegrationCategory[] {
  const categories: IntegrationCategory[] = [];
  const byName = new Map<string, IntegrationCategory>();
  const getCategory = (name: string) => {
    let category = byName.get(name);
    if (!category) {
      category = { name, signals: [], suggestions: [] };
      byName.set(name, category);
      categories.push(category);
    }
    return category;
  };
  for (const category of integrations?.categories ?? []) {
    getCategory(category.integration).signals.push(...category.signals);
  }
  for (const category of suggestions?.categories ?? []) {
    getCategory(category.integration).suggestions.push(...category.items);
  }
  return categories;
}

// Split before parameter/generic lists so their dots cannot become the name boundary.
function splitSignalName(fullName: string) {
  const paren = fullName.indexOf("(");
  const angle = fullName.indexOf("<");
  const bounds = [paren, angle].filter(i => i >= 0);
  const cut = bounds.length ? Math.min(...bounds) : -1;
  const head = cut < 0 ? fullName : fullName.slice(0, cut);
  const suffix = cut < 0 ? "" : fullName.slice(cut);
  const dot = head.lastIndexOf(".");
  return {
    short: (dot < 0 ? head : head.slice(dot + 1)) + suffix,
    qualifier: dot < 0 ? "" : head.slice(0, dot),
  };
}
