type CommandDefinition = readonly [value: string, hint: string];
type LensDefinition = readonly [id: string, label: string];

interface CommandBarType {
  id: string;
  name: string;
  namespace: string;
  kind: string;
}

interface CommandBarPackage {
  id: string;
  activeFramework: string;
  types: readonly CommandBarType[];
  frameworks: readonly string[];
}

export interface CommandBarState {
  command: string;
  completionIndex: number;
  promptOpen: boolean;
  package: CommandBarPackage;
}

export interface CommandSuggestion {
  value: string;
  hint: string;
  kind: string;
}

interface CommandBarOptions {
  state: CommandBarState;
  lenses: readonly LensDefinition[];
  escapeHtml: (value: unknown) => string;
  execute: (value: string) => void;
  render: () => void;
  focusAfterDismiss: () => void;
}

const ROOT_COMMANDS: readonly CommandDefinition[] = [
  ["type", "select a public type"],
  ["types", "filter or group the type index"],
  ["show", "change the active lens"],
  ["framework", "select a target framework"],
  ["find", "search the current package"],
  ["clear", "clear the current filter"],
  ["share", "copy a link to this selection"],
];

export function commandCompletions(
  state: CommandBarState,
  lenses: readonly LensDefinition[],
): CommandSuggestion[] {
  const input = state.command.trimStart();
  const tokens = input.split(/\s+/).filter(Boolean);
  let entries: CommandSuggestion[];

  if (!tokens.length) {
    entries = ROOT_COMMANDS.map(([value, hint]) => ({ value, hint, kind: "command" }));
  } else if (tokens[0] === "type") {
    entries = state.package.types.map(item => ({
      value: item.name,
      hint: item.namespace,
      kind: item.kind,
    }));
  } else if (tokens[0] === "show") {
    entries = lenses.map(([value, label]) => ({
      value,
      hint: `${label} lens`,
      kind: "lens",
    }));
  } else if (tokens[0] === "framework") {
    entries = state.package.frameworks.map(value => ({
      value,
      hint: "compile assets",
      kind: "framework",
    }));
  } else if (tokens[0] === "types") {
    entries = [
      { value: "public", hint: "public surface (default)", kind: "filter" },
      { value: "namespace", hint: "filter to a namespace", kind: "filter" },
      { value: "kind", hint: "filter by class, struct, interface, or enum", kind: "filter" },
    ];
  } else {
    entries = ROOT_COMMANDS.map(([value, hint]) => ({ value, hint, kind: "command" }));
  }

  if (input.endsWith(" ")) return entries.slice(0, 8);
  const needle = tokens.at(-1)?.toLowerCase() || "";
  return entries
    .filter(entry => entry.value.toLowerCase().includes(needle))
    .slice(0, 8);
}

export function commandSuggestionsHtml(
  items: readonly CommandSuggestion[],
  selectedIndex: number,
  escapeHtml: (value: unknown) => string,
): string {
  return `${items.map((item, index) => `
      <button class="suggestion ${index === selectedIndex ? "selected" : ""}" data-completion="${escapeHtml(item.value)}">
        <strong>${escapeHtml(item.value)}</strong><span>${escapeHtml(item.hint)}</span><small>${escapeHtml(item.kind)}</small>
      </button>`).join("")}
      <div class="suggestion-help"><span>↑↓ select</span><span>tab complete</span><span>enter run</span><span>esc dismiss</span></div>`;
}

export function commandBarHtml(
  state: CommandBarState,
  items: readonly CommandSuggestion[],
  escapeHtml: (value: unknown) => string,
): string {
  return `
      <section class="command-area">
        <div class="command-panel ${state.promptOpen ? "open" : ""}">
          <div class="suggestions" id="command-suggestions" role="listbox">
            ${commandSuggestionsHtml(items, state.completionIndex, escapeHtml)}
          </div>
          <div class="command-line">
            <span class="command-scope">${escapeHtml(state.package.id)}:${escapeHtml(state.package.activeFramework)}</span>
            <span class="prompt">›</span>
            <input id="command" value="${escapeHtml(state.command)}" placeholder="type a command…  try “type JsonSerializer”" autocomplete="off" spellcheck="false" />
            <kbd>⌘K</kbd>
          </div>
        </div>
      </section>`;
}

