import {
  commandPaletteResults,
  commandPaletteRowHtml,
  type CommandContext,
  type CommandPaletteResult,
} from "./command-bar.ts";
import type { KeybindingRegistry } from "./keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";
import { packageRemoveButton } from "./package-removal.ts";
import {
  replaceChildrenPreservingRenderedInteractions,
} from "./rendered-interaction.ts";

type LensDefinition = readonly [id: string, label: string];
type SpotlightFocus = "input" | "chips";

type HighlightRange = readonly [start: number, end: number];

interface SpotlightPackage {
  id: string;
  version: string;
  activeFramework?: string;
  isRuntimePack?: boolean;
}

interface SpotlightType {
  id: string;
  name: string;
  namespace?: string;
  kind: string;
}

interface PackageLoadedResult {
  kind: "pkg-loaded";
  pkg: SpotlightPackage;
  ranges: readonly HighlightRange[];
}

export interface SpotlightPackageHit {
  id: string;
  version?: string;
  exact?: boolean;
}

interface PackageNugetResult {
  kind: "pkg-nuget";
  hit: SpotlightPackageHit;
  ranges: readonly HighlightRange[];
}

interface PackageRecentResult {
  kind: "pkg-recent";
  entry: { id: string; version?: string; framework?: string };
  ranges: readonly HighlightRange[];
}

interface PackageQueryResult {
  kind: "package-query";
  prefix: string;
}

interface PackageActivityResult {
  kind: "package-activity";
}

interface FrameworkLibraryResult {
  kind: "framework-lib";
  assembly: string;
  pack: string;
  publicTypes: number;
  loaded?: boolean;
  ranges: readonly HighlightRange[];
  tfm?: string;
  version?: string;
  role?: string;
}

interface TypeResult {
  kind: "type";
  pkg: SpotlightPackage;
  type: SpotlightType;
  ranges: readonly HighlightRange[];
}

interface MemberResult {
  kind: "member";
  pkg: SpotlightPackage;
  type: SpotlightType;
  memberKey: string;
  name: string;
  ranges: readonly HighlightRange[];
}

export type SpotlightPackageResult =
  | PackageLoadedResult
  | PackageNugetResult
  | PackageRecentResult;

export type SpotlightResult =
  | CommandPaletteResult
  | SpotlightPackageResult
  | PackageQueryResult
  | PackageActivityResult
  | FrameworkLibraryResult
  | TypeResult
  | MemberResult;

export type RemovableSpotlightResult = PackageLoadedResult | PackageRecentResult;

export interface SpotlightState {
  spotlightOpen: boolean;
  spotlightQuery: string;
  spotlightIndex: number;
  spotlightScope: SpotlightScope;
  spotlightFocus: SpotlightFocus;
  spotlightChipIndex: number;
}

interface SpotlightOptions {
  keybindings: KeybindingRegistry;
  state: SpotlightState;
  lenses: () => readonly LensDefinition[];
  escapeHtml: (value: unknown) => string;
  highlightRanges: (
    value: string,
    ranges: readonly HighlightRange[],
  ) => string;
  kindIcon: (kind: string) => string;
  searchResults: () => SpotlightResult[];
  pickResult: (result: SpotlightResult) => void;
  removeResult?: (result: RemovableSpotlightResult) => boolean;
  executeCommand: (
    command: string,
    result: CommandPaletteResult,
  ) => Promise<unknown> | undefined;
  reportCommandError: (error: unknown) => void;
  commandContext: () => CommandContext | null;
  schedulePackageFetch: () => void;
  resetPackageSearch: () => void;
  packageSearchLoading: () => boolean;
  packageSearchError?: () => string;
  packageCount: () => number;
  render: () => void;
  focusAfterDismiss?: () => void;
  captureFocusAfterDismiss?: () => () => void;
}

interface PackageAdditionOptions {
  pickResult: (result: SpotlightPackageResult) => void;
  focusAfterDismiss: () => void;
}

const BASE_SCOPES = [
  { id: "all", label: "All" },
  { id: "packages", label: "Packages" },
  { id: "types", label: "Types" },
  { id: "members", label: "Members" },
] as const;

const COMMAND_SCOPE = { id: "commands", label: "Commands" } as const;

