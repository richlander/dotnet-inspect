import type {
  BrowserEcosystemCatalogEntry,
} from "./facades/inspect-web-catalog.d.ts";
import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";

export type ProductEcosystemCatalogEntry = BrowserEcosystemCatalogEntry;

let catalogEntries: readonly ProductEcosystemCatalogEntry[] = [];
const catalogIdSet = new Set<string>();
const ecosystemIconClasses: Readonly<Record<string, string>> = {
  "ecosystem.runtime": "sl-ecosystem-runtime",
  "ecosystem.aspnetcore": "sl-ecosystem-aspnetcore",
  "ecosystem.microsoft-extensions": "sl-ecosystem-extensions",
  "ecosystem.aspire": "sl-ecosystem-aspire",
  "ecosystem.ai": "sl-ecosystem-ai",
  "ecosystem.blazor": "sl-ecosystem-blazor",
  "ecosystem.maui": "sl-ecosystem-maui",
};

export function setProductEcosystemCatalog(
  ecosystems: readonly ProductEcosystemCatalogEntry[],
): void {
  catalogEntries = ecosystems.slice();
  catalogIdSet.clear();
  for (const ecosystem of ecosystems)
    catalogIdSet.add(ecosystem.id);
}

export function productEcosystemCatalog():
  readonly ProductEcosystemCatalogEntry[] {
  return catalogEntries;
}

export function isProductEcosystemId(
  value: string | undefined | null,
): value is string {
  return typeof value === "string" && catalogIdSet.has(value);
}

export function productEcosystemIconClass(id: string): string | null {
  return ecosystemIconClasses[id] ?? null;
}

export function isProductEcosystemsPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, ROUTED_ENTRY_PATHS.ecosystems);
}

function countLabel(count: number, singular: string, plural: string): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

function capabilityLabels(
  ecosystem: ProductEcosystemCatalogEntry,
): readonly string[] {
  const labels: string[] = [];
  if (ecosystem.corePackageCount > 0) {
    labels.push(countLabel(
      ecosystem.corePackageCount, "core package", "core packages"));
  }
  if (ecosystem.namespaceRootCount > 0) {
    labels.push(countLabel(
      ecosystem.namespaceRootCount, "namespace root", "namespace roots"));
  }
  if (ecosystem.toolPackageCount > 0) {
    labels.push(countLabel(ecosystem.toolPackageCount, "tool", "tools"));
  }
  if (ecosystem.demoCount > 0) {
    labels.push(countLabel(ecosystem.demoCount, "demo", "demos"));
  }
  if (ecosystem.hasPackageSet) labels.push("Package set");
  if (ecosystem.hasScanner) labels.push("Integration scanner");
  if (ecosystem.hasPopulationLoader) labels.push("Platform libraries");
  if (ecosystem.hasWorkspaceRegistration) labels.push("Workspace-ready");
  return labels;
}

function titleMonogram(title: string): string {
  const parts = title.match(/[A-Za-z0-9]+/g) ?? [];
  return parts.slice(0, 2)
    .map(part => part[0] ?? "")
    .join("")
    .toUpperCase() || "E";
}

function ecosystemIconHtml(
  ecosystem: ProductEcosystemCatalogEntry,
  escapeHtml: (value: unknown) => string,
): string {
  const iconClass = productEcosystemIconClass(ecosystem.id);
  return `<span class="ecosystem-catalog-icon-frame" aria-hidden="true">
    <span class="ecosystem-catalog-icon${
      iconClass ? ` spotlight-ecosystem-icon ${iconClass}` : ""
    }">${iconClass ? "" : escapeHtml(titleMonogram(ecosystem.title))}</span>
  </span>`;
}

function ecosystemCatalogIntro(): string {
  return `<header class="ecosystem-catalog-intro">
    <span>Product map</span>
    <h1 id="ecosystems-heading" tabindex="-1">Ecosystems</h1>
    <p>Explore curated .NET product areas and the discovery capabilities each one brings to your Workspace.</p>
  </header>`;
}

export function productEcosystemsViewHtml(
  escapeHtml: (value: unknown) => string,
  error: string,
): string {
  if (error) {
    return `<section class="ecosystem-catalog" aria-labelledby="ecosystems-heading">
      ${ecosystemCatalogIntro()}
      <div class="empty-state">
        <h2>Ecosystems are unavailable</h2>
        <p role="alert">${escapeHtml(error)}</p>
      </div>
    </section>`;
  }
  if (catalogEntries.length === 0) {
    return `<section class="ecosystem-catalog" aria-labelledby="ecosystems-heading">
      ${ecosystemCatalogIntro()}
      <div class="empty-state">
        <h2>No Ecosystems are available.</h2>
      </div>
    </section>`;
  }

  return `<section class="ecosystem-catalog" aria-labelledby="ecosystems-heading">
    ${ecosystemCatalogIntro()}
    <ul class="ecosystem-catalog-list">
      ${catalogEntries.map(ecosystem =>
        `<li class="ecosystem-catalog-row" data-ecosystem="${escapeHtml(ecosystem.id)}">
          ${ecosystemIconHtml(ecosystem, escapeHtml)}
          <div class="ecosystem-catalog-card">
            <div class="ecosystem-catalog-heading">
              <h2>${escapeHtml(ecosystem.title)}</h2>
              ${ecosystem.hasWorkspaceRegistration
                ? `<button type="button" data-ecosystem-open="${escapeHtml(ecosystem.id)}" aria-label="Open ${escapeHtml(ecosystem.title)} Ecosystem">Open</button>`
                : ""}
            </div>
            <p>${escapeHtml(ecosystem.summary)}</p>
            <ul class="ecosystem-capabilities" aria-label="${escapeHtml(ecosystem.title)} capabilities">
              ${capabilityLabels(ecosystem).map(label =>
                `<li>${escapeHtml(label)}</li>`).join("")}
            </ul>
          </div>
        </li>`).join("")}
    </ul>
  </section>`;
}
