import type {
  TypeExplorerAccessibility,
  TypeExplorerBodyMode,
  TypeExplorerIntent,
  TypeExplorerMemberIdentity,
  TypeExplorerPlacement,
} from "./type-explorer-route.ts";
import { assertNever } from "./data.ts";

interface TypeExplorerRange {
  readonly start: number;
  readonly length: number;
}

export type TypeExplorerBodyRole =
  | "Method"
  | "Getter"
  | "Setter"
  | "Init"
  | "Adder"
  | "Remover";

export interface TypeExplorerBodyDestination {
  readonly moduleVersionId: string;
  readonly member: TypeExplorerMemberIdentity;
  readonly metadataToken: number;
}

export interface TypeExplorerBody {
  readonly bodyId: number;
  readonly role: TypeExplorerBodyRole;
  readonly range: TypeExplorerRange;
  readonly destination: TypeExplorerBodyDestination | null;
}

export interface TypeExplorerDeclaration {
  readonly declarationId: number;
  readonly identity: TypeExplorerMemberIdentity;
  readonly declarationToken: number;
  readonly kind: string;
  readonly accessibility: string;
  readonly placement: string;
  readonly origin: string;
  readonly supportsSelectedBody: boolean;
  readonly range: TypeExplorerRange;
  readonly bodies: readonly TypeExplorerBody[];
}

export interface TypeExplorerProjection {
  readonly revision: string;
  readonly text: string;
  readonly declarations: readonly TypeExplorerDeclaration[];
  readonly diagnostics: readonly {
    readonly kind: string;
    readonly message: string;
  }[];
}

interface TypeExplorerDocument {
  readonly assemblyName: string;
  readonly pdbSupplied: boolean;
  readonly symbolSource: string;
  readonly renderingPolicy: string;
  readonly documentationCapability: string;
  readonly contractRelationshipCapability: string;
  readonly projection: TypeExplorerProjection | null;
  readonly projectionFailure: {
    readonly kind: string;
    readonly message: string;
  } | null;
}

export interface TypeExplorerInspection {
  readonly outcome: "Available" | "Incomplete" | "Unavailable" | "Rejected";
  readonly reason: string | null;
  readonly bodyProjectionsAttempted: number;
  readonly failedBodyIds: readonly number[];
  readonly document: TypeExplorerDocument | null;
  readonly diagnostics: readonly {
    readonly code: string;
    readonly severity: string;
    readonly message: string;
  }[];
}

export type TypeExplorerViewState =
  | { readonly status: "idle" }
  | { readonly status: "loading" }
  | {
      readonly status: "ready";
      readonly inspection: TypeExplorerInspection;
    }
  | { readonly status: "failed"; readonly error: string };

export type TypeExplorerSelectionPane = "outline" | "source";

export type TypeExplorerBodyInspectionState =
  | { readonly status: "idle" }
  | { readonly status: "loading"; readonly bodyId: number }
  | {
      readonly status: "failed";
      readonly bodyId: number;
      readonly error: string;
    };

export interface TypeExplorerViewOptions {
  readonly typeDisplay: string;
  readonly packageDisplay: string;
  readonly intent: TypeExplorerIntent;
  readonly state: TypeExplorerViewState;
  readonly bodyInspection?: TypeExplorerBodyInspectionState;
  readonly outlineOpen?: boolean;
  readonly escapeHtml: (value: unknown) => string;
  readonly highlightCSharp: (value: string) => string;
}

export interface TypeExplorerViewActions {
  readonly close: () => void;
  readonly retry: () => void;
  readonly toggleOutline: () => void;
  readonly selectBodyMode: (mode: TypeExplorerBodyMode) => void;
  readonly selectPlacement: (placement: TypeExplorerPlacement) => void;
  readonly selectAccessibility: (
    accessibility: TypeExplorerAccessibility,
    included: boolean,
  ) => void;
  readonly setIncludeGenerated: (included: boolean) => void;
  readonly setIncludeDocumentation: (included: boolean) => void;
  readonly setIncludeAttributes: (included: boolean) => void;
  readonly selectMember: (
    declaration: TypeExplorerDeclaration,
    pane: TypeExplorerSelectionPane,
  ) => void;
  readonly inspectBody: (
    declaration: TypeExplorerDeclaration,
    body: TypeExplorerBody,
  ) => void;
}

