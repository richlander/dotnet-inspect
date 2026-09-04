import {
  commandPaletteResults,
  commandPaletteRowHtml,
  type CommandContext,
  type CommandPaletteResult,
} from "./command-bar.ts";
import type { KeybindingRegistry } from "./keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";

type LensDefinition = readonly [id: string, label: string];
type SpotlightFocus = "input" | "chips";

type HighlightRange = readonly [start: number, end: number];

interface SpotlightPackage {
  id: string;
  version: string;
  activeFramework?: string;
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

interface RuntimeSuggestionResult {
  kind: "rtpack-suggest";
}

interface RuntimeStatusResult {
  kind: "rtpack-status";
  loading?: boolean;
  error?: string;
}

interface PlatformLibraryResult {
  kind: "platform-lib";
  assembly: string;
  pack: string;
  publicTypes: number;
  loaded?: boolean;
  ranges: readonly HighlightRange[];
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

export type SpotlightResult =
  | CommandPaletteResult
  | PackageLoadedResult
  | PackageNugetResult
  | PackageRecentResult
  | PackageQueryResult
  | RuntimeSuggestionResult
  | RuntimeStatusResult
  | PlatformLibraryResult
  | TypeResult
  | MemberResult;

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
  executeCommand: (
    command: string,
    result: CommandPaletteResult,
  ) => Promise<unknown> | undefined;
  reportCommandError: (error: unknown) => void;
  commandContext: () => CommandContext | null;
  schedulePackageFetch: () => void;
  resetPackageSearch: () => void;
  packageSearchLoading: () => boolean;
  packageCount: () => number;
  activeFramework: () => string;
  render: () => void;
  focusAfterDismiss?: () => void;
}

const BASE_SCOPES = [
  { id: "all", label: "All" },
  { id: "packages", label: "Packages" },
  { id: "types", label: "Types" },
  { id: "members", label: "Members" },
  { id: "runtime", label: "Platform" },
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
  "pkg-loaded": "Packages",
  "pkg-nuget": "Packages",
  type: "Types",
  member: "Members",
  "platform-lib": "Libraries",
  "rtpack-suggest": "Runtime",
  "rtpack-status": "Runtime",
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

export function visibleSpotlightPackageHits(
  query: string,
  resolvedQuery: string,
  hits: readonly SpotlightPackageHit[],
): readonly SpotlightPackageHit[] {
  return query === resolvedQuery ? hits : [];
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
      return JSON.stringify([result.kind, result.hit.id, result.hit.version ?? ""]);
    case "pkg-recent":
      return JSON.stringify([
        result.kind,
        result.entry.id,
        result.entry.version ?? "",
        result.entry.framework ?? "",
      ]);
    case "package-query":
      return JSON.stringify([result.kind, result.prefix]);
    case "platform-lib":
      return JSON.stringify([result.kind, result.pack, result.assembly]);
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
      return result.kind;
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

export function createSpotlight(options: SpotlightOptions) {
  const { state, escapeHtml } = options;
  let interactionGeneration = 0;
  let renderedResults: readonly SpotlightResult[] = [];
  let selectedResultIdentity: string | null = null;

  function scopes() {
    return options.commandContext()
      ? [...BASE_SCOPES, COMMAND_SCOPE]
      : [...BASE_SCOPES];
  }

  function results(): SpotlightResult[] {
    if (state.spotlightScope === "commands") {
      const context = options.commandContext();
      return context
        ? commandPaletteResults(context, options.lenses())
        : [];
    }
    return options.searchResults();
  }

  function rowHtml(result: SpotlightResult, index: number): string {
    const selected = index === state.spotlightIndex;
    if (result.kind === "command") {
      return commandPaletteRowHtml(result, index, selected, escapeHtml);
    }

    const selectedClass = selected ? "selected" : "";
    const base = `id="spotlight-result-${index}" class="spotlight-item ${selectedClass}" role="option" aria-selected="${selected}" data-sl-index="${index}"`;
    if (result.kind === "pkg-loaded") {
      return `<button ${base} data-sl-pkg-open="${escapeHtml(result.pkg.id)}">
        <span class="kind-icon sl-pkg">▣</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.pkg.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(result.pkg.version)} · open</span>
      </button>`;
    }
    if (result.kind === "pkg-nuget") {
      return `<button ${base} data-sl-pkg-load="${escapeHtml(result.hit.id)}" data-sl-pkg-version="${escapeHtml(result.hit.version || "")}">
        <span class="kind-icon sl-pkg-new">↓</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.hit.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(result.hit.version || "")} · nuget.org</span>
      </button>`;
    }
    if (result.kind === "pkg-recent") {
      const version = result.entry.version && result.entry.version !== "latest"
        ? result.entry.version
        : "";
      return `<button ${base} data-sl-pkg-recent="${escapeHtml(result.entry.id)}">
        <span class="kind-icon sl-pkg">▣</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.entry.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${version ? `${escapeHtml(version)} · ` : ""}recent</span>
      </button>`;
    }
    if (result.kind === "package-query") {
      const suffix = result.prefix
        ? `Start with “${escapeHtml(result.prefix)}”`
        : "Choose a package ID prefix and nuspec facets";
      return `<button ${base} data-sl-package-query="1">
        <span class="kind-icon sl-command">⌕</span>
        <span class="spotlight-item-name">Package query</span>
        <span class="spotlight-item-ns">${suffix}</span>
      </button>`;
    }
    if (result.kind === "rtpack-suggest") {
      const framework = options.activeFramework() || "runtime";
      return `<button ${base} data-sl-load-runtime="1">
        <span class="kind-icon sl-pkg-new">↓</span>
        <span class="spotlight-item-name">Load .NET runtime pack</span>
        <span class="spotlight-item-ns">Search platform types (TextWriter, String…) · ${escapeHtml(framework)}</span>
      </button>`;
    }
    if (result.kind === "rtpack-status") {
      const text = result.loading
        ? "Loading .NET runtime pack — this can take a while…"
        : `Runtime pack failed: ${result.error || "unknown error"}`;
      return `<div id="spotlight-result-${index}" class="spotlight-item spotlight-status ${selectedClass}" role="option" aria-selected="${selected}" aria-disabled="true" data-sl-index="${index}">
        <span class="kind-icon">${result.loading ? "◔" : "⚠"}</span>
        <span class="spotlight-item-name">${escapeHtml(text)}</span>
      </div>`;
    }
    if (result.kind === "platform-lib") {
      const label = PLATFORM_PACK_LABEL[result.pack] || result.pack;
      const types = `${result.publicTypes} type${result.publicTypes === 1 ? "" : "s"}`;
      const meta = `${label} · ${types}${result.loaded ? " · loaded" : ""}`;
      return `<button ${base} data-sl-platform-lib="${escapeHtml(result.assembly)}" data-sl-platform-pack="${escapeHtml(result.pack)}">
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
    if (!items.length) {
      const query = state.spotlightQuery.trim();
      if (state.spotlightScope === "commands") {
        return `<div class="spotlight-empty">${query
          ? `No command matches “${escapeHtml(query)}”.`
          : "Choose a command to run in the current workspace."}</div>`;
      }
      if (!query) {
        return '<div class="spotlight-empty">Search packages, types, and members — pick a target below.</div>';
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
    if (options.packageSearchLoading()
      && (state.spotlightScope === "all" || state.spotlightScope === "packages")) {
      html += '<div class="spotlight-hint">Searching nuget.org…</div>';
    }
    return html;
  }

  function chipsHtml(): string {
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
    return `
      <div class="spotlight-backdrop" id="spotlight-backdrop">
        <div class="spotlight" role="dialog" aria-modal="true" aria-label="${commands ? "Run a command" : "Go to anything"}">
          <div class="spotlight-search">
            <span class="spotlight-glyph">${commands ? "›" : "⌕"}</span>
            <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="${commands ? "Run a command…" : "Go to anything…  package, type, or member"}" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results"${activeDescendantAttribute(items)} />
            <kbd>esc</kbd>
          </div>
          <div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>
          <div class="spotlight-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
          <div class="spotlight-foot"><span><kbd>Ctrl P</kbd> search</span><span>↑↓ select</span><span>→ target</span><span>↵ ${commands ? "complete / run" : "open"}</span><span>esc close</span></div>
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
          <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Search NuGet — a package, type, or member…" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results"${activeDescendantAttribute(items)} ${disabled ? "disabled" : ""} />
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
    root.querySelectorAll<HTMLElement>("[data-sl-index]").forEach(item => {
      item.addEventListener("click", () => {
        const index = Number(item.dataset.slIndex);
        const result = renderedResults[index];
        if (result) pick(result);
      });
    });
  }

  function focus(): void {
    requestAnimationFrame(() => {
      const input = document.querySelector<HTMLInputElement>("#spotlight-input");
      if (!input) return;
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
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
    container.innerHTML = resultsHtml(items);
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
    options.resetPackageSearch();
    state.spotlightOpen = false;
    state.spotlightQuery = "";
    state.spotlightScope = "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
    renderedResults = [];
    selectedResultIdentity = null;
  }

  function close(): void {
    reset();
    options.render();
    options.focusAfterDismiss?.();
  }

  function open(seed = "", scope: SpotlightScope = "all"): void {
    interactionGeneration++;
    options.resetPackageSearch();
    state.spotlightOpen = true;
    state.spotlightQuery = seed;
    state.spotlightScope = availableScope(scope) ?? "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
    selectedResultIdentity = null;
    options.schedulePackageFetch();
    options.render();
    focus();
  }

  function pick(result: SpotlightResult | undefined): void {
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
    options.render();
    const focusAfterExecution = () => {
      if (generation === interactionGeneration) options.focusAfterDismiss?.();
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
    if (event.key === "Escape") {
      close();
      return true;
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
        key: ["Escape", "Tab", "ArrowRight", "ArrowLeft", "ArrowUp", "ArrowDown", "Enter"],
        allowExtraModifiers: true,
        priority: WORKBENCH_KEYBINDING_PRIORITY.element,
        run: mode === "modal" ? handleModalKeys : handleInlineKeys,
      }, input);
    }
    bindChipClicks(root);
    bindResultClicks(root);
    if (mode === "modal") {
      root.querySelector("#spotlight-backdrop")?.addEventListener(
        "mousedown",
        event => {
          const target = event.target;
          if (hasElementId(target) && target.id === "spotlight-backdrop") close();
        },
      );
      focus();
    }
  }

  return {
    bind,
    close,
    inlineHtml,
    modalHtml,
    open,
    refresh,
    reset,
    results,
    updateResults,
  };
}
