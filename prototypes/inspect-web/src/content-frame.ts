export type ContentFramePane = "navigation" | "detail";
export type ContentFrameFocusOwner =
  | ContentFramePane
  | "navigation-toggle"
  | null;
export type ContentFrameFocusTarget =
  | "navigation"
  | "navigation-toggle"
  | null;

export const CONTENT_FRAME_NARROW_QUERY = "(max-width: 780px)";

export interface ContentFrameResizeDecision {
  pane: ContentFramePane;
  render: boolean;
  focus: ContentFrameFocusTarget;
}

export function decideContentFrameResize(
  pane: ContentFramePane,
  narrow: boolean,
  focusOwner: ContentFrameFocusOwner,
): ContentFrameResizeDecision {
  if (!narrow) {
    return {
      pane,
      render: false,
      focus: focusOwner === "navigation-toggle" ? "navigation" : null,
    };
  }

  const nextPane = focusOwner === "navigation"
    ? "navigation"
    : focusOwner === "detail" || focusOwner === "navigation-toggle"
      ? "detail"
      : pane;
  const render = nextPane !== pane;
  return {
    pane: nextPane,
    render,
    focus: render
      ? nextPane === "navigation"
        ? "navigation"
        : "navigation-toggle"
      : null,
  };
}

export function renderContentNavigationBar(label: "Types" | "Members") {
  return `
    <div class="content-navigation-bar">
      <button id="content-navigation-toggle" class="content-navigation-toggle"
        type="button" aria-controls="content-navigation-pane">
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <path d="M12.5 4.5 7 10l5.5 5.5"></path>
        </svg>
        <span>${label}</span>
      </button>
    </div>`;
}

export function renderContentNavigationCloseButton() {
  return `
    <button id="content-navigation-close" class="content-navigation-close"
      type="button" title="Show details" aria-label="Show details">
      <svg viewBox="0 0 20 20" aria-hidden="true">
        <path d="m7.5 4.5 5.5 5.5-5.5 5.5"></path>
      </svg>
    </button>`;
}

export interface ContentFrameBindingActions {
  onShowDetail: () => void;
  onShowNavigation: () => void;
}

export function bindContentFrame(
  root: ParentNode,
  actions: ContentFrameBindingActions,
) {
  root.querySelector("#content-navigation-toggle")?.addEventListener(
    "click",
    actions.onShowNavigation);
  root.querySelector("#content-navigation-close")?.addEventListener(
    "click",
    actions.onShowDetail);
}

export function bindContentFrameMedia(
  media: MediaQueryList,
  onChange: (event: MediaQueryListEvent) => void,
) {
  media.addEventListener("change", onChange);
}

export function focusContentNavigation(root: ParentNode) {
  const list = root.querySelector<HTMLElement>("#type-list");
  list?.focus({ preventScroll: true });
  root.querySelector<HTMLElement>("#type-list .selected")
    ?.scrollIntoView({ block: "nearest" });
}

export function focusContentNavigationToggle(root: ParentNode) {
  root.querySelector<HTMLElement>("#content-navigation-toggle")
    ?.focus({ preventScroll: true });
}
