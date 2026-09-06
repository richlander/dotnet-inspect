export interface OverviewSurfaceOptions {
  subject: "package" | "library";
  subjectLabel: string;
  displayName: string;
  iconHtml: string;
  details?: readonly string[];
  packageId: string;
  packageVersion: string;
  activeFramework: string;
  totalTypes: number;
  totalMembers: number;
  coordinateFieldsHtml?: string;
  contentHtml: string;
  escapeHtml: (value: unknown) => string;
}

export function renderOverviewSurface(
  options: OverviewSurfaceOptions,
): string {
  const {
    subject, subjectLabel, displayName, iconHtml, details = [],
    packageId, packageVersion, activeFramework, totalTypes, totalMembers,
    coordinateFieldsHtml, contentHtml, escapeHtml,
  } = options;
  const coordinate = `${packageId}@${packageVersion}`;
  return `<section class="overview-surface ${subject}-overview-surface${coordinateFieldsHtml ? " overview-with-controls" : ""}" aria-labelledby="${subject}-overview-title">
    <header class="api-surface-head overview-surface-head">
      <span class="overview-surface-label">Overview</span>
      <p>${totalTypes.toLocaleString()} type${totalTypes === 1 ? "" : "s"} &middot; ${totalMembers.toLocaleString()} member${totalMembers === 1 ? "" : "s"}</p>
    </header>
    ${coordinateFieldsHtml ? `<section class="overview-controls" aria-label="Package coordinate">
      <div class="package-coordinate-fields">${coordinateFieldsHtml}</div>
    </section>` : ""}
    <div class="overview-scroll">
      <header class="overview-identity">
        ${iconHtml}
        <div class="overview-identity-text">
          <p class="overview-subject-label">${escapeHtml(subjectLabel)}</p>
          <h1 id="${subject}-overview-title">${escapeHtml(displayName)}</h1>
          ${details.map(detail => `<p class="overview-identity-detail">${escapeHtml(detail)}</p>`).join("")}
        </div>
      </header>
      ${contentHtml}
    </div>
    <footer class="api-surface-footer overview-surface-footer">
      <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
      <span title="${escapeHtml(activeFramework)}">${escapeHtml(activeFramework)}</span>
    </footer>
  </section>`;
}
