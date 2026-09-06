import type { PackageVersionState } from "./catalog-requests.ts";

export interface ComparisonPackage {
  id: string;
  version: string;
  activeFramework: string;
  source: { kind: string };
}

export type DiffTarget = { kind: "previous" } | { kind: "exact"; version: string };
export type CloneTarget<T> = { kind: "workspace" } | { kind: "package"; package: T };

export function createPackageComparisonTargets<T extends ComparisonPackage>(
  packages: () => readonly T[],
) {
  const settings = new WeakMap<T, {
    diff: DiffTarget;
    clone: CloneTarget<T>;
  }>();
  const get = (pkg: T) =>
    settings.get(pkg) ?? {
      diff: { kind: "previous" } as const,
      clone: { kind: "workspace" } as const,
    };
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
        const value = get(original);
        const clone = value.clone.kind === "package"
          ? {
            kind: "package" as const,
            package: copies.get(value.clone.package) ?? value.clone.package,
          }
          : value.clone;
        settings.set(copy, { diff: value.diff, clone });
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
      settings.set(pkg, { ...get(pkg), diff });
    },
    selectClone(pkg: T, clone: CloneTarget<T>) {
      requireResident(pkg);
      if (clone.kind === "package") requireResident(clone.package);
      settings.set(pkg, { ...get(pkg), clone });
    },
  };
}

export function diffTargetDescription(
  diff: DiffTarget,
  versions: PackageVersionState,
): string {
  if (diff.kind === "exact") return `Compare against ${diff.version}.`;
  if (versions.status === "failed") return versions.message;
  if (versions.status !== "available") return "Reading available versions...";
  const { previousVersion, previousVersionUnavailableReason } = versions.inventory;
  return previousVersionUnavailableReason
    ?? (previousVersion
      ? `Compare against ${previousVersion} (previous version).`
      : "No earlier listed version is available.");
}

export interface ComparisonTargetView<T extends ComparisonPackage> {
  package: T;
  packages: readonly T[];
  diff: DiffTarget;
  clone: CloneTarget<T>;
  versions: PackageVersionState;
}

export function renderPackageComparisonTargets<T extends ComparisonPackage>(
  view: ComparisonTargetView<T>,
  escapeHtml: (value: unknown) => string,
): string {
  const { package: pkg, packages, diff, clone, versions } = view;
  const supported = pkg.source.kind === "nuget.org";
  const choices = versions.status === "available"
    ? [...versions.inventory.versions] : [];
  if (diff.kind === "exact" && !choices.includes(diff.version))
    choices.unshift(diff.version);
  const targetIndex = clone.kind === "package" ? packages.indexOf(clone.package) : -1;
  const unavailableClone = clone.kind === "package" && targetIndex < 0;
  const packageLabel = (item: T) => `${item.id} ${item.version} (${item.activeFramework})`;
  const cloneDescription = clone.kind === "workspace"
    ? "All libraries in the current Workspace, including this library."
    : unavailableClone
      ? `${packageLabel(clone.package)} is no longer in this Workspace. Choose another target.`
      : `All libraries in ${packageLabel(clone.package)}.`;

  return `<div class="section-title"><h2>Comparison targets</h2><span>Browser session</span></div>
    <p>These settings prepare targets for the forthcoming Diff and Clone inspectors.</p>
    <div class="package-coordinate-fields">
      <label class="version-select">
        <span>Diff against</span>
        <select id="package-diff-target" aria-describedby="package-diff-target-status"${supported ? "" : " disabled"}>
          <option value="previous"${diff.kind === "previous" ? " selected" : ""}>Previous version (automatic)</option>
          ${choices.map(version => `<option value="exact:${escapeHtml(version)}"${diff.kind === "exact" && diff.version === version ? " selected" : ""}>${escapeHtml(version)}</option>`).join("")}
        </select>
      </label>
      <label class="version-select">
        <span>Clone across</span>
        <select id="package-clone-target" aria-describedby="package-clone-target-status">
          <option value="workspace"${clone.kind === "workspace" ? " selected" : ""}>Workspace (including self)</option>
          ${unavailableClone ? `<option value="unavailable" selected disabled>Unavailable: ${escapeHtml(packageLabel(clone.package))}</option>` : ""}
          ${packages.map((item, index) => `<option value="package:${index}"${clone.kind === "package" && index === targetIndex ? " selected" : ""}>${escapeHtml(packageLabel(item))}</option>`).join("")}
        </select>
      </label>
    </div>
    <p id="package-diff-target-status" role="status">${escapeHtml(supported
      ? diffTargetDescription(diff, versions)
      : "Version comparison targets are currently available for Gallery packages.")}</p>
    ${supported && (versions.status === "failed"
      || (versions.status === "available" && versions.inventory.previousVersionUnavailableReason))
      ? '<button type="button" id="package-comparison-retry">Retry versions</button>' : ""}
    <p id="package-clone-target-status" role="status">${escapeHtml(cloneDescription)}</p>
    <p>Automatic Diff uses listed stable releases; preview coordinates can also select earlier previews. Targets are not included in shared links.</p>`;
}

export function bindPackageComparisonTargets<T extends ComparisonPackage>(
  root: ParentNode,
  packages: readonly T[],
  actions: {
    selectDiff: (target: DiffTarget) => void;
    selectClone: (target: CloneTarget<T>) => void;
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
  const clone = root.querySelector<HTMLSelectElement>("#package-clone-target");
  clone?.addEventListener("change", () => {
    if (clone.value === "workspace") actions.selectClone({ kind: "workspace" });
    else {
      const index = clone.value.startsWith("package:")
        ? Number(clone.value.slice(8)) : -1;
      const target = packages[index];
      if (!Number.isInteger(index) || !target)
        throw new Error("Unknown Clone target selection.");
      actions.selectClone({ kind: "package", package: target });
    }
  });
  root.querySelector("#package-comparison-retry")
    ?.addEventListener("click", () => actions.retry());
}
