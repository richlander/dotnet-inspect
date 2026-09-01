import {
  parsePackageQuery,
  type ParsedPackageQuery,
} from "./package-controls.ts";
import {
  isProductHomeDemoId,
  type ProductHomeDemoId,
} from "./product-home-demos.ts";

/** Product home-demo ids (`ProductInspectionDemos` / CLI `demo <id>`). */
export type HomeDemo = ProductHomeDemoId;

export interface WorkbenchShellBindingActions {
  onDismissNotice: () => void;
  onDismissPackageNotice: () => void;
  onGoHome: () => void;
  onHelp: () => void;
  onNavigateBack: () => void;
  onNavigateForward: () => void;
  onRetryNotice: () => void;
  onSearch: () => void;
  onShare: () => void;
}

export interface HomeShellBindingActions {
  onDemo: (demo: HomeDemo) => void;
  onDismissNotice: () => void;
  onOpenCredits: () => void;
  onToggleTheme: () => void;
}

export interface LoadErrorShellBindingActions {
  onOpenPackage: (query: ParsedPackageQuery) => void;
  onRetry: () => void;
}

export interface WorkbenchShellHtmlOptions {
  subjectInspectorHtml: string;
  workspaceTitleHtml: string;
}

export function workbenchShellHtml(
  options: WorkbenchShellHtmlOptions,
): string {
  return `
      <header class="titlebar">
        <a class="brand" href="/" aria-label="dotnet inspect home"><span class="brand-glyph">◇</span><span>dotnet-inspect</span></a>
        ${options.subjectInspectorHtml}
        <div class="workspace-title" aria-label="Active workspace">
          ${options.workspaceTitleHtml}
        </div>
        <nav class="title-actions" aria-label="Application">
          <button id="help" type="button" aria-label="Keyboard help">?</button>
          <button id="open-search" type="button" aria-haspopup="dialog" title="Search (Ctrl/Command+P)">Search</button>
          <button id="go-home" type="button">Home</button>
          <button id="open-settings" type="button">Settings</button>
        </nav>
      </header>`;
}

export function bindWorkbenchShell(
  root: ParentNode,
  actions: WorkbenchShellBindingActions,
) {
  root.querySelector("#share")
    ?.addEventListener("click", actions.onShare);
  root.querySelector("#dismiss-notice")
    ?.addEventListener("click", actions.onDismissNotice);
  root.querySelector("#retry-notice")
    ?.addEventListener("click", actions.onRetryNotice);
  root.querySelector("#dismiss-package-notice")
    ?.addEventListener("click", actions.onDismissPackageNotice);
  root.querySelector("#nav-back")
    ?.addEventListener("click", actions.onNavigateBack);
  root.querySelector("#nav-forward")
    ?.addEventListener("click", actions.onNavigateForward);
  root.querySelector("#go-home")
    ?.addEventListener("click", actions.onGoHome);
  root.querySelector("#open-search")
    ?.addEventListener("click", () => actions.onSearch());
  root.querySelector("#help")
    ?.addEventListener("click", actions.onHelp);
}

export function focusWorkbenchSearch(root: ParentNode): boolean {
  const search = root.querySelector<HTMLElement>("#open-search");
  if (!search) return false;
  search.focus();
  return true;
}

export function bindHomeShell(
  root: ParentNode,
  actions: HomeShellBindingActions,
) {
  root.querySelector("#home-theme")
    ?.addEventListener("click", actions.onToggleTheme);
  root.querySelector("#dismiss-notice")
    ?.addEventListener("click", actions.onDismissNotice);
  root.querySelector("#home-credits")
    ?.addEventListener("click", event => {
      if (("button" in event && event.button !== 0)
          || ("metaKey" in event && event.metaKey === true)
          || ("ctrlKey" in event && event.ctrlKey === true)
          || ("shiftKey" in event && event.shiftKey === true)
          || ("altKey" in event && event.altKey === true)) {
        return;
      }
      event.preventDefault();
      actions.onOpenCredits();
    });
  root.querySelectorAll<HTMLElement>("[data-home-demo]").forEach(button =>
    button.addEventListener("click", () => {
      const demo = button.dataset.homeDemo;
      if (isProductHomeDemoId(demo)) {
        actions.onDemo(demo);
      }
    }));
}

export function bindLoadErrorShell(
  root: ParentNode,
  actions: LoadErrorShellBindingActions,
) {
  root.querySelector("#retry-load")
    ?.addEventListener("click", actions.onRetry);
  root.querySelector("#error-package-query")
    ?.addEventListener("submit", event => {
      event.preventDefault();
      const input =
        root.querySelector<HTMLInputElement>("#error-package-input");
      const parsed = parsePackageQuery(input?.value ?? "");
      if (parsed) actions.onOpenPackage(parsed);
    });
  root.querySelector("#toggle-error-detail")
    ?.addEventListener("click", () => {
      const detail =
        root.querySelector<HTMLElement>(".load-error-detail");
      if (detail) detail.hidden = !detail.hidden;
    });
}
