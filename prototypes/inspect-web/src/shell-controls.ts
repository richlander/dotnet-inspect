import { parsePackageQuery } from "./package-bar.ts";
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
  onShare: () => void;
  onToggleTheme: () => void;
}

export interface HomeShellBindingActions {
  onDemo: (demo: HomeDemo) => void;
  onDismissNotice: () => void;
  onOpenCredits: () => void;
  onToggleTheme: () => void;
}

export interface LoadErrorShellBindingActions {
  onOpenPackage: (packageId: string, version: string) => void;
  onRetry: () => void;
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
  root.querySelector("#theme-toggle")
    ?.addEventListener("click", actions.onToggleTheme);
  root.querySelector("#help")
    ?.addEventListener("click", actions.onHelp);
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
      if (parsed) actions.onOpenPackage(parsed.packageId, parsed.version);
    });
  root.querySelector("#toggle-error-detail")
    ?.addEventListener("click", () => {
      const detail =
        root.querySelector<HTMLElement>(".load-error-detail");
      if (detail) detail.hidden = !detail.hidden;
    });
}
