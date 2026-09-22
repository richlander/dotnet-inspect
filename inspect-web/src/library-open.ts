export const MAX_LIBRARY_UPLOAD_BYTES = 32 * 1024 * 1024;

export type LibraryOpenInput = "picker" | "drop" | "paste";

export interface LibraryOpenView {
  open: boolean;
  busy: boolean;
  error: string;
}

export interface LibraryOpenActions {
  onDismiss(): void;
  onReject(message: string, input: LibraryOpenInput): void;
  onFile(file: File, input: LibraryOpenInput): void;
}

export interface LibraryUploadCandidate {
  readonly name: string;
  readonly size: number;
}

export type LibraryUploadAdmission<TFile extends LibraryUploadCandidate> =
  | { kind: "accepted"; file: TFile }
  | { kind: "rejected"; message: string };

export function admitLibraryUpload<TFile extends LibraryUploadCandidate>(
  files: ArrayLike<TFile>,
  maximumBytes = MAX_LIBRARY_UPLOAD_BYTES,
): LibraryUploadAdmission<TFile> {
  if (files.length === 0) {
    return {
      kind: "rejected",
      message: "Choose one managed .NET assembly.",
    };
  }
  if (files.length !== 1) {
    return {
      kind: "rejected",
      message: "Open one managed .NET assembly at a time.",
    };
  }

  const file = files[0]!;
  if (file.size === 0) {
    return {
      kind: "rejected",
      message: "The selected file is empty.",
    };
  }
  if (file.size > maximumBytes) {
    return {
      kind: "rejected",
      message:
        `The selected file exceeds the ${formatByteCount(maximumBytes)} upload limit.`,
    };
  }
  return { kind: "accepted", file };
}

function submitLibraryUpload(
  files: ArrayLike<File>,
  source: LibraryOpenInput,
  actions: LibraryOpenActions,
): void {
  const admission = admitLibraryUpload(files);
  if (admission.kind === "accepted") {
    actions.onFile(admission.file, source);
  } else {
    actions.onReject(admission.message, source);
  }
}

function hasFiles(event: DragEvent): boolean {
  return [...(event.dataTransfer?.types ?? [])].includes("Files");
}

export function bindLibraryOpenDocument(
  root: Document,
  currentView: () => Pick<LibraryOpenView, "open" | "busy">,
  actions: LibraryOpenActions,
): () => void {
  const dragover = (event: DragEvent) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = currentView().busy ? "none" : "copy";
    }
    if (currentView().busy) return;
    root.querySelector("#library-open-dropzone")
      ?.classList.add("is-dragging");
  };
  const drop = (event: DragEvent) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    root.querySelector("#library-open-dropzone")
      ?.classList.remove("is-dragging");
    if (currentView().busy) return;
    submitLibraryUpload(event.dataTransfer?.files ?? [], "drop", actions);
  };
  const paste = (event: ClipboardEvent) => {
    const view = currentView();
    if (!view.open || view.busy) return;
    const files = event.clipboardData?.files;
    if (!files?.length) return;
    event.preventDefault();
    submitLibraryUpload(files, "paste", actions);
  };

  root.addEventListener("dragover", dragover);
  root.addEventListener("drop", drop);
  root.addEventListener("paste", paste);

  return () => {
    root.removeEventListener("dragover", dragover);
    root.removeEventListener("drop", drop);
    root.removeEventListener("paste", paste);
  };
}

export function renderLibraryOpenDialog(
  view: LibraryOpenView,
  escapeHtml: (value: unknown) => string,
): string {
  if (!view.open) return "";
  const status = view.busy
    ? `<div id="library-open-status" class="library-open-status"
        role="status" aria-live="polite" tabindex="-1">
        <span class="loader" aria-hidden="true"></span>
        <span>Opening managed assembly…</span>
      </div>`
    : view.error
      ? `<div id="library-open-error" class="library-open-error"
          role="alert" tabindex="-1">${escapeHtml(view.error)}</div>`
      : `<p class="library-open-hint">The Library stays in this tab and is not uploaded to a server.</p>`;
  return `<div id="library-open-backdrop" class="modal-backdrop">
    <section id="library-open-dialog" class="application-dialog library-open-dialog"
      role="dialog" aria-modal="true" aria-labelledby="library-open-title">
      <header class="application-dialog-head">
        <div>
          <p class="section-eyebrow">Open</p>
          <h2 id="library-open-title" tabindex="-1">Managed Library</h2>
        </div>
        <button id="library-open-close" type="button"
          ${view.busy ? "disabled" : ""}>Close</button>
      </header>
      <div class="library-open-body">
        <label id="library-open-dropzone" class="library-open-dropzone"
          for="library-open-input">
          <strong>Drop a .dll or .exe here</strong>
          <span>or choose a managed assembly from this device</span>
          <span class="library-open-button">Choose file</span>
          <input id="library-open-input" type="file"
            accept=".dll,.exe,.netmodule,application/octet-stream"
            ${view.busy ? "disabled" : ""}>
        </label>
        ${status}
      </div>
    </section>
  </div>`;
}

export function bindLibraryOpen(
  root: Document,
  view: LibraryOpenView,
  actions: LibraryOpenActions,
): () => void {
  const dialog = root.querySelector<HTMLElement>("#library-open-dialog");
  const backdrop =
    root.querySelector<HTMLElement>("#library-open-backdrop");
  const input =
    root.querySelector<HTMLInputElement>("#library-open-input");
  const dropzone =
    root.querySelector<HTMLElement>("#library-open-dropzone");

  const dragleave = (event: DragEvent) => {
    if (event.target === dropzone) dropzone?.classList.remove("is-dragging");
  };

  input?.addEventListener("change", () =>
    submitLibraryUpload(input.files ?? [], "picker", actions));
  root.querySelector("#library-open-close")
    ?.addEventListener("click", () => actions.onDismiss());
  backdrop?.addEventListener("click", event => {
    if (event.target === backdrop && !view.busy) actions.onDismiss();
  });
  dialog?.addEventListener("keydown", event => {
    if (event.key === "Escape" && !view.busy) {
      event.preventDefault();
      actions.onDismiss();
    } else if (event.key === "Tab") {
      trapModalTab(dialog, event);
    }
  });
  dropzone?.addEventListener("dragleave", dragleave);

  if (dialog && !dialog.contains(root.activeElement)) {
    const focusTarget = view.error
      ? root.querySelector<HTMLElement>("#library-open-error")
      : view.busy
        ? root.querySelector<HTMLElement>("#library-open-status")
        : root.querySelector<HTMLElement>("#library-open-title");
    focusTarget?.focus({ preventScroll: true });
  }

  return () => dropzone?.removeEventListener("dragleave", dragleave);
}

function formatByteCount(bytes: number): string {
  return bytes % (1024 * 1024) === 0
    ? `${bytes / (1024 * 1024)} MiB`
    : `${bytes.toLocaleString()} bytes`;
}
import { trapModalTab } from "./shell-controls.ts";
