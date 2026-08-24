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
 * platform → Microsoft.NETCore.App runtime-pack mapping, product section →
 * web member-section tokens, and the imperative call-graph runner inputs.
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
    selectedTypeId: "",
    selectedMemberKey: "",
    selectedOverloadIndex: null,
    memberSection: "overview",
    selectedBodyTarget: null,
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
 * Returns null when the demo needs the imperative multi-package runner
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
  const focusPackage = tabs[active].id;
  return locationHref(workspaceUrlState({
    package: focusPackage,
    tabs,
    active,
    library: demo.view.library,
    selectedTypeId: demo.view.type ?? "",
  }));
}

/** Inputs for the residual imperative multi-package call-graph runner. */
export function callGraphDemoRunnerSpec(demo: ProductHomeDemoResolved): {
  packages: { id: string; version: string; framework: string }[];
  /** Package id for the navigation focus tab (primary activation target). */
  focusPackageId: string;
  typeId: string;
  memberName: string;
  memberKind: string;
  memberAnchorDigest: string;
  memberSection: "call-graph";
} {
  if (demo.view.section !== "Call Graph" || !demo.view.memberAnchor || !demo.view.type) {
    throw new Error(
      `Product home demo '${demo.id}' is not a member-bound Call Graph runner.`);
  }

  const packages = demo.workspaceMembers.map(packageTab);
  if (packages.length === 0) {
    throw new Error(`Product home demo '${demo.id}' has no workspace members.`);
  }

  const focusTabs = demo.tabs.map(tab => packageTab(tab.member));
  if (focusTabs.length === 0) {
    throw new Error(`Product home demo '${demo.id}' has no navigation tabs.`);
  }
  const focusIndex = Math.min(
    Math.max(demo.focusTabIndex, 0),
    focusTabs.length - 1);
  const focusPackageId = focusTabs[focusIndex].id;

  const memberKey = demo.view.memberKey ?? "";
  const colon = memberKey.indexOf(":");
  const memberKind = colon >= 0 ? memberKey.slice(0, colon) : "method";
  const memberName = colon >= 0 ? memberKey.slice(colon + 1) : memberKey;
  if (!memberName) {
    throw new Error(
      `Product home demo '${demo.id}' Call Graph view is missing memberKey.`);
  }

  return {
    packages,
    focusPackageId,
    typeId: demo.view.type,
    memberName,
    memberKind,
    memberAnchorDigest: demo.view.memberAnchor,
    memberSection: "call-graph",
  };
}

/**
 * Pending home-row paint while the Wasm engine catalog is not yet installed.
 * Layout-only placeholders — titles come from `ListHomeDemos` after bootstrap.
 */
export const HOME_DEMO_PENDING_SLOT_COUNT = 3;

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