const ACCESSIBILITIES: readonly [
  TypeExplorerAccessibility,
  string,
][] = [
  ["Public", "public"],
  ["Protected", "protected"],
  ["ProtectedInternal", "protected internal"],
  ["Internal", "internal"],
  ["PrivateProtected", "private protected"],
  ["Private", "private"],
  ["Unknown", "unclassified"],
];

export function renderTypeExplorerView(
  options: TypeExplorerViewOptions,
): string {
  const {
    typeDisplay,
    packageDisplay,
    intent,
    state,
    bodyInspection = { status: "idle" },
    outlineOpen = false,
    escapeHtml,
    highlightCSharp,
  } = options;
  let content: string;
  if (state.status === "idle" || state.status === "loading") {
    content = `
      <section class="type-explorer-status" role="status">
        <span class="loader" aria-hidden="true"></span>
        <div>
          <h2>Building the complete Type document…</h2>
          <p>Resolving declarations, bodies, and structural identity.</p>
        </div>
      </section>`;
  } else if (state.status === "failed") {
    content = failureHtml(
      "Type Explorer failed",
      state.error,
      escapeHtml,
      true);
  } else {
    content = inspectionHtml(
      state.inspection,
      intent,
      bodyInspection,
      outlineOpen,
      escapeHtml,
      highlightCSharp);
  }
  return `
    <main id="type-explorer" class="type-explorer-route">
      <header class="type-explorer-header">
        <button id="type-explorer-back" type="button"
          aria-label="Back to Type Source">← Type Source</button>
        <div>
          <h1 id="type-explorer-title" tabindex="-1">Type Explorer: ${escapeHtml(typeDisplay)}</h1>
          <p>${escapeHtml(packageDisplay)}</p>
        </div>
      </header>
      ${content}
    </main>`;
}

export function bindTypeExplorerView(
  root: ParentNode,
  state: TypeExplorerViewState,
  actions: TypeExplorerViewActions,
): void {
  root.querySelector<HTMLButtonElement>("#type-explorer-back")
    ?.addEventListener("click", actions.close);
  root.querySelector<HTMLButtonElement>("#type-explorer-retry")
    ?.addEventListener("click", actions.retry);
  root.querySelector<HTMLButtonElement>("#type-explorer-outline-toggle")
    ?.addEventListener("click", actions.toggleOutline);
  for (const control of root.querySelectorAll<HTMLInputElement>(
    'input[name="type-explorer-body"]',
  )) {
    control.addEventListener("change", () => {
      const value = typeExplorerBodyMode(control.value);
      if (control.checked && value !== null)
        actions.selectBodyMode(value);
    });
  }
  for (const control of root.querySelectorAll<HTMLInputElement>(
    'input[name="type-explorer-placement"]',
  )) {
    control.addEventListener("change", () => {
      const value = typeExplorerPlacement(control.value);
      if (control.checked && value !== null)
        actions.selectPlacement(value);
    });
  }
  for (const control of root.querySelectorAll<HTMLInputElement>(
    "[data-type-explorer-accessibility]",
  )) {
    control.addEventListener("change", () => {
      const value = typeExplorerAccessibility(control.value);
      if (value !== null)
        actions.selectAccessibility(value, control.checked);
    });
  }
  bindCheckbox(
    root,
    "#type-explorer-generated",
    actions.setIncludeGenerated);
  bindCheckbox(
    root,
    "#type-explorer-documentation",
    actions.setIncludeDocumentation);
  bindCheckbox(
    root,
    "#type-explorer-attributes",
    actions.setIncludeAttributes);

  const projection = state.status === "ready"
    ? state.inspection.document?.projection ?? null
    : null;
  if (projection === null) return;
  const declarations = new Map(
    projection.declarations.map(declaration =>
      [String(declaration.declarationId), declaration]));
  const bodies = new Map(
    projection.declarations.flatMap(declaration =>
      declaration.bodies.map(body =>
        [String(body.bodyId), { declaration, body }] as const)));
  for (const target of root.querySelectorAll<HTMLButtonElement>(
    "[data-type-explorer-inspect-body]",
  )) {
    target.addEventListener("click", event => {
      event.stopPropagation();
      if (target.dataset.typeExplorerInspectBusy === "true") return;
      const entry = bodies.get(
        target.dataset.typeExplorerInspectBody ?? "");
      if (entry !== undefined)
        actions.inspectBody(entry.declaration, entry.body);
    });
  }
  for (const target of root.querySelectorAll<HTMLElement>(
    "[data-type-explorer-declaration]",
  )) {
    const select = () => {
      const declaration = declarations.get(
        target.dataset.typeExplorerDeclaration ?? "");
      if (declaration !== undefined) {
        actions.selectMember(
          declaration,
          target.closest(".type-explorer-outline") === null
            ? "source"
            : "outline");
      }
    };
    target.addEventListener("click", select);
    target.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      select();
    });
  }
}

