import {
  commandPaletteResults,
  commandPaletteRowHtml,
  type CommandContext,
  type CommandPaletteResult,
} from "./command-bar.ts";

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

interface PackageNugetResult {
  kind: "pkg-nuget";
  hit: { id: string; version?: string };
  ranges: readonly HighlightRange[];
}

interface PackageRecentResult {
  kind: "pkg-recent";
  entry: { id: string; version?: string; framework?: string };
  ranges: readonly HighlightRange[];
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
  | RuntimeSuggestionResult
  | RuntimeStatusResult
  | PlatformLibraryResult
  | TypeResult
  | MemberResult;

export interface SpotlightState {
  spotlightOpen: boolean;
  spotlightQuery: string;
  spotlightIndex: number;
  spotlightScope: string;
  spotlightFocus: SpotlightFocus;
  spotlightChipIndex: number;
}

interface SpotlightOptions {
  state: SpotlightState;
  lenses: readonly LensDefinition[];
  escapeHtml: (value: unknown) => string;
  highlightRanges: (
    value: string,
    ranges: readonly HighlightRange[],
  ) => string;
  kindIcon: (kind: string) => string;
  searchResults: () => SpotlightResult[];
  pickResult: (result: SpotlightResult) => void;
  executeCommand: (command: string) => void;
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
const PLATFORM_PACK_LABEL: Readonly<Record<string, string>> = {
  "netcore.app": ".NET",
  "aspnetcore.app": "ASP.NET Core",
};
const GROUP_LABELS: Readonly<Record<SpotlightResult["kind"], string>> = {
  command: "Commands",
  "pkg-recent": "Recent",
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

export function createSpotlight(options: SpotlightOptions) {
  const { state, escapeHtml } = options;

  function scopes() {
    return options.commandContext()
      ? [...BASE_SCOPES, COMMAND_SCOPE]
      : [...BASE_SCOPES];
  }

  function results(): SpotlightResult[] {
    if (state.spotlightScope === "commands") {
      const context = options.commandContext();
      return context
        ? commandPaletteResults(context, options.lenses)
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
    const base = `class="spotlight-item ${selectedClass}" role="option" aria-selected="${selected}" data-sl-index="${index}"`;
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
      return `<div class="spotlight-item spotlight-status ${selectedClass}" data-sl-index="${index}">
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

  function modalHtml(): string {
    const items = results();
    clampSelection(items);
    const commands = state.spotlightScope === "commands";
    return `
      <div class="spotlight-backdrop" id="spotlight-backdrop">
        <div class="spotlight" role="dialog" aria-modal="true" aria-label="${commands ? "Run a command" : "Go to anything"}">
          <div class="spotlight-search">
            <span class="spotlight-glyph">${commands ? "›" : "⌕"}</span>
            <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="${commands ? "Run a command…" : "Go to anything…  package, type, or member"}" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results" />
            <kbd>esc</kbd>
          </div>
          <div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>
          <div class="spotlight-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
          <div class="spotlight-foot"><span>↑↓ select</span><span>→ target</span><span>↵ ${commands ? "complete / run" : "open"}</span><span>esc close</span></div>
        </div>
      </div>`;
  }

  function inlineHtml(disabled: boolean): string {
    const items = results();
    clampSelection(items);
    return `
      <div class="home-search-content" ${disabled ? "inert" : ""}>
        <div class="home-search-box">
          <span class="spotlight-glyph">⌕</span>
          <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Search NuGet — a package, type, or member…" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results" ${disabled ? "disabled" : ""} />
        </div>
        <div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>
        <div class="spotlight-results home-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
      </div>`;
  }

  function bindChipClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-sl-scope]").forEach(button => {
      button.addEventListener("click", () => {
        const scope = button.dataset.slScope;
        if (scope !== undefined) setScope(scope);
      });
    });
  }

  function bindResultClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-sl-index]").forEach(item => {
      item.addEventListener("click", () => {
        const index = Number(item.dataset.slIndex);
        const result = results()[index];
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
    const items = results();
    clampSelection(items);
    container.innerHTML = resultsHtml(items);
    bindResultClicks(container);
    container.querySelector(".spotlight-item.selected")
      ?.scrollIntoView({ block: "nearest" });
  }

  function refresh(): void {
    updateChips();
    updateResults();
  }

  function setScope(scope: string): void {
    const available = scopes();
    if (!available.some(item => item.id === scope)) return;
    state.spotlightScope = scope;
    state.spotlightIndex = 0;
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
  }

  function close(): void {
    reset();
    options.render();
    options.focusAfterDismiss?.();
  }

  function open(seed = "", scope = "all"): void {
    options.resetPackageSearch();
    state.spotlightOpen = true;
    state.spotlightQuery = seed;
    state.spotlightScope = scopes().some(item => item.id === scope)
      ? scope
      : "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
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
      const input = document.querySelector<HTMLInputElement>("#spotlight-input");
      if (input) input.value = state.spotlightQuery;
      updateResults();
      focus();
      return;
    }

    reset();
    options.executeCommand(result.command);
    options.render();
    options.focusAfterDismiss?.();
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

  function handleModalKeys(event: KeyboardEvent): void {
    if (event.key === "Escape") {
      event.preventDefault();
      close();
      return;
    }
    if (event.key === "Tab") {
      event.preventDefault();
      const available = scopes();
      const current = available.findIndex(scope => scope.id === state.spotlightScope);
      const next = nextSpotlightScope(
        current,
        available.length,
        event.shiftKey,
      );
      state.spotlightChipIndex = next;
      setScope(available[next].id);
      return;
    }

    if (state.spotlightFocus === "chips") {
      const available = scopes();
      if (event.key === "ArrowRight") {
        event.preventDefault();
        if (state.spotlightChipIndex < available.length - 1) {
          moveChip(state.spotlightChipIndex + 1);
        }
      } else if (event.key === "ArrowLeft") {
        event.preventDefault();
        if (state.spotlightChipIndex === 0) focusInput();
        else moveChip(state.spotlightChipIndex - 1);
      } else if (event.key === "ArrowUp") {
        event.preventDefault();
        focusInput();
      } else if (event.key === "ArrowDown" || event.key === "Enter") {
        event.preventDefault();
        state.spotlightIndex = 0;
        focusInput();
        highlightSelection();
      }
      return;
    }

    if (event.key === "ArrowRight") {
      const input = event.currentTarget as HTMLInputElement;
      const atEnd = input.selectionStart === input.selectionEnd
        && input.selectionStart === input.value.length;
      if (atEnd) {
        event.preventDefault();
        state.spotlightFocus = "chips";
        state.spotlightChipIndex = scopeIndex();
        updateChips();
      }
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      moveSelection(1);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      if (!moveSelection(-1)) {
        state.spotlightFocus = "chips";
        state.spotlightChipIndex = scopeIndex();
        updateChips();
      }
    } else if (event.key === "Enter") {
      event.preventDefault();
      pick(results()[state.spotlightIndex]);
    }
  }

  function handleInlineKeys(event: KeyboardEvent): void {
    const items = results();
    if (event.key === "ArrowDown") {
      event.preventDefault();
      state.spotlightIndex = nextSpotlightSelection(
        state.spotlightIndex,
        1,
        items.length,
      ) ?? 0;
      updateResults();
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      state.spotlightIndex = nextSpotlightSelection(
        state.spotlightIndex,
        -1,
        items.length,
      ) ?? 0;
      updateResults();
    } else if (event.key === "Enter") {
      event.preventDefault();
      pick(items[state.spotlightIndex]);
    }
  }

  function bind(root: ParentNode, mode: "modal" | "inline"): void {
    const input = root.querySelector<HTMLInputElement>("#spotlight-input");
    if (input) {
      input.addEventListener("input", () => {
        state.spotlightQuery = input.value;
        state.spotlightIndex = 0;
        if (state.spotlightFocus === "chips") {
          state.spotlightFocus = "input";
          updateChips();
        }
        options.schedulePackageFetch();
        updateResults();
      });
      input.addEventListener(
        "keydown",
        mode === "modal" ? handleModalKeys : handleInlineKeys,
      );
    }
    bindChipClicks(root);
    bindResultClicks(root);
    if (mode === "modal") {
      root.querySelector("#spotlight-backdrop")?.addEventListener(
        "mousedown",
        event => {
          const target = event.target as HTMLElement;
          if (target.id === "spotlight-backdrop") close();
        },
      );
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
