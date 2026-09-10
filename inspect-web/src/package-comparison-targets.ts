import type { PackageVersionState } from "./catalog-requests.ts";

export interface ComparisonPackage {
  id: string;
  version: string;
  activeFramework: string;
  source: { kind: string };
}

export type DiffTarget = { kind: "previous" } | { kind: "exact"; version: string };

export function createPackageComparisonTargets<T extends ComparisonPackage>(
  packages: () => readonly T[],
) {
  const settings = new WeakMap<T, DiffTarget>();
  const get = (pkg: T) =>
    ({ diff: settings.get(pkg) ?? { kind: "previous" } as const });
  const requireResident = (pkg: T) => {
    if (!packages().includes(pkg))
      throw new Error("The Package is no longer in this Workspace.");
  };

  return {
    get,
    forget(pkg: T) {
      settings.delete(pkg);
    },
    copyPackages(copies: ReadonlyMap<T, T>) {
      for (const [original, copy] of copies) {
        settings.set(copy, get(original).diff);
      }
    },
    selectDiff(pkg: T, diff: DiffTarget, versions: PackageVersionState) {
      requireResident(pkg);
      if (pkg.source.kind !== "nuget.org")
        throw new Error("Diff target selection is currently available for Gallery packages.");
      if (diff.kind === "exact"
        && (versions.status !== "available"
          || !versions.inventory.versions.includes(diff.version)))
        throw new Error("Select a version from this Package's available versions.");
      settings.set(pkg, diff);
    },
  };
}

export function diffTargetDescription(
  diff: DiffTarget,
  versions: PackageVersionState,
): string {
  if (versions.status === "failed") return versions.message;
  if (diff.kind === "exact") return "Exact version";
  if (versions.status !== "available") return "Reading available versions...";
  const { previousVersion, previousVersionUnavailableReason } = versions.inventory;
  return previousVersionUnavailableReason
    ?? (previousVersion
      ? "Previous listed release"
      : "No earlier listed version is available.");
}

export interface ComparisonTargetView<T extends ComparisonPackage> {
  package: T;
  diff: DiffTarget;
  versions: PackageVersionState;
}

export function renderPackageComparisonTargets<T extends ComparisonPackage>(
  view: ComparisonTargetView<T>,
  escapeHtml: (value: unknown) => string,
): string {
  const { package: pkg, diff, versions } = view;
  const supported = pkg.source.kind === "nuget.org";
  const choices = versions.status === "available"
    ? [...versions.inventory.versions] : [];
  if (diff.kind === "exact" && !choices.includes(diff.version))
    choices.unshift(diff.version);
  const automaticDiffLabel = versions.status === "available"
    ? versions.inventory.previousVersionUnavailableReason
      ? "Automatic: listing unavailable"
      : versions.inventory.previousVersion
        ? `Automatic: ${versions.inventory.previousVersion}`
        : "Automatic: no earlier version"
    : "Automatic: previous listed version";
  const retry = supported && (versions.status === "failed"
    || (versions.status === "available"
      && versions.inventory.previousVersionUnavailableReason));
  const diffStatusClass = versions.status === "failed"
    ? " comparison-target-status-error" : "";

  return `<div class="section-title comparison-targets-title"><h2>Comparison targets</h2><span>Target setup</span></div>
    <div class="comparison-target-list">
      <section class="comparison-target-row" aria-labelledby="package-diff-target-label">
        <div class="comparison-target-heading">
          <h3 id="package-diff-target-label">Diff baseline</h3>
        </div>
        <div class="comparison-target-selection">
          <select id="package-diff-target" aria-labelledby="package-diff-target-label" aria-describedby="package-diff-target-status"${supported ? "" : " disabled"}>
          <option value="previous"${diff.kind === "previous" ? " selected" : ""}>${escapeHtml(automaticDiffLabel)}</option>
          ${choices.map(version => `<option value="exact:${escapeHtml(version)}"${diff.kind === "exact" && diff.version === version ? " selected" : ""}>${escapeHtml(version)}</option>`).join("")}
          </select>
          <div class="comparison-target-status${diffStatusClass}">
            <p id="package-diff-target-status" role="status">${escapeHtml(supported
              ? diffTargetDescription(diff, versions)
              : "Version targets are available only for Gallery packages.")}</p>
            ${retry
              ? '<button type="button" class="comparison-target-retry" id="package-comparison-retry">Retry versions</button>' : ""}
          </div>
        </div>
      </section>
    </div>
    <p class="comparison-target-policy">Session only. Choosing a target does not run a comparison or change shared links.</p>`;
}

export function bindPackageComparisonTargets(
  root: ParentNode,
  actions: {
    selectDiff: (target: DiffTarget) => void;
    retry: () => void;
  },
): void {
  const diff = root.querySelector<HTMLSelectElement>("#package-diff-target");
  diff?.addEventListener("change", () => {
    if (diff.value === "previous") actions.selectDiff({ kind: "previous" });
    else if (diff.value.startsWith("exact:"))
      actions.selectDiff({ kind: "exact", version: diff.value.slice(6) });
    else throw new Error("Unknown Diff target selection.");
  });
  root.querySelector("#package-comparison-retry")
    ?.addEventListener("click", () => actions.retry());
}
