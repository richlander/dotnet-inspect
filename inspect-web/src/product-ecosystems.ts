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

export function productEcosystemsViewHtml(
  escapeHtml: (value: unknown) => string,
  error: string,
): string {
  if (error) {
    return `<section class="ecosystem-catalog" aria-labelledby="ecosystems-heading">
      <h1 id="ecosystems-heading" tabindex="-1">Ecosystems</h1>
      <div class="empty-state">
        <h2>Ecosystems are unavailable</h2>
        <p role="alert">${escapeHtml(error)}</p>
      </div>
    </section>`;
  }

  if (catalogEntries.length === 0) {
    return `<section class="ecosystem-catalog" aria-labelledby="ecosystems-heading">
      <h1 id="ecosystems-heading" tabindex="-1">Ecosystems</h1>
      <p>Curated .NET product areas available for discovery.</p>
      <div class="empty-state">
        <h2>No Ecosystems are available.</h2>
      </div>
    </section>`;
  }

  return `<section class="ecosystem-catalog" aria-labelledby="ecosystems-heading">
    <h1 id="ecosystems-heading" tabindex="-1">Ecosystems</h1>
    <p>Curated .NET product areas and the discovery capabilities they provide.</p>
    <ul class="ecosystem-catalog-list">
      ${catalogEntries.map(ecosystem =>
        `<li class="ecosystem-catalog-row" data-ecosystem="${escapeHtml(ecosystem.id)}">
          <div class="ecosystem-catalog-heading">
            <strong>${escapeHtml(ecosystem.title)}</strong>
            ${ecosystem.hasWorkspaceRegistration
              ? `<button type="button" data-ecosystem-open="${escapeHtml(ecosystem.id)}" aria-label="Open ${escapeHtml(ecosystem.title)} Ecosystem">Open</button>`
              : ""}
          </div>
          <p>${escapeHtml(ecosystem.summary)}</p>
          <ul class="ecosystem-capabilities" aria-label="${escapeHtml(ecosystem.title)} capabilities">
            ${capabilityLabels(ecosystem).map(label =>
              `<li>${escapeHtml(label)}</li>`).join("")}
          </ul>
        </li>`).join("")}
    </ul>
  </section>`;
}
