import type {
  BrowserHomeDemoCatalogEntry,
  BrowserHomeDemoMember,
  BrowserHomeDemoResolved,
} from "./inspect-web-engine.d.ts";
import {
  encodeWorkspaceShareState,
  type WorkspaceUrlState,
} from "./workspace-navigation.ts";

/**
 * Browser host adapters over product home demos exported by the Wasm engine
 * (`ListHomeDemos` / `ResolveHomeDemo` → `ProductInspectionDemos`).
 *
 * Catalog ids/titles/summaries and resolved coordinates come from C#. This
 * module owns only host encoding: workspace share packets, the residual
 * platform → Microsoft.NETCore.App runtime-pack mapping, and location state
 * for demos that restore through a share packet.
 */

export type ProductHomeDemoId = string;

export type ProductHomeDemoCatalogEntry = BrowserHomeDemoCatalogEntry;

export type ProductHomeDemoResolved = BrowserHomeDemoResolved;

/** Residual browser share encoding for product unversioned `runtime` platform. */
export const PLATFORM_RUNTIME_PACK = {
  id: "Microsoft.NETCore.App",
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

export function isProductHomeDemoId(
  value: string | undefined | null,
): value is ProductHomeDemoId {
  return typeof value === "string" && catalogIdSet.has(value);
}

function workspaceUrlState(partial: Partial<WorkspaceUrlState> & Pick<
  WorkspaceUrlState,
  "package" | "tabs" | "active"
>): WorkspaceUrlState {
  return {
    lens: "api",
    atPackageRoot: false,
    packageLens: "overview",
    library: null,
    libraryPack: null,
    selectedTypeId: "",
    selectedMemberKey: "",
    selectedOverloadIndex: null,
    memberSection: "overview",
    selectedBodyTarget: null,
    graphTarget: null,
    memberBrowse: false,
    memberTextFilter: "",
    memberKindFilter: "all",
    memberAccessibilityFilter: "all",
    memberTraitFilter: "",
    ...partial,
  };
}

function locationHref(state: WorkspaceUrlState): string {
  const params = new URLSearchParams();
  params.set("package", state.package);
  params.set("w", encodeWorkspaceShareState(state));
  return `?${params.toString()}`;
}

function packageTab(member: BrowserHomeDemoMember): {
  id: string;
  version: string;
  framework: string;
} {
  if (member.kind === "package") {
    if (!member.version || !member.framework) {
      throw new Error(
        `Product home demo package '${member.id}' is missing version/framework pins.`);
    }
    return {
      id: member.id,
      version: member.version,
      framework: member.framework,
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
    return { ...PLATFORM_RUNTIME_PACK };
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
): string | null {
  const section = demo.view.section;
  if (section === "Call Graph" && demo.view.memberAnchor) {
    return null;
  }

  const tabs = demo.tabs.map(tab => packageTab(tab.member));
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
  const focusPackage = focusTab.id;
  return locationHref(workspaceUrlState({
    package: focusPackage,
    tabs,
    active,
    library: demo.view.library,
    selectedTypeId: demo.view.type ?? "",
  }));
}

/**
 * Pending home-row paint while the Wasm engine catalog is not yet installed.
 * Layout-only placeholders — titles come from `ListHomeDemos` after bootstrap.
 */
export const HOME_DEMO_PENDING_SLOT_COUNT = 5;

export function homeDemoRowHtml(
  enginePending: boolean,
  escapeHtml: (value: string) => string,
): string {
  const catalog = getProductHomeDemoCatalog();
  if (catalog.length > 0) {
    return catalog.map(entry =>
      `<button class="home-demo" data-home-demo="${escapeHtml(entry.id)}" ${enginePending ? "disabled" : ""}><strong>${escapeHtml(entry.title)}</strong><small>${escapeHtml(entry.summary)}</small></button>`).join("");
  }
  if (!enginePending) return "";
  return Array.from({ length: HOME_DEMO_PENDING_SLOT_COUNT }, () =>
    `<button class="home-demo home-demo-pending" type="button" disabled aria-hidden="true"><strong>&nbsp;</strong><small>&nbsp;</small></button>`).join("");
}
