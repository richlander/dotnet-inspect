export interface PackageBarPackage {
  id: string;
  version: string;
  activeFramework: string;
  isRuntimePack: boolean;
}

export interface PackageBarState {
  packages: readonly PackageBarPackage[];
  package: PackageBarPackage | null;
}

export interface ParsedPackageQuery {
  packageId: string;
  version: string;
}

interface PackageBarOptions {
  state: PackageBarState;
  escapeHtml: (value: unknown) => string;
  packageIdentityKey: (pkg: PackageBarPackage) => string;
  runtimePackPackage: () => PackageBarPackage | null;
  selectPackageTab: (pkg: PackageBarPackage) => void;
  closePackageTab: (packageKey: string) => void;
  openRuntimePack: () => void;
  openPackage: (packageId: string, version: string) => void;
  showToast: (message: string) => void;
}

export function packageIdentityEquals(
  left: PackageBarPackage | null,
  right: PackageBarPackage | null,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): boolean {
  return Boolean(left && right && packageIdentityKey(left) === packageIdentityKey(right));
}

// The always-present, non-closable, left-most "Platform" tab. It abstracts the .NET runtime
// packs (netcore.app, aspnetcore.app, …) behind a single surface: when a pack is resident it
// activates it; otherwise clicking loads it lazily. Rendered separately from the normal tab
// map so it is always first and never carries a close affordance.
export function platformTabHtml(
  runtimePack: PackageBarPackage | null,
  activePackage: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  const active = runtimePack && activePackage && activePackage.id === runtimePack.id ? "active" : "";
  const framework = runtimePack?.activeFramework || activePackage?.activeFramework || "";
  const attr = runtimePack
    ? `data-package-key="${escapeHtml(packageIdentityKey(runtimePack))}"`
    : `data-platform-open="1"`;
  return `<button class="package-tab platform ${active}" ${attr} role="tab" title="Platform · .NET runtime libraries">
      <span class="package-cube">◎</span>
      <span class="tab-label">Platform</span>
      <small>${escapeHtml(framework || "load")}</small>
    </button>`;
}

export function packageTabHtml(
  item: PackageBarPackage,
  activePackage: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  const active = packageIdentityEquals(item, activePackage, packageIdentityKey);
  const key = escapeHtml(packageIdentityKey(item));
  return `
            <div class="package-tab ${active ? "active" : ""}" data-package-key="${key}" role="tab" tabindex="0">
              <span class="package-cube">⬡</span>
              <span class="tab-label">${escapeHtml(item.id)}</span>
              <small>${escapeHtml(item.version)} · ${escapeHtml(item.activeFramework)}</small>
              ${active
                ? `<button class="tab-close" data-package-close="${key}" type="button" aria-label="Close ${escapeHtml(item.id)}">×</button>`
                : ""}
            </div>`;
}

export function packageTabsHtml(
  state: PackageBarState,
  runtimePack: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  const tabs = state.packages
    .filter(item => !item.isRuntimePack)
    .map(item => packageTabHtml(item, state.package, escapeHtml, packageIdentityKey))
    .join("");
  return `${platformTabHtml(runtimePack, state.package, escapeHtml, packageIdentityKey)}${tabs}`;
}

export function packageBarHtml(
  state: PackageBarState,
  runtimePack: PackageBarPackage | null,
  escapeHtml: (value: unknown) => string,
  packageIdentityKey: (pkg: PackageBarPackage) => string,
): string {
  return `
        <div class="package-tabs" role="tablist" aria-label="Package scope">
          ${packageTabsHtml(state, runtimePack, escapeHtml, packageIdentityKey)}
        </div>
        <form class="package-query" id="package-query">
          <span>+</span>
          <input id="package-query-input" placeholder="Package or Package@version" aria-label="Open NuGet package" autocomplete="off" spellcheck="false" />
          <button>open</button>
        </form>`;
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
  return { packageId, version };
}

export function createPackageBar(options: PackageBarOptions) {
  const {
    state,
    escapeHtml,
    packageIdentityKey,
    runtimePackPackage,
    selectPackageTab,
    closePackageTab,
    openRuntimePack,
    openPackage,
    showToast,
  } = options;

  function html(): string {
    return packageBarHtml(state, runtimePackPackage(), escapeHtml, packageIdentityKey);
  }

  function bind(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-package-key]").forEach(tab => {
      const activate = () => {
        const target = state.packages.find(item => packageIdentityKey(item) === tab.dataset.packageKey);
        if (target) selectPackageTab(target);
      };
      tab.addEventListener("click", event => {
        if (event.target instanceof Element && event.target.closest("[data-package-close]")) return;
        activate();
      });
      tab.addEventListener("keydown", event => {
        if (event.key !== "Enter" && event.key !== " ") return;
        event.preventDefault();
        activate();
      });
    });

    root.querySelectorAll<HTMLButtonElement>("[data-package-close]").forEach(button =>
      button.addEventListener("click", event => {
        event.stopPropagation();
        const key = button.dataset.packageClose;
        if (key !== undefined) closePackageTab(key);
      }));

    root.querySelector<HTMLElement>("[data-platform-open]")?.addEventListener("click", () => openRuntimePack());

    // Browser-tab behavior for a crowded strip: keep the active tab in view, and let a
    // vertical wheel scroll the horizontal strip so hidden tabs stay reachable.
    const tabStrip = root.querySelector<HTMLElement>(".package-tabs");
    if (tabStrip) {
      requestAnimationFrame(() =>
        tabStrip.querySelector(".package-tab.active")?.scrollIntoView({ block: "nearest", inline: "nearest" }));
      tabStrip.addEventListener("wheel", event => {
        if (event.deltaY === 0) return;
        event.preventDefault();
        tabStrip.scrollLeft += event.deltaY;
      }, { passive: false });
    }

    const form = root.querySelector<HTMLFormElement>("#package-query");
    if (!form) throw new Error("The package bar query form is unavailable.");
    form.addEventListener("submit", event => {
      event.preventDefault();
      const input = root.querySelector<HTMLInputElement>("#package-query-input");
      if (!input) throw new Error("The package bar query input is unavailable.");
      const parsed = parsePackageQuery(input.value);
      if (!parsed) {
        showToast("enter a package, optionally followed by @version");
        return;
      }
      openPackage(parsed.packageId, parsed.version);
    });
  }

  return {
    bind,
    html,
  };
}
