import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";

export const DIAGNOSTICS_PATH = ROUTED_ENTRY_PATHS.diagnostics;

const DIAGNOSTICS_HISTORY_KEY = "inspectWebDiagnosticsEntry";

function historyRecord(value: unknown): Record<string, unknown> {
  const record: Record<string, unknown> = {};
  if (typeof value !== "object" || value === null) return record;
  for (const key of Reflect.ownKeys(value)) {
    if (typeof key === "string") record[key] = Reflect.get(value, key);
  }
  return record;
}

export function isDiagnosticsPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, DIAGNOSTICS_PATH);
}

export function diagnosticsHistoryState(
  currentState: unknown,
): Record<string, unknown> {
  return {
    ...historyRecord(currentState),
    [DIAGNOSTICS_HISTORY_KEY]: true,
  };
}

export function isDiagnosticsHistoryEntry(state: unknown): boolean {
  return typeof state === "object"
    && state !== null
    && Reflect.get(state, DIAGNOSTICS_HISTORY_KEY) === true;
}
