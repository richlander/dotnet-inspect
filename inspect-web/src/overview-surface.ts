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

export interface TypeOverviewSubject {
  displayName: string;
  namespace: string;
  kind: string;
  accessibility?: string;
  assembly: string;
  members: number;
}

export interface TypeOverviewExactFacts {
  fullName: string;
  namespace: string | null;
  kind: string;
  accessibility: string | null;
  isSealed: boolean;
  isAbstract: boolean;
  isStatic: boolean;
  isByRefLike: boolean;
  isReadOnly: boolean;
  baseType: string | null;
  interfaces: readonly string[];
}

export interface TypeOverviewProjection {
  status: "loading" | "available" | "failed" | "unavailable";
  exact: TypeOverviewExactFacts | null;
  complete: boolean;
  detail?: string;
}

export interface TypeOverviewOptions {
  subject: TypeOverviewSubject;
  projection: TypeOverviewProjection;
  rollups?: readonly OverviewEnablement[];
  relatedTypeHtml: (name: string) => string;
  escapeHtml: (value: unknown) => string;
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

function renderOverviewBadges(
  label: string,
  badges: readonly OverviewEnablement[],
  className: string,
  escapeHtml: (value: unknown) => string,
): string {
  if (badges.length === 0) return "";
  return `<div class="subject-overview-summary-group ${className}">
    <span class="subject-overview-summary-label">${escapeHtml(label)}</span>
    <ul aria-label="${escapeHtml(label)}">${badges.map(badge =>
      `<li data-overview-badge="${escapeHtml(badge.id)}">${escapeHtml(badge.label)}</li>`).join("")}</ul>
  </div>`;
}

export function renderTypeOverview(options: TypeOverviewOptions): string {
  const {
    subject, projection, rollups = [], relatedTypeHtml, escapeHtml,
  } = options;
  const exact = projection.exact;
  const metadata = [
    { id: exact?.kind ?? subject.kind, label: exact?.kind ?? subject.kind },
    ...exact?.isStatic
      ? [{ id: "static", label: "static" }]
      : [
          ...(exact?.isAbstract ? [{ id: "abstract", label: "abstract" }] : []),
          ...(exact?.isSealed ? [{ id: "sealed", label: "sealed" }] : []),
        ],
    ...(exact?.isReadOnly ? [{ id: "readonly", label: "readonly" }] : []),
    ...(exact?.isByRefLike ? [{ id: "ref", label: "ref" }] : []),
    {
      id: exact?.accessibility ?? subject.accessibility ?? "public",
      label: exact?.accessibility ?? subject.accessibility ?? "public",
    },
  ];
  const relationshipContent = (() => {
    switch (projection.status) {
      case "loading":
        return `<p class="subject-overview-status" role="status">Loading Type relationships…</p>`;
      case "failed":
        return `<p class="subject-overview-status inspection-error" role="alert">Type relationships are unavailable: ${escapeHtml(projection.detail ?? "metadata projection failed")}</p>`;
      case "unavailable":
        return `<p class="subject-overview-status">Type relationships are unavailable for this inspection.</p>`;
      case "available": {
        if (!exact) {
          return `<p class="subject-overview-status inspection-error" role="alert">The available metadata projection did not contain the exact Type.</p>`;
        }
        const base = exact.baseType
          ? `<div class="type-overview-relationship" data-type-relationship="base">
              <span>Base type</span>${relatedTypeHtml(exact.baseType)}
            </div>`
          : "";
        const interfaces = exact.interfaces.map(name =>
          `<div class="type-overview-relationship" data-type-relationship="interface">
            <span>Implements</span>${relatedTypeHtml(name)}
          </div>`).join("");
        const empty = base || interfaces
          ? ""
          : `<p class="subject-overview-status">No base Type or implemented interfaces.</p>`;
        const incomplete = projection.complete
          ? ""
          : `<p class="metadata-warning">Type relationship evidence is incomplete.</p>`;
        return `${base}${interfaces}${empty}${incomplete}`;
      }
      default:
        return "";
    }
  })();
  const fullName = exact?.fullName ?? [
    subject.namespace,
    subject.displayName,
  ].filter(Boolean).join(".");
  const namespace = exact?.namespace ?? (subject.namespace || "global");

  return `<section class="overview-surface type-overview-surface" aria-labelledby="type-overview-title">
    <header class="api-surface-head overview-surface-head">
      <h1 id="type-overview-title">Overview</h1>
      <p>${escapeHtml(subject.displayName)}</p>
    </header>
    <div class="overview-scroll type-overview-scroll">
      <section class="subject-overview-summary" aria-label="Type summary">
        ${renderOverviewBadges("Type metadata", metadata, "type-overview-metadata", escapeHtml)}
        ${renderOverviewBadges("Type rollup", rollups, "type-overview-rollups", escapeHtml)}
        <div class="subject-overview-summary-group type-overview-navigation">
          <span class="subject-overview-summary-label">Navigate</span>
          <button type="button" data-type-members>${subject.members.toLocaleString()} member${subject.members === 1 ? "" : "s"}</button>
        </div>
      </section>
      <section class="document-section type-overview-relationships">
        <div class="section-title"><h2>Relationships</h2></div>
        ${relationshipContent}
      </section>
      <section class="document-section type-overview-metadata">
        <div class="section-title"><h2>Metadata</h2></div>
        <dl class="fact-grid">
          <div><dt>Full name</dt><dd><code>${escapeHtml(fullName)}</code></dd></div>
          <div><dt>Namespace</dt><dd>${escapeHtml(namespace)}</dd></div>
          <div><dt>Assembly</dt><dd>${escapeHtml(subject.assembly)}</dd></div>
        </dl>
      </section>
    </div>
  </section>`;
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
