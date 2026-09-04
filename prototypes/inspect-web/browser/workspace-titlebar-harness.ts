import {
  bindScopeBar,
  captureScopeBarFocus,
  createScopeBarState,
  renderScopeBar,
  restoreScopeBarFocus,
  scopeBarShortLabel,
  type ScopeBarBinding,
} from "../src/scope-bar.ts";
import { renderAnnotatedSourcePageActions } from "../src/annotated-source.ts";
import type {
  MemberSection,
  PackageLens,
  TypeLens,
  WorkspaceScope,
} from "../src/data.ts";
import {
  applicationMenuOwnsFocus,
  bindWorkbenchShell,
  captureApplicationMenuFocusOwner,
  focusApplicationMenuButton,
  focusWorkbenchSearch,
  renderApplicationMenu,
  renderApplicationMenuButton,
  renderKeyboardHelpDialog,
  renderTitleNavigation,
  restoreApplicationMenuFocusIfOwned,
  type ApplicationAction,
  type WorkbenchShellBinding,
  type WorkbenchShellBindingActions,
  workbenchShellHtml,
} from "../src/shell-controls.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import {
  bindSettingsPanel,
  renderSettingsView,
} from "../src/settings-panel.ts";
import {
  renderSourcePageActions,
  renderSourceResult,
} from "../src/type-panel.ts";
import {
  bindWorkspaceSubject,
  focusWorkspace,
  renderWorkspaceSubject,
  renderWorkspaceView,
} from "../src/workspace-subject.ts";

declare global {
  interface Window {
    focusWorkbenchSearchProbe: () => boolean;
    renderPackageScopeProbe: () => void;
    rerenderApplicationMenuProbe: () => void;
    rerenderScopeBarProbe: () => void;
  }
}

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const app = document.querySelector<HTMLElement>("#app");
if (!app) throw new Error("The workspace-titlebar harness root is unavailable.");
const appRoot: HTMLElement = app;
const scopeBarState = createScopeBarState();
let scopeBarBinding: ScopeBarBinding | null = null;
let workbenchShellBinding: WorkbenchShellBinding | null = null;
let applicationDialog: "settings" | "keyboard-help" | null = null;
const params = new URL(location.href).searchParams;
const workspaceMode = params.has("workspace");
const packageMode = params.has("package");
const memberMode = params.has("member");
const emptyMode = params.has("empty");
const annotatedMode = params.has("annotated");
const sourceMode = params.has("source");
const graphMode = params.has("graph");
const limitationMode = params.has("limitation");
const historyBackMode = params.has("history-back");
const historyForwardMode = params.has("history-forward");
const longMode = params.has("long");
const defaultPackageIcon =
  "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png";
const systemTextJsonIcon =
  "data:image/png;base64,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";
const packageIcon = params.has("fallback")
  ? defaultPackageIcon
  : systemTextJsonIcon;
const subjectPath = workspaceMode
  ? [{ kind: "workspace", label: "System.Text.Json", copyable: false }]
  : packageMode
    ? [{ kind: "package", label: "System.Text.Json", copyable: true }]
    : memberMode
      ? [
          { kind: "package", label: "System.Text.Json", copyable: true },
          {
            kind: "type",
            label: longMode
              ? "System.Text.Json.Serialization.Metadata.JsonTypeInfoResolverWithAddedModifiers"
              : "System.Text.Json.JsonSerializer",
            copyable: true,
          },
          {
            kind: "member",
            label: longMode
              ? "DeserializeAsyncEnumerableWithCustomConverterAndCancellation"
              : "DeserializeSync",
            copyable: true,
          },
        ]
      : [
          { kind: "package", label: "System.Text.Json", copyable: true },
          {
            kind: "type",
            label: longMode
              ? "System.Text.Json.Serialization.Metadata.JsonTypeInfoResolverWithAddedModifiers"
              : "System.Text.Json.JsonSerializer",
            copyable: true,
          },
        ];
