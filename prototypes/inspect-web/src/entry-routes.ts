export const ROUTED_ENTRY_PATHS = {
  credits: "/credits",
  packageQuery: "/query",
} as const;

export const ENTRY_DOCUMENT_PATHS = [
  "/",
  "/index.html",
  ROUTED_ENTRY_PATHS.credits,
  ROUTED_ENTRY_PATHS.packageQuery,
] as const;

export function isRoutedEntryPath(
  pathname: string,
  route: (typeof ROUTED_ENTRY_PATHS)[keyof typeof ROUTED_ENTRY_PATHS],
): boolean {
  return pathname === route || pathname === `${route}/`;
}