export type SpotlightScope =
  | (typeof BASE_SCOPES)[number]["id"]
  | typeof COMMAND_SCOPE.id;
const PLATFORM_PACK_LABEL: Readonly<Record<string, string>> = {
  "netcore.app": ".NET",
  "aspnetcore.app": "ASP.NET Core",
};
const GROUP_LABELS: Readonly<Record<SpotlightResult["kind"], string>> = {
  command: "Commands",
  "pkg-recent": "Recent",
  "package-query": "Query",
  "package-activity": "Query",
  "pkg-loaded": "Packages",
  "pkg-nuget": "Packages",
  type: "Types",
  member: "Members",
  "framework-lib": "Libraries",
};

export function nextSpotlightSelection(
  current: number,
  delta: number,
  count: number,
): number | null {
  if (count <= 0) return null;
  const next = current + delta;
  return next < 0 ? null : Math.min(count - 1, next);
}

export function nextSpotlightScope(
  current: number,
  count: number,
  backward: boolean,
): number {
  if (count <= 0) return 0;
  return backward
    ? (current - 1 + count) % count
    : (current + 1) % count;
}

export function spotlightResultIdentity(result: SpotlightResult): string {
  switch (result.kind) {
    case "command":
      return JSON.stringify([
        result.kind,
        result.action,
        result.command,
        result.targetTypeId ?? "",
      ]);
    case "pkg-loaded":
      return JSON.stringify([
        result.kind,
        result.pkg.id,
        result.pkg.version,
        result.pkg.activeFramework ?? "",
      ]);
    case "pkg-nuget":
      return JSON.stringify([
        result.kind,
        result.hit.id,
        result.hit.version ?? "",
        result.hit.exact === true,
      ]);
    case "pkg-recent":
      return JSON.stringify([
        result.kind,
        result.entry.id,
        result.entry.version ?? "",
        result.entry.framework ?? "",
      ]);
    case "package-query":
      return JSON.stringify([result.kind, result.prefix]);
    case "package-activity":
      return JSON.stringify([result.kind]);
    case "framework-lib":
      return JSON.stringify([result.kind, result.tfm ?? "", result.version ?? "", result.pack, result.assembly]);
    case "type":
      return JSON.stringify([
        result.kind,
        result.pkg.id,
        result.pkg.version,
        result.pkg.activeFramework ?? "",
        result.type.id,
      ]);
    case "member":
      return JSON.stringify([
        result.kind,
        result.pkg.id,
        result.pkg.version,
        result.pkg.activeFramework ?? "",
        result.type.id,
        result.memberKey,
      ]);
    default:
      throw new Error("Unknown Spotlight result.");
  }
}

function isTextInputTarget(value: EventTarget | null): value is HTMLInputElement {
  return value !== null
    && "selectionStart" in value
    && "selectionEnd" in value
    && "value" in value;
}

function hasElementId(value: EventTarget | null): value is EventTarget & { id: string } {
  return value !== null && "id" in value && typeof value.id === "string";
}

function isPackageAdditionResult(result: SpotlightResult): result is SpotlightPackageResult {
  return result.kind === "pkg-nuget"
    || result.kind === "pkg-recent"
    || (result.kind === "pkg-loaded" && !result.pkg.isRuntimePack);
}

