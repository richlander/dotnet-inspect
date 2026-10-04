import type {
  PackageQueryState,
  QueryCompletion,
  QueryProgress,
  QueryResultRow,
} from "./package-query.ts";

const ECOSYSTEM_PACKAGE_CAPACITIES = [24, 48, 96] as const;
export type EcosystemPackageCapacity =
  typeof ECOSYSTEM_PACKAGE_CAPACITIES[number];

export interface EcosystemPackageDiscoveryViewState {
  readonly query: PackageQueryState;
  readonly capacity: EcosystemPackageCapacity;
  readonly pendingCapacity: EcosystemPackageCapacity | null;
  readonly navigationError: string;
  readonly packageAddStates: ReadonlyMap<string, EcosystemPackageAddState>;
}

export interface EcosystemPackageDiscoveryActions {
  onCapacity: (capacity: EcosystemPackageCapacity) => void;
  onPackageOpen: (packageId: string, version: string) => void;
  onPackageAdd: (packageId: string, version: string) => void;
}

export interface EcosystemPackageAddState {
  readonly status: "adding" | "added" | "failed";
  readonly message?: string;
}

export function isEcosystemPackageCapacity(
  value: string | undefined,
): value is `${EcosystemPackageCapacity}` {
  return value !== undefined
    && ECOSYSTEM_PACKAGE_CAPACITIES.some(
      capacity => String(capacity) === value);
}

function completionIsActive(completion: QueryCompletion): boolean {
  return completion.kind === "streaming";
}

function capacityControl(
  state: EcosystemPackageDiscoveryViewState,
): string {
  const active = completionIsActive(state.query.outcome.completion);
  const options = ECOSYSTEM_PACKAGE_CAPACITIES
    .filter(capacity => capacity <= state.capacity || active)
    .map(capacity => {
      if (capacity <= state.capacity) {
        return `<span${capacity === state.capacity
          ? ` aria-current="true"`
          : ""}>${capacity}</span>`;
      }
      const pending = state.pendingCapacity === capacity;
      return `<button type="button" data-ecosystem-package-capacity="${capacity}"${state.pendingCapacity !== null ? " disabled" : ""}>${pending ? `Showing ${capacity}…` : capacity}</button>`;
    })
    .join(`<span aria-hidden="true"> | </span>`);
  return `
    <div class="ecosystem-package-capacity">
      <span>${state.query.outcome.rows.length.toLocaleString()} package${state.query.outcome.rows.length === 1 ? "" : "s"} shown</span>
      <span>Show: ${options}</span>
    </div>`;
}

function progressLabel(progress: QueryProgress): string {
  const phase = progress.phase === "search"
    ? "Searching"
    : progress.phase === "manifest"
      ? "Reading manifests"
      : progress.phase === "package-content"
        ? "Inspecting package content"
        : progress.phase === "dependency-traversal"
          ? "Traversing dependencies"
          : "Inspecting assemblies";
  return `${phase}: ${progress.completed.toLocaleString()} of ${progress.limit.toLocaleString()}`;
}

function status(
  state: EcosystemPackageDiscoveryViewState,
  escapeHtml: (value: unknown) => string,
): string {
  const { completion, progress } = state.query.outcome;
  if (completion.kind === "failed") {
    return `<p class="query-error" role="alert">${escapeHtml(completion.reason)}</p>`;
  }
  if (completion.kind === "cancelled") {
    return `<p class="query-footer">Package discovery was cancelled.</p>`;
  }
  if (completion.kind === "bounded") {
    return `<p class="query-footer">${escapeHtml(completion.reason)}</p>`;
  }
  if (completion.kind === "exhausted") {
    return `<p class="query-footer">All matching packages are shown.</p>`;
  }
  if (completion.kind === "exact"
    || completion.kind === "library-literal") {
    return `<p class="query-footer">Package discovery is complete.</p>`;
  }
  if (completion.kind === "idle") {
    return `<p class="query-footer">Package discovery is ready.</p>`;
  }
  const latest = progress.at(-1);
  return `<p class="query-footer" aria-live="polite">${latest
    ? escapeHtml(progressLabel(latest))
    : "Discovering packages…"}</p>`;
}

