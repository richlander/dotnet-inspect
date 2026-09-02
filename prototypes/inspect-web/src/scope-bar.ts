import {
  isMemberSection,
  isPackageLens,
  isTypeLens,
  isWorkspaceScope,
  type MemberSection,
  type PackageLens,
  type TypeLens,
  type WorkspaceScope,
} from "./data.ts";

type LensDefinition<TId extends string = string> = readonly [id: TId, label: string];

// `TId` is inferred from the strip catalog, so the active id has to be a member of the
// strip being rendered. A `string` there let a caller pass an id no button carries, which
// renders a strip with nothing active and no failure anywhere. `NoInfer` keeps the strip
// as the sole inference site; without it TypeScript infers the union of both and a
// mismatched pair type-checks.
export interface RenderScopeBarOptions<TId extends string = string> {
  scope: WorkspaceScope;
  strip: readonly LensDefinition<TId>[];
  activeStripId: NoInfer<TId> | null;
  stripAttribute: string;
  panelId?: string;
  subjectPanelId?: string;
  showMemberScope?: boolean;
  emptyStripLabel?: string;
  escapeHtml: (value: unknown) => string;
}

export interface ScopeBarBindingActions {
  onMemberSectionSelect: (section: MemberSection) => void;
  onPackageLensSelect: (lens: PackageLens) => void;
  onScopeSelect: (scope: WorkspaceScope) => void;
  onTypeLensSelect: (lens: TypeLens) => void;
}

export function bindScopeBar(
  root: ParentNode,
  actions: ScopeBarBindingActions,
) {
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>("[data-subject-tab]"),
  ]);
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>("[data-inspector-tab]"),
  ]);
  root.querySelectorAll<HTMLElement>("[data-scope]").forEach(button =>
    button.addEventListener("click", () => {
      const scope = button.dataset.scope;
      if (isWorkspaceScope(scope)) actions.onScopeSelect(scope);
    }));
  root.querySelectorAll<HTMLElement>("[data-package-lens]").forEach(button =>
    button.addEventListener("click", () => {
      const lens = button.dataset.packageLens;
      if (isPackageLens(lens)) actions.onPackageLensSelect(lens);
    }));
  root.querySelectorAll<HTMLElement>("[data-lens]").forEach(button =>
    button.addEventListener("click", () => {
      const lens = button.dataset.lens;
      if (isTypeLens(lens)) actions.onTypeLensSelect(lens);
    }));
  root.querySelectorAll<HTMLElement>("[data-member-section]").forEach(button =>
    button.addEventListener("click", () => {
      const section = button.dataset.memberSection;
      if (isMemberSection(section)) actions.onMemberSectionSelect(section);
    }));
}

function bindRovingTabs(tabs: readonly HTMLButtonElement[]): void {
  tabs.forEach((tab, index) =>
    tab.addEventListener("keydown", event => {
      const targetIndex = event.key === "ArrowLeft"
        ? (index - 1 + tabs.length) % tabs.length
        : event.key === "ArrowRight"
          ? (index + 1) % tabs.length
          : event.key === "Home"
            ? 0
            : event.key === "End"
              ? tabs.length - 1
              : null;
      if (targetIndex === null) return;
      const target = tabs[targetIndex];
      if (!target) return;
      event.preventDefault();
      tabs.forEach(candidate => {
        candidate.tabIndex = -1;
      });
      target.tabIndex = 0;
      target.focus();
    }));
}

function lensButton(
  id: string,
  label: string,
  active: boolean,
  tabStop: boolean,
  attribute: string,
  index: number,
  panelId: string | undefined,
  escapeHtml: (value: unknown) => string,
): string {
  const escapedLabel = escapeHtml(label);
  const activeAttributes = active
    ? ` id="active-inspector-tab"${panelId ? ` aria-controls="${escapeHtml(panelId)}"` : ""}`
    : "";
  return `<button class="lens ${active ? "active" : ""}" ${attribute}="${id}" data-inspector-tab role="tab" aria-selected="${active}" tabindex="${tabStop ? "0" : "-1"}"${activeAttributes} aria-label="${escapedLabel}" title="${escapedLabel}"><span class="lens-label">${escapedLabel}</span><kbd aria-hidden="true">${index + 1}</kbd></button>`;
}

function scopeSegment(
  id: string,
  label: string,
  active: boolean,
  subjectPanelId: string,
): string {
  const activeAttributes = active ? ' id="active-subject-tab"' : "";
  return `<button class="scope-seg ${active ? "active" : ""}" data-scope="${id}" role="tab" aria-selected="${active}" tabindex="${active ? "0" : "-1"}"${activeAttributes} data-subject-tab aria-controls="${subjectPanelId}">${label}</button>`;
}

// The leading control is the subject ladder. Workspace manages retained coordinates;
// Package, Type, and Member swap in their applicable inspectors. Library joins once its
// product-issued descriptor and behavior are available.
export function renderScopeBar<TId extends string>(
  options: RenderScopeBarOptions<TId>,
): string {
  const {
    scope,
    strip,
    activeStripId,
    stripAttribute,
    panelId,
    subjectPanelId = "subject-panel",
    showMemberScope = scope === "member",
    emptyStripLabel = "",
    escapeHtml,
  } = options;
  const activeIndex = activeStripId === null
    ? -1
    : strip.findIndex(([id]) => id === activeStripId);
  const subjectLabel = scope === "workspace"
    ? "Workspace"
    : scope === "package"
      ? "Package"
      : scope === "type"
        ? "Type"
        : "Member";
  const escapedSubjectPanelId = escapeHtml(subjectPanelId);
  const stripHtml = strip.length > 0
    ? `<div class="inspector-strip" role="tablist" aria-label="${subjectLabel} lenses">${strip
        .map(([id, label], index) => lensButton(
          id,
          label,
          index === activeIndex,
          index === (activeIndex >= 0 ? activeIndex : 0),
          stripAttribute,
          index,
          panelId,
          escapeHtml))
        .join("")}</div>`
    : (emptyStripLabel
        ? `<span class="lens-context">${escapeHtml(emptyStripLabel)}</span>`
        : "");
  return `
    <nav class="lensbar" aria-label="Subjects and inspectors">
      <div class="scope-switch" role="tablist" aria-label="Subject">
        ${scopeSegment("workspace", "Workspace", scope === "workspace", escapedSubjectPanelId)}
        ${scopeSegment("package", "Package", scope === "package", escapedSubjectPanelId)}
        ${scopeSegment("type", "Type", scope === "type", escapedSubjectPanelId)}
        ${showMemberScope ? scopeSegment("member", "Member", scope === "member", escapedSubjectPanelId) : ""}
      </div>
      <span class="lens-separator"></span>
      ${stripHtml}
    </nav>`;
}
