import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";

export function isPackageQueryPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, ROUTED_ENTRY_PATHS.packageQuery);
}

export type PackageQueryReturnFocus =
  | "application-query"
  | "home-search"
  | "package-search";

export interface PackageQueryHistory {
  predecessorEntryId: string;
  returnFocus: PackageQueryReturnFocus;
}

export type PackageQueryWorkspaceSuccessor =
  | { url: URL; projected: true; projectionError: null }
  | { url: URL; projected: false; projectionError: unknown };

const ENTRY_ID_KEY = "dotnetInspectEntryId";
const PACKAGE_QUERY_KEY = "dotnetInspectPackageQuery";

function historyRecord(value: unknown): Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? { ...value }
    : {};
}

export function historyEntryId(value: unknown): string | null {
  const entryId = historyRecord(value)[ENTRY_ID_KEY];
  return typeof entryId === "string" && entryId.length > 0
    ? entryId
    : null;
}

export function withHistoryEntryId(
  value: unknown,
  entryId: string,
): Record<string, unknown> {
  return {
    ...historyRecord(value),
    [ENTRY_ID_KEY]: entryId,
  };
}

export function packageQueryHistoryState(
  value: unknown,
  entryId: string,
  history: PackageQueryHistory,
): Record<string, unknown> {
  return {
    ...withHistoryEntryId(value, entryId),
    [PACKAGE_QUERY_KEY]: history,
  };
}

export function readPackageQueryHistory(
  value: unknown,
): PackageQueryHistory | null {
  const query = historyRecord(value)[PACKAGE_QUERY_KEY];
  if (typeof query !== "object" || query === null || Array.isArray(query)) {
    return null;
  }
  if (!("predecessorEntryId" in query) || !("returnFocus" in query)) {
    return null;
  }
  const predecessorEntryId = query.predecessorEntryId;
  const returnFocus = query.returnFocus;
  return typeof predecessorEntryId === "string"
    && predecessorEntryId.length > 0
    && (returnFocus === "application-query"
      || returnFocus === "home-search"
      || returnFocus === "package-search")
    ? { predecessorEntryId, returnFocus }
    : null;
}

export function isPackageQueryPredecessor(
  value: unknown,
  predecessorEntryId: string | null,
): boolean {
  return predecessorEntryId !== null
    && historyEntryId(value) === predecessorEntryId;
}

export function resolvePackageQueryWorkspaceSuccessor(
  buildRetainedWorkspaceUrl: () => URL,
  buildFallbackWorkspaceUrl: () => URL,
): PackageQueryWorkspaceSuccessor {
  try {
    return {
      url: buildRetainedWorkspaceUrl(),
      projected: true,
      projectionError: null,
    };
  } catch (projectionError) {
    return {
      url: buildFallbackWorkspaceUrl(),
      projected: false,
      projectionError,
    };
  }
}

export function validPackageQuerySearchText(value: string): string {
  return value.trim().length === 0 ? "" : value;
}
