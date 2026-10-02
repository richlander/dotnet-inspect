interface CodeEvidenceViewerRegion {
  html: string;
  label: string;
  className?: string;
  scrollAttribute?: string;
  scrollKey?: string;
}

interface CodeEvidenceViewerControls {
  html: string;
  className?: string;
  scrollAttribute?: string;
  scrollKey?: string;
}

type CodeEvidenceViewerBody =
  | {
      kind: "workspace";
      className?: string;
      content: CodeEvidenceViewerRegion;
      rail?: CodeEvidenceViewerRegion;
    }
  | {
      kind: "failure";
      html: string;
      className?: string;
    };

export interface CodeEvidenceViewerOptions {
  backdropId: string;
  viewerId: string;
  labelledBy: string;
  header: string;
  body: CodeEvidenceViewerBody;
  escapeHtml: (value: unknown) => string;
  backdropClassName?: string;
  viewerClassName?: string;
  headerClassName?: string;
  notice?: string;
  controls?: CodeEvidenceViewerControls;
  detail?: string;
}

export function renderCodeEvidenceViewer(
  options: CodeEvidenceViewerOptions,
): string {
  const {
    body,
    escapeHtml,
  } = options;
  return `
    <div id="${escapeHtml(options.backdropId)}"
      class="${classes(
        "code-evidence-viewer-backdrop",
        options.backdropClassName,
      )}">
      <section id="${escapeHtml(options.viewerId)}"
        class="${classes("code-evidence-viewer", options.viewerClassName)}"
        role="dialog" aria-modal="true"
        aria-labelledby="${escapeHtml(options.labelledBy)}">
        <header class="${classes(
          "code-evidence-viewer-header",
          options.headerClassName,
        )}">
          ${options.header}
        </header>
        ${options.notice ?? ""}
        ${options.controls
          ? `<div class="${classes(
              "code-evidence-viewer-controls",
              options.controls.className,
            )}"${scrollAttribute(
              options.controls.scrollAttribute,
              options.controls.scrollKey,
              escapeHtml,
            )}>
              ${options.controls.html}
            </div>`
          : ""}
        ${body.kind === "workspace"
          ? renderWorkspace(body, escapeHtml)
          : `<section class="${classes(
              "code-evidence-viewer-failure",
              body.className,
            )}" role="alert">
              ${body.html}
            </section>`}
        ${options.detail ?? ""}
      </section>
    </div>`;
}

function renderWorkspace(
  workspace: Extract<CodeEvidenceViewerBody, { kind: "workspace" }>,
  escapeHtml: (value: unknown) => string,
): string {
  return `<div class="${classes(
    "code-evidence-viewer-workspace",
    workspace.rail ? undefined : "code-evidence-viewer-workspace-full",
    workspace.className,
  )}">
    ${renderRegion(
      workspace.content,
      "code-evidence-viewer-content",
      "section",
      escapeHtml,
    )}
    ${workspace.rail
      ? renderRegion(
          workspace.rail,
          "code-evidence-viewer-rail",
          "aside",
          escapeHtml,
        )
      : ""}
  </div>`;
}

function renderRegion(
  region: CodeEvidenceViewerRegion,
  baseClassName: string,
  tagName: "section" | "aside",
  escapeHtml: (value: unknown) => string,
): string {
  return `<${tagName} class="${classes(baseClassName, region.className)}"
    aria-label="${escapeHtml(region.label)}"${
      scrollAttribute(region.scrollAttribute, region.scrollKey, escapeHtml)
    }>
    ${region.html}
  </${tagName}>`;
}

function scrollAttribute(
  attribute: string | undefined,
  scrollKey: string | undefined,
  escapeHtml: (value: unknown) => string,
): string {
  if (scrollKey === undefined) return "";
  const name = attribute ?? "data-code-evidence-scroll";
  if (!/^data-[a-z0-9-]+$/.test(name)) {
    throw new TypeError(`Invalid data attribute: ${name}`);
  }
  return ` ${name}="${escapeHtml(scrollKey)}"`;
}

function classes(
  ...values: readonly (string | undefined)[]
): string {
  return values.filter(value => value !== undefined && value !== "").join(" ");
}
