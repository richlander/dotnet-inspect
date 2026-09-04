import type {
  BrowserHomeDemoCatalogEntry,
  BrowserHomeDemoMember,
  BrowserHomeDemoResolved,
  BrowserWorkspaceShareTab,
} from "./inspect-web-engine.d.ts";
import {
  encodeWorkspaceShareState,
  type WorkspaceShareEncoder,
  type WorkspaceUrlState,
} from "./workspace-navigation.ts";
import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";

/**
 * Browser host adapters over product home demos exported by the Wasm engine
 * (`ListHomeDemos` / `ResolveHomeDemo` → `ProductInspectionDemos`).
 *
 * Catalog ids/titles/summaries and resolved coordinates come from C#. This
 * module owns only the Browser projection into the long-form share transport.
 * The engine owns packet validation, transposition, and canonical encoding.
 */

export type ProductHomeDemoId = string;

export type ProductHomeDemoCatalogEntry = BrowserHomeDemoCatalogEntry;

export type ProductHomeDemoResolved = BrowserHomeDemoResolved;

/** Residual Browser target for the product's unversioned `runtime` platform. */
export const PLATFORM_RUNTIME_PACK = {
  source: ":Platform",
  version: "10.0.10",
  framework: "net10.0",
} as const;

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

function getProductHomeDemoCatalog(): readonly ProductHomeDemoCatalogEntry[] {
  return catalogEntries;
}

export function productHomeDemoCatalog():
  readonly ProductHomeDemoCatalogEntry[] {
  return getProductHomeDemoCatalog();
}

export function isProductHomeDemoId(
  value: string | undefined | null,
): value is ProductHomeDemoId {
  return typeof value === "string" && catalogIdSet.has(value);
}

export function isProductHomeDemosPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, ROUTED_ENTRY_PATHS.demos);
}

function locationHref(
  state: WorkspaceUrlState,
  encode: WorkspaceShareEncoder,
): string {
  const params = new URLSearchParams();
  params.set("package", state.package);
  params.set("w", encodeWorkspaceShareState(state, encode));
  return `?${params.toString()}`;
}

const BROWSER_RUNTIME_PACKAGE = "Microsoft.NETCore.App";

function packageTab(
  member: BrowserHomeDemoMember,
  index: number,
): BrowserWorkspaceShareTab {
  if (member.kind === "package") {
    if (!member.version || !member.framework) {
      throw new Error(
        `Product home demo package '${member.id}' is missing version/framework pins.`);
    }
    return {
      id: `t${index}`,
      kind: "package",
      source: member.id,
      version: member.version,
      framework: member.framework,
      runtimeIdentifier: null,
    };
  }

  if (member.kind === "platform" && member.id === "runtime") {
    // Residual only for the product unversioned `runtime` shape. Explicit pins
    // are not silently rewritten to the browser runtime-pack defaults.
    if (member.version || member.framework || member.assembly) {
      throw new Error(
        "Product home demo platform 'runtime' is pinned; browser residual maps only the unversioned shape.");
    }
    // Residual until WorkspaceContextLoader platform groups are the browser substrate.
    return {
      id: `t${index}`,
      kind: "group",
      source: PLATFORM_RUNTIME_PACK.source,
      version: PLATFORM_RUNTIME_PACK.version,
      framework: PLATFORM_RUNTIME_PACK.framework,
      runtimeIdentifier: null,
    };
  }

  throw new Error(
    `Product home demo member '${member.kind}:${member.id}' has no browser tab mapping.`);
}

/**
 * Deep-link for demos that restore via workspace location.
 * Returns null when the demo runs through an engine operation instead
 * (member-bound Call Graph today).
 */
export function productHomeDemoLocationHref(
  demo: ProductHomeDemoResolved,
  encode: WorkspaceShareEncoder,
): string | null {
  const section = demo.view.section;
  if (section === "Call Graph" && demo.view.memberAnchor) {
    return null;
  }

  const tabs = demo.tabs.map((tab, index) => packageTab(tab.member, index));
  if (tabs.length === 0) {
    throw new Error(`Product home demo '${demo.id}' has no navigation tabs.`);
  }

  const active = Math.min(
    Math.max(demo.focusTabIndex, 0),
    tabs.length - 1);
  const focusTab = tabs[active];
  if (!focusTab) {
    throw new Error(`Product home demo '${demo.id}' has no active navigation tab.`);
  }
  const groupTabs = tabs.filter(tab => tab.kind === "group");
  if (groupTabs.length > 1) {
    throw new Error(
      `Product home demo '${demo.id}' has multiple platform group tabs.`);
  }
  const contextTabIds = [
    ...groupTabs.map(tab => tab.id),
    ...tabs.filter(tab => tab.kind === "package").map(tab => tab.id),
  ];
  return locationHref({
    package: focusTab.kind === "group"
      ? BROWSER_RUNTIME_PACKAGE
      : focusTab.source,
    subject: null,
    tabs,
    contexts: [{
      id: "g0",
      tabIds: contextTabIds,
    }],
    activeTabId: focusTab.id,
    selectedContextId: "g0",
    view: {
      lens: "api",
      type: demo.view.type,
      memberAnchor: null,
      memberSignature: null,
      section: null,
      libraries: demo.view.library ? [demo.view.library] : [],
    },
  }, encode);
}

export function homeDemosEntryHtml(
  enginePending: boolean,
  catalogError: string,
  escapeHtml: (value: string) => string,
): string {
  const catalog = getProductHomeDemoCatalog();
  const disabled = enginePending || Boolean(catalogError) || catalog.length === 0;
  const count = enginePending
    ? "Loading catalog"
    : catalogError
      ? "Catalog unavailable"
      : catalog.length === 0
        ? "No demos available"
        : `${catalog.length} available`;
  return `<button id="home-demos" class="home-demo" type="button" ${disabled ? "disabled" : ""}><strong>Demos</strong><small>${escapeHtml(count)}</small></button>`;
}
