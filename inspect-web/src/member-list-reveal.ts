// Keeps the navigation list's scroll position across renders, and reveals a restored member
// selection at the top of its member list.
//
// Every render rebuilds the navigation list (`#type-list`), which resets its scroll position.
// The keeper records the list's scope and scroll position before the render and restores them
// afterwards when the scope is unchanged, so a reader's position -- or a reveal -- survives the
// renders that follow (documentation, declarations, and other member detail loading).
//
// A member list that appears for a new type scope arms a reveal. The first render that shows a
// selected member in that scope moves the member's row to the top of the list, as far as the
// rows after it allow. The selection may arrive in a later render than the list itself, as it
// does for graph-member deep links. Any user input disarms a pending reveal, so clicking or
// keyboard stepping in a list never makes it jump. The list scrolls itself; the page does not.

export interface RevealableRow {
  getBoundingClientRect(): { top: number };
}

export interface RevealableList extends RevealableRow {
  readonly dataset: { navScope?: string | undefined };
  readonly classList: { contains(token: string): boolean };
  scrollTop: number;
  readonly scrollHeight: number;
  readonly clientHeight: number;
  querySelector(selector: string): RevealableRow | null;
}

export interface RevealableDocument {
  querySelector(selector: string): RevealableList | null;
  addEventListener(
    type: "pointerdown" | "keydown",
    listener: () => void,
    options: { capture: true; passive: true },
  ): void;
}

export const NAVIGATION_LIST_SELECTOR = "#type-list";
const MEMBER_LIST_CLASS = "member-list";

// The member's own row, so a selected overload keeps its group heading in view above it.
const REVEALED_ROW_SELECTOR = ".active-group, .selected";

export function revealRowAtTop(list: RevealableList, row: RevealableRow): void {
  const offset = row.getBoundingClientRect().top - list.getBoundingClientRect().top;
  const maximum = Math.max(0, list.scrollHeight - list.clientHeight);
  list.scrollTop = Math.min(Math.max(0, list.scrollTop + offset), maximum);
}

export interface NavigationScrollKeeper {
  beforeRender(document: RevealableDocument): void;
  afterRender(document: RevealableDocument): void;
}

export function createNavigationScrollKeeper(): NavigationScrollKeeper {
  let listening = false;
  let carried: { scope: string; scrollTop: number } | null = null;
  let seenMemberScope: string | null = null;
  let revealArmed = false;
  const disarm = () => {
    revealArmed = false;
  };
  return {
    beforeRender(document) {
      if (!listening) {
        listening = true;
        const options = { capture: true, passive: true } as const;
        document.addEventListener("pointerdown", disarm, options);
        document.addEventListener("keydown", disarm, options);
      }
      const list = document.querySelector(NAVIGATION_LIST_SELECTOR);
      const scope = list?.dataset.navScope;
      carried = list && scope !== undefined
        ? { scope, scrollTop: list.scrollTop }
        : null;
    },
    afterRender(document) {
      const list = document.querySelector(NAVIGATION_LIST_SELECTOR);
      const scope = list?.dataset.navScope;
      if (!list || scope === undefined) {
        seenMemberScope = null;
        revealArmed = false;
        return;
      }
      if (carried?.scope === scope)
        list.scrollTop = carried.scrollTop;
      carried = null;

      if (!list.classList.contains(MEMBER_LIST_CLASS)) {
        seenMemberScope = null;
        revealArmed = false;
        return;
      }
      if (scope !== seenMemberScope) {
        seenMemberScope = scope;
        revealArmed = true;
      }
      if (!revealArmed || list.clientHeight === 0)
        return;
      const row = list.querySelector(REVEALED_ROW_SELECTOR);
      if (!row)
        return;
      revealRowAtTop(list, row);
      revealArmed = false;
    },
  };
}