export function canceledTypeExplorerView(
  reason: string | null,
): TypeExplorerViewState {
  const error = reason === "worker-restarted"
    ? "The inspection engine restarted while building Type Explorer. Retry to rebuild the Type document."
    : reason === "timeout"
      ? "Type Explorer timed out before the Type document completed. Retry to rebuild it."
      : "Type Explorer was canceled before the Type document completed. Retry to rebuild it.";
  return { status: "failed", error };
}

export function restoreTypeExplorerMemberSelection(
  root: ParentNode,
  projection: TypeExplorerProjection,
  identity: TypeExplorerMemberIdentity,
  pane: TypeExplorerSelectionPane,
): boolean {
  const declaration = projection.declarations.find(candidate =>
    sameIdentity(candidate.identity, identity));
  if (declaration === undefined) return false;
  const selector =
    `[data-type-explorer-declaration="${declaration.declarationId}"]`;
  const outline = root.querySelector<HTMLElement>(
    `.type-explorer-outline ${selector}`);
  const source = root.querySelector<HTMLElement>(
    `.type-explorer-source ${selector}`);
  outline?.scrollIntoView({ block: "nearest" });
  source?.scrollIntoView({ block: "nearest" });
  const target = pane === "outline" ? outline : source;
  if (target === null) return false;
  target.focus({ preventScroll: true });
  return true;
}

export interface TypeExplorerViewportAnchor {
  readonly identity: TypeExplorerMemberIdentity;
  readonly sourceOffsetTop: number | null;
  readonly outlineOffsetTop: number | null;
}

function relativeTop(
  container: HTMLElement | null,
  target: HTMLElement | null,
): number | null {
  if (container === null || target === null) return null;
  return target.getBoundingClientRect().top
    - container.getBoundingClientRect().top;
}

export function captureTypeExplorerViewportAnchor(
  root: ParentNode,
  projection: TypeExplorerProjection,
  declarationId: number,
): TypeExplorerViewportAnchor | null {
  const declaration = projection.declarations.find(candidate =>
    candidate.declarationId === declarationId);
  if (declaration === undefined) return null;
  const selector =
    `[data-type-explorer-declaration="${declaration.declarationId}"]`;
  const outline = root.querySelector<HTMLElement>(".type-explorer-outline");
  const source = root.querySelector<HTMLElement>(
    ".type-explorer-source pre");
  return {
    identity: declaration.identity,
    outlineOffsetTop: relativeTop(
      outline,
      root.querySelector<HTMLElement>(
        `.type-explorer-outline ${selector}`)),
    sourceOffsetTop: relativeTop(
      source,
      root.querySelector<HTMLElement>(
        `.type-explorer-source ${selector}`)),
  };
}

export function restoreTypeExplorerViewportAnchor(
  root: ParentNode,
  projection: TypeExplorerProjection,
  anchor: TypeExplorerViewportAnchor,
): boolean {
  const declaration = projection.declarations.find(candidate =>
    sameIdentity(candidate.identity, anchor.identity));
  if (declaration === undefined) return false;
  const selector =
    `[data-type-explorer-declaration="${declaration.declarationId}"]`;
  let restored = false;
  for (const pane of [
    {
      container: root.querySelector<HTMLElement>(
        ".type-explorer-outline"),
      target: root.querySelector<HTMLElement>(
        `.type-explorer-outline ${selector}`),
      offsetTop: anchor.outlineOffsetTop,
    },
    {
      container: root.querySelector<HTMLElement>(
        ".type-explorer-source pre"),
      target: root.querySelector<HTMLElement>(
        `.type-explorer-source ${selector}`),
      offsetTop: anchor.sourceOffsetTop,
    },
  ]) {
    if (pane.container === null
      || pane.target === null
      || pane.offsetTop === null) {
      continue;
    }
    const currentOffset = relativeTop(pane.container, pane.target);
    if (currentOffset === null) continue;
    pane.container.scrollTop += currentOffset - pane.offsetTop;
    restored = true;
  }
  return restored;
}

