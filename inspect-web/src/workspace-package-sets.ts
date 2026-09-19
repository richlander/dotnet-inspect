import type {
  BrowserWorkspacePackageSetCatalog,
  BrowserWorkspacePackageSetDescriptor,
} from "./facades/inspect-web-package.d.ts";
import { trapModalTab } from "./shell-controls.ts";

export interface WorkspacePackageSetPickerState {
  open: boolean;
  loading: boolean;
  error: string;
  availableSlots: number;
  packageSets: readonly BrowserWorkspacePackageSetDescriptor[];
}

export interface WorkspacePackageSetPickerActions {
  onClose: () => void;
  onAdd: (id: string) => void;
}

export function workspacePackageSets(
  catalog: BrowserWorkspacePackageSetCatalog,
): BrowserWorkspacePackageSetDescriptor[] {
  if (catalog.version !== 1) {
    throw new Error(
      `Workspace package-set catalog version '${catalog.version}' is unsupported.`);
  }
  if (catalog.packageSets.length === 0) {
    throw new Error("Workspace package-set catalog is empty.");
  }

  const ids = new Set<string>();
  let priorOrder: number | null = null;
  return catalog.packageSets.map(packageSet => {
    if (!/^package-set\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/.test(packageSet.id)
      || !packageSet.title.trim()
      || !packageSet.summary.trim()
      || !Number.isSafeInteger(packageSet.order)
      || packageSet.sourceKind !== "PackageGroup"
      || packageSet.coordinates.length === 0) {
      throw new TypeError(
        "Workspace package-set catalog contains an invalid descriptor.");
    }
    if (ids.has(packageSet.id)) {
      throw new TypeError(
        `Workspace package-set catalog repeats '${packageSet.id}'.`);
    }
    ids.add(packageSet.id);
    if (priorOrder !== null && packageSet.order <= priorOrder) {
      throw new TypeError(
        "Workspace package-set catalog is not in product order.");
    }
    priorOrder = packageSet.order;

    const packageIds = new Set<string>();
    const coordinates = packageSet.coordinates.map(coordinate => {
      if (!coordinate.packageId.trim()
        || coordinate.version !== null
        || coordinate.framework !== null
        || coordinate.runtimeIdentifier !== null) {
        throw new TypeError(
          `Workspace package set '${packageSet.id}' contains an invalid package coordinate.`);
      }
      const key = coordinate.packageId.toLowerCase();
      if (packageIds.has(key)) {
        throw new TypeError(
          `Workspace package set '${packageSet.id}' repeats '${coordinate.packageId}'.`);
      }
      packageIds.add(key);
      return { ...coordinate };
    });
    return { ...packageSet, coordinates };
  });
}

export function renderWorkspacePackageSetPicker(
  state: WorkspacePackageSetPickerState,
  escapeHtml: (value: unknown) => string,
): string {
  if (!state.open) return "";

  const body = state.loading
    ? '<p class="workspace-package-set-status">Loading product package sets…</p>'
    : state.error
      ? `<p class="workspace-package-set-status" role="alert">${escapeHtml(state.error)}</p>`
      : `<ul class="workspace-package-set-list">
          ${state.packageSets.map(packageSet => {
            const count = packageSet.coordinates.length;
            const canAdd = count <= state.availableSlots;
            const detail = canAdd
              ? `${count} packages`
              : `${count} packages · requires ${count} available slots; ${state.availableSlots} remain`;
            return `<li>
              <div>
                <strong>${escapeHtml(packageSet.title)}</strong>
                <p>${escapeHtml(packageSet.summary)}</p>
                <small>${escapeHtml(detail)}</small>
              </div>
              <button type="button" data-workspace-add-package-set="${escapeHtml(packageSet.id)}"${canAdd ? "" : " disabled"}>Add</button>
            </li>`;
          }).join("")}
        </ul>`;

  return `<div id="workspace-package-set-backdrop" class="modal-backdrop">
    <section id="workspace-package-set-dialog"
      class="application-dialog workspace-package-set-dialog"
      role="dialog" aria-modal="true"
      aria-labelledby="workspace-package-set-title" tabindex="-1">
      <header class="application-dialog-head">
        <div>
          <p class="section-eyebrow">Workspace</p>
          <h2 id="workspace-package-set-title">Add package set</h2>
        </div>
        <button type="button" data-workspace-package-set-close aria-label="Close">esc</button>
      </header>
      <p>Choose one complete product-owned package set. Membership is never truncated.</p>
      ${body}
    </section>
  </div>`;
}

export function bindWorkspacePackageSetPicker(
  root: ParentNode,
  actions: WorkspacePackageSetPickerActions,
): void {
  const backdrop = root.querySelector<HTMLElement>(
    "#workspace-package-set-backdrop");
  const dialog = root.querySelector<HTMLElement>(
    "#workspace-package-set-dialog");
  root.querySelector<HTMLElement>("[data-workspace-package-set-close]")
    ?.addEventListener("click", actions.onClose);
  root.querySelectorAll<HTMLElement>("[data-workspace-add-package-set]")
    .forEach(button => button.addEventListener("click", () => {
      const id = button.dataset.workspaceAddPackageSet;
      if (id !== undefined) actions.onAdd(id);
    }));
  backdrop?.addEventListener("click", event => {
    if (event.target === backdrop) actions.onClose();
  });
  dialog?.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      event.preventDefault();
      actions.onClose();
    } else if (event.key === "Tab") {
      trapModalTab(dialog, event);
    }
  });
}
