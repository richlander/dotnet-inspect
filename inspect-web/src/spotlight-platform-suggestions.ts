// Browser-local suggestion preferences do not change Platform membership or search.
export interface PlatformSuggestion {
  assembly: string;
  pack: string;
}

const storageKey = "inspect-dismissed-platform-suggestions";
function key(row: PlatformSuggestion): string {
  return JSON.stringify([row.pack, row.assembly.toLowerCase()]);
}

export function createPlatformSpotlightSuggestions(
  storage: Pick<Storage, "getItem" | "setItem">,
) {
  let dismissed = new Set<string>();
  try {
    const entries: unknown = JSON.parse(storage.getItem(storageKey) || "[]");
    if (Array.isArray(entries)) {
      dismissed = new Set(entries.filter((entry): entry is string => typeof entry === "string"));
    }
  } catch {
    // A missing or unreadable preference does not hide catalog suggestions.
  }

  function update(row: PlatformSuggestion, hide: boolean): void {
    const identity = key(row);
    if (dismissed.has(identity) === hide) return;
    const next = new Set(dismissed);
    if (hide) next.add(identity);
    else next.delete(identity);
    // A failed write must not look like a successful dismissal or restoration.
    storage.setItem(storageKey, JSON.stringify([...next]));
    dismissed = next;
  }

  return {
    isDismissed: (row: PlatformSuggestion): boolean => dismissed.has(key(row)),
    dismiss: (row: PlatformSuggestion): void => update(row, true),
    remember: (row: PlatformSuggestion): void => update(row, false),
  };
}
