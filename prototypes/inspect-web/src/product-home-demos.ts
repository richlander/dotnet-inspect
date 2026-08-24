import {
  encodeWorkspaceShareState,
  type WorkspaceUrlState,
} from "./workspace-navigation.ts";

/**
 * Browser projection of `ProductInspectionDemos` (CLI `demo list` / `demo <id>`).
 * Ids, titles, summaries, package pins, and type/member anchors must stay aligned
 * with `src/DotnetInspector.Queries/Definitions/ProductInspectionDemos.cs`.
 *
 * Acquisition substrate still differs for platform: product uses an unversioned
 * host `runtime` coordinate; inspect-web loads the resident Microsoft.NETCore.App
 * runtime pack until WorkspaceContextLoader platform groups land. Call-graph run
 * stays imperative (multi-package member section) rather than a share deep link.
 */

export const STJ_SERIALIZER_DEMO_ID = "stj-serializer";
export const EXTENSIONS_CALLGRAPH_DEMO_ID = "extensions-callgraph";
export const PLATFORM_LIST_DEMO_ID = "platform-list";

export type ProductHomeDemoId =
  | typeof STJ_SERIALIZER_DEMO_ID
  | typeof EXTENSIONS_CALLGRAPH_DEMO_ID
  | typeof PLATFORM_LIST_DEMO_ID;

export interface ProductHomeDemoCatalogEntry {
  id: ProductHomeDemoId;
  title: string;
  summary: string;
}

/** Catalog order matches `ProductInspectionDemos.Entries`. */
export const PRODUCT_HOME_DEMO_CATALOG: readonly ProductHomeDemoCatalogEntry[] = [
  {
    id: STJ_SERIALIZER_DEMO_ID,
    title: "System.Text.Json",
    summary: "Browse a real package API",
  },
  {
    id: EXTENSIONS_CALLGRAPH_DEMO_ID,
    title: "Cross-package call graph",
    summary: "Trace calls across three packages",
  },
  {
    id: PLATFORM_LIST_DEMO_ID,
    title: ".NET Platform",
    summary: "Inspect platform BCL types",
  },
];

const PRODUCT_HOME_DEMO_ID_SET: ReadonlySet<string> = new Set(
  PRODUCT_HOME_DEMO_CATALOG.map(entry => entry.id),
);

export function isProductHomeDemoId(value: string | undefined | null): value is ProductHomeDemoId {
  return typeof value === "string" && PRODUCT_HOME_DEMO_ID_SET.has(value);
}

/** STJ package pin shared with `ProductInspectionDemos.CreateStjSerializerRecords`. */
export const STJ_SERIALIZER_PACKAGE = {
  id: "System.Text.Json",
  version: "10.0.0",
  framework: "net10.0",
} as const;

export const STJ_SERIALIZER_TYPE = "System.Text.Json.JsonSerializer";

/**
 * Browser runtime-pack tab for the platform-list demo. Product CLI uses unversioned
 * host `runtime`; this is the inspect-web share encoding of that platform member.
 */
export const PLATFORM_RUNTIME_PACK = {
  id: "Microsoft.NETCore.App",
  version: "10.0.10",
  framework: "net10.0",
} as const;

export const PLATFORM_LIST_LIBRARY = "System.Private.CoreLib";
export const PLATFORM_LIST_TYPE = "System.Collections.Generic.List`1";

/** Packages and member anchor for `extensions-callgraph` (product + web). */
export const EXTENSIONS_CALLGRAPH = {
  packages: [
    {
      id: "Microsoft.Extensions.DependencyInjection.Abstractions",
      version: "10.0.0",
      framework: "net10.0",
    },
    {
      id: "Microsoft.Extensions.Logging",
      version: "10.0.0",
      framework: "net10.0",
    },
    {
      id: "Microsoft.Extensions.Http",
      version: "10.0.0",
      framework: "net10.0",
    },
  ],
  typeId:
    "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
  memberName: "TryAddEnumerable",
  memberKind: "method",
  memberAnchorDigest: "74b6b4b321",
  memberSection: "call-graph",
} as const;

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

/** Deep-link for demos that restore via workspace location; null = imperative runner. */
export function productHomeDemoLocationHref(id: ProductHomeDemoId): string | null {
  switch (id) {
    case STJ_SERIALIZER_DEMO_ID:
      return locationHref(workspaceUrlState({
        package: STJ_SERIALIZER_PACKAGE.id,
        tabs: [{ ...STJ_SERIALIZER_PACKAGE }],
        active: 0,
        selectedTypeId: STJ_SERIALIZER_TYPE,
      }));
    case PLATFORM_LIST_DEMO_ID:
      return locationHref(workspaceUrlState({
        package: PLATFORM_RUNTIME_PACK.id,
        tabs: [
          { ...STJ_SERIALIZER_PACKAGE },
          { ...PLATFORM_RUNTIME_PACK },
        ],
        active: 1,
        library: PLATFORM_LIST_LIBRARY,
        selectedTypeId: PLATFORM_LIST_TYPE,
      }));
    case EXTENSIONS_CALLGRAPH_DEMO_ID:
      return null;
    default: {
      const _exhaustive: never = id;
      return _exhaustive;
    }
  }
}