export function applyCommandCompletion(command: string, value: string): string {
  const tokens = command.trim().split(/\s+/).filter(Boolean);
  if (!tokens.length) return `${value} `;
  if (command.endsWith(" ")) return `${command}${value} `;

  tokens[tokens.length - 1] = value;
  return `${tokens.join(" ")} `;
}

export function createCommandBar(options: CommandBarOptions) {
  const {
    state,
    lenses,
    escapeHtml,
    execute,
    render,
    focusAfterDismiss,
  } = options;

  function completions(): CommandSuggestion[] {
    return commandCompletions(state, lenses);
  }

  function suggestionsHtml(items: readonly CommandSuggestion[] = completions()): string {
    return commandSuggestionsHtml(items, state.completionIndex, escapeHtml);
  }

  function html(): string {
    const items = completions();
    state.completionIndex = Math.min(
      state.completionIndex,
      Math.max(items.length - 1, 0),
    );
    return commandBarHtml(state, items, escapeHtml);
  }

  function focus(): void {
    requestAnimationFrame(() => {
      const input = document.querySelector<HTMLInputElement>("#command");
      if (!input) throw new Error("The command bar input is unavailable.");
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
    });
  }

  function bindCompletionClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-completion]").forEach(button => {
      button.addEventListener("mousedown", event => {
        event.preventDefault();
        const value = button.dataset.completion;
        if (value === undefined) {
          throw new Error("A command completion is missing its value.");
        }
        applyCompletion(value);
      });
    });
  }

  function updateSuggestions(): void {
    const container = document.querySelector<HTMLElement>("#command-suggestions");
    if (!container) return;

    const items = completions();
    state.completionIndex = Math.min(
      state.completionIndex,
      Math.max(items.length - 1, 0),
    );
    container.innerHTML = suggestionsHtml(items);
    bindCompletionClicks(container);
    container.querySelector(".suggestion.selected")?.scrollIntoView({ block: "nearest" });
  }

  function applyCompletion(value: string): void {
    state.command = applyCommandCompletion(state.command, value);
    state.completionIndex = 0;
    state.promptOpen = true;

    const input = document.querySelector<HTMLInputElement>("#command");
    if (!input) throw new Error("The command bar input is unavailable.");
    input.value = state.command;
    updateSuggestions();
    focus();
  }

  function run(): void {
    const value = state.command.trim();
    if (!value) return;
    execute(value);
    state.command = "";
    state.promptOpen = false;
    render();
  }

  function handleKeys(event: KeyboardEvent): void {
    const items = completions();
    if (event.key === "ArrowDown") {
      event.preventDefault();
      state.completionIndex = (state.completionIndex + 1) % Math.max(1, items.length);
      updateSuggestions();
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      state.completionIndex = (
        state.completionIndex - 1 + Math.max(1, items.length)
      ) % Math.max(1, items.length);
      updateSuggestions();
    } else if (event.key === "Tab" && items.length) {
      event.preventDefault();
      applyCompletion(items[state.completionIndex].value);
    } else if (event.key === "Enter") {
      event.preventDefault();
      run();
    } else if (event.key === "Escape") {
      state.promptOpen = false;
      state.command = "";
      render();
      focusAfterDismiss();
    }
  }

  function bind(root: ParentNode): void {
    bindCompletionClicks(root);

    const command = root.querySelector<HTMLInputElement>("#command");
    const panel = root.querySelector<HTMLElement>(".command-panel");
    if (!command || !panel) {
      throw new Error("The command bar markup is incomplete.");
    }

    command.addEventListener("focus", () => {
      state.promptOpen = true;
      panel.classList.add("open");
    });
    command.addEventListener("input", () => {
      state.command = command.value;
      state.promptOpen = true;
      state.completionIndex = 0;
      updateSuggestions();
    });
    command.addEventListener("keydown", handleKeys);
  }

  function open(value = ""): void {
    state.command = value;
    state.promptOpen = true;
    render();
    focus();
  }

  return {
    bind,
    completions,
    html,
    open,
    suggestionsHtml,
  };
}
