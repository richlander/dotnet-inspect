import type { PackageVersionState } from "./catalog-requests.ts";

export interface ComparisonPackage {
  id: string;
  version: string;
  activeFramework: string;
  source: { kind: string };
}

export type DiffTarget = { kind: "previous" } | { kind: "exact"; version: string };
export type CloneTarget<T> = { kind: "workspace" } | { kind: "package"; package: T };

// Compare mode is Browser presentation state scoped to the retained Package
// model: it survives Compare-owned drill-down, history restoration, and Explore
// return, and is discarded with the Package's other session-local comparison
// settings. It is neither a Navigation lens identity nor a portable Workspace
// field. A Package with no retained mode state presents Diff.
export type CompareMode = "diff" | "clone";

export function isCompareMode(
  value: string | null | undefined,
): value is CompareMode {
  return value === "diff" || value === "clone";
}

export type EffectiveDiffTarget =
  | { kind: "available"; version: string }
  | { kind: "loading"; message: string }
  | { kind: "unavailable"; message: string };

export function resolveEffectiveDiffTarget(
  diff: DiffTarget,
  versions: PackageVersionState,
): EffectiveDiffTarget {
  if (diff.kind === "exact")
    return { kind: "available", version: diff.version };
  if (versions.status !== "available") {
    if (versions.status === "failed")
      return { kind: "unavailable", message: versions.message };
    return {
      kind: "loading",
      message: "Reading available versions...",
    };
  }
  const { previousVersion, previousVersionUnavailableReason } =
    versions.inventory;
  if (previousVersion !== undefined)
    return { kind: "available", version: previousVersion };
  return {
    kind: "unavailable",
    message: previousVersionUnavailableReason
      ?? "No earlier listed version is available.",
  };
}

export function createPackageComparisonTargets<T extends ComparisonPackage>(
  packages: () => readonly T[],
) {
  const settings = new WeakMap<T, {
    diff: DiffTarget;
    clone: CloneTarget<T>;
    mode: CompareMode;
  }>();
  const get = (pkg: T) =>
    settings.get(pkg) ?? {
      diff: { kind: "previous" } as const,
      clone: { kind: "workspace" } as const,
      mode: "diff" as const,
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
        settings.set(copy, { diff: value.diff, clone, mode: value.mode });
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
    selectMode(pkg: T, mode: CompareMode) {
      requireResident(pkg);
      settings.set(pkg, { ...get(pkg), mode });
    },
  };
}

export function diffTargetDescription(
  diff: DiffTarget,
  versions: PackageVersionState,
): string {
  if (versions.status === "failed") return versions.message;
  if (diff.kind === "exact") return "Exact version";
  const target = resolveEffectiveDiffTarget(diff, versions);
  return target.kind === "available"
    ? "Previous listed release"
    : target.message;
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
    ? "All loaded Packages, including this one"
    : unavailableClone
      ? `${packageLabel(clone.package)} is no longer in this Workspace. Choose another target.`
      : "All libraries in the selected Package";
  const automaticDiffLabel = versions.status === "available"
    ? versions.inventory.previousVersionUnavailableReason !== undefined
      ? "Automatic: listing unavailable"
      : versions.inventory.previousVersion !== undefined
        ? `Automatic: ${versions.inventory.previousVersion}`
        : "Automatic: no earlier version"
    : "Automatic: previous listed version";
  const retry = supported && (versions.status === "failed"
    || (versions.status === "available"
      && versions.inventory.previousVersionUnavailableReason !== undefined));
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
      <section class="comparison-target-row" aria-labelledby="package-clone-target-label">
        <div class="comparison-target-heading">
          <h3 id="package-clone-target-label">Clone search scope</h3>
        </div>
        <div class="comparison-target-selection">
          <select id="package-clone-target" aria-labelledby="package-clone-target-label" aria-describedby="package-clone-target-status">
            <option value="workspace"${clone.kind === "workspace" ? " selected" : ""}>Workspace: all libraries</option>
            ${unavailableClone ? `<option value="unavailable" selected disabled>Unavailable: ${escapeHtml(packageLabel(clone.package))}</option>` : ""}
            ${packages.map((item, index) => `<option value="package:${index}"${clone.kind === "package" && index === targetIndex ? " selected" : ""}>${escapeHtml(packageLabel(item))}</option>`).join("")}
          </select>
          <div class="comparison-target-status">
            <p id="package-clone-target-status" role="status">${escapeHtml(cloneDescription)}</p>
          </div>
        </div>
      </section>
    </div>
    <p class="comparison-target-policy">Session only. Choosing a target does not run a comparison or change shared links.</p>`;
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
