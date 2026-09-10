import type { PlatformAssemblyRow, PlatformCatalogTarget } from "./platform-index.ts";
import type { BrowserAssemblySurface } from "./facades/inspect-web-package.d.ts";

export interface PlatformNavigationState {
  tfm: string;
  version: string;
  includeAllLibraries: boolean;
  filter: string;
}

export interface PlatformSubjectStatus {
  loading: boolean;
  error: string;
}

export function platformTargetKey(target: { tfm: string; version: string }): string {
  return JSON.stringify([target.tfm, target.version]);
}

export function platformSupportsRuntimeAcquisition(target: PlatformCatalogTarget): boolean {
  return target.rows.some(row => row.pack === "netcore.app" || row.pack === "aspnetcore.app");
}

export function platformLibraryKey(row: PlatformAssemblyRow): string {
  return JSON.stringify([row.pack, row.file]);
}

export function platformAssemblyRequest(row: PlatformAssemblyRow): string {
  // Existing engine operations select by metadata identity, not physical filename.
  return `${row.assembly}.dll`;
}

export function platformGraphLibraryForTarget(
  target: PlatformCatalogTarget,
  assembly: string,
  pack: PlatformAssemblyRow["pack"] | null,
): PlatformAssemblyRow | null {
  const name = assembly.replace(/\.dll$/i, "").toLowerCase();
  const matches = target.rows.filter(row =>
    row.hasImplementation
    && (!pack || row.pack === pack)
    && row.assembly.toLowerCase() === name);
  return matches.length === 1 ? matches[0]! : null;
}

export function platformLibraryMatchesDescriptor(
  row: PlatformAssemblyRow,
  descriptor: BrowserAssemblySurface,
): boolean {
  return descriptor.platformPack === row.pack
    && descriptor.name.toLowerCase() === row.assembly.toLowerCase()
    && descriptor.asset.split(/[\\/]/).at(-1) === row.file;
}

export function platformLibraryRole(row: PlatformAssemblyRow) {
  if (!row.hasImplementation)
    return { id: "reference", icon: "R", label: "Reference only", detail: "Runtime implementation unavailable" };
  if (row.kind === "facade")
    return { id: "facade", icon: "F", label: "Facade", detail: row.forwardsTo ? `Forwards to ${row.forwardsTo}` : "Type forwarding library" };
  if (row.inReferencePack)
    return { id: "implementation", icon: "I", label: "Implementation", detail: "In reference pack" };
  return { id: "private", icon: "P", label: "Private implementation", detail: "Outside reference pack" };
}

export function platformInventory(
  target: PlatformCatalogTarget,
  includeAllLibraries: boolean,
  filter: string,
): readonly PlatformAssemblyRow[] {
  const query = filter.trim().toLowerCase();
  return target.rows.filter(row =>
    (includeAllLibraries || row.inReferencePack)
    && (!query || row.assembly.toLowerCase().includes(query)))
    .sort((a, b) => a.assembly.localeCompare(b.assembly) || a.pack.localeCompare(b.pack));
}

export function parsePlatformVersions(value: unknown): string[] {
  if (!Array.isArray(value) || value.length === 0
    || !value.every((item): item is string => typeof item === "string" && item.trim().length > 0)) {
    throw new Error("Platform version discovery returned an invalid version list.");
  }
  return [...new Set(value)];
}

export function requireMatchingPlatformTarget(
  target: PlatformCatalogTarget,
  tfm: string,
  version: string,
): PlatformCatalogTarget {
  if (target.tfm !== tfm || target.version !== version) {
    throw new Error(`The Platform catalog does not match ${tfm}@${version}.`);
  }
  return target;
}

export interface PlatformSubjectOptions {
  target: PlatformCatalogTarget | null;
  selection: PlatformNavigationState | null;
  frameworks: readonly string[];
  versions: readonly string[];
  catalog: PlatformSubjectStatus;
  discovery: PlatformSubjectStatus;
  warmup: PlatformSubjectStatus;
  opening: PlatformSubjectStatus;
  escapeHtml: (value: unknown) => string;
}

