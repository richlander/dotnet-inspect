import {
  isLibraryLens,
  isMemberSection,
  isPackageLens,
  isTypeLens,
  isWorkspaceScope,
  type LibraryLens,
  type MemberSection,
  type PackageLens,
  type TypeLens,
  type WorkspaceScope,
} from "./data.ts";

type LensDefinition<TId extends string = string> = readonly [
  id: TId,
  label: string,
];

export type AdaptiveNavigationForm = "tabs" | "chooser";

export interface AdaptiveNavigationPair {
  subject: AdaptiveNavigationForm;
  inspector: AdaptiveNavigationForm | null;
}

export interface RenderScopeBarOptions<TId extends string = string> {
  scope: WorkspaceScope;
  strip: readonly LensDefinition<TId>[];
  activeStripId: NoInfer<TId> | null;
  stripAttribute: string;
  panelId?: string;
  subjectPanelId?: string;
  availableScopes?: readonly WorkspaceScope[];
  showMemberScope?: boolean;
  emptyStripLabel?: string;
  escapeHtml: (value: unknown) => string;
}

export interface NavigationDescriptorBarItem {
  key: string;
  identity: string | null;
  label: string;
  summary: string | null;
  state: string;
  current: boolean;
  action: string | null;
  localAction: "choose-member" | null;
  evidence: string | null;
}

export interface NavigationLensOutcomeBar {
  status: "Lens unavailable" | "Lens failed";
  evidence: string | null;
}

export interface RenderNavigationDescriptorBarOptions {
  subjects: readonly NavigationDescriptorBarItem[];
  inspectors: readonly NavigationDescriptorBarItem[];
  subjectLabel: string;
  lensOutcome: NavigationLensOutcomeBar | null;
  subjectPanelId?: string;
  inspectorPanelId?: string;
  memberChoicesPanelId?: string;
  escapeHtml: (value: unknown) => string;
}

export interface ScopeBarBindingActions {
  onLibraryLensSelect: (lens: LibraryLens) => void;
  onMemberSectionSelect: (section: MemberSection) => void;
  onPackageLensSelect: (lens: PackageLens) => void;
  onScopeSelect: (scope: WorkspaceScope) => void;
  onTypeLensSelect: (lens: TypeLens) => void;
}

type NavigationGroupName = "subject" | "inspector";
type NavigationItemPresentation = "tab" | "menuitem";

export type ScopeBarFocusTarget =
  | {
      kind: "navigation-trigger";
      value: NavigationGroupName;
    }
  | {
      kind: "library-lens";
      value: LibraryLens;
      presentation?: NavigationItemPresentation;
    }
  | {
      kind: "member-section";
      value: MemberSection;
      presentation?: NavigationItemPresentation;
    }
  | {
      kind: "package-lens";
      value: PackageLens;
      presentation?: NavigationItemPresentation;
    }
  | {
      kind: "scope";
      value: WorkspaceScope;
      presentation?: NavigationItemPresentation;
    }
  | {
      kind: "type-lens";
      value: TypeLens;
      presentation?: NavigationItemPresentation;
    }
  | {
      kind: "product-navigation";
      value: string;
      group: NavigationGroupName;
      presentation?: NavigationItemPresentation;
    };

interface AdaptiveNavigationGroupState {
  key: string;
  open: boolean;
  focusedId: string | null;
}

export interface ScopeBarState {
  subject: AdaptiveNavigationGroupState;
  inspector: AdaptiveNavigationGroupState;
}

export interface ScopeBarBinding {
  disconnect(): void;
  revealFocusTarget(target: ScopeBarFocusTarget): void;
  restoreOpenMenuFocus(): boolean;
}

interface AdaptiveNavigationGroup {
  name: NavigationGroupName;
  element: HTMLElement;
  tabs: HTMLElement;
  trigger: HTMLButtonElement;
  menu: HTMLElement;
  tabItems: readonly HTMLButtonElement[];
  menuItems: readonly HTMLButtonElement[];
  committedId: string | null;
  state: AdaptiveNavigationGroupState;
  tabsWidth: number;
  chooserWidth: number;
  frameWidth: number;
  form: AdaptiveNavigationForm;
}

function isNavigationGroupName(
  value: string | null | undefined,
): value is NavigationGroupName {
  return value === "subject" || value === "inspector";
}

function itemPresentation(
  element: HTMLElement,
): NavigationItemPresentation | undefined {
  const value = element.dataset.navigationItem;
  return value === "tab" || value === "menuitem" ? value : undefined;
}

export function createScopeBarState(): ScopeBarState {
  return {
    subject: { key: "", open: false, focusedId: null },
    inspector: { key: "", open: false, focusedId: null },
  };
}

export function selectAdaptiveNavigationPair(options: {
  availableWidth: number;
  separatorAndGapsWidth: number;
  subjectTabsWidth: number;
  subjectChooserWidth: number;
  subjectCount: number;
  subjectCommitted: boolean;
  inspectorTabsWidth?: number;
  inspectorChooserWidth?: number;
  inspectorCount?: number;
  inspectorCommitted?: boolean;
  pinnedChooser?: NavigationGroupName | null;
}): AdaptiveNavigationPair {
  const {
    availableWidth: navigationWidth,
    separatorAndGapsWidth,
    subjectTabsWidth,
    subjectChooserWidth,
    subjectCount,
    subjectCommitted,
    inspectorTabsWidth,
    inspectorChooserWidth,
    inspectorCount = 0,
    inspectorCommitted = false,
    pinnedChooser = null,
  } = options;
  if (inspectorTabsWidth === undefined
    || inspectorChooserWidth === undefined) {
    return {
      subject: pinnedChooser === "subject"
        || subjectTabsWidth > navigationWidth
        ? "chooser"
        : "tabs",
      inspector: null,
    };
  }

  const fits = (subjectWidth: number, inspectorWidth: number) =>
    subjectWidth + inspectorWidth + separatorAndGapsWidth <= navigationWidth;
  if (pinnedChooser === "subject") {
    return {
      subject: "chooser",
      inspector: fits(subjectChooserWidth, inspectorTabsWidth)
        ? "tabs"
        : "chooser",
    };
  }
  if (pinnedChooser === "inspector") {
    return {
      subject: fits(subjectTabsWidth, inspectorChooserWidth)
        ? "tabs"
        : "chooser",
      inspector: "chooser",
    };
  }
  if (fits(subjectTabsWidth, inspectorTabsWidth)) {
    return { subject: "tabs", inspector: "tabs" };
  }

  const subjectTabsFit = fits(subjectTabsWidth, inspectorChooserWidth);
  const inspectorTabsFit = fits(subjectChooserWidth, inspectorTabsWidth);
  if (subjectTabsFit || inspectorTabsFit) {
    if (subjectTabsFit && inspectorTabsFit) {
      const subjectGain = subjectCount - (subjectCommitted ? 1 : 0);
      const inspectorGain = inspectorCount - (inspectorCommitted ? 1 : 0);
      return subjectGain >= inspectorGain
        ? { subject: "tabs", inspector: "chooser" }
        : { subject: "chooser", inspector: "tabs" };
    }
    return subjectTabsFit
      ? { subject: "tabs", inspector: "chooser" }
      : { subject: "chooser", inspector: "tabs" };
  }
  return { subject: "chooser", inspector: "chooser" };
}

