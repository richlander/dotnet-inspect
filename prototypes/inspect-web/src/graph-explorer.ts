import { trapModalTab } from "./shell-controls.ts";

export interface GraphExplorerTarget {
  key: string;
  title: string;
  context: string;
  content: HTMLElement;
  invoker: HTMLElement;
}

export function bindGraphExplore(root: ParentNode, open: () => void) {
  root.querySelector("[data-graph-explore]")?.addEventListener("click", open);
}

/** A placement change for an existing graph, not a second graph session. */
export function createGraphExplorer(document: Document) {
  let target: GraphExplorerTarget | null = null;
  let placeholder: Comment | null = null;
  let dialog: HTMLDialogElement | null = null;
  let contentHost: HTMLElement | null = null;
  let heading: HTMLElement | null = null;
  let context: HTMLElement | null = null;
  let focusId = "";

  function releaseContent() {
    if (placeholder?.isConnected && target) {
      placeholder.replaceWith(target.content);
    }
    placeholder = null;
  }

  function placeContent(next: GraphExplorerTarget) {
    target = next;
    placeholder = document.createComment("inline graph");
    next.content.before(placeholder);
    contentHost!.replaceChildren(next.content);
    heading!.textContent = next.title;
    context!.textContent = next.context;
    context!.title = next.context;
  }

  function close(restoreFocus = true): boolean {
    if (!dialog || !target) return false;
    const invoker = target.invoker;
    const inlineContent = target.content;
    releaseContent();
    dialog.close();
    dialog.remove();
    dialog = null;
    target = null;
    contentHost = null;
    heading = null;
    context = null;
    focusId = "";
    if (restoreFocus && invoker.isConnected) {
      if (!invoker.matches(":disabled")) {
        invoker.focus({ preventScroll: true });
      } else {
        const fallback = inlineContent.querySelector<HTMLElement>("h2");
        if (fallback) {
          fallback.tabIndex = -1;
          fallback.focus({ preventScroll: true });
        }
      }
    }
    return true;
  }

  return {
    get isOpen() { return dialog !== null; },
    open(next: GraphExplorerTarget) {
      close(false);
      dialog = document.createElement("dialog");
      dialog.className = "graph-explorer";
      dialog.setAttribute("aria-labelledby", "graph-explorer-title");
      dialog.innerHTML = `
        <header class="graph-explorer-head">
          <h2 id="graph-explorer-title" tabindex="-1"></h2>
          <span class="graph-explorer-context"></span>
          <button type="button" id="graph-explorer-close">Close</button>
        </header>
        <div class="graph-explorer-content"></div>`;
      heading = dialog.querySelector<HTMLElement>("h2")!;
      context = dialog.querySelector<HTMLElement>(".graph-explorer-context")!;
      contentHost = dialog.querySelector<HTMLElement>(".graph-explorer-content")!;
      dialog.querySelector("button")!.addEventListener("click", () => close());
      dialog.addEventListener("cancel", event => {
        event.preventDefault();
        close();
      });
      dialog.addEventListener("keydown", event => {
        if (event.key === "Tab") trapModalTab(dialog!, event);
      });
      document.body.append(dialog);
      placeContent(next);
      dialog.showModal();
      heading.focus({ preventScroll: true });
    },
    close,
    // Put the old result back before its application frame is replaced. The
    // dialog itself remains mounted, so a same-owner update stays modal.
    beforeRender(key: string | null) {
      if (!target) return;
      if (target.key !== key) {
        close(false);
        return;
      }
      focusId = dialog?.contains(document.activeElement)
        ? document.activeElement?.id ?? ""
        : "";
      releaseContent();
    },
    afterRender(next: GraphExplorerTarget | null) {
      if (!target) return;
      if (!next || next.key !== target.key) {
        close(false);
        return;
      }
      placeContent(next);
      const previous = focusId ? document.getElementById(focusId) : null;
      if (previous && dialog!.contains(previous)) {
        previous.focus({ preventScroll: true });
      } else {
        heading!.focus({ preventScroll: true });
      }
      focusId = "";
    },
  };
}