const subjectPathLabel = subjectPath.map(segment => segment.label).join(" > ");
const coordinates = [
  {
    id: "System.Text.Json",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
  {
    id: "Microsoft.Extensions.DependencyInjection",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
  {
    id: "Microsoft.Extensions.Http",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
  {
    id: "Microsoft.Extensions.Options",
    version: "10.0.0",
    activeFramework: "net10.0",
    isRuntimePack: false,
  },
];
function workspaceNavigationHtml(): string {
  return renderWorkspaceSubject({
    packageCount: coordinates.length,
    selected: true,
    escapeHtml,
  });
}

function workspaceDetailHtml(): string {
  return renderWorkspaceView({
    occurrences: coordinates
      .filter(item => !item.isRuntimePack)
      .map((item, index) => ({
        action: `occurrence-${index}`,
        package: item.id,
        version: item.version,
        framework: item.activeFramework,
      })),
    packages: coordinates,
      demos: [],
      demoError: "",
      loading: false,
      error: "",
    escapeHtml,
  });
}

let activeScope: WorkspaceScope = workspaceMode
  ? "workspace"
  : packageMode
    ? "package"
    : memberMode
      ? "member"
      : "type";
let activePackageLens: PackageLens = "overview";
let activeTypeLens: TypeLens = sourceMode ? "source" : "api";
let activeMemberSection: MemberSection = sourceMode ? "source" : "overview";
const source = {
  provider: limitationMode ? "decompiled" : "pdb",
  provenance: limitationMode
    ? "dotnet-inspect from System.Text.Json 10.0.0 lib/net10.0/System.Text.Json.dll"
    : "SourceLink · github.com/dotnet/runtime",
  url: "https://github.com/dotnet/runtime",
  pdbSourceLimitation: limitationMode
    ? "The selected type's primary source document is not uniquely identified in the portable PDB."
    : null,
  text: `public static object? DeserializeSync(string json)
{
    return JsonSerializer.Deserialize(json, typeof(object));
}`,
};
const packageStrip: readonly (
  readonly [PackageLens, string, string, string]
)[] = [
  ["overview", "Overview", scopeBarShortLabel("Overview"), "◫"],
  ["dependencies", "Dependencies", scopeBarShortLabel("Dependencies"), "⇄"],
];
const typeStrip: readonly (
  readonly [TypeLens, string, string, string]
)[] = [
  ["api", "API", scopeBarShortLabel("API"), "⌘"],
  ["metadata", "Metadata", scopeBarShortLabel("Metadata"), "≡"],
  ["source", "Source", scopeBarShortLabel("Source"), "⌑"],
];
const memberStrip: readonly (
  readonly [MemberSection, string, string, string]
)[] = [
  ["overview", "Overview", scopeBarShortLabel("Overview"), "◫"],
  ["call-graph", "Call graph", scopeBarShortLabel("Call graph"), "⑂"],
  ["facts", "Facts", scopeBarShortLabel("Facts"), "·"],
  ["source", "Source", scopeBarShortLabel("Source"), "⌑"],
  ["annotated", "Annotated source", scopeBarShortLabel("Annotated source"), "✎"],
];

function scopeBarHtml() {
  const strip = activeScope === "workspace"
    ? []
    : activeScope === "package"
      ? packageStrip
      : activeScope === "member"
        ? (emptyMode ? [] : memberStrip)
        : typeStrip;
  return renderScopeBar({
    scope: activeScope,
    strip,
    activeStripId: activeScope === "workspace"
      || (activeScope === "member" && emptyMode)
      ? null
      : activeScope === "package"
        ? activePackageLens
        : activeScope === "member"
          ? activeMemberSection
          : activeTypeLens,
    stripAttribute: activeScope === "package"
      ? "data-package-lens"
      : activeScope === "member"
        ? "data-member-section"
        : "data-lens",
    panelId: "inspector-panel",
    showMemberScope: memberMode,
    emptyStripLabel: emptyMode ? "Filtered member list" : "",
    escapeHtml,
  });
}

const navigationHtml = workspaceMode
  ? workspaceNavigationHtml()
  : `<section class="type-browser">
      <header class="browser-head">Target inventory</header>
      <label class="type-search">
        <span>/</span>
        <input aria-label="Filter types" placeholder="Filter types" />
      </label>
      <div class="namespace-picker">
        <select id="namespace-jump" class="scope-select">
          <option>All namespaces · 1</option>
        </select>
      </div>
      <div class="chip-stack">
        <div class="namespace-chips kind-chips">
          <button class="active">all kinds</button>
        </div>
      </div>
      <div class="type-list">
        <button class="namespace-row">System.Text.Json</button>
      </div>
    </section>`;
const harnessKeybindings = new KeybindingRegistry();
harnessKeybindings.register({
  id: "workspace.open-all",
  key: "p",
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: 100,
  run: () => true,
});
harnessKeybindings.register({
  id: "workspace.drill-out-escape",
  key: "Escape",
  available: () => !packageMode && !workspaceMode,
  allowExtraModifiers: true,
  priority: 100,
  run: () => true,
});
harnessKeybindings.register({
  id: "workspace.focus-filter",
  key: "f",
  available: () => !workspaceMode,
  modifiers: { commandOrControl: true },
  allowExtraModifiers: true,
  priority: 100,
  run: () => true,
});
harnessKeybindings.register({
  id: "workspace.select-lens",
  key: ["1", "2", "3"],
  available: () => !workspaceMode,
  allowExtraModifiers: true,
  priority: 100,
  run: () => true,
});
harnessKeybindings.register({
  id: "workspace.navigate-horizontal",
  key: ["ArrowLeft", "ArrowRight"],
  available: () => !workspaceMode,
  priority: 100,
  run: () => true,
});
for (const [key, available] of [
  ["ArrowLeft", () => historyBackMode],
  ["ArrowRight", () => historyForwardMode],
] as const) {
  harnessKeybindings.register({
    id: `workspace.history-alt-${key}`,
    key,
    available,
    modifiers: { alt: true },
    allowExtraModifiers: true,
    priority: 100,
    run: () => true,
  });
  harnessKeybindings.register({
    id: `workspace.history-shift-${key}`,
    key,
    available,
    modifiers: { shift: true },
    priority: 100,
    run: () => true,
  });
}
harnessKeybindings.register({
  id: "workspace.drill-in",
  key: "Enter",
  allowExtraModifiers: true,
  priority: 100,
  run: () => true,
});
const graphHelpScope = graphMode ? new EventTarget() : null;
if (graphHelpScope) {
  harnessKeybindings.register({
    id: "graph.zoom",
    key: ["+", "=", "-", "_", "0"],
    allowExtraModifiers: true,
    priority: 200,
    run: () => true,
  }, graphHelpScope);
  harnessKeybindings.register({
    id: "graph.pan-horizontal",
    key: ["ArrowLeft", "ArrowRight"],
    allowExtraModifiers: true,
    priority: 200,
    run: () => true,
  }, graphHelpScope);
  harnessKeybindings.register({
    id: "graph.pan-vertical",
    key: ["ArrowUp", "ArrowDown"],
    allowExtraModifiers: true,
    priority: 200,
    run: () => true,
  }, graphHelpScope);
}
const harnessKeyboardHelpBindings = [
  ...harnessKeybindings.availableBindingsFor(),
  ...(graphHelpScope
    ? harnessKeybindings.availableBindingsFor(graphHelpScope)
    : []),
];
app.innerHTML = `
  <div class="workbench">
    ${workbenchShellHtml({
      contextualActionsHtml: annotatedMode || sourceMode
        ? `<div class="working-surface-actions" role="group" aria-label="${annotatedMode ? "Annotated Source actions" : "Source actions"}">
            ${annotatedMode ? renderAnnotatedSourcePageActions(true) : ""}
            ${sourceMode
              ? renderSourcePageActions({
                  source,
                  copyButtonId: memberMode
                    ? "copy-source"
                    : "copy-type-source",
                  escapeHtml,
                })
              : ""}
          </div>`
        : "",
      inspectedTargetHtml: `
        <div class="inspected-target" aria-label="Inspected target">
          <span class="subject-icon" aria-hidden="true">${workspaceMode
            ? "W"
            : `<img src="${packageIcon}" alt="" data-package-icon>`}</span>
          <div class="subject-path" aria-label="${subjectPathLabel}" title="${subjectPathLabel}">
            ${subjectPath.map((segment, index) => {
              const className = `subject-path-segment${index === 0 ? " root" : ""}${index === subjectPath.length - 1 ? " current" : ""}`;
              const content = segment.copyable
                ? `<button type="button" class="${className}" data-subject-copy="${index}" title="Copy ${escapeHtml(segment.label)}" aria-label="Copy ${segment.kind} name ${escapeHtml(segment.label)}">${escapeHtml(segment.label)}</button>`
                : `<span class="${className}">${escapeHtml(segment.label)}</span>`;
              return `${index === 0 ? "" : '<span class="subject-path-separator" aria-hidden="true">&gt;</span>'}${content}`;
            }).join("")}
          </div>
        </div>`,
      subjectInspectorHtml: scopeBarHtml(),
      titleNavigationHtml: renderTitleNavigation(
        historyBackMode,
        historyForwardMode),
    })}
    <div class="notice-stack"></div>
    <main id="subject-panel" class="workspace" role="tabpanel" aria-labelledby="active-subject-tab">
      ${navigationHtml}
      <section class="detail-pane">
        <article id="inspector-panel" class="detail-scroll${annotatedMode ? " annotated-working-surface" : ""}${sourceMode ? " source-working-surface" : ""}"${workspaceMode ? "" : ' role="tabpanel" aria-labelledby="active-inspector-tab"'}>
          ${sourceMode
            ? renderSourceResult({
                source,
                escapeHtml,
                highlightCSharp: escapeHtml,
              })
            : workspaceMode
              ? workspaceDetailHtml()
              : `<h1>${subjectPath.at(-1)?.label}</h1>
              ${packageMode ? `
                <section class="document-section package-coordinate-editor">
                  <div class="section-title"><h2>Package coordinate</h2><span>1 target framework</span></div>
                  <div class="package-coordinate-fields">
                    <label class="version-select"><span>Version</span><select id="package-version"><option>10.0.0</option></select></label>
                    <label class="framework-select"><span>Framework</span><select id="framework"><option>net10.0</option></select></label>
                  </div>
                </section>` : ""}`}
        </article>
      </section>
    </main>
  </div>
  ${renderApplicationMenu(true)}
  ${renderSettingsView({
    theme: "dark",
    settingsReturn: "workbench",
    styleCatalog: {
      styleTiers: [],
      styleOptions: [],
      styleCatalogError: "",
      taste: [],
    },
    escapeHtml,
  }).replace(
    'id="settings-backdrop" class="modal-backdrop"',
    'id="settings-backdrop" class="modal-backdrop" hidden',
  )}
  ${renderKeyboardHelpDialog(harnessKeyboardHelpBindings).replace(
    'id="keyboard-help-backdrop" class="modal-backdrop"',
    'id="keyboard-help-backdrop" class="modal-backdrop" hidden',
  )}`;

function setApplicationDialog(
  next: "settings" | "keyboard-help" | null,
): void {
  applicationDialog = next;
  const workbench = document.querySelector<HTMLElement>(".workbench");
  const settings = document.querySelector<HTMLElement>("#settings-backdrop");
  const help =
    document.querySelector<HTMLElement>("#keyboard-help-backdrop");
  if (workbench) workbench.inert = next !== null;
  if (settings) settings.hidden = next !== "settings";
  if (help) help.hidden = next !== "keyboard-help";
  if (next === "settings") {
    document.querySelector<HTMLElement>("#settings-title")?.focus();
  } else if (next === "keyboard-help") {
    document.querySelector<HTMLElement>("#keyboard-help-title")?.focus();
  } else {
    document.querySelector<HTMLElement>("#application-menu-button")?.focus();
  }
}

function handleApplicationAction(action: ApplicationAction): void {
  if (action === "share") {
    const focusOwner = captureApplicationMenuFocusOwner(document);
    setTimeout(() => {
      document.body.dataset.shared = "true";
      restoreApplicationMenuFocusIfOwned(document, focusOwner);
    }, 50);
    return;
  }
  setApplicationDialog(applicationDialog === action ? null : action);
}

const harnessDispatchKeybindings = new KeybindingRegistry();
harnessDispatchKeybindings.register({
  id: "workspace.drill-in",
  key: "Enter",
  available: () => applicationDialog === null,
  allowExtraModifiers: true,
  priority: 100,
  when: event => !event.metaKey
    && !event.ctrlKey
    && !event.altKey
    && !(event.target instanceof Element
      && event.target.matches(
        "button, a[href], input, select, textarea, summary, "
        + "[role=button], [role=link], [role=checkbox]")),
  run: () => {
    document.body.dataset.drillIn = "true";
    return true;
  },
});
harnessDispatchKeybindings.attach(document);

const workbenchShellActions: WorkbenchShellBindingActions = {
  onApplicationAction: handleApplicationAction,
  onCopySubjectSegment: index => {
    document.body.dataset.copiedSubject = subjectPath[index]?.label ?? "";
  },
  onDismissNotice() {},
  onDismissPackageNotice() {},
  onNavigateBack() {},
  onNavigateForward() {},
  onRetryNotice() {},
  onSearch() {},
};
workbenchShellBinding =
  bindWorkbenchShell(document, workbenchShellActions);
bindSettingsPanel(document, {
  onClose: () => setApplicationDialog(null),
  onOpen: () => setApplicationDialog("settings"),
  onTasteClear() {},
  onTasteToggle() {},
  onThemeSelect() {},
});
document.addEventListener("keydown", event => {
  if (event.key === "Escape" && applicationDialog !== null) {
    event.preventDefault();
    setApplicationDialog(null);
  }
});

function renderHarnessScopeBar() {
  const focusedElement = document.activeElement instanceof HTMLElement
    ? document.activeElement
    : null;
  const scopeBarOwnsFocus = focusedElement
    ?.closest("[data-scope-bar]") != null;
  const focusTarget = focusedElement
    ? captureScopeBarFocus(focusedElement)
    : null;
  const scopeBar = document.querySelector<HTMLElement>(".lensbar");
  if (!scopeBar) throw new Error("The scope bar is unavailable.");
  if (scopeBarOwnsFocus) {
    appRoot.tabIndex = -1;
    appRoot.focus({ preventScroll: true });
  }
  scopeBarBinding?.disconnect();
  scopeBar.outerHTML = scopeBarHtml();
  bindHarnessScopeBar();
  if (scopeBarOwnsFocus) {
    let restored = false;
    if (focusTarget) {
      scopeBarBinding?.revealFocusTarget(focusTarget);
      restored = restoreScopeBarFocus(document, focusTarget);
    }
    if (!restored) {
      document.querySelector<HTMLElement>(".brand")
        ?.focus({ preventScroll: true });
    }
    appRoot.removeAttribute("tabindex");
  }
}

function bindHarnessScopeBar() {
  scopeBarBinding = bindScopeBar(document, {
    onMemberSectionSelect: section => {
      activeMemberSection = section;
      renderHarnessScopeBar();
    },
    onPackageLensSelect: lens => {
      activePackageLens = lens;
      renderHarnessScopeBar();
    },
    onScopeSelect: scope => {
      activeScope = scope;
      renderHarnessScopeBar();
    },
    onTypeLensSelect: lens => {
      activeTypeLens = lens;
      renderHarnessScopeBar();
    },
  }, scopeBarState);
}

function renderHarnessWorkspace() {
  const navigation =
    document.querySelector<HTMLElement>(".workspace-nav");
  const detail =
    document.querySelector<HTMLElement>("#inspector-panel");
  const path =
    document.querySelector<HTMLElement>(".subject-path");
  const pathSegment =
    path?.querySelector<HTMLElement>(".subject-path-segment");
  if (!navigation || !detail || !path || !pathSegment)
    throw new Error("The workspace packet harness is incomplete.");
  navigation.outerHTML = workspaceNavigationHtml();
  detail.innerHTML = workspaceDetailHtml();
  const title = "Workspace";
  path.setAttribute("aria-label", title);
  path.title = title;
  pathSegment.textContent = title;
  bindHarnessWorkspace();
  requestAnimationFrame(() =>
    focusWorkspace(document));
}

function bindHarnessWorkspace() {
  if (!workspaceMode) return;
  bindWorkspaceSubject(document, {
    onSelect: renderHarnessWorkspace,
    onActivate: action => {
      const count = Number(document.body.dataset.workspaceExecutionCount ?? "0");
      document.body.dataset.workspaceExecutionCount = String(count + 1);
      document.body.dataset.workspaceExecution = action;
    },
    onDemo: () => {},
    onRetry: () => {},
  });
}

bindHarnessScopeBar();
bindHarnessWorkspace();
if (workspaceMode) document.body.dataset.workspaceExecutionCount = "0";

window.focusWorkbenchSearchProbe = () => focusWorkbenchSearch(document);
window.renderPackageScopeProbe = () => {
  activeScope = "package";
  activePackageLens = "overview";
  renderHarnessScopeBar();
};
window.rerenderApplicationMenuProbe = () => {
  const applicationMenuHadFocus = applicationMenuOwnsFocus(document);
  workbenchShellBinding?.disconnect();
  const slot =
    document.querySelector<HTMLElement>(".application-menu-slot");
  const overlay =
    document.querySelector<HTMLElement>(".application-menu-overlay");
  if (!slot || !overlay)
    throw new Error("The Application menu shell is unavailable.");
  slot.outerHTML = renderApplicationMenuButton();
  overlay.outerHTML = renderApplicationMenu(true);
  workbenchShellBinding =
    bindWorkbenchShell(document, workbenchShellActions);
  if (applicationMenuHadFocus) focusApplicationMenuButton(document);
};
window.rerenderScopeBarProbe = renderHarnessScopeBar;