export function captureScopeBarFocus(
  element: HTMLElement,
): ScopeBarFocusTarget | null {
  const trigger = element.dataset.navigationTrigger;
  if (isNavigationGroupName(trigger)) {
    return { kind: "navigation-trigger", value: trigger };
  }

  const presentation = itemPresentation(element);
  const productNavigation = element.dataset.navigationId;
  const productGroup = element.dataset.navigationGroup;
  if (element.dataset.productNavigationItem !== undefined
    && productNavigation !== undefined
    && isNavigationGroupName(productGroup)) {
    return {
      kind: "product-navigation",
      value: productNavigation,
      group: productGroup,
      ...(presentation ? { presentation } : {}),
    };
  }
  const scope = element.dataset.scope;
  if (isWorkspaceScope(scope)) {
    return { kind: "scope", value: scope, ...(presentation ? { presentation } : {}) };
  }

  const packageLens = element.dataset.packageLens;
  if (isPackageLens(packageLens)) {
    return {
      kind: "package-lens",
      value: packageLens,
      ...(presentation ? { presentation } : {}),
    };
  }

  const libraryLens = element.dataset.libraryLens;
  if (isLibraryLens(libraryLens)) {
    return {
      kind: "library-lens",
      value: libraryLens,
      ...(presentation ? { presentation } : {}),
    };
  }

  const typeLens = element.dataset.lens;
  if (isTypeLens(typeLens)) {
    return {
      kind: "type-lens",
      value: typeLens,
      ...(presentation ? { presentation } : {}),
    };
  }

  const memberSection = element.dataset.memberSection;
  return isMemberSection(memberSection)
    ? {
        kind: "member-section",
        value: memberSection,
        ...(presentation ? { presentation } : {}),
      }
    : null;
}

export function focusRenderedElement(
  element: HTMLElement | null,
  options?: FocusOptions,
): boolean {
  if (!element || element.hidden) return false;
  const visible = typeof element.checkVisibility === "function"
    ? element.checkVisibility()
    : element.getClientRects().length > 0;
  if (!visible) return false;
  element.focus(options);
  return true;
}

function focusTargetSelector(
  target: Exclude<ScopeBarFocusTarget, { kind: "navigation-trigger" }>,
): [selector: string, value: string] {
  return target.kind === "scope"
    ? ["[data-scope]", target.value]
      : target.kind === "package-lens"
        ? ["[data-package-lens]", target.value]
        : target.kind === "library-lens"
          ? ["[data-library-lens]", target.value]
          : target.kind === "type-lens"
            ? ["[data-lens]", target.value]
            : target.kind === "member-section"
              ? ["[data-member-section]", target.value]
              : [
                  `[data-product-navigation-item][data-navigation-group="${target.group}"]`,
                  target.value,
                ];
}

function elementIdentity(element: HTMLElement): string | undefined {
  return element.dataset.scope
    ?? element.dataset.packageLens
    ?? element.dataset.libraryLens
    ?? element.dataset.lens
    ?? element.dataset.memberSection
    ?? element.dataset.navigationId;
}

export function restoreScopeBarFocus(
  root: ParentNode,
  target: ScopeBarFocusTarget,
): boolean {
  if (target.kind === "navigation-trigger") {
    const trigger = root.querySelector<HTMLElement>(
      `[data-navigation-trigger="${target.value}"]`);
    if (focusRenderedElement(trigger)) return true;
    const fallback = root.querySelector<HTMLElement>(
      `[data-navigation-group="${target.value}"] `
      + '[data-navigation-item="tab"][tabindex="0"]');
    return focusRenderedElement(fallback);
  }

  const [selector, value] = focusTargetSelector(target);
  const candidates = [...root.querySelectorAll<HTMLElement>(selector)]
    .filter(element => elementIdentity(element) === value);
  const preferred = "presentation" in target && target.presentation
    ? candidates.find(element =>
        itemPresentation(element) === target.presentation
        && !element.hidden
        && !element.closest<HTMLElement>("[hidden]"))
    : undefined;
  const replacement = preferred ?? candidates.find(element =>
    !element.hidden && !element.closest<HTMLElement>("[hidden]"));
  if (replacement && itemPresentation(replacement) === "tab") {
    const group = replacement.closest<HTMLElement>("[data-navigation-group]");
    group?.querySelectorAll<HTMLElement>('[data-navigation-item="tab"]')
      .forEach(tab => {
        tab.tabIndex = tab === replacement ? 0 : -1;
      });
  }
  if (replacement && focusRenderedElement(replacement)) return true;
  const group = (replacement ?? candidates[0])
    ?.closest<HTMLElement>("[data-navigation-group]");
  const trigger = group?.querySelector<HTMLElement>(
    "[data-navigation-trigger]");
  return focusRenderedElement(trigger ?? null);
}

function bindRovingTabs(tabs: readonly HTMLButtonElement[]): void {
  tabs.forEach((tab, index) => {
    tab.addEventListener("focus", () => {
      tabs.forEach(candidate => {
        candidate.tabIndex = candidate === tab ? 0 : -1;
      });
    });
    tab.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        const id = groupItemId(tab);
        tab.click();
        if (id && tab.dataset.localNavigationAction === undefined) {
          queueMicrotask(() => {
            const replacement = tab.ownerDocument.querySelector<HTMLElement>(
              `[data-navigation-item="tab"][data-navigation-id="${CSS.escape(id)}"]`);
            focusRenderedElement(replacement, { preventScroll: true });
          });
        }
        return;
      }
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
      target.focus();
    });
  });
}

function navigationItemIsDisabled(item: HTMLButtonElement): boolean {
  return item.disabled || item.ariaDisabled === "true";
}

function bindItemActions(
  root: ParentNode,
  actions: ScopeBarBindingActions,
): void {
  root.querySelectorAll<HTMLButtonElement>("[data-scope]").forEach(button =>
    button.addEventListener("click", () => {
      if (button.dataset.navigationCurrent === "true"
        || navigationItemIsDisabled(button)) return;
      const scope = button.dataset.scope;
      if (isWorkspaceScope(scope)) actions.onScopeSelect(scope);
    }));
  root.querySelectorAll<HTMLButtonElement>("[data-package-lens]").forEach(
    button => button.addEventListener("click", () => {
      if (button.dataset.navigationCurrent === "true"
        || navigationItemIsDisabled(button)) return;
      const lens = button.dataset.packageLens;
      if (isPackageLens(lens)) actions.onPackageLensSelect(lens);
    }));
  root.querySelectorAll<HTMLButtonElement>("[data-library-lens]").forEach(
    button => button.addEventListener("click", () => {
      if (button.dataset.navigationCurrent === "true"
        || navigationItemIsDisabled(button)) return;
      const lens = button.dataset.libraryLens;
      if (isLibraryLens(lens)) actions.onLibraryLensSelect(lens);
    }));
  root.querySelectorAll<HTMLButtonElement>("[data-lens]").forEach(button =>
    button.addEventListener("click", () => {
      if (button.dataset.navigationCurrent === "true"
        || navigationItemIsDisabled(button)) return;
      const lens = button.dataset.lens;
      if (isTypeLens(lens)) actions.onTypeLensSelect(lens);
    }));
  root.querySelectorAll<HTMLButtonElement>("[data-member-section]").forEach(
    button => button.addEventListener("click", () => {
      if (button.dataset.navigationCurrent === "true"
        || navigationItemIsDisabled(button)) return;
      const section = button.dataset.memberSection;
      if (isMemberSection(section)) actions.onMemberSectionSelect(section);
    }));
}