function inspectionHtml(
  inspection: TypeExplorerInspection,
  intent: TypeExplorerIntent,
  bodyInspection: TypeExplorerBodyInspectionState,
  outlineOpen: boolean,
  escapeHtml: TypeExplorerViewOptions["escapeHtml"],
  highlightCSharp: TypeExplorerViewOptions["highlightCSharp"],
): string {
  if (inspection.document === null) {
    return failureHtml(
      inspection.outcome === "Rejected"
        ? "Type document rejected"
        : "Type document unavailable",
      inspection.reason ?? "No complete Type document was returned.",
      escapeHtml,
      false);
  }

  const document = inspection.document;
  const projection = document.projection;
  const selectedDeclaration = projection?.declarations.find(declaration =>
    declaration.declarationId === intent.selectedDeclarationId) ?? null;
  const selectedBodyAvailable =
    selectedDeclaration?.supportsSelectedBody === true;
  const incomplete = inspection.outcome === "Incomplete";
  const projectionFailure = document.projectionFailure;
  return `
    <section class="type-explorer-controls" aria-label="Type structure">
      ${radioGroup(
        "View",
        "type-explorer-body",
        [
          ["Bodies", "Bodies", false],
          ["Skeleton", "Skeleton", false],
          [
            "SelectedBody",
            "Selected body",
            intent.selectedDeclarationId === null
              || !selectedBodyAvailable,
          ],
        ],
        intent.bodyMode)}
      ${radioGroup(
        "Members",
        "type-explorer-placement",
        [
          ["All", "All", false],
          ["Instance", "Instance", false],
          ["Static", "Static", false],
        ],
        intent.placement)}
      <fieldset>
        <legend>Access</legend>
        <div class="type-explorer-checks">
          ${ACCESSIBILITIES.map(([value, label]) =>
            checkboxHtml(
              `type-explorer-access-${value}`,
              label,
              intent.accessibilities.includes(value),
              `data-type-explorer-accessibility value="${value}"`))
            .join("")}
        </div>
      </fieldset>
      <fieldset>
        <legend>Include</legend>
        <div class="type-explorer-checks">
          ${checkboxHtml(
            "type-explorer-documentation",
            "XML docs",
            intent.includeDocumentation,
            document.documentationCapability === "Unavailable"
              ? "disabled"
              : "")}
          ${checkboxHtml(
            "type-explorer-attributes",
            "Attributes",
            intent.includeAttributes)}
          ${checkboxHtml(
            "type-explorer-generated",
            "Generated",
            intent.includeGenerated)}
        </div>
      </fieldset>
    </section>
    <div class="type-explorer-provenance">
      <strong>${escapeHtml(document.assemblyName)}</strong>
      <span>Decompiled Type document · ${escapeHtml(document.symbolSource)}</span>
      <span>${incomplete
        ? `${inspection.failedBodyIds.length.toLocaleString()} body projections unavailable`
        : `${inspection.bodyProjectionsAttempted.toLocaleString()} body projections attempted`}</span>
    </div>
    ${incomplete
      ? `<div class="type-explorer-warning" role="status">
          This Type document is incomplete. Unavailable bodies remain explicit
          and are not presented as empty implementations.
        </div>`
      : ""}
    ${projectionFailure
      ? `<div class="type-explorer-failure" role="alert">
          <strong>${escapeHtml(projectionFailure.kind)}</strong>
          <span>${escapeHtml(projectionFailure.message)}</span>
        </div>`
      : ""}
    ${projection === null
      ? ""
      : projectionHtml(
          projection,
          intent.selectedDeclarationId,
          bodyInspection,
          outlineOpen,
          escapeHtml,
          highlightCSharp)}
    ${diagnosticsHtml(inspection, escapeHtml)}`;
}