function packageRow(
  row: QueryResultRow,
  addState: EcosystemPackageAddState | undefined,
  admissionPending: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  return `
    <article class="query-row" role="listitem">
      <div class="query-row-head">
        <div>
          <h3>${escapeHtml(row.packageId)}</h3>
          <span class="query-row-version">${escapeHtml(row.version)}</span>
        </div>
      </div>
      ${row.description?.trim()
        ? `<p class="query-row-description">${escapeHtml(row.description)}</p>`
        : ""}
      <div class="query-row-meta">
        <span>${row.totalDownloads === null
          ? "Lifetime downloads unavailable"
          : `${row.totalDownloads.toLocaleString()} lifetime downloads`}</span>
        <span class="query-row-actions">
          <button type="button" data-ecosystem-package-open="${escapeHtml(row.packageId)}" data-ecosystem-package-version="${escapeHtml(row.version)}">Open</button>
          ${row.ecosystemAdmission === null
              || row.ecosystemAdmission === undefined
            ? ""
            : `<button type="button" data-ecosystem-package-add="${escapeHtml(row.packageId)}" data-ecosystem-package-version="${escapeHtml(row.version)}"${admissionPending || addState?.status === "added" ? " disabled" : ""}>${addState?.status === "adding" ? "Adding\u2026" : addState?.status === "added" ? "Added" : "Add to workspace"}</button>`}
        </span>
      </div>
      ${addState?.status === "failed"
        ? `<p class="query-error" role="alert">${escapeHtml(addState.message ?? "Package admission failed.")}</p>`
        : ""}
    </article>`;
}

export function renderEcosystemPackageDiscovery(
  state: EcosystemPackageDiscoveryViewState,
  escapeHtml: (value: unknown) => string,
): string {
  const admissionPending = [...state.packageAddStates.values()]
    .some(addState => addState.status === "adding");
  const rows = state.query.outcome.rows
    .map(row => packageRow(
      row,
      state.packageAddStates.get(`${row.packageId}\u0000${row.version}`),
      admissionPending,
      escapeHtml,
    ))
    .join("");
  const failures = state.query.outcome.failures
    .map(failure => `<li>${escapeHtml(failure)}</li>`)
    .join("");
  return `
    <section class="ecosystem-package-discovery" aria-labelledby="ecosystem-packages-heading">
      <header>
        <div>
          <p class="type-namespace">Package discovery</p>
          <h2 id="ecosystem-packages-heading">Packages</h2>
        </div>
        ${capacityControl(state)}
      </header>
      <p>Core packages are followed by catalog-authored package prefixes. Increasing the capacity continues this discovery operation.</p>
      ${state.navigationError
        ? `<p class="query-error" role="alert">${escapeHtml(state.navigationError)}</p>`
        : ""}
      ${failures
        ? `<div class="query-partial-failure" role="status"><strong>Partial failure</strong><ul>${failures}</ul></div>`
        : ""}
      <div class="query-result-list" role="list">${rows}</div>
      ${status(state, escapeHtml)}
    </section>`;
}

export function bindEcosystemPackageDiscovery(
  root: ParentNode,
  actions: EcosystemPackageDiscoveryActions,
): void {
  root.querySelectorAll<HTMLButtonElement>(
    "[data-ecosystem-package-capacity]",
  ).forEach(button => button.addEventListener("click", () => {
    const value = button.dataset.ecosystemPackageCapacity;
    const capacity = ECOSYSTEM_PACKAGE_CAPACITIES.find(
      candidate => String(candidate) === value);
    if (capacity !== undefined) {
      actions.onCapacity(capacity);
    }
  }));
  root.querySelectorAll<HTMLButtonElement>(
    "[data-ecosystem-package-open]",
  ).forEach(button => button.addEventListener("click", () => {
    const packageId = button.dataset.ecosystemPackageOpen;
    const version = button.dataset.ecosystemPackageVersion;
    if (packageId && version) actions.onPackageOpen(packageId, version);
  }));
  root.querySelectorAll<HTMLButtonElement>(
    "[data-ecosystem-package-add]",
  ).forEach(button => button.addEventListener("click", () => {
    const packageId = button.dataset.ecosystemPackageAdd;
    const version = button.dataset.ecosystemPackageVersion;
    if (packageId && version) actions.onPackageAdd(packageId, version);
  }));
}
