// Reveals a restored member selection at the top of its member list.
//
// A member list appears for a new type scope with its member already selected when the view is
// restored from a URL, navigation history, or another deep link. Keyboard stepping and clicks
// keep the nearest-edge behavior of the list they act in; only the first render of a scope with
// a selection moves the selected member's row to the top, as far as the list's remaining rows
// allow. The list scrolls itself, so the page does not.

export interface RevealableRow {
  getBoundingClientRect(): { top: number };
}

export interface RevealableList extends RevealableRow {
  readonly dataset: { navScope?: string | undefined };
  scrollTop: number;
  readonly scrollHeight: number;
  readonly clientHeight: number;
  querySelector(selector: string): RevealableRow | null;
}

export interface RevealableDocument {
  querySelector(selector: string): RevealableList | null;
}

export const MEMBER_LIST_SELECTOR = "#type-list.member-list";

// The member's own row, so a selected overload keeps its group heading in view above it.
const REVEALED_ROW_SELECTOR = ".active-group, .selected";

export function revealRowAtTop(list: RevealableList, row: RevealableRow): void {
  const offset = row.getBoundingClientRect().top - list.getBoundingClientRect().top;
  const maximum = Math.max(0, list.scrollHeight - list.clientHeight);
  list.scrollTop = Math.min(Math.max(0, list.scrollTop + offset), maximum);
}

export interface MemberListRevealer {
  afterRender(document: RevealableDocument): void;
}

export function createMemberListRevealer(): MemberListRevealer {
  let seenScope: string | null = null;
  return {
    afterRender(document) {
      const list = document.querySelector(MEMBER_LIST_SELECTOR);
      const scope = list?.dataset.navScope ?? null;
      if (!list || scope === null) {
        seenScope = null;
        return;
      }
      if (scope === seenScope) return;
      seenScope = scope;
      const row = list.querySelector(REVEALED_ROW_SELECTOR);
      if (row) revealRowAtTop(list, row);
    },
  };
}
