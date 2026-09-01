export interface PackageControlPackage {
  id: string;
  version: string;
  activeFramework: string;
  isRuntimePack: boolean;
}

export interface PackageControlsState {
  packages: readonly PackageControlPackage[];
  package: PackageControlPackage | null;
}

export interface ParsedPackageQuery {
  packageId: string;
  version: string;
  explicitVersion: boolean;
}

interface PackageControlsOptions {
  selectFramework: (framework: string) => void;
  selectVersion: (version: string) => void;
}

export interface PackageSelectionActions {
  onFrameworkSelect: (framework: string) => void;
  onVersionSelect: (version: string) => void;
}

export function bindPackageSelections(
  root: ParentNode,
  actions: PackageSelectionActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-framework-chip]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onFrameworkSelect(button.dataset.frameworkChip ?? "")));
  const framework = root.querySelector<HTMLSelectElement>("#framework");
  framework?.addEventListener(
    "change",
    () => actions.onFrameworkSelect(framework.value));
  const version = root.querySelector<HTMLSelectElement>("#package-version");
  version?.addEventListener(
    "change",
    () => actions.onVersionSelect(version.value));
}

export function packageIdentityEquals(
  left: PackageControlPackage | null,
  right: PackageControlPackage | null,
  packageIdentityKey: (pkg: PackageControlPackage) => string,
): boolean {
  return Boolean(left && right && packageIdentityKey(left) === packageIdentityKey(right));
}

// Only an empty query or "package@" with nothing after the "@" is rejected, matching the
// inline handler's original bounds exactly. A leading "@" (no package id, e.g. "@1.0.0")
// is preserved as-is rather than treated specially: separator > 0 is false, so the whole
// trimmed string — "@" included — becomes the package id, same as the handler this
// replaces. That is an existing quirk of the original code, not a rejection case.
export function parsePackageQuery(value: string): ParsedPackageQuery | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const separator = trimmed.lastIndexOf("@");
  if (separator === trimmed.length - 1) return null;

  const packageId = separator > 0 ? trimmed.slice(0, separator) : trimmed;
  const version = separator > 0 ? trimmed.slice(separator + 1) : "latest";
  return { packageId, version, explicitVersion: separator > 0 };
}

export function findOpenPackageForQuery(
  state: PackageControlsState,
  query: ParsedPackageQuery,
): PackageControlPackage | null {
  const idMatches = state.packages.filter(item =>
    !item.isRuntimePack
    && item.id.toLowerCase() === query.packageId.toLowerCase());
  const matches = query.explicitVersion
    ? idMatches.filter(item =>
      item.version.toLowerCase() === query.version.toLowerCase())
    : idMatches;

  if (state.package && matches.includes(state.package))
    return state.package;
  // Prefer the most recently retained matching coordinate when another package is active.
  return matches.at(-1) ?? null;
}

export function createPackageControls(options: PackageControlsOptions) {
  const {
    selectFramework,
    selectVersion,
  } = options;

  function bind(root: ParentNode): void {
    bindPackageSelections(root, {
      onFrameworkSelect: selectFramework,
      onVersionSelect: selectVersion,
    });
  }

  return {
    bind,
  };
}