function projectionHtml(
  projection: TypeExplorerProjection,
  selectedDeclarationId: number | null,
  bodyInspection: TypeExplorerBodyInspectionState,
  outlineOpen: boolean,
  escapeHtml: TypeExplorerViewOptions["escapeHtml"],
  highlightCSharp: TypeExplorerViewOptions["highlightCSharp"],
): string {
  const declarations = projection.declarations;
  const outline = declarations.length === 0
    ? `<p class="type-explorer-empty">This Type has no declarations under the current structural filters.</p>`
    : `<ol class="type-explorer-outline-list">
        ${declarations.map(declaration => {
          const selected =
            declaration.declarationId === selectedDeclarationId;
          return `<li>
            <button type="button"
              data-type-explorer-declaration="${declaration.declarationId}"
              ${selected ? 'aria-current="true"' : ""}
              title="${escapeHtml(declaration.identity.canonicalSignature)}">
              <strong>${escapeHtml(declaration.identity.memberName)}</strong>
              <span>${escapeHtml(declaration.kind)} · ${escapeHtml(
                accessibilityLabel(declaration.accessibility))}</span>
              <span class="type-explorer-outline-signature">${
                escapeHtml(declaration.identity.canonicalSignature)
              }</span>
            </button>
          </li>`;
        }).join("")}
      </ol>`;
  return `
    <button id="type-explorer-outline-toggle"
      class="type-explorer-outline-toggle" type="button"
      aria-expanded="${outlineOpen}"
      aria-controls="type-explorer-outline">${
        outlineOpen ? "Source" : "Members"
      }</button>
    <section class="type-explorer-document${
      outlineOpen ? " outline-open" : ""
    }">
      <aside id="type-explorer-outline"
        class="type-explorer-outline" aria-label="Type members">
        <h2>Members</h2>
        ${outline}
      </aside>
      <section class="type-explorer-source" aria-label="Whole-Type C#">
        <h2 class="sr-only">Whole-Type C#</h2>
        <pre class="language-csharp" tabindex="0"><code class="language-csharp">${
          renderSourceDeclarations(
            projection,
            selectedDeclarationId,
            bodyInspection,
            escapeHtml,
            highlightCSharp)
        }</code></pre>
      </section>
    </section>`;
}

function renderSourceDeclarations(
  projection: TypeExplorerProjection,
  selectedDeclarationId: number | null,
  bodyInspection: TypeExplorerBodyInspectionState,
  escapeHtml: TypeExplorerViewOptions["escapeHtml"],
  highlightCSharp: TypeExplorerViewOptions["highlightCSharp"],
): string {
  let cursor = 0;
  let html = "";
  for (const declaration of projection.declarations) {
    const start = declaration.range.start;
    const end = start + declaration.range.length;
    const selected = declaration.declarationId === selectedDeclarationId;
    html += highlightCSharp(projection.text.slice(cursor, start));
    const bodies = selected
      ? [...declaration.bodies]
          .filter(body =>
            body.destination !== null
            && body.range.start >= start
            && body.range.start + body.range.length <= end)
          .sort((left, right) => left.range.start - right.range.start)
      : [];
    let declarationCursor = start;
    let primarySegment = true;
    for (const body of bodies) {
      if (body.range.start < declarationCursor) continue;
      html += declarationSegmentHtml(
        projection.text.slice(declarationCursor, body.range.start),
        declaration,
        selected,
        primarySegment,
        highlightCSharp);
      if (body.range.start > declarationCursor) primarySegment = false;
      html += inspectBodyHtml(body, bodyInspection, escapeHtml);
      const bodyEnd = body.range.start + body.range.length;
      html += declarationSegmentHtml(
        projection.text.slice(body.range.start, bodyEnd),
        declaration,
        selected,
        primarySegment,
        highlightCSharp);
      primarySegment = false;
      declarationCursor = bodyEnd;
    }
    html += declarationSegmentHtml(
      projection.text.slice(declarationCursor, end),
      declaration,
      selected,
      primarySegment,
      highlightCSharp);
    cursor = end;
  }
  return html + highlightCSharp(projection.text.slice(cursor));
}

function declarationSegmentHtml(
  source: string,
  declaration: TypeExplorerDeclaration,
  selected: boolean,
  primary: boolean,
  highlightCSharp: TypeExplorerViewOptions["highlightCSharp"],
): string {
  if (source.length === 0) return "";
  return `<span class="type-explorer-source-declaration${
    selected ? " selected" : ""
  }"${primary
    ? ` role="button" tabindex="0"${selected
      ? ' aria-current="true"'
      : ""}`
    : ""}
    data-type-explorer-declaration="${declaration.declarationId}">${
      highlightCSharp(source)
    }</span>`;
}

function inspectBodyHtml(
  body: TypeExplorerBody,
  state: TypeExplorerBodyInspectionState,
  escapeHtml: TypeExplorerViewOptions["escapeHtml"],
): string {
  const active = state.status !== "idle" && state.bodyId === body.bodyId;
  const loading = active && state.status === "loading";
  const failure = active && state.status === "failed"
    ? `<span class="type-explorer-body-inspect-error" role="alert">${
        escapeHtml(state.error)
      }</span>`
    : "";
  return `<span class="type-explorer-body-inspect">
    <button type="button"
      data-type-explorer-inspect-body="${body.bodyId}"
      aria-label="${escapeHtml(inspectBodyAccessibleName(body.role))}"${
        loading
          ? ' aria-busy="true" aria-disabled="true" data-type-explorer-inspect-busy="true"'
          : ""
      }>Inspect</button>
    ${loading
      ? '<span class="type-explorer-body-inspect-status" role="status">Inspecting…</span>'
      : failure}
  </span>`;
}

