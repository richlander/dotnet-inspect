export interface PackageOverviewSurfaceOptions {
  packageId: string;
  packageVersion: string;
  activeFramework: string;
  totalTypes: number;
  totalMembers: number;
  coordinateFieldsHtml: string;
  contentHtml: string;
  escapeHtml: (value: unknown) => string;
}

export function renderPackageOverviewSurface(
  options: PackageOverviewSurfaceOptions,
): string {
  const {
    packageId, packageVersion, activeFramework, totalTypes, totalMembers,
    coordinateFieldsHtml, contentHtml, escapeHtml,
  } = options;
  const coordinate = `${packageId}@${packageVersion}`;
  return `<section class="package-overview-surface" aria-labelledby="package-overview-surface-title">
    <header class="api-surface-head package-overview-surface-head">
      <h1 id="package-overview-surface-title">Overview</h1>
      <p>${totalTypes.toLocaleString()} type${totalTypes === 1 ? "" : "s"} &middot; ${totalMembers.toLocaleString()} member${totalMembers === 1 ? "" : "s"}</p>
    </header>
    <section class="package-overview-controls" aria-label="Package coordinate">
      <div class="package-coordinate-fields">${coordinateFieldsHtml}</div>
    </section>
    <div class="package-overview-scroll">
      ${contentHtml}
    </div>
    <footer class="api-surface-footer package-overview-surface-footer">
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
      <span title="${escapeHtml(activeFramework)}">${escapeHtml(activeFramework)}</span>
    </footer>
  </section>`;
}
