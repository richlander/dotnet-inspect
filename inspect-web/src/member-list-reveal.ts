// Reveals a restored member selection at the top of its member list, and holds it there until
// the reader acts.
//
// A member list that appears for a new type scope arms a reveal. The first render that shows a
// selected member in that scope moves the member's row to the top of the list, as far as the
// rows after it allow. The selection may arrive in a later render than the list itself, as it
// does for graph-member deep links.
//
// Every render rebuilds the navigation list (`#type-list`), which resets its scroll position,
// and a restored view is followed by member detail renders (documentation, declarations). The
// revealed offset is therefore reapplied after each rebuild while the list shows the same scope
// and selection. Member focus snapshots taken between those renders capture the revealed
// offset, so their restores keep it too.
//
// Any user input, or a change of scope or selection, ends both a pending reveal and a held one.
// From then on the list behaves exactly as it did before: rebuilt lists start at the top unless
// the member focus restore returns them to the reader's position. The list scrolls itself; the
// page does not.

export interface RevealableRow {
  getBoundingClientRect(): { top: number };
}

export interface RevealableList extends RevealableRow {
  readonly dataset: { navScope?: string | undefined; navSelection?: string | undefined };
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

export interface MemberListRevealer {
  afterRender(document: RevealableDocument): void;
}

export function createMemberListRevealer(): MemberListRevealer {
  let listening = false;
  let seenScope: string | null = null;
  let armed = false;
  let held: { scope: string; selection: string; scrollTop: number } | null = null;
  const stop = () => {
    armed = false;
    held = null;
  };
  return {
    afterRender(document) {
      if (!listening) {
        listening = true;
        const options = { capture: true, passive: true } as const;
        document.addEventListener("pointerdown", stop, options);
        document.addEventListener("keydown", stop, options);
      }
      const list = document.querySelector(NAVIGATION_LIST_SELECTOR);
      const scope = list?.dataset.navScope;
      if (!list || scope === undefined || !list.classList.contains(MEMBER_LIST_CLASS)) {
        seenScope = null;
        stop();
        return;
      }
      const selection = list.dataset.navSelection ?? "";
      if (held !== null) {
        if (held.scope === scope && held.selection === selection) {
          list.scrollTop = held.scrollTop;
          return;
        }
        held = null;
      }
      if (scope !== seenScope) {
        seenScope = scope;
        armed = true;
      }
      if (!armed || list.clientHeight === 0)
        return;
      const row = list.querySelector(REVEALED_ROW_SELECTOR);
      if (!row)
        return;
      revealRowAtTop(list, row);
      armed = false;
      held = { scope, selection, scrollTop: list.scrollTop };
    },
  };
}
