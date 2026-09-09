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
  if (diff.kind === "exact") return `Compare against ${diff.version}.`;
  if (versions.status !== "available") return "Reading available versions...";
  const { previousVersion, previousVersionUnavailableReason } = versions.inventory;
  return previousVersionUnavailableReason
    ?? (previousVersion
      ? `Compare against ${previousVersion} (previous version).`
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

  return `<div class="section-title"><h2>Comparison targets</h2><span>Browser session</span></div>
    <p>These settings prepare targets for the forthcoming Diff inspector.</p>
    <div class="package-coordinate-fields">
      <label class="version-select">
        <span>Diff against</span>
        <select id="package-diff-target" aria-describedby="package-diff-target-status"${supported ? "" : " disabled"}>
          <option value="previous"${diff.kind === "previous" ? " selected" : ""}>Previous version (automatic)</option>
          ${choices.map(version => `<option value="exact:${escapeHtml(version)}"${diff.kind === "exact" && diff.version === version ? " selected" : ""}>${escapeHtml(version)}</option>`).join("")}
        </select>
      </label>
    </div>
    <p id="package-diff-target-status" role="status">${escapeHtml(supported
      ? diffTargetDescription(diff, versions)
      : "Version comparison targets are currently available for Gallery packages.")}</p>
    ${supported && (versions.status === "failed"
      || (versions.status === "available" && versions.inventory.previousVersionUnavailableReason))
      ? '<button type="button" id="package-comparison-retry">Retry versions</button>' : ""}
    <p>Automatic Diff uses listed stable releases; preview coordinates can also select earlier previews. Targets are not included in shared links.</p>`;
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