export function bindScopeBar(
  root: ParentNode,
  actions: ScopeBarBindingActions,
  state?: ScopeBarState,
): ScopeBarBinding {
  const controller = ScopeBarController.create(
    root,
    state ?? createScopeBarState());
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>("[data-subject-tab]"),
  ]);
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>("[data-inspector-tab]"),
  ]);
  bindItemActions(root, actions);
  return {
    disconnect() {
      controller?.disconnect();
    },
    revealFocusTarget(target) {
      controller?.revealFocusTarget(target);
    },
    restoreOpenMenuFocus() {
      return controller?.restoreOpenMenuFocus() ?? false;
    },
  };
}

function subjectDefinitions(
  showMemberScope: boolean,
  platform: boolean,
): readonly (readonly [Exclude<WorkspaceScope, "workspace">, string])[] {
  return [
    platform ? ["platform", "Platform"] : ["package", "Package"],
    ["library", "Library"],
    ["type", "Type"],
    ...(showMemberScope
      ? [["member", "Member"] as const]
      : []),
  ];
}

function itemAttributes(
  id: string,
  attribute: string,
  current: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  return [
    `${attribute}="${escapeHtml(id)}"`,
    `data-navigation-id="${escapeHtml(id)}"`,
    `data-navigation-current="${current}"`,
  ].join(" ");
}

function tabButton(
  definition: LensDefinition,
  current: boolean,
  tabStop: boolean,
  attribute: string,
  panelId: string | undefined,
  escapeHtml: (value: unknown) => string,
): string {
  const [id, label] = definition;
  const escapedLabel = escapeHtml(label);
  const activeAttributes = current
    ? ` id="active-inspector-tab"${panelId
      ? ` aria-controls="${escapeHtml(panelId)}"`
      : ""}`
    : "";
  return `<button type="button" class="adaptive-navigation-tab lens ${current ? "active" : ""}" ${itemAttributes(id, attribute, current, escapeHtml)} data-navigation-item="tab" data-inspector-tab role="tab" aria-selected="${current}" tabindex="${tabStop ? "0" : "-1"}"${activeAttributes} aria-label="${escapedLabel}" title="${escapedLabel}"><span class="lens-label">${escapedLabel}</span></button>`;
}

function subjectTab(
  id: Exclude<WorkspaceScope, "workspace">,
  label: string,
  current: boolean,
  tabStop: boolean,
  subjectPanelId: string,
  escapeHtml: (value: unknown) => string,
): string {
  const escapedLabel = escapeHtml(label);
  return `<button type="button" class="adaptive-navigation-tab scope-seg ${current ? "active" : ""}" ${itemAttributes(id, "data-scope", current, escapeHtml)} data-navigation-item="tab" data-subject-tab role="tab" aria-selected="${current}" tabindex="${tabStop ? "0" : "-1"}"${current ? ' id="active-subject-tab"' : ""} aria-controls="${escapeHtml(subjectPanelId)}" aria-label="${escapedLabel}" title="${escapedLabel}"><span>${escapedLabel}</span></button>`;
}

function menuItem(
  id: string,
  label: string,
  current: boolean,
  attribute: string,
  escapeHtml: (value: unknown) => string,
): string {
  const escapedLabel = escapeHtml(label);
  return `<button type="button" class="adaptive-navigation-menu-item ${current ? "active" : ""}" ${itemAttributes(id, attribute, current, escapeHtml)} data-navigation-item="menuitem" role="menuitemradio" aria-checked="${current}" tabindex="-1" aria-label="${escapedLabel}" title="${escapedLabel}">${escapedLabel}</button>`;
}

function descriptorStateLabel(state: string): string {
  const normalized = state.toLowerCase();
  return normalized === "available"
      || normalized === "current"
      || normalized === "selectionrequired"
    ? ""
    : state;
}

function descriptorAttributes(
  item: NavigationDescriptorBarItem,
  escapeHtml: (value: unknown) => string,
  group?: NavigationGroupName,
): string {
  const action = item.action === null
    ? ""
    : ` data-product-navigation-action="${escapeHtml(item.action)}"`;
  const localAction = item.localAction === null
    ? ""
    : ` data-local-navigation-action="${item.localAction}"`;
  const identity = item.identity === null
    ? ""
    : ` data-product-navigation-id="${escapeHtml(item.identity)}"`;
  const groupAttribute = group
    ? ` data-navigation-group="${group}"`
    : "";
  return `data-product-navigation-item${groupAttribute} data-navigation-id="${escapeHtml(item.key)}"${identity} data-navigation-current="${item.current}" data-navigation-state="${escapeHtml(item.state)}"${action}${localAction}`;
}

function descriptorLabel(
  item: NavigationDescriptorBarItem,
  escapeHtml: (value: unknown) => string,
): string {
  const status = descriptorStateLabel(item.state);
  const evidence = item.evidence
    ? `${status ? ": " : ""}${item.evidence}`
    : "";
  return `${escapeHtml(item.label)}${status || evidence
    ? `<span class="navigation-status"> ${escapeHtml(status + evidence)}</span>`
    : ""}`;
}

function descriptorAccessibleLabel(
  item: NavigationDescriptorBarItem,
  includeSummary: boolean,
): string {
  const description = [
    includeSummary ? item.summary : null,
    descriptorStateLabel(item.state),
    item.evidence,
  ].filter(value => value).join(". ");
  return description ? `${item.label}: ${description}` : item.label;
}

interface DescriptorDescription {
  readonly id: string;
  readonly text: string;
}

function descriptorDescription(
  items: readonly NavigationDescriptorBarItem[],
  item: NavigationDescriptorBarItem,
  index: number,
  group: NavigationGroupName,
  presentation: NavigationItemPresentation,
): DescriptorDescription | null {
  const sameTitle = items.filter(candidate => candidate.label === item.label);
  if (sameTitle.length < 2) return null;

  const sameSummary = sameTitle.filter(candidate =>
    candidate.summary === item.summary);
  const identity = sameSummary.length > 1
    ? item.identity ?? item.key
    : null;
  const text = [item.summary, identity].filter(value => value).join(" · ");
  return text
    ? {
        id: `${group}-${presentation}-${index}-navigation-description`,
        text,
      }
    : null;
}

