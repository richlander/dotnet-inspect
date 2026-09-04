export interface MemberFocusSnapshot {
  selector: string;
  dataTarget: {
    selector: string;
    key: string;
    value: string;
  } | null;
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

function isHtmlElement(value: Element | null): value is HTMLElement {
  return value !== null && "dataset" in value && "focus" in value;
}

function isTextInput(value: HTMLElement): value is HTMLInputElement {
  return "selectionStart" in value && "setSelectionRange" in value;
}

export function captureMemberFocus(
  document: Document,
): MemberFocusSnapshot {
  const active = isHtmlElement(document.activeElement) ? document.activeElement : null;
  const navigationList = document.querySelector<HTMLElement>("#type-list");
  let selector = "";
  let dataTarget: MemberFocusSnapshot["dataTarget"] = null;
  let selection: MemberFocusSnapshot["selection"] = null;
  if (active?.id === "member-filter" || active?.id === "type-filter") {
    const input = isTextInput(active) ? active : null;
    selector = `#${active.id}`;
    selection = input
      ? {
          start: input.selectionStart,
          end: input.selectionEnd,
          direction: input.selectionDirection,
        }
      : null;
  } else if (active?.id === "clear-member-filter") {
    selector = "#clear-member-filter";
  } else if (active?.dataset.type !== undefined) {
    dataTarget = {
      selector: "[data-type]",
      key: "type",
      value: active.dataset.type,
    };
  } else if (active?.dataset.namespace !== undefined) {
    dataTarget = {
      selector: "[data-namespace]",
      key: "namespace",
      value: active.dataset.namespace,
    };
  } else if (active?.dataset.kindFilter !== undefined) {
    dataTarget = {
      selector: "[data-kind-filter]",
      key: "kindFilter",
      value: active.dataset.kindFilter,
    };
  } else if (active?.dataset.accessChip !== undefined) {
    dataTarget = {
      selector: "[data-access-chip]",
      key: "accessChip",
      value: active.dataset.accessChip,
    };
  } else if (active?.dataset.libraryChip !== undefined) {
    dataTarget = {
      selector: "[data-library-chip]",
      key: "libraryChip",
      value: active.dataset.libraryChip,
    };
  } else if (active?.dataset.memberKindFilter !== undefined) {
    dataTarget = {
      selector: "[data-member-kind-filter]",
      key: "memberKindFilter",
      value: active.dataset.memberKindFilter,
    };
  } else if (active?.dataset.memberAccessFilter !== undefined) {
    dataTarget = {
      selector: "[data-member-access-filter]",
      key: "memberAccessFilter",
      value: active.dataset.memberAccessFilter,
    };
  } else if (active?.dataset.memberTraitFilter !== undefined) {
    dataTarget = {
      selector: "[data-member-trait-filter]",
      key: "memberTraitFilter",
      value: active.dataset.memberTraitFilter,
    };
  } else if (active?.dataset.navMember !== undefined) {
    dataTarget = {
      selector: "[data-nav-member]",
      key: "navMember",
      value: active.dataset.navMember,
    };
  } else if (active?.dataset.navOverload !== undefined) {
    dataTarget = {
      selector: "[data-nav-overload]",
      key: "navOverload",
      value: active.dataset.navOverload,
    };
  } else if (active?.dataset.taste !== undefined) {
    dataTarget = {
      selector: "[data-taste]",
      key: "taste",
      value: active.dataset.taste,
    };
  } else if (active?.dataset.packageLens !== undefined) {
    dataTarget = {
      selector: "[data-package-lens]",
      key: "packageLens",
      value: active.dataset.packageLens,
    };
  } else if (active?.dataset.lens !== undefined) {
    dataTarget = {
      selector: "[data-lens]",
      key: "lens",
      value: active.dataset.lens,
    };
  } else if (active?.dataset.memberSection !== undefined) {
    dataTarget = {
      selector: "[data-member-section]",
      key: "memberSection",
      value: active.dataset.memberSection,
    };
  } else if (active?.dataset.scope !== undefined) {
    dataTarget = {
      selector: "[data-scope]",
      key: "scope",
      value: active.dataset.scope,
    };
  } else if (active?.id === "type-list") {
    selector = "#type-list";
  } else if (active?.id && /^[A-Za-z][A-Za-z0-9_-]*$/.test(active.id)) {
    selector = `#${active.id}`;
  }

  return {
    selector,
    dataTarget,
    selection,
    navigationScope: navigationList?.dataset.navScope ?? null,
    navigationSelection: navigationList?.dataset.navSelection ?? null,
    navigationScrollTop: navigationList?.scrollTop ?? null,
    focusLost:
      active === null
      || active === document.body
      || ! active.isConnected,
  };
}

export function resolveMemberFocusSnapshot(
  current: MemberFocusSnapshot,
  fallback: MemberFocusSnapshot | null,
): MemberFocusSnapshot {
  return current.focusLost && fallback ? fallback : current;
}

export function focusPlatformGraphError(document: Document): boolean {
  const error =
    document.querySelector<HTMLElement>("#platform-drill-error");
  if (!error)
    return false;

  error.focus();
  return true;
}

export function restoreMemberFocus(
  document: Document,
  snapshot: MemberFocusSnapshot,
  requestFrame: (callback: FrameRequestCallback) => number,
  isCurrent: () => boolean = () => true,
): void {
  if (!snapshot.selector && !snapshot.dataTarget && snapshot.navigationScope === null)
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
    const replacement = snapshot.dataTarget
      ? [...document.querySelectorAll<HTMLElement>(snapshot.dataTarget.selector)]
        .find(element =>
          element.dataset[snapshot.dataTarget!.key] === snapshot.dataTarget!.value)
        ?? null
      : snapshot.selector
        ? document.querySelector<HTMLElement>(snapshot.selector)
        : null;
    const active = isHtmlElement(document.activeElement) ? document.activeElement : null;
    const canRestoreFocus =
      active === null
      || active === document.body
      || ! active.isConnected
      || active === replacement;
    if (!replacement || !canRestoreFocus)
      return;

    replacement.focus();
    if (snapshot.selection && isTextInput(replacement)) {
      replacement.setSelectionRange(
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
