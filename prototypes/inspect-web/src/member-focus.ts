export interface MemberFocusSnapshot {
  selector: string;
  selection: {
    start: number | null;
    end: number | null;
    direction: "forward" | "backward" | "none" | null;
  } | null;
  memberListScrollTop: number | null;
  focusLost: boolean;
}

export function captureMemberFocus(
  document: Document,
  escapeSelectorValue: (value: string) => string,
): MemberFocusSnapshot {
  const active = document.activeElement as HTMLElement | null;
  const memberList = document.querySelector<HTMLElement>("#type-list");
  let selector = "";
  let selection: MemberFocusSnapshot["selection"] = null;
  if (active?.id === "member-filter") {
    const input = active as HTMLInputElement;
    selector = "#member-filter";
    selection = {
      start: input.selectionStart,
      end: input.selectionEnd,
      direction: input.selectionDirection,
    };
  } else if (active?.id === "clear-member-filter") {
    selector = "#clear-member-filter";
  } else if (active?.dataset.memberKindFilter !== undefined) {
    selector =
      `[data-member-kind-filter="${escapeSelectorValue(active.dataset.memberKindFilter)}"]`;
  } else if (active?.dataset.memberAccessFilter !== undefined) {
    selector =
      `[data-member-access-filter="${escapeSelectorValue(active.dataset.memberAccessFilter)}"]`;
  } else if (active?.dataset.memberTraitFilter !== undefined) {
    selector =
      `[data-member-trait-filter="${escapeSelectorValue(active.dataset.memberTraitFilter)}"]`;
  } else if (active?.id === "type-list") {
    selector = "#type-list";
  }

  return {
    selector,
    selection,
    memberListScrollTop: memberList?.scrollTop ?? null,
    focusLost:
      active === null
      || active === document.body
      || active.isConnected === false,
  };
}

export function resolveMemberFocusSnapshot(
  current: MemberFocusSnapshot,
  fallback: MemberFocusSnapshot | null,
): MemberFocusSnapshot {
  return current.focusLost && fallback ? fallback : current;
}

export function restoreMemberFocus(
  document: Document,
  snapshot: MemberFocusSnapshot,
  requestFrame: (callback: FrameRequestCallback) => number,
): void {
  if (!snapshot.selector && snapshot.memberListScrollTop === null)
    return;

  requestFrame(() => {
    const memberList = document.querySelector<HTMLElement>("#type-list");
    if (memberList && snapshot.memberListScrollTop !== null)
      memberList.scrollTop = snapshot.memberListScrollTop;
    const replacement = snapshot.selector
      ? document.querySelector<HTMLElement>(snapshot.selector)
      : null;
    replacement?.focus();
    const input = replacement as HTMLInputElement | null;
    if (snapshot.selection && typeof input?.setSelectionRange === "function") {
      input.setSelectionRange(
        snapshot.selection.start,
        snapshot.selection.end,
        snapshot.selection.direction ?? undefined,
      );
    }
  });
}