function descriptorDescriptionAttributes(
  description: DescriptorDescription | null,
  escapeHtml: (value: unknown) => string,
): string {
  return description
    ? ` aria-describedby="${escapeHtml(description.id)}"`
    : "";
}

function descriptorDescriptionElement(
  description: DescriptorDescription | null,
  escapeHtml: (value: unknown) => string,
): string {
  return description
    ? `<span id="${escapeHtml(description.id)}" class="navigation-item-description">${escapeHtml(description.text)}</span>`
    : "";
}

function descriptorTab(
  item: NavigationDescriptorBarItem,
  tabStop: boolean,
  panelId: string,
  memberChoicesPanelId: string,
  group: NavigationGroupName,
  description: DescriptorDescription | null,
  escapeHtml: (value: unknown) => string,
): string {
  const label = descriptorLabel(item, escapeHtml);
  const accessibleLabel = descriptorAccessibleLabel(item, description === null);
  const disabled = item.action === null
    && item.localAction === null
    && !item.current;
  const controls = item.localAction === "choose-member"
    ? memberChoicesPanelId
    : group === "inspector" && !item.current
      ? null
      : panelId;
  const current = group === "subject" && item.current
    ? ' aria-current="page"'
    : "";
  const title = description
    ? `${item.label}: ${description.text}`
    : accessibleLabel;
  return `<button type="button" class="adaptive-navigation-tab ${group === "subject" ? "scope-seg" : "lens"} ${item.current ? "active" : ""}" ${descriptorAttributes(item, escapeHtml, group)} data-navigation-item="tab" ${group === "subject" ? "data-subject-tab" : "data-inspector-tab"} role="tab" aria-selected="${item.current}" aria-disabled="${disabled}" tabindex="${tabStop ? "0" : "-1"}"${item.current ? ` id="active-${group}-tab"` : ""}${current}${controls ? ` aria-controls="${escapeHtml(controls)}"` : ""} aria-label="${escapeHtml(accessibleLabel)}"${descriptorDescriptionAttributes(description, escapeHtml)} title="${escapeHtml(title)}"><span${group === "inspector" ? ' class="lens-label"' : ""}>${label}</span>${descriptorDescriptionElement(description, escapeHtml)}</button>`;
}

function descriptorMenuItem(
  item: NavigationDescriptorBarItem,
  memberChoicesPanelId: string,
  group: NavigationGroupName,
  description: DescriptorDescription | null,
  escapeHtml: (value: unknown) => string,
): string {
  const accessibleLabel = descriptorAccessibleLabel(item, description === null);
  const selectionRequired = item.localAction === "choose-member";
  const disabled = item.action === null
    && !selectionRequired
    && !item.current;
  const role = selectionRequired ? "menuitem" : "menuitemradio";
  const checked = selectionRequired
    ? ""
    : ` aria-checked="${item.current}"`;
  const controls = selectionRequired
    ? ` aria-controls="${escapeHtml(memberChoicesPanelId)}"`
    : "";
  const current = group === "subject" && item.current
    ? ' aria-current="page"'
    : "";
  const title = description
    ? `${item.label}: ${description.text}`
    : accessibleLabel;
  return `<button type="button" class="adaptive-navigation-menu-item ${item.current ? "active" : ""}" ${descriptorAttributes(item, escapeHtml, group)} data-navigation-item="menuitem" role="${role}"${checked}${current} aria-disabled="${disabled}"${controls} tabindex="-1" aria-label="${escapeHtml(accessibleLabel)}"${descriptorDescriptionAttributes(description, escapeHtml)} title="${escapeHtml(title)}">${descriptorLabel(item, escapeHtml)}${descriptorDescriptionElement(description, escapeHtml)}</button>`;
}

function navigationGroup(options: {
  name: NavigationGroupName;
  label: string;
  chooserLabel: string;
  key: string;
  committedId: string | null;
  panelId?: string;
  tabHtml: string;
  menuHtml: string;
  escapeHtml: (value: unknown) => string;
}): string {
  const {
    name,
    label,
    chooserLabel,
    key,
    committedId,
    panelId,
    tabHtml,
    menuHtml,
    escapeHtml,
  } = options;
  const menuId = `${name}-navigation-menu`;
  const triggerId = name === "inspector"
    ? "active-inspector-chooser"
    : "active-subject-chooser";
  const escapedChooserLabel = escapeHtml(chooserLabel);
  return `
    <div class="adaptive-navigation-group adaptive-navigation-${name}${name === "subject" ? " scope-switch" : " inspector-strip"}"
         data-navigation-group="${name}"
         data-navigation-key="${escapeHtml(key)}"
         data-committed-id="${escapeHtml(committedId ?? "")}"
         ${panelId ? `data-panel-id="${escapeHtml(panelId)}"` : ""}>
      <div class="adaptive-navigation-tabs"
           data-navigation-tabs
           role="tablist"
           aria-label="${escapeHtml(label)}">
        ${tabHtml}
      </div>
      <button id="${triggerId}"
              type="button"
              class="adaptive-navigation-trigger"
              data-navigation-trigger="${name}"
              aria-label="${escapedChooserLabel}"
              title="${escapedChooserLabel}"
              aria-haspopup="menu"
              aria-expanded="false"
              aria-controls="${menuId}"
              hidden>
        <span>${escapedChooserLabel}</span><span aria-hidden="true">▾</span>
      </button>
      <div id="${menuId}"
           class="adaptive-navigation-menu"
           data-navigation-menu="${name}"
           role="menu"
           aria-label="${escapeHtml(label)}"
           popover="manual">
        ${menuHtml}
      </div>
    </div>`;
}