export function renderPlatformSubject(options: PlatformSubjectOptions): string {
  const { target, selection, escapeHtml: e } = options;
  const status = (value: PlatformSubjectStatus, pending: string, retry: string) =>
    value.error
      ? `<p class="platform-status" role="alert">${e(value.error)} <button type="button" data-platform-retry="${retry}">Retry</button></p>`
      : value.loading ? `<p class="platform-status" role="status">${pending}</p>` : "";
  const rows = target && selection
    ? platformInventory(target, selection.includeAllLibraries, selection.filter)
    : [];
  const versions = [...new Set([...(target ? [target.version] : []), ...options.versions])];
  return `<header class="type-heading platform-heading">
    <div class="type-badge">.NET</div><div><div class="type-namespace">Platform</div><h1>.NET Platform</h1>
    ${target ? `<code class="type-signature">${e(target.tfm)} · ${e(target.version)}</code>` : ""}</div>
  </header>
  <div class="platform-subject">
    ${status(options.catalog, "Loading Platform catalog...", "catalog")}
    ${target && selection ? `<div class="platform-controls">
      <label>Target <select id="platform-framework" aria-label="Platform target">${options.frameworks.map(tfm =>
        `<option${tfm === target.tfm ? " selected" : ""}>${e(tfm)}</option>`).join("")}</select></label>
      <label>Version <select id="platform-version" aria-label="Platform version">${versions.map(version =>
        `<option${version === target.version ? " selected" : ""}>${e(version)}</option>`).join("")}</select></label>
      <label class="platform-filter">Library <input id="platform-filter" type="search" placeholder="Filter libraries" value="${e(selection.filter)}"></label>
      <label><input id="platform-include-all" type="checkbox"${selection.includeAllLibraries ? " checked" : ""}> Include all libraries</label>
    </div>
    <p>Reference-pack membership is shown by default. Inspection uses runtime implementations.</p>
    ${status(options.discovery, "Discovering available versions...", "versions")}
    ${status(options.warmup, "Downloading runtime packs in the background. Libraries remain available.", "warmup")}
    ${status(options.opening, "Opening the selected Library...", "library")}
    <section class="document-section">
      <div class="section-title"><h2>Libraries</h2><span>${rows.length} of ${target.rows.length}</span></div>
      <ul class="platform-library-list" aria-label="Platform libraries" tabindex="-1">${rows.map(row => {
        const role = platformLibraryRole(row);
        const content = `<span class="platform-role-icon" aria-hidden="true">${role.icon}</span>
          <span class="platform-library-name">${e(row.assembly)}</span>
          <span class="platform-library-role">${role.label}</span>
          <small>${e(row.pack)} · ${e(role.detail)}</small>`;
        return `<li class="platform-library-row" data-platform-role="${role.id}">${row.hasImplementation
          ? `<button type="button" data-platform-library="${e(platformLibraryKey(row))}" title="Inspect ${e(row.assembly)}">${content}</button>`
          : `<div role="note">${content}<span>Unsupported: no runtime implementation</span></div>`}</li>`;
      }).join("")}</ul>
      ${rows.length ? "" : '<p>No libraries match these filters.</p>'}
    </section>` : ""}
  </div>`;
}

export function bindPlatformSubject(root: ParentNode, actions: {
  onFramework: (tfm: string) => void;
  onVersion: (version: string) => void;
  onFilter: (filter: string) => void;
  onIncludeAll: (include: boolean) => void;
  onLibrary: (key: string) => void;
  onRetry: (kind: string) => void;
}): void {
  const framework = root.querySelector<HTMLSelectElement>("#platform-framework");
  framework?.addEventListener("change", () => actions.onFramework(framework.value));
  const version = root.querySelector<HTMLSelectElement>("#platform-version");
  version?.addEventListener("change", () => actions.onVersion(version.value));
  const filter = root.querySelector<HTMLInputElement>("#platform-filter");
  filter?.addEventListener("input", () => actions.onFilter(filter.value));
  const includeAll = root.querySelector<HTMLInputElement>("#platform-include-all");
  includeAll?.addEventListener("change", () => actions.onIncludeAll(includeAll.checked));
  root.querySelectorAll<HTMLElement>("[data-platform-library]").forEach(button =>
    button.addEventListener("click", () => actions.onLibrary(button.dataset.platformLibrary!)));
  root.querySelectorAll<HTMLElement>("[data-platform-retry]").forEach(button =>
    button.addEventListener("click", () => actions.onRetry(button.dataset.platformRetry!)));
}