function inspectBodyAccessibleName(role: TypeExplorerBodyRole): string {
  switch (role) {
    case "Method": return "Inspect method body";
    case "Getter": return "Inspect getter body";
    case "Setter": return "Inspect setter body";
    case "Init": return "Inspect init body";
    case "Adder": return "Inspect event subscription body";
    case "Remover": return "Inspect event unsubscription body";
    default: return assertNever(role, "Type Explorer body role");
  }
}

function diagnosticsHtml(
  inspection: TypeExplorerInspection,
  escapeHtml: TypeExplorerViewOptions["escapeHtml"],
): string {
  const messages = [
    ...inspection.diagnostics.map(diagnostic => diagnostic.message),
    ...(inspection.document?.projection?.diagnostics ?? [])
      .map(diagnostic => diagnostic.message),
  ];
  if (messages.length === 0) return "";
  return `<details class="type-explorer-diagnostics">
    <summary>Diagnostics (${messages.length.toLocaleString()})</summary>
    <ul>${messages.map(message =>
      `<li>${escapeHtml(message)}</li>`).join("")}</ul>
  </details>`;
}

function failureHtml(
  heading: string,
  message: string,
  escapeHtml: TypeExplorerViewOptions["escapeHtml"],
  retry: boolean,
): string {
  return `<section class="type-explorer-status type-explorer-status-failed" role="alert">
    <span class="large-glyph" aria-hidden="true">⌁</span>
    <div>
      <h2>${escapeHtml(heading)}</h2>
      <p>${escapeHtml(message)}</p>
      ${retry
        ? '<button id="type-explorer-retry" type="button">Retry</button>'
        : ""}
    </div>
  </section>`;
}

function radioGroup<T extends string>(
  label: string,
  name: string,
  values: readonly (readonly [T, string, boolean])[],
  selected: T,
): string {
  return `<fieldset>
    <legend>${label}</legend>
    <div class="type-explorer-radio-group">
      ${values.map(([value, text, disabled]) =>
        `<label><input type="radio" name="${name}" value="${value}"${
          value === selected ? " checked" : ""
        }${disabled ? " disabled" : ""}><span>${text}</span></label>`)
        .join("")}
    </div>
  </fieldset>`;
}

function checkboxHtml(
  id: string,
  label: string,
  checked: boolean,
  attributes = "",
): string {
  return `<label for="${id}"><input id="${id}" type="checkbox"${
    checked ? " checked" : ""
  } ${attributes}><span>${label}</span></label>`;
}

function bindCheckbox(
  root: ParentNode,
  selector: string,
  action: (included: boolean) => void,
): void {
  root.querySelector<HTMLInputElement>(selector)
    ?.addEventListener("change", event => {
      if (event.currentTarget instanceof HTMLInputElement)
        action(event.currentTarget.checked);
    });
}

function typeExplorerBodyMode(
  value: string,
): TypeExplorerBodyMode | null {
  return value === "Bodies"
    || value === "Skeleton"
    || value === "SelectedBody"
    ? value
    : null;
}

function typeExplorerPlacement(
  value: string,
): TypeExplorerPlacement | null {
  return value === "All" || value === "Instance" || value === "Static"
    ? value
    : null;
}

function typeExplorerAccessibility(
  value: string,
): TypeExplorerAccessibility | null {
  return value === "Unknown"
    || value === "Private"
    || value === "PrivateProtected"
    || value === "Protected"
    || value === "Internal"
    || value === "ProtectedInternal"
    || value === "Public"
    ? value
    : null;
}

function sameIdentity(
  left: TypeExplorerMemberIdentity,
  right: TypeExplorerMemberIdentity | null,
): boolean {
  return right !== null
    && left.stableSelector === right.stableSelector
    && left.canonicalSignature === right.canonicalSignature
    && left.fingerprint === right.fingerprint
    && left.typeFullName === right.typeFullName
    && left.memberName === right.memberName;
}

function accessibilityLabel(value: string): string {
  return ACCESSIBILITIES.find(([candidate]) => candidate === value)?.[1]
    ?? value;
}
