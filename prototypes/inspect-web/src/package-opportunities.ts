import type {
  BrowserOpportunityItem,
  BrowserPackageOpportunities,
} from "./facades/inspect-web-analysis.d.ts";

export type OpportunityItem = BrowserOpportunityItem;
type PackageOpportunities = Pick<
  BrowserPackageOpportunities,
  "categories" | "totalOpportunities" | "isComplete" | "inspectionError"
>;

export interface RenderPackageOpportunitiesOptions {
  libraryName: string;
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  requireLibrary: boolean;
  pickerHtml: string;
  fresh: boolean;
  loading: boolean;
  error: string;
  data: PackageOpportunities | null;
  escapeHtml: (value: unknown) => string;
}

export interface PackageOpportunitiesBindingActions {
  onLookForSelect: (query: string) => void;
  onPackageSelect: (packageId: string) => void;
  onTypeSelect: (target: PackageOpportunityTarget) => void;
}

export interface PackageOpportunityTarget {
  typeId: string;
  sourceIdentity: "legacy" | "exact" | "unknown";
  sourceDefinitionId: string | null;
  sourceAssembly: string | null;
  sourceAssemblyVersion: string | null;
  sourceAssemblyCulture: string | null;
  sourceAssemblyPublicKeyToken: string | null;
}

