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

function lensButton(
  id: string,
  label: string,
  active: boolean,
  attribute: string,
  index: number,
  escapeHtml: (value: unknown) => string,
): string {
  const escapedLabel = escapeHtml(label);
  return `<button class="lens ${active ? "active" : ""}" ${attribute}="${id}" aria-label="${escapedLabel}" title="${escapedLabel}"><span class="lens-label">${escapedLabel}</span><kbd aria-hidden="true">${index + 1}</kbd></button>`;
}

function scopeSegment(id: string, label: string, active: boolean): string {
  return `<button class="scope-seg ${active ? "active" : ""}" data-scope="${id}" role="tab" aria-selected="${active}">${label}</button>`;
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
    showMemberScope = scope === "member",
    emptyStripLabel = "",
    escapeHtml,
  } = options;
  const stripHtml = strip
    .map(([id, label], i) => lensButton(id, label, activeStripId === id, stripAttribute, i, escapeHtml))
    .join("") || (emptyStripLabel
      ? `<span class="lens-context">${escapeHtml(emptyStripLabel)}</span>`
      : "");
  return `
    <nav class="lensbar" aria-label="Subjects and inspectors">
      <div class="scope-switch" role="tablist" aria-label="Subject">
        ${scopeSegment("workspace", "Workspace", scope === "workspace")}
        ${scopeSegment("package", "Package", scope === "package")}
        ${scopeSegment("type", "Type", scope === "type")}
        ${showMemberScope ? scopeSegment("member", "Member", scope === "member") : ""}
      </div>
      <span class="lens-separator"></span>
      ${stripHtml}
    </nav>`;
}
