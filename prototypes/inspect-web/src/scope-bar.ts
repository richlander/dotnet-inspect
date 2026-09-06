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
import {
  SlideStripDomController,
  type SlideStripAppliedResult,
  type SlideStripContinuityState,
  type SlideStripResolveIntent,
} from "./slide-strip-dom.ts";
import type {
  SlideStripItem,
  SlideStripPolicy,
  SlideStripResult,
} from "./slide-strip.ts";

type LensDefinition<TId extends string = string> = readonly [
  id: TId,
  label: string,
  shortLabel?: string,
  icon?: string,
];

export type ApplicationScope = "query" | "workspace";

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

export interface ScopeBarBindingActions {
  onApplicationScopeSelect: (scope: ApplicationScope) => void;
  onLibraryLensSelect: (lens: LibraryLens) => void;
  onMemberSectionSelect: (section: MemberSection) => void;
  onPackageLensSelect: (lens: PackageLens) => void;
  onScopeSelect: (scope: WorkspaceScope) => void;
  onTypeLensSelect: (lens: TypeLens) => void;
}

export interface ApplicationScopeBarBindingActions {
  onApplicationScopeSelect: (scope: ApplicationScope) => void;
  onFocusedControlUnavailable?: () => void;
}

export interface ApplicationScopeBarBinding {
  disconnect(): void;
}

export type ScopeBarFocusTarget =
  | { kind: "application-scope"; value: ApplicationScope }
  | { kind: "library-lens"; value: LibraryLens }
  | { kind: "member-section"; value: MemberSection }
  | { kind: "package-lens"; value: PackageLens }
  | { kind: "scope"; value: WorkspaceScope }
  | { kind: "type-lens"; value: TypeLens };

export interface ScopeBarState {
  subject: SlideStripContinuityState;
  inspector: SlideStripContinuityState;
  allocationKey: string;
  allocationOrdinal: number;
}

export interface ScopeBarBinding {
  disconnect(): void;
  revealFocusTarget(target: ScopeBarFocusTarget): void;
}

interface AllocationPair {
  subject: SlideStripAppliedResult;
  inspector: SlideStripAppliedResult;
}

interface AllocationFocusTransfer {
  strip: "subject" | "inspector";
  id: string;
}

function focusTransferIntent(
  transfer: AllocationFocusTransfer | null,
  strip: AllocationFocusTransfer["strip"],
): SlideStripResolveIntent {
  return transfer?.strip === strip
    ? { pendingFocusId: transfer.id }
    : {};
}

export function createScopeBarState(): ScopeBarState {
  return {
    subject: { key: "" },
    inspector: { key: "" },
    allocationKey: "",
    allocationOrdinal: 0,
  };
}

export function clampAllocationOrdinal(
  requested: number,
  levelCount: number,
): number {
  return Math.max(0, Math.min(requested, levelCount - 1));
}

export function scopeBarShortLabel(label: string): string {
  return label
    .trim()
    .split(/\s+/)
    .map(word => word[0] ?? "")
    .join("")
    .toUpperCase();
}

function isApplicationScope(
  value: string | null | undefined,
): value is ApplicationScope {
  return value === "query" || value === "workspace";
}

export function captureScopeBarFocus(
  element: HTMLElement,
): ScopeBarFocusTarget | null {
  const applicationScope = element.dataset.applicationScope;
  if (isApplicationScope(applicationScope)) {
    return { kind: "application-scope", value: applicationScope };
  }

  const scope = element.dataset.scope;
  if (isWorkspaceScope(scope)) return { kind: "scope", value: scope };

  const packageLens = element.dataset.packageLens;
  if (isPackageLens(packageLens)) {
    return { kind: "package-lens", value: packageLens };
  }

  const libraryLens = element.dataset.libraryLens;
  if (isLibraryLens(libraryLens)) {
    return { kind: "library-lens", value: libraryLens };
  }

  const typeLens = element.dataset.lens;
  if (isTypeLens(typeLens)) return { kind: "type-lens", value: typeLens };

  const memberSection = element.dataset.memberSection;
  return isMemberSection(memberSection)
    ? { kind: "member-section", value: memberSection }
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

export function restoreScopeBarFocus(
  root: ParentNode,
  target: ScopeBarFocusTarget,
): boolean {
  const [selector, value] = target.kind === "application-scope"
    ? ["[data-application-scope]", target.value]
    : target.kind === "scope"
      ? ["[data-scope]", target.value]
      : target.kind === "package-lens"
        ? ["[data-package-lens]", target.value]
        : target.kind === "library-lens"
          ? ["[data-library-lens]", target.value]
        : target.kind === "type-lens"
          ? ["[data-lens]", target.value]
          : ["[data-member-section]", target.value];
  const tabs = [...root.querySelectorAll<HTMLElement>(selector)];
  const replacement = tabs.find(element => (
      element.dataset.applicationScope
      ?? element.dataset.scope
      ?? element.dataset.packageLens
      ?? element.dataset.libraryLens
      ?? element.dataset.lens
      ?? element.dataset.memberSection
    ) === value);
  if (!replacement || replacement.hidden) return false;
  tabs.forEach(tab => {
    tab.tabIndex = tab === replacement ? 0 : -1;
  });
  return focusRenderedElement(replacement);
}

function targetIdentity(target: ScopeBarFocusTarget): string {
  return target.value;
}

function targetStrip(target: ScopeBarFocusTarget): "subject" | "inspector" {
  return target.kind === "scope" ? "subject" : "inspector";
}

function bindRovingTabs(
  tabs: readonly HTMLButtonElement[],
  reveal: (target: HTMLButtonElement) => void,
  activateOnNavigation = false,
): void {
  tabs.forEach((tab, index) =>
    tab.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        tab.click();
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
      reveal(target);
      tabs.forEach(candidate => {
        candidate.tabIndex = -1;
      });
      target.tabIndex = 0;
      target.focus();
      if (activateOnNavigation) target.click();
    }));
}

