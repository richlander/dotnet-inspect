export interface MemberFocusSnapshot {
  selector: string;
  selection: {
    start: number | null;
    end: number | null;
    direction: "forward" | "backward" | "none" | null;
  } | null;
  navigationScope: string | null;
  navigationSelection: string | null;
  navigationScrollTop: number | null;
  focusLost: boolean;
}

export interface MemberFocusRestorer {
  resolve(
    current: MemberFocusSnapshot,
    fallback: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
  schedule(
    document: Document,
    snapshot: MemberFocusSnapshot,
    requestFrame: (callback: FrameRequestCallback) => number,
  ): void;
}

export function captureMemberFocus(
  document: Document,
  escapeSelectorValue: (value: string) => string,
): MemberFocusSnapshot {
  const active = document.activeElement as HTMLElement | null;
  const navigationList = document.querySelector<HTMLElement>("#type-list");
  let selector = "";
  let selection: MemberFocusSnapshot["selection"] = null;
  if (active?.id === "member-filter" || active?.id === "type-filter") {
    const input = active as HTMLInputElement;
    selector = `#${active.id}`;
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
    navigationScope: navigationList?.dataset.navScope ?? null,
    navigationSelection: navigationList?.dataset.navSelection ?? null,
    navigationScrollTop: navigationList?.scrollTop ?? null,
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
  isCurrent: () => boolean = () => true,
): void {
  if (!snapshot.selector && snapshot.navigationScope === null)
    return;

  requestFrame(() => {
    if (!isCurrent())
      return;

    const navigationList = document.querySelector<HTMLElement>("#type-list");
    if (navigationList
      && snapshot.navigationScope !== null
      && navigationList.dataset.navScope === snapshot.navigationScope
      && (navigationList.dataset.navSelection ?? null) === snapshot.navigationSelection
      && snapshot.navigationScrollTop !== null) {
      navigationList.scrollTop = snapshot.navigationScrollTop;
    }
    const replacement = snapshot.selector
      ? document.querySelector<HTMLElement>(snapshot.selector)
      : null;
    const active = document.activeElement as HTMLElement | null;
    const canRestoreFocus =
      active === null
      || active === document.body
      || active.isConnected === false
      || active === replacement;
    if (!replacement || !canRestoreFocus)
      return;

    replacement.focus();
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

export function createMemberFocusRestorer(): MemberFocusRestorer {
  let latest: MemberFocusSnapshot | null = null;
  let generation = 0;
  return {
    resolve(current, fallback) {
      const orderedFallback = fallback === null ? null : (latest ?? fallback);
      return resolveMemberFocusSnapshot(current, orderedFallback);
    },
    schedule(document, snapshot, requestFrame) {
      latest = snapshot;
      const scheduledGeneration = ++generation;
      restoreMemberFocus(
        document,
        snapshot,
        requestFrame,
        () => scheduledGeneration === generation,
      );
    },
  };
}
