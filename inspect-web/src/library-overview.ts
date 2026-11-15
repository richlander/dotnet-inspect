import type {
  BrowserLibraryDocumentInspection,
} from "./facades/inspect-web-library.d.ts";

// Exact-Library Overview: the Type Overview frame (summary, then a fact
// table) over metadata-cheap Library document facts. Values arrive already
// spelled by the shared C# `LibraryFactLabels` owner; this module only
// arranges them.

type FactRow = readonly [key: string, value: string];

export interface LibraryOverviewOptions {
  name: string;
  asset: string;
  /** The Overview facts inspection, or null while it is loading or failed. */
  inspection: BrowserLibraryDocumentInspection | null;
  /** A failed facts request; empty while loading or available. */
  error: string;
  /**
   * The unfiltered row count of the Type list that Navigate opens, so the
   * button and its destination agree; null when not yet known.
   */
  typeCount: number | null;
  /** Enabled Library enablements; Not enabled and Unavailable are never passed. */
  enablements: readonly { id: string; label: string }[];
  escapeHtml: (value: unknown) => string;
  factRows: (rows: readonly FactRow[]) => string;
}

// Matches the CLI's canonical byte spelling (`0.#` over powers of 1024).
export function formatLibraryImageSize(bytes: number): string {
  const units: [number, string][] = [
    [1024 ** 3, "GB"],
    [1024 ** 2, "MB"],
    [1024, "KB"],
  ];
  for (const [size, unit] of units) {
    if (bytes >= size) return `${Math.round(bytes / size * 10) / 10} ${unit}`;
  }
  return `${bytes} B`;
}

function plural(count: number, singular: string, pluralLabel: string) {
  return `${count.toLocaleString()} ${count === 1 ? singular : pluralLabel}`;
}

export function libraryOverviewFactRows(
  asset: string,
  inspection: BrowserLibraryDocumentInspection,
): FactRow[] {
  const assembly = inspection.assembly;
  const image = inspection.image ?? null;
  const description = inspection.description ?? null;
  const rows: [string, string | null | undefined][] = [
    ["Name", assembly?.name],
    ["Asset", asset],
    ["Assembly Version", assembly?.version],
    ["Informational Version", description?.informationalVersion],
    ["Target Framework", image?.targetFramework],
    ["Public Key Token", assembly?.publicKeyToken],
    ["File Size", image ? formatLibraryImageSize(image.imageBytes) : null],
    ["Compilation", image?.compilation],
    ["Architecture", image?.architecture],
    // CLI Library Info shows Signed only for a signed image.
    ["Signed", image?.strongNameSigned ? "Yes" : null],
    ["Reproducible", image?.reproducible],
    ["Company", description?.company],
    ["Product", description?.product],
    ["Copyright", description?.copyright],
  ];
  return rows.filter((row): row is [string, string] =>
    typeof row[1] === "string" && row[1].length > 0);
}

export function renderLibraryOverview(options: LibraryOverviewOptions): string {
  const {
    name, asset, inspection, error, typeCount, enablements, escapeHtml,
    factRows,
  } = options;
  const available = inspection?.outcome === "Available" ? inspection : null;
  const image = available?.image ?? null;
  const badges = image
    ? [
      image.compilation,
      image.architecture,
      image.strongNameSigned ? "signed" : null,
      image.reproducible === "Yes" ? "reproducible" : null,
    ].filter((badge): badge is string => Boolean(badge))
    : [];
  const typesLabel = typeCount === null
    ? "Types"
    : plural(typeCount, "type", "types");
  const badgeList = (label: string, values: readonly string[]) =>
    `<div class="subject-overview-summary-group">
      <span class="subject-overview-summary-label">${escapeHtml(label)}</span>
      <ul aria-label="${escapeHtml(label)}">${values
        .map(value => `<li data-overview-badge="${escapeHtml(value)}">${escapeHtml(value)}</li>`)
        .join("")}</ul>
    </div>`;
  const summary = `
    <section class="subject-overview-summary" aria-label="Library summary">
      ${badges.length ? badgeList("Library metadata", badges) : ""}
      ${enablements.length
        ? badgeList("Enabled", enablements.map(enablement => enablement.label))
        : ""}
      <div class="subject-overview-summary-group library-overview-navigation">
        <span class="subject-overview-summary-label">Navigate</span>
        <div><button type="button" data-library-types>${escapeHtml(typesLabel)}</button></div>
      </div>
    </section>`;

  let body: string;
  if (error) {
    body = `<section class="document-section metadata-surface-state empty-document">
      <span class="large-glyph">!</span>
      <h2>Library facts unavailable</h2>
      <p>${escapeHtml(error)}</p>
      <button type="button" data-library-api-retry>Retry</button>
    </section>`;
  } else if (!inspection) {
    body = `<section class="document-section metadata-surface-state source-progress">
      <span class="loader"></span>
      <h2>Reading library metadata…</h2>
      <p>Reading ${escapeHtml(name)} through the shared Library inspection.</p>
    </section>`;
  } else if (!available) {
    body = `<section class="document-section metadata-surface-state empty-document">
      <span class="large-glyph">!</span>
      <h2>Library facts unavailable</h2>
      <p>${escapeHtml(inspection.detail || `Could not inspect ${name}.`)}</p>
    </section>`;
  } else {
    const diagnostics = available.diagnostics.length
      ? `<section class="document-section metadata-warning" role="status">
          <p>This library could not be inspected completely.</p>
          <ul>${available.diagnostics.map(diagnostic =>
            `<li>${escapeHtml(diagnostic.summary)}</li>`).join("")}</ul>
        </section>`
      : "";
    body = `<section class="document-section metadata-shape-section">
        <div class="section-title"><h2>Library info</h2><span>assembly metadata</span></div>
        ${factRows(libraryOverviewFactRows(asset, available))}
      </section>${diagnostics}`;
  }

  return `<section class="metadata-surface library-overview-surface" aria-labelledby="library-overview-title">
    <header class="metadata-surface-head library-overview-head">
      <span class="library-overview-label">Overview</span>
      <h1 id="library-overview-title">${escapeHtml(name)}</h1>
    </header>
    <div class="metadata-surface-scroll">${summary}${body}</div>
  </section>`;
}