export function bindScopeBar(
  root: ParentNode,
  actions: ScopeBarBindingActions,
  state?: ScopeBarState,
): ScopeBarBinding {
  const controller = state
    ? ScopeBarController.create(root, state)
    : null;
  const applicationBinding = bindApplicationScopeBar(root, {
    onApplicationScopeSelect: actions.onApplicationScopeSelect,
    onFocusedControlUnavailable: () => {
      root.querySelector<HTMLElement>(".brand")
        ?.focus({ preventScroll: true });
    },
  });
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>(
      "[data-subject-tab]"),
  ], target => controller?.reveal(target), true);
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>("[data-inspector-tab]"),
  ], target => controller?.reveal(target));
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
  root.querySelectorAll<HTMLElement>("[data-library-lens]").forEach(button =>
    button.addEventListener("click", () => {
      const lens = button.dataset.libraryLens;
      if (isLibraryLens(lens)) actions.onLibraryLensSelect(lens);
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
  return {
    disconnect() {
      applicationBinding.disconnect();
      controller?.disconnect();
    },
    revealFocusTarget(target) {
      controller?.revealFocusTarget(target);
    },
  };
}

export function bindApplicationScopeBar(
  root: ParentNode,
  actions: ApplicationScopeBarBindingActions,
): ApplicationScopeBarBinding {
  bindRovingTabs([
    ...root.querySelectorAll<HTMLButtonElement>(
      "[data-application-scope-tab]:not([disabled])"),
  ], () => {});
  root.querySelectorAll<HTMLElement>("[data-application-scope]").forEach(
    button => button.addEventListener("click", () => {
      const applicationScope = button.dataset.applicationScope;
      if (isApplicationScope(applicationScope)) {
        actions.onApplicationScopeSelect(applicationScope);
      }
    }));
  const region = actions.onFocusedControlUnavailable
    && typeof ResizeObserver !== "undefined"
    ? root.querySelector<HTMLElement>(".application-scope-region")
    : null;
  const observer = region
    ? new ResizeObserver(() => {
        const focused = region.querySelector<HTMLElement>(
          "[data-application-scope]:focus");
        if (!focused) return;
        if (!applicationScopeMustYield(region)
          && fullyRenderedWithin(focused, region)) return;
        actions.onFocusedControlUnavailable?.();
      })
    : null;
  if (region) {
    observer?.observe(region);
    observer?.observe(region.ownerDocument.documentElement);
  }
  return {
    disconnect() {
      observer?.disconnect();
    },
  };
}

function applicationScopeMustYield(region: HTMLElement): boolean {
  return region.ownerDocument.defaultView
    ?.getComputedStyle(region)
    .getPropertyValue("--application-scope-yield")
    .trim() === "1";
}

function fullyRenderedWithin(
  element: HTMLElement,
  clippingRegion: HTMLElement,
): boolean {
  const elementBounds = element.getBoundingClientRect();
  const regionBounds = clippingRegion.getBoundingClientRect();
  const viewport = element.ownerDocument.documentElement;
  return elementBounds.width > 0
    && elementBounds.height > 0
    && elementBounds.left >= Math.max(regionBounds.left, 0)
    && elementBounds.right <= Math.min(regionBounds.right, viewport.clientWidth)
    && elementBounds.top >= Math.max(regionBounds.top, 0)
    && elementBounds.bottom <= Math.min(
      regionBounds.bottom,
      viewport.clientHeight);
}

function presentationHtml(
  label: string,
  shortLabel: string | undefined,
  icon: string | undefined,
  index: number,
  escapeHtml: (value: unknown) => string,
): string {
  return [
    `<span class="slide-strip-label lens-label" data-slide-strip-representation="label">${escapeHtml(label)}</span>`,
    shortLabel
      ? `<span class="slide-strip-short-label" data-slide-strip-representation="short-label">${escapeHtml(shortLabel)}</span>`
      : "",
    icon
      ? `<span class="slide-strip-icon" data-slide-strip-representation="icon" aria-hidden="true">${escapeHtml(icon)}</span>`
      : "",
    `<kbd data-slide-strip-representation="index" aria-hidden="true">${index + 1}</kbd>`,
  ].join("");
}

function itemData(
  id: string,
  label: string,
  shortLabel: string | undefined,
  icon: string | undefined,
  escapeHtml: (value: unknown) => string,
): string {
  return [
    `data-slide-strip-id="${escapeHtml(id)}"`,
    `data-slide-strip-label="${escapeHtml(label)}"`,
    shortLabel
      ? `data-slide-strip-short-label="${escapeHtml(shortLabel)}"`
      : "",
    icon ? `data-slide-strip-icon="${escapeHtml(icon)}"` : "",
  ].filter(Boolean).join(" ");
}

function lensButton(
  definition: LensDefinition,
  active: boolean,
  tabStop: boolean,
  attribute: string,
  index: number,
  panelId: string | undefined,
  escapeHtml: (value: unknown) => string,
): string {
  const [id, label, shortLabel, icon] = definition;
  const escapedLabel = escapeHtml(label);
  const activeAttributes = active
    ? ` id="active-inspector-tab"${panelId ? ` aria-controls="${escapeHtml(panelId)}"` : ""}`
    : "";
  return `<button class="slide-strip-item lens ${active ? "active" : ""}" ${attribute}="${escapeHtml(id)}" ${itemData(id, label, shortLabel, icon, escapeHtml)} data-inspector-tab role="tab" aria-selected="${active}" tabindex="${tabStop ? "0" : "-1"}"${activeAttributes} aria-label="${escapedLabel}" title="${escapedLabel}">${presentationHtml(label, shortLabel, icon, index, escapeHtml)}</button>`;
}

function scopeSegment(
  id: Exclude<WorkspaceScope, "workspace">,
  label: string,
  active: boolean,
  tabStop: boolean,
  subjectPanelId: string,
  index: number,
  escapeHtml: (value: unknown) => string,
): string {
  const activeAttributes = active ? ' id="active-subject-tab"' : "";
  return `<button class="slide-strip-item scope-seg ${active ? "active" : ""}" data-scope="${id}" ${itemData(id, label, undefined, undefined, escapeHtml)} role="tab" aria-selected="${active}" tabindex="${tabStop ? "0" : "-1"}"${activeAttributes} data-subject-tab aria-controls="${subjectPanelId}" aria-label="${escapeHtml(label)}" title="${escapeHtml(label)}">${presentationHtml(label, undefined, undefined, index, escapeHtml)}</button>`;
}

function edgeIndicators(): string {
  return `
    <span class="slide-strip-edge before" data-slide-strip-before hidden aria-hidden="true"></span>
    <span class="slide-strip-edge after" data-slide-strip-after hidden aria-hidden="true"></span>`;
}

function subjectDefinitions(
  showMemberScope: boolean,
): readonly (readonly [Exclude<WorkspaceScope, "workspace">, string])[] {
  return [
    ["package", "Package"],
    ["library", "Library"],
    ["type", "Type"],
    ...(showMemberScope
      ? [["member", "Member"] as const]
      : []),
  ];
}

export function renderApplicationScopeBar(
  activeScope: ApplicationScope | null,
  workspaceAvailable: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  const scopes = [
    ["query", "Query"],
    ["workspace", "Workspace"],
  ] as const;
  return `
    <nav class="application-scope-strip"
         data-application-scope-strip
         aria-label="Application scopes">
      ${scopes.map(([id, label]) => {
        const active = activeScope === id;
        const disabled = id === "workspace" && !workspaceAvailable;
        const tabStop = active
          || (activeScope === null && id === "query");
        return `<button id="application-scope-${id}" type="button" class="application-scope-item ${active ? "active" : ""}" data-application-scope="${id}" data-application-scope-tab${active ? ' aria-current="page"' : ""} tabindex="${tabStop ? "0" : "-1"}"${disabled ? " disabled" : ""} aria-label="${escapeHtml(label)}" title="${escapeHtml(disabled ? "No workspace is open" : label)}">${escapeHtml(label)}</button>`;
      }).join("")}
    </nav>`;
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
    : scope === "package"
      ? "Package"
      : scope === "library"
        ? "Library"
      : scope === "type"
        ? "Type"
        : "Member";
  const subjects = subjectDefinitions(showMemberScope)
    .filter(([id]) => availableScopes?.includes(id) ?? true);
  const subjectIds = subjects.map(([id]) => id).join(",");
  const inspectorIds = strip.map(([id]) => id).join(",");
  const inspectorAnchor = activeIndex >= 0
    ? activeStripId
    : strip[0]?.[0] ?? "";
  const subjectAnchor = subjects.some(([id]) => id === scope)
    ? scope
    : scope === "workspace"
      ? subjects[0]?.[0] ?? ""
      : subjects.at(-1)?.[0] ?? "";
  const escapedSubjectPanelId = escapeHtml(subjectPanelId);
  const inspectorHtml = strip.length > 0
    ? `
      <div class="slide-strip slide-strip-inspector inspector-strip"
           data-slide-strip="inspector"
           data-continuity-key="${escapeHtml(`${scope}:${inspectorIds}:v1`)}"
           data-initial-anchor="${escapeHtml(inspectorAnchor)}"
           role="tablist"
           aria-label="${subjectLabel} lenses">
        <div class="slide-strip-items">
          ${strip.map((definition, index) => lensButton(
            definition,
            index === activeIndex,
            index === (activeIndex >= 0 ? activeIndex : 0),
            stripAttribute,
            index,
            panelId,
            escapeHtml)).join("")}
        </div>
        ${edgeIndicators()}
      </div>`
    : (emptyStripLabel
        ? `<span class="lens-context">${escapeHtml(emptyStripLabel)}</span>`
        : "");
  return `
    <nav class="lensbar"
         data-scope-bar
         data-allocation-key="${escapeHtml(`${scope}:${inspectorIds}`)}"
         aria-label="Subjects and inspectors">
      <div class="slide-strip slide-strip-subject scope-switch"
           data-slide-strip="subject"
           data-continuity-key="${escapeHtml(`${subjectIds}:v1`)}"
           data-initial-anchor="${subjectAnchor}"
           role="tablist"
           aria-label="Subject">
        <div class="slide-strip-items">
          ${subjects.map(([id, label], index) =>
            scopeSegment(
              id,
              label,
              scope === id,
              id === subjectAnchor,
              escapedSubjectPanelId,
              index,
              escapeHtml)).join("")}
        </div>
        ${edgeIndicators()}
      </div>
      ${strip.length > 0
        ? `<div class="slide-strip-allocation" data-slide-strip-allocation hidden>
            <button type="button" data-more-subjects aria-label="Show more subjects">‹</button>
            <button type="button" data-more-inspectors aria-label="Show more inspectors">›</button>
          </div>
          <span class="lens-separator" aria-hidden="true"></span>`
        : ""}
      ${inspectorHtml}
    </nav>`;
}

function readItems(element: HTMLElement): readonly SlideStripItem[] {
  return [...element.querySelectorAll<HTMLElement>("[data-slide-strip-id]")]
    .map(item => {
      const id = item.dataset.slideStripId;
      const label = item.dataset.slideStripLabel;
      if (!id || !label) {
        throw new Error("SlideStrip item markup requires identity and Label.");
      }
      return {
        id,
        label,
        ...(item.dataset.slideStripShortLabel
          ? { shortLabel: item.dataset.slideStripShortLabel }
          : {}),
        ...(item.dataset.slideStripIcon
          ? { icon: item.dataset.slideStripIcon }
          : {}),
      };
    });
}

function readPolicy(
  element: HTMLElement,
  items: readonly SlideStripItem[],
  subject: boolean,
): SlideStripPolicy {
  const initialAnchor = element.dataset.initialAnchor;
  const continuityKey = element.dataset.continuityKey;
  if (!initialAnchor || !continuityKey) {
    throw new Error("SlideStrip markup requires anchor and continuity key.");
  }
  return {
    modes: subject
      ? [{ kind: "label", minimumVisible: 1, gap: 0 }]
      : [
          { kind: "label", minimumVisible: 2, gap: 0 },
          { kind: "short-label", minimumVisible: 2, gap: 0 },
          { kind: "icon", minimumVisible: 2, gap: 0 },
          { kind: "index", minimumVisible: 2, gap: 0 },
        ],
    initialAnchor,
    preferredDirection: "after",
    continuityKey,
    fallbackVisibilityFloor: subject ? 48 : 28,
    oversizedAlignment: "start",
  };
}

function outerWidth(element: HTMLElement): number {
  const style = getComputedStyle(element);
  return element.getBoundingClientRect().width
    + Number.parseFloat(style.marginLeft || "0")
    + Number.parseFloat(style.marginRight || "0");
}

function allocationRichnessKey(pair: AllocationPair): string {
  return [
    pair.subject.result.visibleCount,
    pair.inspector.result.modeIndex,
    pair.inspector.result.visibleCount,
  ].join(":");
}

function inspectorAtLeastAsRich(
  left: SlideStripResult,
  right: SlideStripResult,
): boolean {
  return left.modeIndex < right.modeIndex
    || (left.modeIndex === right.modeIndex
      && left.visibleCount >= right.visibleCount);
}

function inspectorStrictlyRicher(
  left: SlideStripResult,
  right: SlideStripResult,
): boolean {
  return inspectorAtLeastAsRich(left, right)
    && !inspectorAtLeastAsRich(right, left);
}

function pairDominates(left: AllocationPair, right: AllocationPair): boolean {
  const subjectAtLeast = left.subject.result.visibleCount
    >= right.subject.result.visibleCount;
  const inspectorAtLeast = inspectorAtLeastAsRich(
    left.inspector.result,
    right.inspector.result);
  const strict = left.subject.result.visibleCount
      > right.subject.result.visibleCount
    || !inspectorAtLeastAsRich(right.inspector.result, left.inspector.result);
  return subjectAtLeast && inspectorAtLeast && strict;
}

function resultWindowDistance(
  result: SlideStripResult,
  current: SlideStripResult | undefined,
): number {
  return current
    ? Math.abs(result.startIndex - current.startIndex)
      + Math.abs(result.endIndex - current.endIndex)
    : 0;
}

function requestedMinimum(
  controller: SlideStripDomController,
  result: SlideStripResult,
): number {
  const mode = controller.policy.modes[result.modeIndex];
  return Math.min(
    mode?.minimumVisible ?? 1,
    controller.items.length);
}

function satisfiesPolicyMinimum(
  controller: SlideStripDomController,
  applied: SlideStripAppliedResult,
): boolean {
  return !applied.result.fallback
    && applied.result.visibleCount >= requestedMinimum(
      controller,
      applied.result);
}

class ScopeBarController implements ScopeBarBinding {
  private readonly navigation: HTMLElement;
  private readonly state: ScopeBarState;
  private readonly subject: SlideStripDomController;
  private readonly inspector: SlideStripDomController | null;
  private readonly allocation: HTMLElement | null;
  private readonly separator: HTMLElement | null;
  private readonly context: HTMLElement | null;
  private readonly moreSubjects: HTMLButtonElement | null;
  private readonly moreInspectors: HTMLButtonElement | null;
  private readonly observer: ResizeObserver | null;
  private ladder: readonly AllocationPair[] = [];
  private renderedAllocationOrdinal = 0;
  private observedWidth = -1;

  static create(root: ParentNode, state: ScopeBarState): ScopeBarController | null {
    const navigation = root.querySelector<HTMLElement>("[data-scope-bar]");
    return navigation ? new ScopeBarController(navigation, state) : null;
  }

  private constructor(navigation: HTMLElement, state: ScopeBarState) {
    this.navigation = navigation;
    this.state = state;
    const subjectElement = navigation.querySelector<HTMLElement>(
      '[data-slide-strip="subject"]');
    if (!subjectElement) throw new Error("Scope bar requires a subject strip.");
    const subjectItems = readItems(subjectElement);
    this.subject = new SlideStripDomController(
      subjectElement,
      subjectItems,
      readPolicy(subjectElement, subjectItems, true),
      state.subject);
    const inspectorElement = navigation.querySelector<HTMLElement>(
      '[data-slide-strip="inspector"]');
    if (inspectorElement) {
      const inspectorItems = readItems(inspectorElement);
      this.inspector = new SlideStripDomController(
        inspectorElement,
        inspectorItems,
        readPolicy(inspectorElement, inspectorItems, false),
        state.inspector);
    } else {
      this.inspector = null;
    }
    this.allocation = navigation.querySelector(
      "[data-slide-strip-allocation]");
    this.separator = navigation.querySelector(".lens-separator");
    this.context = navigation.querySelector(".lens-context");
    this.moreSubjects = navigation.querySelector("[data-more-subjects]");
    this.moreInspectors = navigation.querySelector("[data-more-inspectors]");
    const allocationKey = navigation.dataset.allocationKey ?? "";
    if (state.allocationKey !== allocationKey) {
      state.allocationKey = allocationKey;
      state.allocationOrdinal = 0;
    }
    this.bind();
    this.observedWidth = this.availableWidth();
    this.layout(this.observedWidth);
    this.observer = typeof ResizeObserver === "undefined"
      ? null
      : new ResizeObserver(() => {
          const available = this.availableWidth();
          if (available === this.observedWidth) return;
          this.observedWidth = available;
          this.layout(available);
        });
    this.observer?.observe(navigation);
  }

  disconnect(): void {
    this.observer?.disconnect();
  }

  revealFocusTarget(target: ScopeBarFocusTarget): void {
    if (target.kind === "application-scope") return;
    const controller = targetStrip(target) === "subject"
      ? this.subject
      : this.inspector;
    this.synchronizeAfterStripTransition(
      controller?.revealForFocus(targetIdentity(target)) ?? false);
  }

  reveal(target: HTMLButtonElement): void {
    const id = target.dataset.slideStripId;
    if (!id) return;
    const controller = target.closest('[data-slide-strip="subject"]')
      ? this.subject
      : this.inspector;
    this.synchronizeAfterStripTransition(
      controller?.revealForFocus(id) ?? false);
  }

  private bind(): void {
    this.navigation.querySelectorAll<HTMLElement>("[data-slide-strip]")
      .forEach(strip => strip.addEventListener("wheel", event => {
        const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY)
          ? event.deltaX
          : event.deltaY;
        if (delta === 0) return;
        const moved = this.controllerFor(strip)?.slide(
          delta < 0 ? "before" : "after") ?? false;
        if (!moved) return;
        this.synchronizeAfterStripTransition(true);
        event.preventDefault();
      }, { passive: false }));
    this.moreSubjects?.addEventListener("click", () => {
      if (this.moreSubjects?.getAttribute("aria-disabled") === "true") return;
      this.moveAllocation(1);
    });
    this.moreInspectors?.addEventListener("click", () => {
      if (this.moreInspectors?.getAttribute("aria-disabled") === "true") return;
      this.moveAllocation(-1);
    });
  }

  private moveAllocation(delta: -1 | 1): void {
    const candidate = this.allocationCandidate(delta);
    if (!candidate) return;
    this.state.allocationOrdinal = candidate.ordinal;
    this.renderedAllocationOrdinal = candidate.ordinal;
    this.applyPair(candidate.pair, "ladder", true);
    this.updateAllocationButtons();
  }

  private allocationCandidate(
    delta: -1 | 1,
  ): { pair: AllocationPair; ordinal: number } | null {
    const subject = this.subject.current?.result;
    const inspector = this.inspector?.current?.result;
    if (!subject || !inspector) return null;
    const candidates = this.ladder.map((pair, ordinal) => ({ pair, ordinal }));
    if (delta < 0) candidates.reverse();
    return candidates.find(({ pair }) => delta > 0
      ? pair.subject.result.visibleCount > subject.visibleCount
        && inspectorStrictlyRicher(inspector, pair.inspector.result)
      : pair.subject.result.visibleCount < subject.visibleCount
        && inspectorStrictlyRicher(pair.inspector.result, inspector)) ?? null;
  }

  private controllerFor(
    element: Element,
  ): SlideStripDomController | null {
    const strip = element.closest<HTMLElement>("[data-slide-strip]");
    return strip?.dataset.slideStrip === "subject"
      ? this.subject
      : strip?.dataset.slideStrip === "inspector"
        ? this.inspector
        : null;
  }

  private synchronizeAfterStripTransition(changed: boolean): void {
    if (!changed
      || !this.inspector
      || this.navigation.dataset.pressure !== "ladder") {
      return;
    }
    const stripWidth = Math.max(
      0,
      this.observedWidth - this.overhead(true));
    const subjectMinimum = this.subject.minimumOuterWidth;
    const inspectorMinimum = this.inspector.minimumOuterWidth;
    if (stripWidth < subjectMinimum + inspectorMinimum) {
      this.layout(this.observedWidth);
      return;
    }
    this.ladder = this.buildLadder(
      stripWidth,
      subjectMinimum,
      inspectorMinimum);
    if (this.ladder.length === 0) {
      this.layout(this.observedWidth);
      return;
    }
    this.state.allocationOrdinal = clampAllocationOrdinal(
      this.state.allocationOrdinal,
      this.ladder.length);
    this.renderedAllocationOrdinal = this.state.allocationOrdinal;
    this.updateAllocationButtons();
  }

  private availableWidth(): number {
    const style = getComputedStyle(this.navigation);
    return Math.max(
      0,
      this.navigation.clientWidth
      - Number.parseFloat(style.paddingLeft || "0")
      - Number.parseFloat(style.paddingRight || "0"));
  }

  private gap(): number {
    const style = getComputedStyle(this.navigation);
    return Number.parseFloat(style.columnGap || style.gap || "0");
  }

  private separatorWidth(): number {
    return this.separator ? outerWidth(this.separator) : 0;
  }

  private allocationWidth(): number {
    if (!this.allocation) return 0;
    const hidden = this.allocation.hidden;
    this.allocation.hidden = false;
    const width = outerWidth(this.allocation);
    this.allocation.hidden = hidden;
    return width;
  }

  private overhead(withControls: boolean): number {
    if (!this.inspector) return 0;
    return this.separatorWidth()
      + (withControls ? this.allocationWidth() : 0)
      + this.gap() * (withControls ? 3 : 2);
  }

  private setControlsVisible(visible: boolean): void {
    if (this.allocation) this.allocation.hidden = !visible;
  }

  private allocationFocusTransfer(
    controls: boolean,
  ): AllocationFocusTransfer | null {
    if (controls || !this.allocation || this.allocation.hidden) return null;
    const active = this.navigation.ownerDocument.activeElement;
    if (!this.allocation.contains(active)) return null;
    if (this.moreSubjects?.contains(active)) {
      const selected = this.subject.element.querySelector<HTMLElement>(
        '[aria-selected="true"]');
      const id = selected?.dataset.slideStripId;
      return id ? { strip: "subject", id } : null;
    }
    const selectedInspector = this.inspector?.element
      .querySelector<HTMLElement>('[aria-selected="true"]');
    const inspectorId = selectedInspector?.dataset.slideStripId;
    if (inspectorId) return { strip: "inspector", id: inspectorId };
    const selectedSubject = this.subject.element.querySelector<HTMLElement>(
      '[aria-selected="true"]');
    const subjectId = selectedSubject?.dataset.slideStripId;
    return subjectId ? { strip: "subject", id: subjectId } : null;
  }

  private applyPair(
    pair: AllocationPair,
    pressure: string,
    controls: boolean,
  ): void {
    this.navigation.dataset.pressure = pressure;
    if (controls) this.setControlsVisible(true);
    this.subject.apply(pair.subject);
    this.inspector?.apply(pair.inspector);
    this.setControlsVisible(controls);
  }

  private layout(available = this.availableWidth()): void {
    if (!this.inspector) {
      this.setControlsVisible(false);
      this.navigation.dataset.pressure = "subject-only";
      const contextWidth = this.context
        ? outerWidth(this.context) + this.gap()
        : 0;
      this.subject.apply(this.subject.resolveRequired(
        Math.max(
          available - contextWidth,
          this.subject.fallbackOuterWidth)));
      return;
    }
    const subjectMinimum = this.subject.minimumOuterWidth;
    const inspectorMinimum = this.inspector.minimumOuterWidth;
    const noControlsWidth = Math.max(0, available - this.overhead(false));
    const transfer = this.allocationFocusTransfer(false);
    const subjectIntent = focusTransferIntent(transfer, "subject");
    const inspectorIntent = focusTransferIntent(transfer, "inspector");
    if (noControlsWidth
      >= this.subject.preferredOuterWidth + this.inspector.preferredOuterWidth) {
      this.applyPair({
        subject: this.subject.resolveRequired(
          this.subject.preferredOuterWidth,
          subjectIntent),
        inspector: this.inspector.resolveRequired(
          this.inspector.preferredOuterWidth,
          inspectorIntent),
      }, "all-preferred", false);
      return;
    }

    const controlsWidth = Math.max(0, available - this.overhead(true));
    if (controlsWidth
      >= subjectMinimum + inspectorMinimum) {
      this.ladder = this.buildLadder(
        controlsWidth,
        subjectMinimum,
        inspectorMinimum);
      if (this.ladder.length > 0) {
        this.state.allocationOrdinal = clampAllocationOrdinal(
          this.state.allocationOrdinal,
          this.ladder.length);
        this.renderedAllocationOrdinal = this.state.allocationOrdinal;
        const pair = this.ladder[this.renderedAllocationOrdinal];
        if (pair) {
          this.applyPair(pair, "ladder", true);
          this.updateAllocationButtons();
          return;
        }
      }
    }

    const noControlsSubjectMinimum = this.subject.minimumOuterWidthFor(
      subjectIntent.pendingFocusId);
    const noControlsInspectorMinimum = this.inspector.minimumOuterWidthFor(
      inspectorIntent.pendingFocusId);
    if (noControlsWidth
      >= noControlsSubjectMinimum + noControlsInspectorMinimum) {
      const provisionalInspectorWidth = noControlsWidth
        - noControlsSubjectMinimum;
      const inspector = this.inspector.resolveRequired(
        provisionalInspectorWidth,
        inspectorIntent);
      const exactInspectorWidth = Math.min(
        provisionalInspectorWidth,
        inspector.result.requiredWidth + this.inspector.chromeWidth);
      this.applyPair({
        subject: this.subject.resolveRequired(
          noControlsWidth - exactInspectorWidth,
          subjectIntent),
        inspector: this.inspector.resolveRequired(
          exactInspectorWidth,
          inspectorIntent),
      }, "control-free", false);
      return;
    }

    this.applyPair(
      this.terminalPair(noControlsWidth, subjectIntent, inspectorIntent),
      "terminal",
      false);
  }

  private buildLadder(
    stripWidth: number,
    subjectMinimum: number,
    inspectorMinimum: number,
  ): readonly AllocationPair[] {
    if (!this.inspector) return [];
    const candidates = new Set<number>([
      inspectorMinimum,
      stripWidth - subjectMinimum,
      ...this.inspector.candidateOuterWidths,
      ...this.subject.candidateOuterWidths.map(width => stripWidth - width),
    ]);
    const pairs: AllocationPair[] = [];
    for (const candidate of [...candidates].sort((left, right) => left - right)) {
      if (candidate < inspectorMinimum
        || candidate > stripWidth - subjectMinimum) {
        continue;
      }
      const inspectorProbe = this.inspector.resolveRequired(candidate);
      if (!satisfiesPolicyMinimum(this.inspector, inspectorProbe)) continue;
      const exactInspectorWidth = inspectorProbe.result.requiredWidth
        + this.inspector.chromeWidth;
      const subject = this.subject.resolveRequired(
        stripWidth - exactInspectorWidth);
      const inspector = this.inspector.resolveRequired(exactInspectorWidth);
      if (!satisfiesPolicyMinimum(this.subject, subject)
        || !satisfiesPolicyMinimum(this.inspector, inspector)) {
        continue;
      }
      pairs.push({ subject, inspector });
    }
    const distinctByRichness = new Map<string, AllocationPair>();
    const currentSubject = this.subject.current?.result;
    const currentInspector = this.inspector.current?.result;
    for (const pair of pairs) {
      const key = allocationRichnessKey(pair);
      const existing = distinctByRichness.get(key);
      if (!existing) {
        distinctByRichness.set(key, pair);
        continue;
      }
      const existingDistance = resultWindowDistance(
        existing.subject.result,
        currentSubject)
        + resultWindowDistance(existing.inspector.result, currentInspector);
      const candidateDistance = resultWindowDistance(
        pair.subject.result,
        currentSubject)
        + resultWindowDistance(pair.inspector.result, currentInspector);
      if (candidateDistance < existingDistance) {
        distinctByRichness.set(key, pair);
      }
    }
    const distinct = [...distinctByRichness.values()];
    const pareto = distinct.filter(pair =>
      !distinct.some(candidate =>
        candidate !== pair && pairDominates(candidate, pair)));
    return pareto.sort((left, right) => {
      if (left.inspector.result.modeIndex !== right.inspector.result.modeIndex) {
        return left.inspector.result.modeIndex
          - right.inspector.result.modeIndex;
      }
      if (left.inspector.result.visibleCount
        !== right.inspector.result.visibleCount) {
        return right.inspector.result.visibleCount
          - left.inspector.result.visibleCount;
      }
      return left.subject.result.visibleCount
        - right.subject.result.visibleCount;
    });
  }

  private terminalPair(
    stripWidth: number,
    subjectIntent: SlideStripResolveIntent = {},
    inspectorIntent: SlideStripResolveIntent = {},
  ): AllocationPair {
    if (!this.inspector) {
      throw new Error("Terminal allocation requires an inspector strip.");
    }
    const minimumInternalWidth = this.subject.fallbackOuterWidth
      + this.inspector.fallbackOuterWidth;
    if (stripWidth < minimumInternalWidth) {
      return {
        subject: this.subject.resolveRequired(
          this.subject.fallbackOuterWidth,
          subjectIntent),
        inspector: this.inspector.resolveRequired(
          this.inspector.fallbackOuterWidth,
          inspectorIntent),
      };
    }
    const targetSubject = Math.floor(stripWidth / 3);
    const clampedTargetSubject = Math.max(
      this.subject.fallbackOuterWidth,
      Math.min(
        targetSubject,
        stripWidth - this.inspector.fallbackOuterWidth));
    const candidates = new Set<number>([
      this.subject.fallbackOuterWidth,
      stripWidth - this.inspector.fallbackOuterWidth,
      clampedTargetSubject,
      ...this.subject.candidateOuterWidths,
      ...this.inspector.candidateOuterWidths.map(width => stripWidth - width),
    ]);
    const pairs = [...candidates].flatMap(subjectWidth => {
      const inspectorWidth = stripWidth - subjectWidth;
      if (subjectWidth < this.subject.fallbackOuterWidth
        || inspectorWidth < this.inspector!.fallbackOuterWidth) {
        return [];
      }
      const subject = this.subject.resolveRequired(
        subjectWidth,
        subjectIntent);
      const inspector = this.inspector!.resolveRequired(
        inspectorWidth,
        inspectorIntent);
      const subjectUnused = subject.result.fallback
        ? 0
        : Math.max(
            0,
            subjectWidth
            - subject.result.requiredWidth
            - this.subject.chromeWidth);
      const inspectorUnused = inspector.result.fallback
        ? 0
        : Math.max(
            0,
            inspectorWidth
            - inspector.result.requiredWidth
            - this.inspector!.chromeWidth);
      return [{
        pair: { subject, inspector },
        unused: subjectUnused + inspectorUnused,
        distance: Math.abs(subjectWidth - targetSubject),
        inspectorWidth,
      }];
    });
    pairs.sort((left, right) =>
      left.unused - right.unused
      || left.distance - right.distance
      || right.inspectorWidth - left.inspectorWidth);
    const selected = pairs[0];
    if (!selected) {
      throw new Error("Scope bar terminal allocation has no valid candidate.");
    }
    return selected.pair;
  }

  private updateAllocationButtons(): void {
    if (!this.moreSubjects || !this.moreInspectors) return;
    this.moreInspectors.setAttribute(
      "aria-disabled",
      String(this.allocationCandidate(-1) === null));
    this.moreSubjects.setAttribute(
      "aria-disabled",
      String(this.allocationCandidate(1) === null));
  }
}
