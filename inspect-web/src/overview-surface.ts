export interface OverviewSurfaceOptions {
  subject: "package" | "library";
  subjectLabel: string;
  displayName: string;
  iconHtml: string;
  showIdentity?: boolean;
  details?: readonly string[];
  /** Enabled Library enablements; Not enabled and Unavailable are never passed. */
  enablements?: readonly OverviewEnablement[];
  packageId: string;
  packageVersion: string;
  activeFramework: string;
  totalTypes: number | null;
  totalMembers: number | null;
  contentHtml: string;
  escapeHtml: (value: unknown) => string;
}

interface OverviewEnablement {
  id: string;
  label: string;
}

function renderOverviewEnablements(
  enablements: readonly OverviewEnablement[],
  escapeHtml: (value: unknown) => string,
): string {
  if (enablements.length === 0) return "";
  return `<ul class="overview-enablements" aria-label="Enabled">${enablements
    .map(enablement => `<li class="overview-enablement" data-enablement="${escapeHtml(enablement.id)}">${escapeHtml(enablement.label)}</li>`)
    .join("")}</ul>`;
}

export interface PackageOverviewContentOptions {
  packageInfoHtml: string;
  packageChildrenHtml: string;
  documentsHtml: string;
}

export interface LibraryOverviewContentOptions {
  namespacesHtml: string;
  typeKindsHtml: string;
}

export function renderPackageOverviewContent(
  options: PackageOverviewContentOptions,
): string {
  return `<div class="package-overview-content">
    <div class="package-overview-summary">
      ${options.packageInfoHtml}
      ${options.packageChildrenHtml}
      ${options.documentsHtml}
    </div>
  </div>`;
}

export function renderLibraryOverviewContent(
  options: LibraryOverviewContentOptions,
): string {
  return `<div class="library-overview-content">
    <div class="library-overview-namespaces">${options.namespacesHtml}</div>
    <aside class="library-overview-kinds" aria-label="Library composition">
      ${options.typeKindsHtml}
    </aside>
  </div>`;
}

export function renderOverviewSurface(
  options: OverviewSurfaceOptions,
): string {
  const {
    subject, subjectLabel, displayName, iconHtml, showIdentity = true, details = [], enablements = [],
    totalTypes, totalMembers,
    contentHtml, escapeHtml,
  } = options;
  const typeCount = totalTypes === null
    ? "Type Count unavailable"
    : `${totalTypes.toLocaleString()} type${totalTypes === 1 ? "" : "s"}`;
  const memberCount = totalMembers === null
    ? "Member Count unavailable"
    : `${totalMembers.toLocaleString()} member${totalMembers === 1 ? "" : "s"}`;
  const surfaceHeader = subject === "package"
    ? ""
    : `<header class="api-surface-head overview-surface-head">
      <span class="overview-surface-label">Overview</span>
      <p>${typeCount} &middot; ${memberCount}</p>
    </header>`;
  return `<section class="overview-surface ${subject}-overview-surface" ${showIdentity ? `aria-labelledby="${subject}-overview-title"` : `aria-label="${escapeHtml(subjectLabel)}"`}>
    ${surfaceHeader}
    <div class="overview-scroll">
      ${showIdentity ? `<header class="overview-identity">
        ${iconHtml}
        <div class="overview-identity-text">
          ${subject === "package" ? "" : `<p class="overview-subject-label">${escapeHtml(subjectLabel)}</p>`}
          <h1 id="${subject}-overview-title">${escapeHtml(displayName)}</h1>
          ${details.map(detail => `<p class="overview-identity-detail">${escapeHtml(detail)}</p>`).join("")}
          ${renderOverviewEnablements(enablements, escapeHtml)}
        </div>
      </header>` : ""}
      ${contentHtml}
    </div>
  </section>`;
}