export function createSpotlight(options: SpotlightOptions) {
  let boundInput: HTMLInputElement | null = null;
  const { state, escapeHtml } = options;
  let interactionGeneration = 0;
  let renderedResults: readonly SpotlightResult[] = [];
  let renderedResultsByIdentity = new Map<string, SpotlightResult>();
  let selectedResultIdentity: string | null = null;
  const boundResultControls = new WeakSet<HTMLElement>();
  const boundRemoveControls = new WeakSet<HTMLElement>();
  const dismissedPackageIds = new Set<string>();
  let dismissalQuery = state.spotlightQuery;
  let packageAddition: PackageAdditionOptions | null = null;

  function scopes() {
    if (packageAddition) return BASE_SCOPES.filter(scope => scope.id === "packages");
    return options.commandContext()
      ? [...BASE_SCOPES, COMMAND_SCOPE]
      : [...BASE_SCOPES];
  }

  function results(): SpotlightResult[] {
    if (packageAddition) return options.searchResults().filter(isPackageAdditionResult);
    if (state.spotlightScope === "commands") {
      const context = options.commandContext();
      return context
        ? commandPaletteResults(context, options.lenses())
        : [];
    }
    if (dismissalQuery !== state.spotlightQuery) {
      dismissedPackageIds.clear();
      dismissalQuery = state.spotlightQuery;
    }
    return options.searchResults().filter(result =>
      result.kind !== "pkg-nuget"
      || !dismissedPackageIds.has(result.hit.id.toLowerCase()));
  }

  function removable(result: SpotlightResult): result is RemovableSpotlightResult {
    return !packageAddition && options.removeResult !== undefined
      && (result.kind === "pkg-recent"
        || (result.kind === "pkg-loaded" && !result.pkg.isRuntimePack));
  }

  function withRemoveButton(
    result: SpotlightResult,
    row: string,
  ): string {
    if (!removable(result)) return row;
    const identity = spotlightResultIdentity(result);
    const label = result.kind === "pkg-recent"
      ? `Forget ${result.entry.id} from recent packages`
      : `Remove ${result.pkg.id} ${result.pkg.version} ${result.pkg.activeFramework ?? ""} from Workspace`;
    return `<div class="package-search-row" role="presentation">${row}${packageRemoveButton(
      "data-sl-remove", identity, label, escapeHtml)}</div>`;
  }

  function rowHtml(result: SpotlightResult, index: number): string {
    const selected = index === state.spotlightIndex;
    const identity = spotlightResultIdentity(result);
    if (result.kind === "command") {
      return commandPaletteRowHtml(
        result,
        index,
        selected,
        identity,
        escapeHtml,
      );
    }

    const selectedClass = selected ? "selected" : "";
    const escapedIdentity = escapeHtml(identity);
    const base = `id="spotlight-result-${index}" class="spotlight-item ${selectedClass}" role="option" aria-selected="${selected}" data-sl-index="${index}" data-sl-result-identity="${escapedIdentity}" data-rendered-interaction-key="spotlight-result:${escapedIdentity}"${packageAddition ? ' tabindex="-1"' : ""}`;
    if (result.kind === "pkg-loaded") {
      return withRemoveButton(result, `<button ${base} data-sl-pkg-open="${escapeHtml(result.pkg.id)}">
        <span class="kind-icon sl-pkg">▣</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.pkg.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(result.pkg.version)} · ${packageAddition ? "already in Workspace" : "open"}</span>
      </button>`);
    }
    if (result.kind === "pkg-nuget") {
      const source = result.hit.exact
        ? "exact coordinate · listed or unlisted"
        : "nuget.org";
      return `<button ${base} data-sl-pkg-load="${escapeHtml(result.hit.id)}" data-sl-pkg-version="${escapeHtml(result.hit.version || "")}">
        <span class="kind-icon sl-pkg-new">↓</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.hit.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(result.hit.version || "")} · ${source}</span>
      </button>`;
    }
    if (result.kind === "pkg-recent") {
      const version = result.entry.version && result.entry.version !== "latest"
        ? result.entry.version
        : "";
      return withRemoveButton(result, `<button ${base} data-sl-pkg-recent="${escapeHtml(result.entry.id)}">
        <span class="kind-icon sl-pkg">▣</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.entry.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${version ? `${escapeHtml(version)} · ` : ""}recent</span>
      </button>`);
    }
    if (result.kind === "package-query") {
      const suffix = result.prefix
        ? `Start with “${escapeHtml(result.prefix)}”`
        : "Choose a package ID prefix and inspection facts";
      return `<button ${base} data-sl-package-query="1">
        <span class="kind-icon sl-command">⌕</span>
        <span class="spotlight-item-name">Package query</span>
        <span class="spotlight-item-ns">${suffix}</span>
      </button>`;
    }
    if (result.kind === "package-activity") {
      return `<button ${base} data-sl-package-activity="1">
        <span class="kind-icon sl-command">↻</span>
        <span class="spotlight-item-name">Package Activity</span>
        <span class="spotlight-item-ns">Review product package changes over time</span>
      </button>`;
    }
    if (result.kind === "framework-lib") {
      const label = PLATFORM_PACK_LABEL[result.pack] || result.pack;
      const types = `${result.publicTypes} type${result.publicTypes === 1 ? "" : "s"}`;
      const meta = `${label} library${result.tfm ? ` · ${result.tfm}` : ""}${result.version ? ` · ${result.version}` : ""} · ${result.role ?? types}${result.loaded ? " · loaded" : ""}`;
      return `<button ${base} data-sl-framework-lib="${escapeHtml(result.assembly)}" data-sl-framework-pack="${escapeHtml(result.pack)}">
        <span class="kind-icon sl-lib">▤</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.assembly, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(meta)}</span>
      </button>`;
    }
    if (result.kind === "member") {
      const packageName = options.packageCount() > 1
        ? ` · ${escapeHtml(result.pkg.id)}`
        : "";
      return `<button ${base} data-sl-member="${escapeHtml(result.memberKey)}" data-sl-pkg="${escapeHtml(result.pkg.id)}" data-sl-type="${escapeHtml(result.type.id)}">
        <span class="kind-icon sl-member">ƒ</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.name, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(result.type.name)}${packageName}</span>
      </button>`;
    }

    const packageName = options.packageCount() > 1
      ? ` · ${escapeHtml(result.pkg.id)}`
      : "";
    return `<button ${base} data-sl-type="${escapeHtml(result.type.id)}" data-sl-pkg="${escapeHtml(result.pkg.id)}">
      <span class="kind-icon">${options.kindIcon(result.type.kind)}</span>
      <span class="spotlight-item-name">${options.highlightRanges(result.type.name, result.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(result.type.namespace || "")}${packageName}</span>
    </button>`;
  }

  function resultsHtml(items: readonly SpotlightResult[]): string {
    const searchError = state.spotlightScope === "all" || state.spotlightScope === "packages"
      ? options.packageSearchError?.() : "";
    const errorHtml = searchError
      ? `<div class="spotlight-hint" role="status">${escapeHtml(searchError)}</div>`
      : "";
    if (!items.length) {
      if (errorHtml) return errorHtml;
      const query = state.spotlightQuery.trim();
      if (state.spotlightScope === "commands") {
        return `<div class="spotlight-empty">${query
          ? `No command matches “${escapeHtml(query)}”.`
          : "Choose a command to run in the current workspace."}</div>`;
      }
      if (!query) {
        if (packageAddition) {
          return '<div class="spotlight-empty">Search NuGet or enter PackageId@Version to add an exact coordinate.</div>';
        }
        return '<div class="spotlight-empty">Search packages, types, and members, or enter PackageId@Version.</div>';
      }
      if (options.packageSearchLoading()) {
        return '<div class="spotlight-empty">Searching…</div>';
      }
      return `<div class="spotlight-empty">Nothing matches “${escapeHtml(query)}”.</div>`;
    }

    const grouped = state.spotlightScope === "all";
    let html = "";
    let lastGroup = "";
    items.forEach((result, index) => {
      if (grouped) {
        const group = GROUP_LABELS[result.kind];
        if (group && group !== lastGroup) {
          html += `<div class="spotlight-group">${group}</div>`;
          lastGroup = group;
        }
      }
      html += rowHtml(result, index);
    });
    html += errorHtml;
    if (!searchError && options.packageSearchLoading()
      && (state.spotlightScope === "all" || state.spotlightScope === "packages")) {
      html += '<div class="spotlight-hint">Searching nuget.org…</div>';
    }
    return html;
  }

  function chipsHtml(): string {
    if (packageAddition) return "";
    return scopes().map((scope, index) => {
      const active = state.spotlightScope === scope.id ? "active" : "";
      const focused = state.spotlightFocus === "chips"
        && state.spotlightChipIndex === index
        ? "focused"
        : "";
      return `<button class="spotlight-chip ${active} ${focused}" data-sl-scope="${scope.id}" data-sl-chip="${index}">${scope.label}</button>`;
    }).join("");
  }

  function clampSelection(items: readonly SpotlightResult[]): void {
    state.spotlightIndex = Math.min(
      state.spotlightIndex,
      Math.max(items.length - 1, 0),
    );
  }

  function rememberSelection(items: readonly SpotlightResult[]): void {
    const selected = items[state.spotlightIndex];
    selectedResultIdentity = selected
      ? spotlightResultIdentity(selected)
      : null;
  }

  function restoreSelection(items: readonly SpotlightResult[]): void {
    if (selectedResultIdentity) {
      const index = items.findIndex(
        item => spotlightResultIdentity(item) === selectedResultIdentity,
      );
      if (index >= 0) state.spotlightIndex = index;
    }
    clampSelection(items);
    rememberSelection(items);
  }

  function resultsForRender(): readonly SpotlightResult[] {
    const items = results();
    restoreSelection(items);
    renderedResults = items;
    renderedResultsByIdentity = new Map(
      items.map(item => [spotlightResultIdentity(item), item]),
    );
    return items;
  }

  function activeDescendantAttribute(items: readonly SpotlightResult[]): string {
    return items.length
      ? ` aria-activedescendant="spotlight-result-${state.spotlightIndex}"`
      : "";
  }

  function syncActiveDescendant(count: number): void {
    const input = document.querySelector<HTMLInputElement>("#spotlight-input");
    if (!input) return;
    if (count > 0) {
      input.setAttribute(
        "aria-activedescendant",
        `spotlight-result-${state.spotlightIndex}`,
      );
    } else {
      input.removeAttribute("aria-activedescendant");
    }
  }

  function modalHtml(): string {
    const items = resultsForRender();
    const commands = state.spotlightScope === "commands";
    const name = packageAddition ? "Add package" : commands ? "Run a command" : "Go to anything";
    const placeholder = packageAddition
      ? "Search NuGet or enter PackageId@Version…"
      : commands
        ? "Run a command…"
        : "Go to anything… package, type, member, or PackageId@Version";
    return `
      <div class="spotlight-backdrop" id="spotlight-backdrop">
        <div class="spotlight" role="dialog" aria-modal="true" aria-label="${name}">
          ${packageAddition ? '<div class="spotlight-foot"><strong>Add package</strong></div>' : ""}
          <div class="spotlight-search">
            <span class="spotlight-glyph">${commands ? "›" : "⌕"}</span>
            <input id="spotlight-input"${packageAddition ? ' aria-label="Add package"' : ""} value="${escapeHtml(state.spotlightQuery)}" placeholder="${placeholder}" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results"${activeDescendantAttribute(items)} />
            <kbd>esc</kbd>
          </div>
          ${packageAddition ? "" : `<div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>`}
          <div class="spotlight-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
          <div class="spotlight-foot">${packageAddition
            ? '<span>↑↓ select</span><span>Add <kbd>Enter</kbd></span><span>esc cancel</span><button type="button" id="spotlight-cancel">Cancel</button>'
            : `<span><kbd>Ctrl P</kbd> search</span><span>↑↓ select</span><span>→ target</span><span>↵ ${commands ? "complete / run" : "open"}</span>${options.removeResult && !commands ? "<span>Shift+Delete remove</span>" : ""}<span>esc close</span>`}</div>
        </div>
      </div>`;
  }

  function inlineHtml(disabled: boolean, showReadyGlint = false): string {
    const items = resultsForRender();
    return `
      <div class="home-search-content" ${disabled ? "inert" : ""}>
        <div class="home-search-box">
          ${showReadyGlint ? `<svg class="home-search-glint" aria-hidden="true">
            <rect class="home-search-glint-glow" pathLength="1"></rect>
            <rect class="home-search-glint-line" pathLength="1"></rect>
          </svg>` : ""}
          <span class="spotlight-glyph">⌕</span>
          <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Search NuGet — package, type, member, or PackageId@Version…" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results"${activeDescendantAttribute(items)} ${disabled ? "disabled" : ""} />
        </div>
        <div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>
        <div class="spotlight-results home-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
      </div>`;
  }

  function bindChipClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-sl-scope]").forEach(button => {
      button.addEventListener("click", () => {
        const scope = availableScope(button.dataset.slScope);
        if (scope !== null) setScope(scope);
      });
    });
  }

  function bindResultClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-sl-result-identity]").forEach(item => {
      if (boundResultControls.has(item)) return;
      boundResultControls.add(item);
      const identity = item.dataset.slResultIdentity;
      if (!identity) return;
      item.addEventListener("click", () => {
        const result = renderedResultsByIdentity.get(identity);
        if (result) pick(result);
      });
    });
    root.querySelectorAll<HTMLElement>("[data-sl-remove]").forEach(button => {
      if (boundRemoveControls.has(button)) return;
      boundRemoveControls.add(button);
      const identity = button.dataset.slRemove;
      if (!identity) return;
      button.addEventListener("click", () => {
        const result = renderedResultsByIdentity.get(identity);
        if (result) removeResult(result);
      });
    });
  }

  function removeResult(result: SpotlightResult | undefined): boolean {
    if (!result || !removable(result)) return false;
    const input = document.querySelector<HTMLInputElement>("#spotlight-input");
    const start = input?.selectionStart ?? state.spotlightQuery.length;
    const end = input?.selectionEnd ?? start;
    if (!options.removeResult?.(result)) return true;
    dismissedPackageIds.add((result.kind === "pkg-loaded"
      ? result.pkg.id : result.entry.id).toLowerCase());
    updateResults();
    const replacement = document.querySelector<HTMLInputElement>("#spotlight-input");
    replacement?.focus({ preventScroll: true });
    replacement?.setSelectionRange(start, end);
    return true;
  }

  function removeResultAt(index: number): boolean {
    return removeResult(renderedResults[index]);
  }

  function focus(selection?: {
    start: number | null;
    end: number | null;
    direction: "forward" | "backward" | "none" | null;
  }): void {
    requestAnimationFrame(() => {
      const input = document.querySelector<HTMLInputElement>("#spotlight-input");
      if (!input || document.activeElement === input) return;
      input.focus();
      input.setSelectionRange(
        selection?.start ?? input.value.length,
        selection?.end ?? input.value.length,
        selection?.direction ?? "none");
    });
  }

  function updateChips(): void {
    const container = document.querySelector<HTMLElement>("#spotlight-chips");
    if (!container) return;
    container.innerHTML = chipsHtml();
    bindChipClicks(container);
  }

  function updateResults(): void {
    const container = document.querySelector<HTMLElement>("#spotlight-results");
    if (!container) return;
    const items = resultsForRender();
    replaceChildrenPreservingRenderedInteractions(
      container,
      resultsHtml(items),
    );
    bindResultClicks(container);
    syncActiveDescendant(items.length);
    container.querySelector(".spotlight-item.selected")
      ?.scrollIntoView({ block: "nearest" });
  }

  function refresh(): void {
    updateChips();
    updateResults();
  }

  function availableScope(scope: string | undefined): SpotlightScope | null {
    return scopes().find(item => item.id === scope)?.id ?? null;
  }

  function setScope(scope: SpotlightScope): void {
    const available = scopes();
    if (!available.some(item => item.id === scope)) return;
    state.spotlightScope = scope;
    state.spotlightIndex = 0;
    selectedResultIdentity = null;
    options.schedulePackageFetch();
    refresh();
    focus();
  }

  function reset(): void {
    packageAddition = null;
    boundInput = null;
    dismissedPackageIds.clear();
    options.resetPackageSearch();
    state.spotlightOpen = false;
    state.spotlightQuery = "";
    state.spotlightScope = "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
    renderedResults = [];
    renderedResultsByIdentity = new Map();
    selectedResultIdentity = null;
  }

  function close(): void {
    const focusAfterDismiss = packageAddition?.focusAfterDismiss ?? options.focusAfterDismiss;
    reset();
    options.render();
    focusAfterDismiss?.();
  }

  function open(seed = "", scope: SpotlightScope = "all"): void {
    openWithPurpose(seed, scope, null);
  }

  function openForPackageAddition(addition: PackageAdditionOptions): void {
    openWithPurpose("", "packages", addition);
  }

  function openWithPurpose(
    seed: string,
    scope: SpotlightScope,
    addition: PackageAdditionOptions | null,
  ): void {
    packageAddition = addition;
    boundInput = null;
    dismissedPackageIds.clear();
    interactionGeneration++;
    options.resetPackageSearch();
    state.spotlightOpen = true;
    state.spotlightQuery = seed;
    state.spotlightScope = availableScope(scope) ?? "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
    renderedResults = [];
    selectedResultIdentity = null;
    options.schedulePackageFetch();
    options.render();
    focus();
  }

  function pick(result: SpotlightResult | undefined): void {
    if (packageAddition) {
      if (result && isPackageAdditionResult(result)) packageAddition.pickResult(result);
      return;
    }
    if (!result) {
      close();
      return;
    }
    if (result.kind !== "command") {
      options.pickResult(result);
      return;
    }
    if (result.action === "complete") {
      state.spotlightQuery = `${result.command} `;
      state.spotlightIndex = 0;
      selectedResultIdentity = null;
      const input = document.querySelector<HTMLInputElement>("#spotlight-input");
      if (input) input.value = state.spotlightQuery;
      updateResults();
      focus();
      return;
    }

    const generation = interactionGeneration;
    reset();
    const execution = options.executeCommand(result.command, result);
    const focusAfterDismiss = options.captureFocusAfterDismiss?.()
      ?? options.focusAfterDismiss;
    options.render();
    const focusAfterExecution = () => {
      if (generation === interactionGeneration) focusAfterDismiss?.();
    };
    Promise.resolve(execution).then(
      focusAfterExecution,
      (error: unknown) => {
        options.reportCommandError(error);
        focusAfterExecution();
      });
  }

  function highlightSelection(): number {
    const container = document.querySelector<HTMLElement>("#spotlight-results");
    if (!container) return 0;
    const items = container.querySelectorAll<HTMLElement>(".spotlight-item");
    items.forEach((element, index) => {
      const selected = index === state.spotlightIndex;
      element.classList.toggle("selected", selected);
      element.setAttribute("aria-selected", selected ? "true" : "false");
    });
    syncActiveDescendant(items.length);
    items[state.spotlightIndex]?.scrollIntoView({ block: "nearest" });
    return items.length;
  }

  function moveSelection(delta: number): boolean {
    const container = document.querySelector<HTMLElement>("#spotlight-results");
    const count = container
      ? container.querySelectorAll(".spotlight-item").length
      : 0;
    const next = nextSpotlightSelection(state.spotlightIndex, delta, count);
    if (next === null) return false;
    state.spotlightIndex = next;
    rememberSelection(renderedResults);
    highlightSelection();
    return true;
  }

  function scopeIndex(): number {
    return Math.max(
      0,
      scopes().findIndex(scope => scope.id === state.spotlightScope),
    );
  }

  function moveChip(index: number): void {
    state.spotlightChipIndex = index;
    const scope = scopes()[index];
    if (scope) setScope(scope.id);
  }

  function focusInput(): void {
    state.spotlightFocus = "input";
    updateChips();
    focus();
  }

  function handleModalKeys(event: KeyboardEvent): boolean {
    if (event.key === "Delete" && event.shiftKey) {
      return removeResultAt(state.spotlightIndex);
    }
    if (event.key === "Escape") {
      close();
      return true;
    }
    if (packageAddition) {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        moveSelection(event.key === "ArrowDown" ? 1 : -1);
        return true;
      }
      if (event.key === "Enter") {
        pick(renderedResults[state.spotlightIndex]);
        return true;
      }
      return false;
    }
    if (event.key === "Tab") {
      const available = scopes();
      const current = available.findIndex(scope => scope.id === state.spotlightScope);
      const next = nextSpotlightScope(
        current,
        available.length,
        event.shiftKey,
      );
      const nextScope = available[next];
      if (nextScope) {
        state.spotlightChipIndex = next;
        setScope(nextScope.id);
      }
      return true;
    }

    if (state.spotlightFocus === "chips") {
      const available = scopes();
      if (event.key === "ArrowRight") {
        if (state.spotlightChipIndex < available.length - 1) {
          moveChip(state.spotlightChipIndex + 1);
        }
      } else if (event.key === "ArrowLeft") {
        if (state.spotlightChipIndex === 0) focusInput();
        else moveChip(state.spotlightChipIndex - 1);
      } else if (event.key === "ArrowUp") {
        focusInput();
      } else if (event.key === "ArrowDown" || event.key === "Enter") {
        state.spotlightIndex = 0;
        rememberSelection(renderedResults);
        focusInput();
        highlightSelection();
      } else {
        return false;
      }
      return true;
    }

    if (event.key === "ArrowRight") {
      const input = event.target;
      if (!isTextInputTarget(input)) return false;
      const atEnd = input.selectionStart === input.selectionEnd
        && input.selectionStart === input.value.length;
      if (atEnd) {
        state.spotlightFocus = "chips";
        state.spotlightChipIndex = scopeIndex();
        updateChips();
        return true;
      }
    } else if (event.key === "ArrowDown") {
      moveSelection(1);
      return true;
    } else if (event.key === "ArrowUp") {
      if (!moveSelection(-1)) {
        state.spotlightFocus = "chips";
        state.spotlightChipIndex = scopeIndex();
        updateChips();
      }
      return true;
    } else if (event.key === "Enter") {
      pick(renderedResults[state.spotlightIndex]);
      return true;
    }
    return false;
  }

  function handleInlineKeys(event: KeyboardEvent): boolean {
    if (event.key === "Delete" && event.shiftKey) {
      return removeResultAt(state.spotlightIndex);
    }
    const items = renderedResults;
    if (event.key === "ArrowDown") {
      state.spotlightIndex = nextSpotlightSelection(
        state.spotlightIndex,
        1,
        items.length,
      ) ?? 0;
      rememberSelection(items);
      highlightSelection();
      return true;
    } else if (event.key === "ArrowUp") {
      state.spotlightIndex = nextSpotlightSelection(
        state.spotlightIndex,
        -1,
        items.length,
      ) ?? 0;
      rememberSelection(items);
      highlightSelection();
      return true;
    } else if (event.key === "Enter") {
      pick(items[state.spotlightIndex]);
      return true;
    }
    return false;
  }

  function bind(root: ParentNode, mode: "modal" | "inline"): void {
    const input = root.querySelector<HTMLInputElement>("#spotlight-input");
    const previous = boundInput;
    const selection = previous && input && previous.value === input.value
      ? {
          start: previous.selectionStart,
          end: previous.selectionEnd,
          direction: previous.selectionDirection,
        }
      : undefined;
    boundInput = input;
    if (input) {
      input.addEventListener("input", () => {
        state.spotlightQuery = input.value;
        state.spotlightIndex = 0;
        selectedResultIdentity = null;
        if (state.spotlightFocus === "chips") {
          state.spotlightFocus = "input";
          updateChips();
        }
        options.schedulePackageFetch();
        updateResults();
      });
      options.keybindings.register({
        id: mode === "modal"
          ? "spotlight-modal.navigate"
          : "spotlight-inline.navigate",
        key: ["Escape", "Tab", "ArrowRight", "ArrowLeft", "ArrowUp", "ArrowDown", "Enter", "Delete"],
        allowExtraModifiers: true,
        priority: WORKBENCH_KEYBINDING_PRIORITY.element,
        run: mode === "modal" ? handleModalKeys : handleInlineKeys,
      }, input);
    }
    bindChipClicks(root);
    bindResultClicks(root);
    if (mode === "modal") {
      root.querySelector("#spotlight-cancel")?.addEventListener("click", close);
      const backdrop = root.querySelector("#spotlight-backdrop");
      if (backdrop) options.keybindings.register({
        id: "spotlight-package-addition.dismiss-or-tab",
        key: ["Tab", "Escape"],
        allowExtraModifiers: true,
        priority: WORKBENCH_KEYBINDING_PRIORITY.element,
        available: () => packageAddition !== null,
        run: event => {
          if (event.key === "Escape") close();
          else {
            const cancel = root.querySelector<HTMLButtonElement>("#spotlight-cancel");
            const target = document.activeElement === cancel ? input : cancel;
            target?.focus();
          }
          return true;
        },
      }, backdrop);
      backdrop?.addEventListener(
        "mousedown",
        event => {
          const target = event.target;
          if (hasElementId(target) && target.id === "spotlight-backdrop") close();
        },
      );
      focus(selection);
    }
  }

  return {
    bind,
    close,
    inlineHtml,
    modalHtml,
    open,
    openForPackageAddition,
    refresh,
    reset,
    results,
    updateResults,
  };
}
