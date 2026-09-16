import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";
import {
  historyEntryId,
  withHistoryEntryId,
} from "./package-query-route.ts";

export const PACKAGE_ACTIVITY_PATH = ROUTED_ENTRY_PATHS.activity;

export type PackageActivityReturnFocus =
  | "application-activity"
  | "home-search"
  | "package-search";

export interface PackageActivityHistory {
  predecessorEntryId: string;
  returnFocus: PackageActivityReturnFocus;
}

const PACKAGE_ACTIVITY_KEY = "dotnetInspectPackageActivity";

function historyRecord(value: unknown): Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? { ...value }
    : {};
}

export function isPackageActivityPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, PACKAGE_ACTIVITY_PATH);
}

export function packageActivityHistoryState(
  value: unknown,
  entryId: string,
  activity: PackageActivityHistory,
): Record<string, unknown> {
  return {
    ...withHistoryEntryId(value, entryId),
    [PACKAGE_ACTIVITY_KEY]: activity,
  };
}

export function readPackageActivityHistory(
  value: unknown,
): PackageActivityHistory | null {
  const activity = historyRecord(value)[PACKAGE_ACTIVITY_KEY];
  if (typeof activity !== "object"
    || activity === null
    || Array.isArray(activity)) {
    return null;
  }
  if (!("predecessorEntryId" in activity)
    || !("returnFocus" in activity)) {
    return null;
  }
  const predecessorEntryId = activity.predecessorEntryId;
  const returnFocus = activity.returnFocus;
  return typeof predecessorEntryId === "string"
    && predecessorEntryId.length > 0
    && (returnFocus === "application-activity"
      || returnFocus === "home-search"
      || returnFocus === "package-search")
    ? { predecessorEntryId, returnFocus }
    : null;
}

export function isPackageActivityPredecessor(
  value: unknown,
  predecessorEntryId: string | null,
): boolean {
  return predecessorEntryId !== null
    && historyEntryId(value) === predecessorEntryId;
}