export function bindPackageOpportunities(
  root: ParentNode,
  actions: PackageOpportunitiesBindingActions,
) {
  root.querySelectorAll<HTMLElement>("[data-opp-type]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onTypeSelect({
        typeId: button.dataset.oppType ?? "",
        sourceIdentity: button.dataset.oppSourceIdentity === "exact"
          ? "exact"
          : button.dataset.oppSourceIdentity === "unknown"
            ? "unknown"
            : "legacy",
        sourceDefinitionId: button.dataset.oppSourceDefinition ?? null,
        sourceAssembly: button.dataset.oppSourceAssembly ?? null,
        sourceAssemblyVersion: button.dataset.oppSourceVersion ?? null,
        sourceAssemblyCulture: button.dataset.oppSourceCulture || null,
        sourceAssemblyPublicKeyToken: button.dataset.oppSourceToken || null,
      })));
  root.querySelectorAll<HTMLElement>("[data-opp-package]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onPackageSelect(button.dataset.oppPackage ?? "")));
  root.querySelectorAll<HTMLElement>("[data-opp-lookfor]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onLookForSelect(button.dataset.oppLookfor ?? "")));
}

// Splits a fully-qualified API name (e.g. "System.Collections.Generic.List<T>") into a short
// display name and its qualifier, cutting before any generic-arity or parameter-list suffix so
// the suffix renders alongside the short name rather than the qualifier.
function splitApiName(fullName: string): { short: string; qualifier: string } {
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

// Pulls a leading dotted namespace (a candidate package like "Microsoft.Extensions.AI") off the
// front of an integration-kind phrase so it can render as a load-on-demand package chip. Kinds
// with no dotted prefix (e.g. "IServiceCollection registration") stay as plain muted text.
function splitOpportunityKind(integrationType: string): { package: string | null; text: string } {
  const match = integrationType.match(/^([A-Z][A-Za-z0-9]+(?:\.[A-Z][A-Za-z0-9]+)+)\b\s*(.*)$/);
  const packageName = match?.[1];
  return packageName
    ? { package: packageName, text: match?.[2]?.trim() ?? "" }
    : { package: null, text: integrationType };
}

// Turns the comma-separated "look for" hint into chips. Concrete identifiers open a spotlight
// search (seeded on the base name, generics stripped); wildcard patterns like "Add*" render as
// muted, non-interactive hints because they are naming shapes rather than resolvable types.
function renderLookForChips(lookFor: string, escapeHtml: (value: unknown) => string): string {
  const tokens = lookFor.split(",").map(token => token.trim()).filter(Boolean);
  if (!tokens.length) return `<span class="opp-pattern">any registration surface</span>`;
  return tokens.map(token => {
    if (token.includes("*")) return `<span class="opp-pattern" title="Naming pattern">${escapeHtml(token)}</span>`;
    const seed = token.replace(/<.*$/, "");
    return `<button class="opp-chip" data-opp-lookfor="${escapeHtml(seed)}" title="Search the workspace for ${escapeHtml(token)}">${escapeHtml(token)}</button>`;
  }).join("");
}

// Renders a single integration-opportunity as a signal-style row with live chips: the API
// (a type in this package) navigates in place; a suggested package (a dotted namespace parsed
// from the integration kind) loads on demand; each concrete "look for" API opens the spotlight
// search. Naming patterns (wildcards) stay as muted, non-clickable hints.
function renderOpportunityRow(item: OpportunityItem, escapeHtml: (value: unknown) => string): string {
  const api = splitApiName(item.api);
  const kind = splitOpportunityKind(item.integrationType);
  const hasSourceIdentity = Object.prototype.hasOwnProperty.call(
    item,
    "sourceDefinitionId");
  const sourceIdentity = !hasSourceIdentity
    ? ""
    : item.sourceDefinitionId
      ? ` data-opp-source-identity="exact" data-opp-source-definition="${escapeHtml(item.sourceDefinitionId)}" data-opp-source-assembly="${escapeHtml(item.sourceAssembly)}" data-opp-source-version="${escapeHtml(item.sourceAssemblyVersion)}" data-opp-source-culture="${escapeHtml(item.sourceAssemblyCulture ?? "")}" data-opp-source-token="${escapeHtml(item.sourceAssemblyPublicKeyToken ?? "")}"`
      : ` data-opp-source-identity="unknown"`;
  const kindHtml = kind.package
    ? `<button class="opp-package-chip" data-opp-package="${escapeHtml(kind.package)}" title="Load ${escapeHtml(kind.package)} into the workspace">${escapeHtml(kind.package)}</button>${kind.text ? `<span class="opp-kind-text">${escapeHtml(kind.text)}</span>` : ""}`
    : `<span class="opp-kind-text">${escapeHtml(item.integrationType)}</span>`;
  return `
    <div class="opp-row" role="listitem">
      <span class="signal-badge signal-type">T</span>
      <div class="opp-body">
        <div class="opp-head">
          <button class="opp-type-chip" data-opp-type="${escapeHtml(item.api)}"${sourceIdentity} title="Open ${escapeHtml(item.api)} in this package">
            <span class="opp-type-name">${escapeHtml(api.short)}</span>${api.qualifier ? `<span class="opp-type-ns">${escapeHtml(api.qualifier)}</span>` : ""}
          </button>
          <span class="opp-kind">${kindHtml}</span>
        </div>
        <div class="opp-lookfor"><span class="opp-lookfor-label">look for</span>${renderLookForChips(item.lookFor, escapeHtml)}</div>
      </div>
    </div>`;
}

export function renderPackageOpportunities(options: RenderPackageOpportunitiesOptions): string {
  const {
    libraryName, assemblyIdentity, assetPath, coordinate,
    requireLibrary, pickerHtml, fresh, loading, error, data, escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (requireLibrary) {
    status = "Select a library";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Pick a library to scan</h2><p>Choose a .NET platform library above to compare its public surface against ecosystem integration patterns.</p></section>`;
  } else if (loading && fresh) {
    status = "Scanning opportunities\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Scanning opportunities&hellip;</h2><p>Comparing the public surface against ecosystem integration patterns.</p></section>`;
  } else if (fresh && error) {
    status = "Scan failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x25B3;</span><h2>Opportunity scan failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else {
    const resolved = fresh ? data : null;
    if (!resolved) {
      status = "Loading\u2026";
      content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
    } else {
      const categories = resolved.categories;
      const partial = !resolved.isComplete || Boolean(resolved.inspectionError);
      status = `${categories.length.toLocaleString()} area${categories.length === 1 ? "" : "s"} \u00b7 ${resolved.totalOpportunities.toLocaleString()} suggestion${resolved.totalOpportunities === 1 ? "" : "s"}${partial ? " \u00b7 partial" : ""}`;
      const warning = partial
        ? `<section class="document-section metadata-warning"><strong>&#x26A0; This library could not be scanned completely</strong>${resolved.inspectionError ? `<ul><li><code>${escapeHtml(resolved.inspectionError)}</code></li></ul>` : ""}</section>`
        : "";
      const note = categories.length
        ? `<p class="library-opportunities-note">Types open in this package; suggested packages load on demand; each concrete "look for" API opens a workspace search.</p>`
        : "";
      const blocks = categories.map((category, index) => {
        const rows = category.items.map(item => renderOpportunityRow(item, escapeHtml)).join("");
        return `<section class="opportunity-category" aria-labelledby="opportunity-category-${index}">
          <div class="section-title"><h2 id="opportunity-category-${index}">${escapeHtml(category.integration)}</h2><span>${category.items.length} suggestion${category.items.length === 1 ? "" : "s"}</span></div>
          <div class="opp-list" role="list">${rows}</div>
        </section>`;
      }).join("");
      const empty = partial
        ? `<section class="document-section empty-document"><h2>Opportunity scan incomplete</h2><p>No opportunity suggestions are available from this incomplete scan.</p></section>`
        : `<section class="document-section empty-document"><span class="large-glyph">&#x25C7;</span><h2>No integration opportunities</h2><p>The public surface of ${escapeHtml(libraryName)} shows no obvious auth, cloud-client, configuration, database, or AI-client patterns that suggest a missing ecosystem integration.</p></section>`;
      content = `${warning}${note}${categories.length ? blocks : empty}`;
    }
  }
  const identity = assetPath ? `${assetPath} \u00b7 ${assemblyIdentity}` : assemblyIdentity;
  return `<section class="library-opportunities-surface${pickerHtml ? " library-opportunities-with-controls" : ""}" aria-labelledby="library-opportunities-title">
    <header class="api-surface-head">
      <h1 id="library-opportunities-title">Opportunities</h1>
      <p title="${escapeHtml(status)}">${escapeHtml(status)}</p>
    </header>
    ${pickerHtml ? `<section class="library-opportunities-controls" aria-label="Opportunity scan library">${pickerHtml}</section>` : ""}
    <div class="library-opportunities-scroll">${content}</div>
    <footer class="metadata-surface-footer">
      <span title="${escapeHtml(identity)}">${escapeHtml(identity)}</span>
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
    </footer>
  </section>`;
}
