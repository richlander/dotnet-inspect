import type {
  BrowserHomeDemoCatalogEntry,
  BrowserHomeDemoRunResult,
  BrowserPackageSurface,
} from "./facades/inspect-web-catalog.d.ts";
import {
  createNuGetPackageModel,
  createRuntimePackageModel,
  mergeRuntimePackageSurface,
  type AppPackage,
} from "./package-acquisition.ts";
import {
  platformPackToken,
  type PlatformPack,
} from "./data.ts";
import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";

/**
 * Browser host adapters over product home demos exported by the Wasm engine.
 * Catalog metadata and execution results remain product-owned; this module
 * validates and adapts their source-native surfaces into Browser models.
 */

export type ProductHomeDemoId = string;

export type ProductHomeDemoCatalogEntry = BrowserHomeDemoCatalogEntry;

export type PreparedProductHomeDemoSource =
  | {
    kind: "package";
    packages: AppPackage[];
    focusPackage: AppPackage;
  }
  | {
    kind: "platform";
    package: AppPackage;
    focusAssembly: string;
    focusPack: PlatformPack;
  };

let catalogEntries: readonly ProductHomeDemoCatalogEntry[] = [];
const catalogIdSet = new Set<string>();

/** Installs the engine-exported catalog (call once after `listHomeDemos`). */
export function setProductHomeDemoCatalog(
  demos: readonly ProductHomeDemoCatalogEntry[],
): void {
  catalogEntries = demos.slice();
  catalogIdSet.clear();
  for (const entry of demos)
    catalogIdSet.add(entry.id);
}

export function productHomeDemoCatalog():
  readonly ProductHomeDemoCatalogEntry[] {
  return catalogEntries;
}

export function isProductHomeDemoId(
  value: string | undefined | null,
): value is ProductHomeDemoId {
  return typeof value === "string" && catalogIdSet.has(value);
}

export function isProductHomeDemosPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, ROUTED_ENTRY_PATHS.demos);
}

function focusPlatformSurface(
  surfaces: readonly BrowserPackageSurface[],
  focusAssembly: string,
): BrowserPackageSurface {
  const matches = surfaces.filter(surface =>
    surface.defaultAssemblyId === focusAssembly);
  if (matches.length !== 1) {
    throw new Error(
      `The engine-run Platform demo focus '${focusAssembly}' matched ${matches.length} returned surfaces.`);
  }
  return matches[0]!;
}

function platformPackForFamily(family: string): PlatformPack | null {
  return family === "runtime"
    ? "netcore.app"
    : family === "aspnetcore"
      ? "aspnetcore.app"
      : null;
}

export function prepareProductHomeDemoSource(
  result: BrowserHomeDemoRunResult,
): PreparedProductHomeDemoSource {
  const activation = result.activation;
  if (!activation) {
    throw new Error("The engine returned a product home demo without activation.");
  }
  if (result.packages.length === 0) {
    throw new Error("The engine returned a product home demo without inspection surfaces.");
  }

  if (activation.focusKind === "package") {
    const packages = result.packages.map(createNuGetPackageModel);
    const matches = packages.filter(item =>
      item.id === activation.focusId
      && item.version === activation.focusVersion
      && item.activeFramework === activation.focusFramework);
    if (matches.length !== 1) {
      throw new Error(
        "The engine-run demo package focus was not uniquely present in its returned surfaces.");
    }
    return {
      kind: "package",
      packages,
      focusPackage: matches[0]!,
    };
  }

  if (activation.focusKind === "platform") {
    const focusAssembly = activation.focusAssembly;
    if (!focusAssembly) {
      throw new Error("The engine-run Platform demo omitted its focus assembly.");
    }
    for (const surface of result.packages) {
      if (surface.version !== activation.focusVersion
        || surface.activeFramework !== activation.focusFramework) {
        throw new Error(
          "The engine-run Platform demo returned surfaces from different exact targets.");
      }
    }

    const focusSurface = focusPlatformSurface(
      result.packages,
      focusAssembly);
    const descriptors = focusSurface.assemblies.filter(assembly =>
      assembly.id === focusSurface.defaultAssemblyId
      && assembly.name.toLowerCase() === focusAssembly.toLowerCase());
    if (descriptors.length !== 1) {
      throw new Error(
        "The engine-run Platform demo focus did not retain one exact assembly descriptor.");
    }
    const focusPack = platformPackToken(descriptors[0]!.platformPack);
    if (!focusPack) {
      throw new Error(
        "The engine-run Platform demo focus did not retain its Platform family.");
    }
    const activationPack = platformPackForFamily(activation.focusId);
    if (!activationPack || activationPack !== focusPack) {
      throw new Error(
        "The engine-run Platform demo focus family did not match its assembly descriptor.");
    }

    const ordered = [
      focusSurface,
      ...result.packages.filter(surface => surface !== focusSurface),
    ];
    const packageModel = createRuntimePackageModel(ordered[0]!);
    for (const surface of ordered.slice(1))
      mergeRuntimePackageSurface(packageModel, surface);
    return {
      kind: "platform",
      package: packageModel,
      focusAssembly,
      focusPack,
    };
  }

  throw new Error(
    `The engine returned unsupported product home demo focus '${activation.focusKind}'.`);
}

export function homeDemosEntryHtml(
  enginePending: boolean,
  catalogError: string,
  escapeHtml: (value: string) => string,
): string {
  const catalog = productHomeDemoCatalog();
  const disabled = enginePending || Boolean(catalogError) || catalog.length === 0;
  const count = enginePending
    ? "Loading catalog"
    : catalogError
      ? "Catalog unavailable"
      : catalog.length === 0
        ? "No demos available"
        : `${catalog.length} available`;
  return `<button id="home-demos" class="home-demo" type="button" ${disabled ? "disabled" : ""}><strong>Browse demos →</strong><small>${escapeHtml(count)}</small></button>`;
}
