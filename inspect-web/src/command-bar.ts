type CommandDefinition = readonly [
  value: string,
  hint: string,
  argument: "choice" | "text" | "none",
];
type LensDefinition = readonly [id: string, label: string];

interface CommandType {
  id: string;
  name: string;
  namespace: string;
  kind: string;
}

interface CommandPackage {
  id: string;
  activeFramework: string;
  types: readonly CommandType[];
  frameworks: readonly string[];
}

export interface CommandContext {
  command: string;
  package: CommandPackage;
}

export interface CommandSuggestion {
  value: string;
  hint: string;
  category: string;
  targetTypeId?: string;
}

export interface CommandPaletteResult {
  kind: "command";
  value: string;
  hint: string;
  category: string;
  command: string;
  action: "complete" | "execute";
  targetTypeId?: string;
}

const ROOT_COMMANDS: readonly CommandDefinition[] = [
  ["type", "select a public type", "choice"],
  ["types", "filter or group the type index", "choice"],
  ["show", "change the active lens", "choice"],
  ["framework", "select a target framework", "choice"],
  ["find", "search the current package", "text"],
  ["clear", "clear the current filter", "none"],
  ["share", "copy a link to this selection", "none"],
  ["settings", "open application settings", "none"],
  ["keyboard help", "show keyboard commands", "none"],
];

export function commandCompletions(
  context: CommandContext,
  lenses: readonly LensDefinition[],
): CommandSuggestion[] {
  const input = context.command.trimStart();
  const tokens = input.split(/\s+/).filter(Boolean);
  let entries: CommandSuggestion[];

  if (!tokens.length) {
    entries = ROOT_COMMANDS.map(([value, hint]) => ({
      value,
      hint,
      category: "command",
    }));
  } else if (tokens[0] === "type") {
    entries = context.package.types.map(item => ({
      value: item.name,
      hint: item.namespace,
      category: item.kind,
      targetTypeId: item.id,
    }));
  } else if (tokens[0] === "show") {
    entries = lenses.map(([value, label]) => ({
      value,
      hint: `${label} lens`,
      category: "lens",
    }));
  } else if (tokens[0] === "framework") {
    entries = context.package.frameworks.map(value => ({
      value,
      hint: "compile assets",
      category: "framework",
    }));
  } else if (tokens[0] === "types") {
    entries = [
      { value: "public", hint: "public surface (default)", category: "filter" },
      { value: "namespace", hint: "filter to a namespace", category: "filter" },
      { value: "kind", hint: "filter by class, struct, interface, or enum", category: "filter" },
    ];
  } else {
    entries = ROOT_COMMANDS.map(([value, hint]) => ({
      value,
      hint,
      category: "command",
    }));
  }

  if (!tokens.length) return entries;
  if (input.endsWith(" ")) return entries.slice(0, 8);
  const needle = tokens.at(-1)?.toLowerCase() || "";
  return entries
    .filter(entry => entry.value.toLowerCase().includes(needle))
    .sort((left, right) =>
      Number(right.value.toLowerCase() === needle)
      - Number(left.value.toLowerCase() === needle))
    .slice(0, 8);
}

export function applyCommandCompletion(command: string, value: string): string {
  const tokens = command.trim().split(/\s+/).filter(Boolean);
  if (!tokens.length) return `${value} `;
  if (command.endsWith(" ")) return `${command}${value} `;

  tokens[tokens.length - 1] = value;
  return `${tokens.join(" ")} `;
}

export function commandPaletteResults(
  context: CommandContext,
  lenses: readonly LensDefinition[],
): CommandPaletteResult[] {
  const input = context.command.trimStart();
  const tokens = input.split(/\s+/).filter(Boolean);
  const exactRoot =
    ROOT_COMMANDS.find(([value]) => value === input.trim());
  if (exactRoot?.[2] === "none") {
    return [{
      kind: "command",
      value: exactRoot[0],
      hint: exactRoot[1],
      category: "command",
      command: exactRoot[0],
      action: "execute",
    }];
  }
  const root = ROOT_COMMANDS.find(([value]) => value === tokens[0]);

  if (root && tokens.length === 1
      && (!input.endsWith(" ") || root[2] === "none")) {
    return [{
      kind: "command",
      value: root[0],
      hint: root[1],
      category: "command",
      command: root[0],
      action: root[2] === "none" ? "execute" : "complete",
    }];
  }

  if (root?.[2] === "choice" && tokens.length > 2) return [];
  if (root?.[2] === "none" && tokens.length > 1) return [];
  if (!root && (tokens.length > 1 || input.endsWith(" "))) return [];
  if (root?.[2] === "choice" && tokens.length === 2 && input.endsWith(" ")) {
    return commandPaletteResults({
      ...context,
      command: tokens.join(" "),
    }, lenses);
  }

  if (root?.[2] === "text" && input.includes(" ")) {
    const command = input.trim();
    const argument = command.slice(root[0].length).trim();
    return argument
      ? [{
          kind: "command",
          value: command,
          hint: root[1],
          category: "command",
          command,
          action: "execute",
        }]
      : [{
          kind: "command",
          value: root[0],
          hint: "enter search text",
          category: "command",
          command: root[0],
          action: "complete",
        }];
  }

  const completingRoot = tokens.length < 2 && !input.endsWith(" ");
  return commandCompletions(context, lenses).map(item => {
    if (completingRoot) {
      const definition = ROOT_COMMANDS.find(([value]) => value === item.value);
      const action = definition?.[2] === "none" ? "execute" : "complete";
      return {
        kind: "command",
        ...item,
        command: item.value,
        action,
      };
    }

    return {
      kind: "command",
      ...item,
      command: applyCommandCompletion(context.command, item.value).trim(),
      action: "execute",
    };
  });
}

export function commandPaletteRowHtml(
  result: CommandPaletteResult,
  index: number,
  selected: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  const base = `id="spotlight-result-${index}" class="spotlight-item ${selected ? "selected" : ""}" role="option" aria-selected="${selected}" data-sl-index="${index}"`;
  return `<button ${base}>
    <span class="kind-icon sl-command">›</span>
    <span class="spotlight-item-name">${escapeHtml(result.command)}</span>
    <span class="spotlight-item-ns">${escapeHtml(result.hint)} · ${escapeHtml(result.category)}</span>
  </button>`;
}
