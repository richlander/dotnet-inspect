type PackageQuerySelectionDirection =
  "forward" | "backward" | "none";

export interface PackageQueryEditorSnapshot {
  value: string;
  selectionStart: number | null;
  selectionEnd: number | null;
  selectionDirection: PackageQuerySelectionDirection | null;
}

export interface PackageQueryRenderScheduler {
  scheduleStream(): void;
  renderFull(): void;
  resume(): void;
  cancel(): void;
}

interface PackageQueryEditorElement extends HTMLElement {
  value: string;
  selectionStart: number | null;
  selectionEnd: number | null;
  selectionDirection: string | null;
  setSelectionRange(
    start: number,
    end: number,
    direction?: PackageQuerySelectionDirection,
  ): void;
}

function isPackageQueryEditorElement(
  element: Element | null,
): element is PackageQueryEditorElement {
  return element !== null
    && "value" in element
    && typeof element.value === "string"
    && "selectionStart" in element
    && "selectionEnd" in element
    && "setSelectionRange" in element
    && typeof element.setSelectionRange === "function";
}

function selectionDirection(
  element: PackageQueryEditorElement,
): PackageQuerySelectionDirection | null {
  switch (element.selectionDirection) {
    case "forward":
    case "backward":
    case "none":
      return element.selectionDirection;
    default:
      return null;
  }
}

export function capturePackageQueryEditor(
  element: Element | null,
): PackageQueryEditorSnapshot | null {
  return isPackageQueryEditorElement(element)
    ? {
        value: element.value,
        selectionStart: element.selectionStart,
        selectionEnd: element.selectionEnd,
        selectionDirection: selectionDirection(element),
      }
    : null;
}

export function restorePackageQueryEditor(
  element: Element | null,
  snapshot: PackageQueryEditorSnapshot | null,
): boolean {
  if (!snapshot || !isPackageQueryEditorElement(element)) return false;
  element.value = snapshot.value;
  if (snapshot.selectionStart !== null && snapshot.selectionEnd !== null) {
    element.setSelectionRange(
      snapshot.selectionStart,
      snapshot.selectionEnd,
      snapshot.selectionDirection ?? undefined);
  }
  return true;
}

export function packageQueryEditorCompositionActive(
  root: Document,
): boolean {
  const active = root.activeElement;
  if (active === null || !("dataset" in active)) return false;
  const dataset = active.dataset;
  return typeof dataset === "object"
    && dataset !== null
    && "queryEditorComposing" in dataset
    && dataset.queryEditorComposing === "true";
}

export function bindPackageQueryEditor(
  element: HTMLInputElement | HTMLTextAreaElement,
  publish: () => void,
  compositionSettled?: () => void,
): void {
  element.addEventListener("compositionstart", () => {
    element.dataset.queryEditorComposing = "true";
  });
  element.addEventListener("input", event => {
    if ("isComposing" in event && event.isComposing) {
      element.dataset.queryEditorComposing = "true";
      return;
    }
    publish();
  });
  element.addEventListener("compositionend", () => {
    const wasComposing =
      element.dataset.queryEditorComposing === "true";
    delete element.dataset.queryEditorComposing;
    if (!wasComposing) return;
    publish();
    compositionSettled?.();
  });
}

export function createPackageQueryRenderScheduler(
  options: {
    readonly requestFrame: (callback: () => void) => number;
    readonly cancelFrame: (handle: number) => void;
    readonly shouldRender: () => boolean;
    readonly compositionActive: () => boolean;
    readonly renderStream: () => void;
    readonly renderFull: () => void;
  },
): PackageQueryRenderScheduler {
  let frame: number | null = null;
  let deferred: "stream" | "full" | null = null;

  const cancelPendingFrame = () => {
    if (frame === null) return;
    options.cancelFrame(frame);
    frame = null;
  };

  const cancel = () => {
    deferred = null;
    cancelPendingFrame();
  };

  const scheduleStream = () => {
    if (frame !== null || deferred !== null) return;
    frame = options.requestFrame(() => {
      frame = null;
      if (!options.shouldRender()) return;
      if (options.compositionActive()) {
        deferred = "stream";
        return;
      }
      options.renderStream();
    });
  };

  const renderFull = () => {
    if (!options.shouldRender()) {
      cancel();
      return;
    }
    if (options.compositionActive()) {
      cancelPendingFrame();
      deferred = "full";
      return;
    }
    cancel();
    options.renderFull();
  };

  return {
    scheduleStream,
    renderFull,
    resume: () => {
      const pending = deferred;
      if (pending === null) return;
      deferred = null;
      if (pending === "full") {
        renderFull();
        return;
      }
      scheduleStream();
    },
    cancel,
  };
}
