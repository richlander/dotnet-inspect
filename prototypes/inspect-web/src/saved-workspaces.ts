export interface SavedWorkspace {
  name: string;
  packet: string;
}

export type SavedWorkspaceFocus =
  | { kind: "saved-open" | "saved-remove"; name: string; index: number }
  | { kind: "save" | "save-submit" | "save-cancel" | "saved-retry" }
  | {
      kind: "save-name";
      start?: number | null;
      end?: number | null;
      direction?: "forward" | "backward" | "none" | null;
    };

export interface SavedWorkspacesState {
  entries: readonly SavedWorkspace[];
  available: boolean;
  formOpen: boolean;
  name: string;
  error: string;
}

export const savedWorkspaceNameLimit = 120;

function validateName(name: string): string {
  const trimmed = name.trim();
  if (!trimmed) throw new Error("Enter a name for this Workspace.");
  if (trimmed.length > savedWorkspaceNameLimit)
    throw new Error(`Workspace names can contain at most ${savedWorkspaceNameLimit} characters.`);
  return trimmed;
}

function readEntries(raw: string | null): SavedWorkspace[] {
  if (raw === null) return [];
  const value: unknown = JSON.parse(raw);
  if (typeof value !== "object" || value === null
    || !("version" in value) || value.version !== 1
    || !("entries" in value) || !Array.isArray(value.entries)) {
    throw new Error("The saved Workspace data has an unsupported format.");
  }
  const names = new Set<string>();
  return value.entries.map((entry: unknown) => {
    if (typeof entry !== "object" || entry === null
      || !("name" in entry) || typeof entry.name !== "string"
      || !("packet" in entry) || typeof entry.packet !== "string") {
      throw new Error("A saved Workspace entry could not be read.");
    }
    const name = validateName(entry.name);
    if (name !== entry.name || names.has(name.toLowerCase()))
      throw new Error("The saved Workspace data contains invalid or duplicate names.");
    names.add(name.toLowerCase());
    return { name, packet: entry.packet };
  });
}

export function createSavedWorkspaces(options: {
  read: () => string | null;
  write: (value: string) => void;
  capture: () => string;
  open: (entry: SavedWorkspace) => void;
  render: (focus?: SavedWorkspaceFocus) => void;
}) {
  const state: SavedWorkspacesState = {
    entries: [], available: false, formOpen: false, name: "", error: "",
  };

  function load(): void {
    try {
      const entries = readEntries(options.read());
      state.entries = entries;
      state.available = true;
      state.error = "";
    } catch (error) {
      state.available = false;
      state.error = `Could not read saved Workspaces: ${String(error)}`;
    }
  }

  function persist(entries: readonly SavedWorkspace[]): void {
    if (!state.available) throw new Error("Read saved Workspaces successfully before changing them.");
    options.write(JSON.stringify({ version: 1, entries }));
    state.entries = entries;
  }

  function find(name: string): SavedWorkspace {
    const entry = state.entries.find(candidate => candidate.name === name);
    if (!entry) throw new Error(`The saved Workspace "${name}" is no longer available.`);
    return entry;
  }

  load();

  return {
    state,
    beginSave() {
      state.formOpen = true;
      state.name = "";
      state.error = "";
      options.render({ kind: "save-name" });
    },
    setName(name: string) {
      state.name = name;
    },
    cancelSave() {
      state.formOpen = false;
      state.name = "";
      state.error = "";
      options.render({ kind: "save" });
    },
    save() {
      let focus: SavedWorkspaceFocus = { kind: "save-name" };
      try {
        const name = validateName(state.name);
        if (state.entries.some(entry => entry.name.toLowerCase() === name.toLowerCase())) {
          throw new Error(`A saved Workspace named "${name}" already exists. Choose another name.`);
        }
        const entry = { name, packet: options.capture() };
        persist([...state.entries, entry]);
        state.formOpen = false;
        state.name = "";
        state.error = "";
        focus = { kind: "saved-open", name, index: state.entries.length - 1 };
      } catch (error) {
        state.error = `Could not save Workspace: ${String(error)}`;
      }
      options.render(focus);
    },
    open(name: string) {
      try {
        const entry = find(name);
        state.formOpen = false;
        state.error = "";
        options.open(entry);
      } catch (error) {
        state.error = `Could not open saved Workspace: ${String(error)}`;
        options.render();
      }
    },
    forget(name: string) {
      let focus: SavedWorkspaceFocus | undefined;
      try {
        const entry = find(name);
        const index = state.entries.indexOf(entry);
        persist(state.entries.filter(candidate => candidate !== entry));
        state.error = "";
        focus = { kind: "saved-remove", name, index };
      } catch (error) {
        state.error = `Could not forget saved Workspace: ${String(error)}`;
      }
      options.render(focus);
    },
    retry() {
      load();
      options.render({ kind: "saved-retry" });
    },
  };
}
