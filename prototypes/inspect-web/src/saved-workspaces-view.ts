import { packageRemoveButton } from "./package-removal.ts";
import {
  savedWorkspaceNameLimit,
  type createSavedWorkspaces,
  type SavedWorkspaceFocus,
  type SavedWorkspacesState,
} from "./saved-workspaces.ts";

export interface SavedWorkspacesView {
  state: SavedWorkspacesState;
  canSave: boolean;
  canOpen: boolean;
}

const controls = {
  save: "[data-workspace-save]",
  "save-submit": "[data-workspace-save-submit]",
  "save-cancel": "[data-workspace-save-cancel]",
  "saved-retry": "[data-saved-workspaces-retry]",
} as const;

export function renderWorkspaceSaveButton(view: SavedWorkspacesView): string {
  const disabled = !view.canSave || !view.state.available || view.state.formOpen;
  return `<button type="button" data-workspace-save${disabled ? " disabled" : ""}>Save Workspace</button>`;
}

export function renderSavedWorkspaces(
  view: SavedWorkspacesView,
  escapeHtml: (value: unknown) => string,
): string {
  const { state, canSave, canOpen } = view;
  if (!state.formOpen && !state.entries.length && !state.error) return "";
  const form = state.formOpen
    ? `<form class="workspace-save-form" data-workspace-save-form>
        <label for="workspace-save-name">Workspace name</label>
        <input id="workspace-save-name" value="${escapeHtml(state.name)}" maxlength="${savedWorkspaceNameLimit}" required autocomplete="off"${state.error ? ' aria-describedby="workspace-saves-error"' : ""} />
        <button type="submit" data-workspace-save-submit${canSave && state.available ? "" : " disabled"}>Save</button>
        <button type="button" data-workspace-save-cancel>Cancel</button>
      </form>`
    : "";
  const error = state.error
    ? `<p id="workspace-saves-error" role="alert">${escapeHtml(state.error)}</p>`
    : "";
  const retry = state.available
    ? ""
    : `<button type="button" data-saved-workspaces-retry>Retry reading saved Workspaces</button>`;
  const rows = state.entries.map(entry =>
    `<li class="workspace-saved-row">
      <button type="button" class="workspace-saved-open" data-saved-workspace-open="${escapeHtml(entry.name)}" aria-label="Open saved Workspace ${escapeHtml(entry.name)}"${canOpen && state.available ? "" : " disabled"}>
        <strong>${escapeHtml(entry.name)}</strong><small>Open</small>
      </button>
      ${state.available ? packageRemoveButton(
        "data-saved-workspace-remove", entry.name,
        `Forget saved Workspace ${entry.name}`, escapeHtml) : ""}
    </li>`).join("");
  return `<section class="document-section workspace-section workspace-saved">
    <div class="section-title"><h2>Saved Workspaces</h2><span>On this browser</span></div>
    ${form}${error}${retry}
    ${rows ? `<ul class="workspace-saved-list">${rows}</ul>` : ""}
  </section>`;
}

export function bindSavedWorkspaces(
  root: ParentNode,
  actions: ReturnType<typeof createSavedWorkspaces>,
): void {
  root.querySelector(controls.save)?.addEventListener("click", () => actions.beginSave());
  root.querySelector(controls["save-cancel"])?.addEventListener("click", () => actions.cancelSave());
  root.querySelector(controls["saved-retry"])?.addEventListener("click", () => actions.retry());
  const input = root.querySelector<HTMLInputElement>("#workspace-save-name");
  input?.addEventListener("input", () => actions.setName(input.value));
  root.querySelector("[data-workspace-save-form]")?.addEventListener("submit", event => {
    event.preventDefault();
    actions.save();
  });
  root.querySelectorAll<HTMLElement>("[data-saved-workspace-open]").forEach(button =>
    button.addEventListener("click", () => {
      const name = button.dataset.savedWorkspaceOpen;
      if (name !== undefined) actions.open(name);
    }));
  root.querySelectorAll<HTMLElement>("[data-saved-workspace-remove]").forEach(button =>
    button.addEventListener("click", () => {
      const name = button.dataset.savedWorkspaceRemove;
      if (name !== undefined) actions.forget(name);
    }));
}

export function captureSavedWorkspaceFocus(
  element: HTMLElement | null,
): SavedWorkspaceFocus | null {
  const target = element?.closest<HTMLElement>(
    `${Object.values(controls).join(",")}, #workspace-save-name, [data-saved-workspace-open], [data-saved-workspace-remove]`);
  if (!target) return null;
  if (target.id === "workspace-save-name") {
    const input = target.ownerDocument.querySelector<HTMLInputElement>("#workspace-save-name");
    return {
      kind: "save-name", start: input?.selectionStart ?? null,
      end: input?.selectionEnd ?? null, direction: input?.selectionDirection ?? null,
    };
  }
  for (const kind of ["save", "save-submit", "save-cancel", "saved-retry"] as const) {
    if (target.matches(controls[kind])) return { kind };
  }
  const name = target.dataset.savedWorkspaceOpen ?? target.dataset.savedWorkspaceRemove;
  if (name === undefined) return null;
  const open = target.dataset.savedWorkspaceOpen !== undefined;
  return {
    kind: open ? "saved-open" : "saved-remove",
    name,
    index: [...target.ownerDocument.querySelectorAll(
      open ? "[data-saved-workspace-open]" : "[data-saved-workspace-remove]")].indexOf(target),
  };
}

export function restoreSavedWorkspaceFocus(
  root: ParentNode,
  target: SavedWorkspaceFocus,
): boolean {
  let element: HTMLElement | null = null;
  if (target.kind === "save-name") {
    const input = root.querySelector<HTMLInputElement>("#workspace-save-name");
    if (input) {
      input.focus({ preventScroll: true });
      input.setSelectionRange(
        target.start ?? input.value.length, target.end ?? input.value.length,
        target.direction ?? "none");
      return true;
    }
  } else if (target.kind === "saved-open" || target.kind === "saved-remove") {
    const open = target.kind === "saved-open";
    const buttons = [...root.querySelectorAll<HTMLElement>(
      open ? "[data-saved-workspace-open]" : "[data-saved-workspace-remove]")];
    element = buttons.find(button =>
      (open ? button.dataset.savedWorkspaceOpen : button.dataset.savedWorkspaceRemove) === target.name)
      ?? buttons[Math.min(target.index, buttons.length - 1)]
      ?? null;
  } else {
    element = root.querySelector<HTMLElement>(controls[target.kind]);
  }
  if (!element || element.hasAttribute("disabled")) {
    element = root.querySelector<HTMLElement>(controls.save);
    if (!element || element.hasAttribute("disabled")) {
      element = root.querySelector<HTMLElement>("h1");
      if (element) element.tabIndex = -1;
    }
  }
  element?.focus({ preventScroll: true });
  return element !== null;
}
