import type {
  BrowserPackageContentsDocument,
  BrowserPackageContentsFile,
} from "./facades/inspect-web-package.d.ts";

export interface PackageContentsCoordinate {
  readonly id: string;
  readonly version: string;
}

export type PackageContentsViewState =
  | { readonly status: "idle" | "loading"; readonly directory: string }
  | {
      readonly status: "ready";
      readonly directory: string;
      readonly count: number;
      readonly files: readonly BrowserPackageContentsFile[];
    }
  | {
      readonly status: "failed";
      readonly directory: string;
      readonly error: string;
    };

export type PackageContentsChild =
  | { readonly kind: "directory"; readonly path: string; readonly name: string }
  | {
      readonly kind: "file";
      readonly path: string;
      readonly name: string;
      readonly size: number;
    };

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

export function packageContentsChildren(
  files: readonly BrowserPackageContentsFile[],
  directory: string,
): PackageContentsChild[] {
  const prefix = directory ? `${directory}/` : "";
  const directories = new Map<string, string>();
  const children: PackageContentsChild[] = [];

  for (const file of files) {
    if (!file.path.startsWith(prefix)) continue;
    const relative = file.path.slice(prefix.length);
    if (!relative) continue;
    const separator = relative.indexOf("/");
    if (separator >= 0) {
      const name = relative.slice(0, separator);
      const path = `${prefix}${name}`;
      directories.set(path, name);
    } else {
      children.push({
        kind: "file",
        path: file.path,
        name: relative,
        size: file.size,
      });
    }
  }

  const folders = [...directories]
    .map(([path, name]) => ({ kind: "directory" as const, path, name }))
    .sort((left, right) => compareOrdinal(left.name, right.name));
  const ordinaryFiles = children
    .sort((left, right) => compareOrdinal(left.name, right.name));
  return [...folders, ...ordinaryFiles];
}

function renderBreadcrumbs(
  directory: string,
  escapeHtml: (value: string) => string,
): string {
  const segments = directory.split("/").filter(Boolean);
  let path = "";
  const breadcrumbs = segments.map((segment, index) => {
    path = path ? `${path}/${segment}` : segment;
    const current = index === segments.length - 1;
    return `<button type="button" data-package-content-directory="${escapeHtml(path)}"${current ? ' aria-current="page"' : ""}>${escapeHtml(segment)}</button>`;
  });
  const root = `<button type="button" data-package-content-directory=""${directory ? "" : ' aria-current="page"'}>Package root</button>`;
  return `<nav class="package-contents-breadcrumbs" aria-label="Package folder path">${[root, ...breadcrumbs].join("<span aria-hidden=\"true\"> / </span>")}</nav>`;
}

export function renderPackageContents(
  state: PackageContentsViewState,
  escapeHtml: (value: string) => string,
): string {
  if (state.status === "failed") {
    return `<section class="package-contents-surface" aria-labelledby="package-contents-title">
      <header class="api-surface-head"><h1 id="package-contents-title">Package Contents</h1></header>
      <section class="document-section package-contents-state empty-document">
        <h2>Package contents unavailable</h2>
        <p>${escapeHtml(state.error)}</p>
        <button type="button" data-package-content-retry>Retry</button>
      </section>
    </section>`;
  }

  if (state.status !== "ready") {
    return `<section class="package-contents-surface" aria-labelledby="package-contents-title">
      <header class="api-surface-head"><h1 id="package-contents-title">Package Contents</h1></header>
      <section class="document-section package-contents-state source-progress" role="status"><span class="loader" aria-hidden="true"></span><h2>Reading package files…</h2></section>
    </section>`;
  }

  const children = packageContentsChildren(state.files, state.directory);
  const items = children.map(child => {
    if (child.kind === "directory") {
      return `<li><button type="button" class="package-content-folder" data-package-content-directory="${escapeHtml(child.path)}" aria-label="Open folder ${escapeHtml(child.name)}"><span class="package-content-kind">Folder</span><span class="package-content-name">${escapeHtml(child.name)}</span></button></li>`;
    }
    const assembly = child.path.toLowerCase().endsWith(".dll");
    const action = assembly
      ? `<button type="button" class="package-content-open" data-package-content-assembly="${escapeHtml(child.path)}" aria-label="Open ${escapeHtml(child.path)} as a Library">Open Library</button>`
      : "";
    return `<li><div class="package-content-file">
      <span class="package-content-kind">File</span>
      <span class="package-content-name">${escapeHtml(child.name)}</span>
      <span class="package-content-size">${new Intl.NumberFormat().format(child.size)} bytes</span>
      ${action}
    </div></li>`;
  }).join("");
  const empty = children.length
    ? ""
    : `<li class="package-contents-empty">No files or folders in this location.</li>`;

  return `<section class="package-contents-surface" aria-labelledby="package-contents-title">
    <header class="api-surface-head package-contents-header">
      <h1 id="package-contents-title">Package Contents</h1>
      <p>${new Intl.NumberFormat().format(state.count)} files</p>
    </header>
    ${renderBreadcrumbs(state.directory, escapeHtml)}
    <ul class="package-contents-list" aria-label="Package files">${items}${empty}</ul>
  </section>`;
}

export function createPackageContentsCoordinator<TPackage extends PackageContentsCoordinate>(
  dependencies: {
    readonly query: (
      packageId: string,
      version: string,
    ) => Promise<BrowserPackageContentsDocument>;
    readonly describeError: (error: unknown) => string;
    readonly render: () => void;
  },
) {
  const states = new WeakMap<TPackage, PackageContentsViewState>();

  const get = (pkg: TPackage): PackageContentsViewState =>
    states.get(pkg) ?? { status: "idle", directory: "" };

  const ensure = async (pkg: TPackage): Promise<void> => {
    if (states.has(pkg)) return;
    const pending: PackageContentsViewState = {
      status: "loading",
      directory: "",
    };
    states.set(pkg, pending);
    dependencies.render();
    try {
      const result = await dependencies.query(pkg.id, pkg.version);
      if (states.get(pkg) !== pending) return;
      if (result.status !== "Completed") {
        throw new Error(result.detail || "The package file inventory is unavailable.");
      }
      if (result.files.length !== result.count) {
        throw new Error(
          "The package file inventory count does not match its file rows.",
        );
      }
      states.set(pkg, {
        status: "ready",
        directory: "",
        count: result.count,
        files: result.files,
      });
    } catch (error) {
      if (states.get(pkg) !== pending) return;
      states.set(pkg, {
        status: "failed",
        directory: "",
        error: dependencies.describeError(error),
      });
    }
    dependencies.render();
  };

  return {
    get,
    ensure,
    retry(pkg: TPackage) {
      if (get(pkg).status !== "failed") return;
      states.delete(pkg);
      void ensure(pkg);
    },
    selectDirectory(pkg: TPackage, directory: string) {
      const current = get(pkg);
      if (current.status !== "ready") return;
      if (directory
        && !current.files.some(file => file.path.startsWith(`${directory}/`))) {
        return;
      }
      states.set(pkg, { ...current, directory });
      dependencies.render();
    },
  };
}
