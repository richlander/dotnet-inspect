export function isPackageQueryPath(pathname: string): boolean {
  return pathname === "/query" || pathname === "/query/";
}

export type PackageQueryReturnFocus = "home-search" | "package-search";

export interface PackageQueryHistory {
  predecessorEntryId: string;
  returnFocus: PackageQueryReturnFocus;
}

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
    && (returnFocus === "home-search" || returnFocus === "package-search")
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

export function validPackageQueryPrefix(value: string): string {
  const prefix = value.trim();
  return prefix.length > 0
    && prefix.length <= 100
    && !Array.from(prefix).some(character =>
      character.codePointAt(0)! < 0x20 || character.codePointAt(0) === 0x7f)
    ? prefix
    : "";
}