export function renderNavigationDescriptorBar(
  options: RenderNavigationDescriptorBarOptions,
): string {
  const {
    subjects,
    inspectors,
    subjectLabel,
    lensOutcome,
    subjectPanelId = "subject-panel",
    inspectorPanelId = "inspector-panel",
    memberChoicesPanelId = "content-navigation-pane",
    escapeHtml,
  } = options;
  const renderGroup = (
    name: NavigationGroupName,
    items: readonly NavigationDescriptorBarItem[],
    panelId: string,
  ) => {
    if (items.length === 0) return "";
    const current = items.find(item => item.current) ?? null;
    const fallback = current ?? items[0]!;
    return navigationGroup({
      name,
      label: name === "subject" ? "Subjects" : `${subjectLabel} lenses`,
      chooserLabel: current?.label
        ?? (name === "subject" ? "Choose subject" : "Choose inspector"),
      key: items.map(item => item.key).join(","),
      committedId: current?.key ?? null,
      panelId,
      tabHtml: items.map((item, index) =>
        descriptorTab(
          item,
          item.key === fallback.key,
          panelId,
          memberChoicesPanelId,
          name,
          descriptorDescription(items, item, index, name, "tab"),
          escapeHtml)).join(""),
      menuHtml: items.map((item, index) =>
        descriptorMenuItem(
          item,
          memberChoicesPanelId,
          name,
          descriptorDescription(items, item, index, name, "menuitem"),
          escapeHtml)).join(""),
      escapeHtml,
    });
  };

  const subjectHtml = renderGroup("subject", subjects, subjectPanelId);
  const inspectorHtml = renderGroup(
    "inspector",
    inspectors,
    inspectorPanelId);
  const lensOutcomeHtml = lensOutcome
    ? `<span class="lens-context navigation-lens-outcome" role="status" aria-label="${escapeHtml(`${subjectLabel}: ${lensOutcome.status}${lensOutcome.evidence ? `: ${lensOutcome.evidence}` : ""}`)}">${escapeHtml(lensOutcome.status)}${lensOutcome.evidence ? `: ${escapeHtml(lensOutcome.evidence)}` : ""}</span>`
    : "";
  if (!subjectHtml && !inspectorHtml && !lensOutcomeHtml) return "";
  const hasTrailingContent = Boolean(inspectorHtml || lensOutcomeHtml);
  return `<nav class="lensbar" data-scope-bar aria-label="Subjects and inspectors">${subjectHtml}${subjectHtml && hasTrailingContent ? '<span class="lens-separator" aria-hidden="true"></span>' : ""}${inspectorHtml}${lensOutcomeHtml}</nav>`;
}

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
    availableScopes,
    showMemberScope = scope === "member",
    emptyStripLabel = "",
    escapeHtml,
  } = options;
  const activeIndex = activeStripId === null
    ? -1
    : strip.findIndex(([id]) => id === activeStripId);
  const subjectLabel = scope === "workspace"
    ? "Workspace"
    : scope === "platform"
      ? "Platform"
      : scope === "package"
        ? "Package"
        : scope === "library"
          ? "Library"
        : scope === "type"
          ? "Type"
          : "Member";
  const subjects = subjectDefinitions(
    showMemberScope,
    availableScopes?.includes("platform") ?? scope === "platform",
  )
    .filter(([id]) => availableScopes?.includes(id) ?? true);
  if (subjects.length === 0 && strip.length === 0) return "";
  const subjectCommitted = subjects.some(([id]) => id === scope)
    ? scope
    : null;
  const subjectFallback = subjectCommitted
    ?? subjects[0]?.[0]
    ?? null;
  const subjectHtml = subjects.length > 0
    ? navigationGroup({
        name: "subject",
        label: "Subjects",
        chooserLabel: subjectCommitted
          ? subjects.find(([id]) => id === subjectCommitted)?.[1]
            ?? "Choose subject"
          : "Choose subject",
        key: subjects.map(([id]) => id).join(","),
        committedId: subjectCommitted,
        panelId: subjectPanelId,
        tabHtml: subjects.map(([id, label]) => subjectTab(
          id,
          label,
          id === subjectCommitted,
          id === subjectFallback,
          subjectPanelId,
          escapeHtml)).join(""),
        menuHtml: subjects.map(([id, label]) => menuItem(
          id,
          label,
          id === subjectCommitted,
          "data-scope",
          escapeHtml)).join(""),
        escapeHtml,
      })
    : "";
  const inspectorHtml = strip.length > 0
    ? navigationGroup({
        name: "inspector",
        label: `${subjectLabel} lenses`,
        chooserLabel: activeIndex >= 0
          ? strip[activeIndex]?.[1] ?? "Choose inspector"
          : "Choose inspector",
        key: `${scope}:${strip.map(([id]) => id).join(",")}`,
        committedId: activeIndex >= 0 ? activeStripId : null,
        ...(panelId ? { panelId } : {}),
        tabHtml: strip.map((definition, index) => tabButton(
          definition,
          index === activeIndex,
          index === (activeIndex >= 0 ? activeIndex : 0),
          stripAttribute,
          panelId,
          escapeHtml)).join(""),
        menuHtml: strip.map(([id, label], index) => menuItem(
          id,
          label,
          index === activeIndex,
          stripAttribute,
          escapeHtml)).join(""),
        escapeHtml,
      })
    : (emptyStripLabel
        ? `<span class="lens-context">${escapeHtml(emptyStripLabel)}</span>`
        : "");
  return `
    <nav class="lensbar"
         data-scope-bar
         data-active-scope="${escapeHtml(scope)}"
         aria-label="Subjects and inspectors">
      ${subjectHtml}
      ${strip.length > 0
        ? '<span class="lens-separator" aria-hidden="true"></span>'
        : ""}
      ${inspectorHtml}
    </nav>`;
}

function outerWidth(element: HTMLElement): number {
  const style = getComputedStyle(element);
  return element.getBoundingClientRect().width
    + Number.parseFloat(style.marginLeft || "0")
    + Number.parseFloat(style.marginRight || "0");
}

function measureHidden(element: HTMLElement): number {
  const hidden = element.hidden;
  element.hidden = false;
  element.dataset.navigationMeasuring = "true";
  const width = outerWidth(element);
  delete element.dataset.navigationMeasuring;
  element.hidden = hidden;
  return width;
}

function availableWidth(navigation: HTMLElement): number {
  const style = getComputedStyle(navigation);
  return Math.max(
    0,
    navigation.clientWidth
    - Number.parseFloat(style.paddingLeft || "0")
    - Number.parseFloat(style.paddingRight || "0"));
}

function horizontalFrameWidth(element: HTMLElement): number {
  const style = getComputedStyle(element);
  return Number.parseFloat(style.paddingLeft || "0")
    + Number.parseFloat(style.paddingRight || "0")
    + Number.parseFloat(style.borderLeftWidth || "0")
    + Number.parseFloat(style.borderRightWidth || "0");
}

function navigationGap(navigation: HTMLElement): number {
  const style = getComputedStyle(navigation);
  return Number.parseFloat(style.columnGap || style.gap || "0");
}

function groupItemId(item: HTMLElement | null): string | null {
  return item?.dataset.navigationId ?? null;
}

function committedOrFirst(
  group: AdaptiveNavigationGroup,
  items: readonly HTMLButtonElement[],
): HTMLButtonElement | null {
  return items.find(item =>
    groupItemId(item) === group.committedId)
    ?? items[0]
    ?? null;
}

function syncGroupState(
  groupElement: HTMLElement,
  state: AdaptiveNavigationGroupState,
): void {
  const key = groupElement.dataset.navigationKey ?? "";
  if (state.key === key) return;
  state.key = key;
  state.open = false;
  state.focusedId = null;
}

function readGroup(
  navigation: HTMLElement,
  name: NavigationGroupName,
  state: AdaptiveNavigationGroupState,
): AdaptiveNavigationGroup | null {
  const element = navigation.querySelector<HTMLElement>(
    `[data-navigation-group="${name}"]`);
  if (!element) return null;
  syncGroupState(element, state);
  const tabs = element.querySelector<HTMLElement>("[data-navigation-tabs]");
  const trigger = element.querySelector<HTMLButtonElement>(
    `[data-navigation-trigger="${name}"]`);
  const menu = element.querySelector<HTMLElement>(
    `[data-navigation-menu="${name}"]`);
  if (!tabs || !trigger || !menu) {
    throw new Error(`Adaptive ${name} navigation markup is incomplete.`);
  }
  return {
    name,
    element,
    tabs,
    trigger,
    menu,
    tabItems: [...tabs.querySelectorAll<HTMLButtonElement>(
      '[data-navigation-item="tab"]')],
    menuItems: [...menu.querySelectorAll<HTMLButtonElement>(
      '[data-navigation-item="menuitem"]')],
    committedId: element.dataset.committedId || null,
    state,
    tabsWidth: 0,
    chooserWidth: 0,
    frameWidth: 0,
    form: "tabs",
  };
}

function showPopover(element: HTMLElement): void {
  if ("showPopover" in element
    && typeof element.showPopover === "function") {
    try {
      element.showPopover();
    } catch (error) {
      if (!(error instanceof DOMException && error.name === "InvalidStateError")) {
        throw error;
      }
    }
  }
}

function hidePopover(element: HTMLElement): void {
  if ("hidePopover" in element
    && typeof element.hidePopover === "function") {
    try {
      element.hidePopover();
    } catch (error) {
      if (!(error instanceof DOMException && error.name === "InvalidStateError")) {
        throw error;
      }
    }
  }
}

class ScopeBarController implements ScopeBarBinding {
  private readonly root: ParentNode;
  private readonly navigation: HTMLElement;
  private readonly subject: AdaptiveNavigationGroup | null;
  private readonly inspector: AdaptiveNavigationGroup | null;
  private readonly separator: HTMLElement | null;
  private readonly observer: ResizeObserver | null;
  private readonly modalObserver: MutationObserver | null;
  private modalActive: boolean;
  private cancelDeferredPointerLayout: (() => void) | null = null;
  private cancelDeferredModalRestore: (() => void) | null = null;
  private readonly handleDocumentKeyDown = (event: KeyboardEvent) => {
    const group = this.subject?.state.open
      ? this.subject
      : this.inspector?.state.open
        ? this.inspector
        : null;
    if (!group) return;
    const eventTarget = event.target;
    if (!(eventTarget instanceof Node)
      || (!group.element.contains(eventTarget)
        && !group.menu.contains(eventTarget))) {
      return;
    }
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopImmediatePropagation();
      this.closeMenu(group, true);
      return;
    }
    if (event.key !== "Tab") return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const target = this.adjacentToTrigger(group.trigger, event.shiftKey);
    this.closeMenu(group, false, false);
    target?.focus();
    this.layout();
  };
  private readonly handleDocumentPointerDown = (event: PointerEvent) => {
    const target = event.target;
    if (!(target instanceof Node)) return;
    if (this.hasActiveModal()) return;
    const group = this.subject?.state.open
      ? this.subject
      : this.inspector?.state.open
        ? this.inspector
        : null;
    if (!group
      || group.element.contains(target)
      || group.menu.contains(target)) return;
    this.closeMenu(group, false, false);
    this.deferLayoutUntilPointerActivation();
  };
  private readonly handleViewportResize = () => {
    this.positionOpenMenu();
  };

  static create(
    root: ParentNode,
    state: ScopeBarState,
  ): ScopeBarController | null {
    const navigation = root.querySelector<HTMLElement>("[data-scope-bar]");
    return navigation ? new ScopeBarController(root, navigation, state) : null;
  }

  private constructor(
    root: ParentNode,
    navigation: HTMLElement,
    state: ScopeBarState,
  ) {
    this.root = root;
    this.navigation = navigation;
    this.subject = readGroup(navigation, "subject", state.subject);
    this.inspector = readGroup(navigation, "inspector", state.inspector);
    this.separator = navigation.querySelector(".lens-separator");
    this.modalActive = this.hasActiveModal();
    navigation.ownerDocument.addEventListener(
      "keydown",
      this.handleDocumentKeyDown,
      true);
    navigation.ownerDocument.addEventListener(
      "pointerdown",
      this.handleDocumentPointerDown,
      true);
    navigation.ownerDocument.defaultView?.addEventListener(
      "resize",
      this.handleViewportResize);
    navigation.ownerDocument.defaultView?.visualViewport?.addEventListener(
      "resize",
      this.handleViewportResize);
    navigation.ownerDocument.defaultView?.visualViewport?.addEventListener(
      "scroll",
      this.handleViewportResize);
    this.bindGroup(this.subject);
    this.bindGroup(this.inspector);
    this.layout();
    this.restoreOpenMenus();
    this.observer = typeof ResizeObserver === "undefined"
      ? null
      : new ResizeObserver(() => {
          this.layout();
          this.positionOpenMenu();
        });
    this.observer?.observe(navigation);
    this.modalObserver = typeof MutationObserver === "undefined"
      ? null
      : new MutationObserver(() => {
          const modalActive = this.hasActiveModal();
          if (modalActive === this.modalActive) return;
          this.modalActive = modalActive;
          if (!modalActive) this.deferOpenMenuRestoration();
        });
    this.modalObserver?.observe(
      navigation.ownerDocument.body
        ?? navigation.ownerDocument.documentElement,
      {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["hidden"],
      });
    const fonts = navigation.ownerDocument.fonts;
    void fonts?.ready.then(() => this.layout());
  }

  disconnect(): void {
    this.observer?.disconnect();
    this.modalObserver?.disconnect();
    this.navigation.ownerDocument.removeEventListener(
      "keydown",
      this.handleDocumentKeyDown,
      true);
    this.navigation.ownerDocument.removeEventListener(
      "pointerdown",
      this.handleDocumentPointerDown,
      true);
    this.navigation.ownerDocument.defaultView?.removeEventListener(
      "resize",
      this.handleViewportResize);
    this.navigation.ownerDocument.defaultView?.visualViewport
      ?.removeEventListener("resize", this.handleViewportResize);
    this.navigation.ownerDocument.defaultView?.visualViewport
      ?.removeEventListener("scroll", this.handleViewportResize);
    this.cancelDeferredPointerLayout?.();
    this.cancelDeferredModalRestore?.();
    if (this.subject) hidePopover(this.subject.menu);
    if (this.inspector) hidePopover(this.inspector.menu);
  }

  private hasActiveModal(): boolean {
    return this.navigation.ownerDocument
      .querySelector(':not([hidden]) > [role="dialog"][aria-modal="true"]')
      !== null;
  }

  private deferOpenMenuRestoration(): void {
    this.cancelDeferredModalRestore?.();
    const view = this.navigation.ownerDocument.defaultView;
    if (!view) {
      this.restoreOpenMenus();
      return;
    }
    const timer = view.setTimeout(() => {
      this.cancelDeferredModalRestore = null;
      if (!this.hasActiveModal()) this.restoreOpenMenus();
    }, 0);
    this.cancelDeferredModalRestore = () => {
      view.clearTimeout(timer);
      this.cancelDeferredModalRestore = null;
    };
  }

  revealFocusTarget(target: ScopeBarFocusTarget): void {
    const groupName = target.kind === "navigation-trigger"
      ? target.value
      : target.kind === "scope"
        ? "subject"
        : target.kind === "product-navigation"
            ? target.group
            : "inspector";
    const group = groupName === "subject" ? this.subject : this.inspector;
    if (!group) return;
    if (target.kind === "navigation-trigger") {
      if (group.form === "tabs") {
        committedOrFirst(group, group.tabItems)?.focus();
      }
      return;
    }
    if ("presentation" in target && target.presentation === "menuitem") {
      group.state.open = true;
      this.layout();
      this.openMenu(group, false);
    }
  }

  restoreOpenMenuFocus(): boolean {
    if (this.hasActiveModal()) return false;
    const group = this.subject?.state.open
      ? this.subject
      : this.inspector?.state.open
        ? this.inspector
        : null;
    if (!group) return false;
    if (!group.menu.matches(":popover-open")) {
      showPopover(group.menu);
      this.positionMenu(group);
    }
    const target = group.state.focusedId
      ? group.menuItems.find(item =>
          groupItemId(item) === group.state.focusedId)
      : committedOrFirst(group, group.menuItems);
    if (!target) return false;
    target.focus({ preventScroll: true });
    return group.menu.ownerDocument.activeElement === target;
  }

  private bindGroup(group: AdaptiveNavigationGroup | null): void {
    if (!group) return;
    group.trigger.addEventListener("click", () => {
      if (group.state.open) {
        this.closeMenu(group, true);
      } else {
        this.openMenu(group, true);
      }
    });
    group.trigger.addEventListener("keydown", event => {
      if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
      event.preventDefault();
      this.openMenu(group, true, event.key === "ArrowUp");
    });
    group.menuItems.forEach(item => {
      item.addEventListener("focus", () => {
        group.state.focusedId = groupItemId(item);
      });
      item.addEventListener("click", () => {
        if (navigationItemIsDisabled(item)) return;
        const activates = item.dataset.navigationCurrent !== "true";
        const localAction =
          item.dataset.localNavigationAction !== undefined;
        this.closeMenu(group, !activates);
        if (!activates || localAction) return;
        const id = groupItemId(item);
        const target = id
          ? group.tabItems.find(tab => groupItemId(tab) === id)
          : null;
        if (group.form === "tabs" && target) {
          target.focus({ preventScroll: true });
        } else {
          group.trigger.focus({ preventScroll: true });
        }
      });
      item.addEventListener("keydown", event => {
        this.handleMenuKey(group, item, event);
      });
    });
  }

  private handleMenuKey(
    group: AdaptiveNavigationGroup,
    item: HTMLButtonElement,
    event: KeyboardEvent,
  ): void {
    const index = group.menuItems.indexOf(item);
    const targetIndex = event.key === "ArrowUp"
      ? (index - 1 + group.menuItems.length) % group.menuItems.length
      : event.key === "ArrowDown"
        ? (index + 1) % group.menuItems.length
        : event.key === "Home"
          ? 0
          : event.key === "End"
            ? group.menuItems.length - 1
            : null;
    if (targetIndex !== null) {
      event.preventDefault();
      group.menuItems[targetIndex]?.focus();
      return;
    }
    if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
      event.preventDefault();
      return;
    }
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      item.click();
      return;
    }
    if (event.key.length !== 1 || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    event.preventDefault();
    const prefix = event.key.toLocaleLowerCase();
    const candidates = [
      ...group.menuItems.slice(index + 1),
      ...group.menuItems.slice(0, index + 1),
    ];
    const target = candidates.find(candidate =>
      (candidate.textContent ?? "").trim().toLocaleLowerCase()
        .startsWith(prefix));
    if (target) {
      target.focus();
    }
  }

  private adjacentToTrigger(
    trigger: HTMLButtonElement,
    backwards: boolean,
  ): HTMLElement | undefined {
    const documentRoot = trigger.ownerDocument;
    const candidates = [...documentRoot.querySelectorAll<HTMLElement>(
      'a[href], button, input, select, textarea, [tabindex]')]
      .filter(candidate => candidate.tabIndex >= 0
        && !candidate.hidden
        && !candidate.closest<HTMLElement>("[hidden]")
        && candidate.getAttribute("aria-disabled") !== "true"
        && !(candidate instanceof HTMLButtonElement && candidate.disabled)
        && typeof candidate.checkVisibility === "function"
        && candidate.checkVisibility());
    const index = candidates.indexOf(trigger);
    return backwards ? candidates[index - 1] : candidates[index + 1];
  }

  private deferLayoutUntilPointerActivation(): void {
    this.cancelDeferredPointerLayout?.();
    const document = this.navigation.ownerDocument;
    const view = document.defaultView;
    let timer: number | null = null;
    const cleanup = () => {
      document.removeEventListener("click", finish);
      document.removeEventListener("pointerup", deferFinish, true);
      document.removeEventListener("pointercancel", finish, true);
      if (timer !== null) view?.clearTimeout(timer);
      if (this.cancelDeferredPointerLayout === cleanup) {
        this.cancelDeferredPointerLayout = null;
      }
    };
    const finish = () => {
      cleanup();
      this.layout();
    };
    const deferFinish = () => {
      if (!view) {
        finish();
        return;
      }
      timer = view.setTimeout(finish, 0);
    };
    document.addEventListener("click", finish);
    document.addEventListener("pointerup", deferFinish, true);
    document.addEventListener("pointercancel", finish, true);
    this.cancelDeferredPointerLayout = cleanup;
  }

  private restoreOpenMenus(): void {
    for (const group of [this.subject, this.inspector]) {
      if (!group?.state.open) continue;
      group.trigger.setAttribute("aria-expanded", "true");
      showPopover(group.menu);
      this.positionMenu(group);
    }
  }

  private openMenu(
    group: AdaptiveNavigationGroup,
    focusItem: boolean,
    focusLast = false,
  ): void {
    const peer = group.name === "subject" ? this.inspector : this.subject;
    if (peer?.state.open) this.closeMenu(peer, false);
    group.state.open = true;
    group.trigger.setAttribute("aria-expanded", "true");
    this.layout();
    showPopover(group.menu);
    this.positionMenu(group);
    if (!focusItem) return;
    const target = group.state.focusedId
      ? group.menuItems.find(item =>
          groupItemId(item) === group.state.focusedId)
      : focusLast
        ? group.menuItems.at(-1)
        : committedOrFirst(group, group.menuItems);
    (target ?? committedOrFirst(group, group.menuItems))?.focus();
  }

  private closeMenu(
    group: AdaptiveNavigationGroup,
    returnFocus: boolean,
    relayout = true,
  ): void {
    hidePopover(group.menu);
    group.state.open = false;
    group.state.focusedId = null;
    group.trigger.setAttribute("aria-expanded", "false");
    if (returnFocus) group.trigger.focus();
    if (relayout) this.layout();
  }

  private measure(group: AdaptiveNavigationGroup): void {
    group.frameWidth = horizontalFrameWidth(group.element);
    group.tabsWidth = measureHidden(group.tabs) + group.frameWidth;
    group.chooserWidth = measureHidden(group.trigger) + group.frameWidth;
  }

  private layout(): void {
    if (!this.subject) return;
    this.measure(this.subject);
    if (this.inspector) this.measure(this.inspector);
    const gap = navigationGap(this.navigation);
    const separatorAndGapsWidth = this.inspector
      ? (this.separator ? outerWidth(this.separator) : 0) + gap * 2
      : 0;
    const width = availableWidth(this.navigation);
    const pinnedChooser = this.subject.state.open
      ? "subject"
      : this.inspector?.state.open
        ? "inspector"
        : null;
    const pair = selectAdaptiveNavigationPair({
      availableWidth: width,
      separatorAndGapsWidth,
      subjectTabsWidth: this.subject.tabsWidth,
      subjectChooserWidth: this.subject.chooserWidth,
      subjectCount: this.subject.tabItems.length,
      subjectCommitted: this.subject.committedId !== null,
      ...(this.inspector
        ? {
            inspectorTabsWidth: this.inspector.tabsWidth,
            inspectorChooserWidth: this.inspector.chooserWidth,
            inspectorCount: this.inspector.tabItems.length,
            inspectorCommitted: this.inspector.committedId !== null,
          }
        : {}),
      pinnedChooser,
    });
    this.applyForms(pair, width, separatorAndGapsWidth);
  }

  private applyForms(
    pair: AdaptiveNavigationPair,
    width: number,
    overhead: number,
  ): void {
    if (!this.subject) return;
    this.applyForm(this.subject, pair.subject);
    if (!this.inspector || pair.inspector === null) {
      this.subject.element.style.width = `${Math.min(
        width,
        pair.subject === "tabs"
          ? this.subject.tabsWidth
          : this.subject.chooserWidth)}px`;
      this.navigation.dataset.subjectForm = pair.subject;
      delete this.navigation.dataset.inspectorForm;
      return;
    }
    this.applyForm(this.inspector, pair.inspector);
    const internalWidth = Math.max(0, width - overhead);
    const subjectIdeal = pair.subject === "tabs"
      ? this.subject.tabsWidth
      : this.subject.chooserWidth;
    const inspectorIdeal = pair.inspector === "tabs"
      ? this.inspector.tabsWidth
      : this.inspector.chooserWidth;
    if (pair.subject === "chooser"
      && pair.inspector === "chooser"
      && subjectIdeal + inspectorIdeal > internalWidth) {
      const subjectWidth = Math.floor(internalWidth / 2);
      this.subject.element.style.width = `${subjectWidth}px`;
      this.inspector.element.style.width = `${internalWidth - subjectWidth}px`;
    } else {
      this.subject.element.style.width = `${subjectIdeal}px`;
      this.inspector.element.style.width = `${inspectorIdeal}px`;
    }
    this.navigation.dataset.subjectForm = pair.subject;
    this.navigation.dataset.inspectorForm = pair.inspector;
    this.updatePanelSemantics(this.subject);
    this.updatePanelSemantics(this.inspector);
  }

  private applyForm(
    group: AdaptiveNavigationGroup,
    form: AdaptiveNavigationForm,
  ): void {
    const active = group.element.ownerDocument.activeElement;
    const transferToTrigger = group.form !== form
      && form === "chooser"
      && group.tabs.contains(active);
    const transferToTabs = group.form !== form
      && form === "tabs"
      && active === group.trigger
      && !group.state.open;
    group.form = form;
    group.tabs.hidden = form !== "tabs";
    group.trigger.hidden = form !== "chooser";
    if (transferToTrigger) {
      group.trigger.focus({ preventScroll: true });
    } else if (transferToTabs) {
      const target = committedOrFirst(group, group.tabItems);
      if (target) {
        group.tabItems.forEach(tab => {
          tab.tabIndex = tab === target ? 0 : -1;
        });
        target.focus({ preventScroll: true });
      }
    }
    if (form === "tabs" && group.state.open) {
      this.closeMenu(group, false);
    }
    this.updatePanelSemantics(group);
  }

  private updatePanelSemantics(group: AdaptiveNavigationGroup): void {
    const panelId = group.element.dataset.panelId;
    if (!panelId || !group.committedId) return;
    const panel = this.root.querySelector<HTMLElement>(`#${CSS.escape(panelId)}`);
    if (!panel) return;
    const labelOwner = group.form === "tabs"
      ? group.tabItems.find(item =>
          groupItemId(item) === group.committedId)
      : group.trigger;
    if (!labelOwner?.id) return;
    panel.setAttribute(
      "role",
      group.form === "tabs" ? "tabpanel" : "region");
    panel.setAttribute("aria-labelledby", labelOwner.id);
  }

  private positionOpenMenu(): void {
    if (this.subject?.state.open) this.positionMenu(this.subject);
    if (this.inspector?.state.open) this.positionMenu(this.inspector);
  }

  private positionMenu(group: AdaptiveNavigationGroup): void {
    const bounds = group.trigger.getBoundingClientRect();
    const document = group.trigger.ownerDocument;
    const documentViewport = document.documentElement;
    const visualViewport = document.defaultView?.visualViewport;
    const viewportLeft = visualViewport?.offsetLeft ?? 0;
    const viewportTop = visualViewport?.offsetTop ?? 0;
    const viewportWidth = visualViewport?.width ?? documentViewport.clientWidth;
    const viewportHeight =
      visualViewport?.height ?? documentViewport.clientHeight;
    const margin = 8;
    const gap = 4;
    const usableTop = viewportTop + margin;
    const usableBottom = Math.max(
      usableTop,
      viewportTop + viewportHeight - margin);
    const availableMenuWidth = Math.max(
      0,
      viewportWidth - margin * 2);
    const menuWidth = Math.min(
      Math.max(bounds.width, 180),
      availableMenuWidth);
    const maxLeft = Math.max(
      viewportLeft + margin,
      viewportLeft + viewportWidth - margin - menuWidth);
    const left = Math.min(
      Math.max(viewportLeft + margin, bounds.left),
      maxLeft);
    const belowTop = Math.min(
      Math.max(bounds.bottom + gap, usableTop),
      usableBottom);
    const aboveBottom = Math.min(
      Math.max(bounds.top - gap, usableTop),
      usableBottom);
    const availableBelow = Math.max(0, usableBottom - belowTop);
    const availableAbove = Math.max(0, aboveBottom - usableTop);
    const naturalMenuHeight = group.menu.scrollHeight;
    const placeBelow = availableBelow >= naturalMenuHeight
      || (availableAbove < naturalMenuHeight
        && availableBelow >= availableAbove);
    const availableHeight = placeBelow ? availableBelow : availableAbove;
    group.menu.style.left = `${left}px`;
    group.menu.style.width = `${menuWidth}px`;
    group.menu.style.maxWidth = `${availableMenuWidth}px`;
    group.menu.style.maxHeight = `${availableHeight}px`;
    const menuHeight = group.menu.offsetHeight;
    const desiredTop = placeBelow ? belowTop : aboveBottom - menuHeight;
    const maximumTop = Math.max(usableTop, usableBottom - menuHeight);
    const top = Math.min(
      Math.max(desiredTop, usableTop),
      maximumTop);
    group.menu.style.top = `${top}px`;
  }
}
