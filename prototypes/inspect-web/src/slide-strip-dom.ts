import {
  adjacentSlideTarget,
  resolveSlideStrip,
  slideStripCandidateWidths,
  slideStripMinimumWidth,
  slideStripPreferredWidth,
  type ResolveSlideStripOptions,
  type SlideStripDirection,
  type SlideStripItem,
  type SlideStripItemMeasurement,
  type SlideStripPolicy,
  type SlideStripRepresentation,
  type SlideStripResult,
  type SlideStripWindowTarget,
} from "./slide-strip.ts";

export interface SlideStripContinuityState {
  key: string;
  leadingId?: string;
}

export interface SlideStripResolveIntent {
  pendingFocusId?: string;
  windowTarget?: SlideStripWindowTarget;
}

export interface SlideStripAppliedResult {
  result: SlideStripResult;
  outerWidth: number;
}

interface SlideStripEmptyAppliedResult {
  result: null;
  outerWidth: number;
}

export type SlideStripResolvedResult =
  | SlideStripAppliedResult
  | SlideStripEmptyAppliedResult;

function numericStyle(value: string): number {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function elementChromeWidth(element: HTMLElement): number {
  const style = getComputedStyle(element);
  return numericStyle(style.paddingLeft)
    + numericStyle(style.paddingRight)
    + numericStyle(style.borderLeftWidth)
    + numericStyle(style.borderRightWidth);
}

function elementGap(element: HTMLElement): number {
  const style = getComputedStyle(element);
  return numericStyle(style.columnGap || style.gap);
}

function elementOuterWidth(element: HTMLElement): number {
  const style = getComputedStyle(element);
  return element.getBoundingClientRect().width
    + numericStyle(style.marginLeft)
    + numericStyle(style.marginRight);
}

function itemId(
  element: HTMLElement | null,
): string | undefined {
  const id = element?.dataset.slideStripId;
  return id || undefined;
}

function measuredPolicy(
  policy: SlideStripPolicy,
  gaps: ReadonlyMap<SlideStripRepresentation, number>,
): SlideStripPolicy {
  return {
    ...policy,
    modes: policy.modes.map(mode => ({
      ...mode,
      gap: gaps.get(mode.kind) ?? mode.gap,
    })),
  };
}

interface DomMeasurements {
  itemMeasurements: readonly SlideStripItemMeasurement[];
  gaps: ReadonlyMap<SlideStripRepresentation, number>;
}

export class SlideStripDomController {
  readonly element: HTMLElement;
  readonly items: readonly SlideStripItem[];
  readonly state: SlideStripContinuityState;
  readonly chromeWidth: number;
  readonly measurements: readonly SlideStripItemMeasurement[];
  readonly policy: SlideStripPolicy;
  private readonly candidateWidths: readonly number[];
  private readonly preferredWidth: number;
  private readonly buttons: readonly HTMLButtonElement[];
  private readonly edgeBefore: HTMLElement | null;
  private readonly edgeAfter: HTMLElement | null;
  private applied: SlideStripResolvedResult | null = null;
  private minimumWidthCache:
    { key: string; width: number } | null = null;

  constructor(
    element: HTMLElement,
    items: readonly SlideStripItem[],
    policy: SlideStripPolicy,
    state: SlideStripContinuityState,
  ) {
    this.element = element;
    this.items = items;
    this.state = state;
    this.buttons = [
      ...element.querySelectorAll<HTMLButtonElement>("[data-slide-strip-id]"),
    ];
    this.edgeBefore = element.querySelector("[data-slide-strip-before]");
    this.edgeAfter = element.querySelector("[data-slide-strip-after]");
    this.chromeWidth = elementChromeWidth(element);
    const itemContainer = element.querySelector<HTMLElement>(
      ".slide-strip-items");
    if (!itemContainer) {
      throw new Error("SlideStrip requires a .slide-strip-items container.");
    }
    const measurements = this.measure(policy, itemContainer);
    this.policy = measuredPolicy(policy, measurements.gaps);
    this.measurements = measurements.itemMeasurements;
    this.candidateWidths = slideStripCandidateWidths(
      this.items,
      this.measurements,
      this.policy);
    this.preferredWidth = slideStripPreferredWidth(
      this.items,
      this.measurements,
      this.policy);
    if (state.key !== policy.continuityKey
      || (state.leadingId !== undefined
        && !items.some(item => item.id === state.leadingId))) {
      state.key = policy.continuityKey;
      delete state.leadingId;
    }
  }

  get current(): SlideStripAppliedResult | null {
    return this.applied?.result ? this.applied : null;
  }

  get fallbackOuterWidth(): number {
    return (this.items.length === 0
      ? 0
      : this.policy.fallbackVisibilityFloor) + this.chromeWidth;
  }

  get minimumOuterWidth(): number {
    return this.minimumOuterWidthFor();
  }

  minimumOuterWidthFor(pendingFocusId?: string): number {
    const focusedId = this.focusedId();
    const key = [
      focusedId ?? "",
      this.state.leadingId ?? "",
      pendingFocusId ?? "",
    ].join(":");
    if (this.minimumWidthCache?.key === key) {
      return this.minimumWidthCache.width;
    }
    const width = slideStripMinimumWidth(
      this.items,
      this.measurements,
      this.policy,
      {
        ...(focusedId === undefined ? {} : { focusedId }),
        ...(this.state.leadingId === undefined
          ? {}
          : { retainedLeadingId: this.state.leadingId }),
        ...(pendingFocusId === undefined ? {} : { pendingFocusId }),
      }) + this.chromeWidth;
    this.minimumWidthCache = { key, width };
    return width;
  }

  get preferredOuterWidth(): number {
    return this.preferredWidth + this.chromeWidth;
  }

  get candidateOuterWidths(): readonly number[] {
    return this.candidateWidths
      .map(width => width + this.chromeWidth);
  }

  resolve(
    outerWidth: number,
    intent: SlideStripResolveIntent = {},
  ): SlideStripResolvedResult {
    const focusedId = this.focusedId();
    const options: ResolveSlideStripOptions = {
      items: this.items,
      measurements: this.measurements,
      policy: this.policy,
      viewportWidth: Math.max(0, outerWidth - this.chromeWidth),
      ...(this.state.leadingId === undefined
        ? {}
        : { retainedLeadingId: this.state.leadingId }),
      ...(focusedId === undefined ? {} : { focusedId }),
      ...(intent.pendingFocusId === undefined
        ? {}
        : { pendingFocusId: intent.pendingFocusId }),
      ...(intent.windowTarget === undefined
        ? {}
        : { windowTarget: intent.windowTarget }),
    };
    const result = resolveSlideStrip(options);
    return { result, outerWidth };
  }

  resolveRequired(
    outerWidth: number,
    intent: SlideStripResolveIntent = {},
  ): SlideStripAppliedResult {
    const applied = this.resolve(outerWidth, intent);
    if (!applied.result) {
      throw new Error("The composed SlideStrip requires a non-empty inventory.");
    }
    return applied;
  }

  apply(applied: SlideStripResolvedResult): void {
    const { result, outerWidth } = applied;
    this.element.style.width = `${Math.max(0, outerWidth)}px`;
    if (!result) {
      delete this.element.dataset.mode;
      delete this.element.dataset.fallback;
      delete this.element.dataset.oversizedAlignment;
      this.buttons.forEach(button => button.hidden = true);
      if (this.edgeBefore) this.edgeBefore.hidden = true;
      if (this.edgeAfter) this.edgeAfter.hidden = true;
      this.applied = applied;
      return;
    }
    this.element.dataset.mode = result.mode;
    this.element.dataset.fallback = String(result.fallback);
    this.element.dataset.oversizedAlignment = result.oversizedAlignment;

    const pending = result.pendingFocusId === undefined
      ? null
      : this.button(result.pendingFocusId);
    if (pending) {
      pending.hidden = false;
      this.buttons.forEach(button => {
        button.tabIndex = button === pending ? 0 : -1;
      });
      pending.focus({ preventScroll: true });
    }

    const visible = new Set<string>(result.visibleIds);
    this.buttons.forEach(button => {
      button.hidden = !visible.has(button.dataset.slideStripId ?? "");
    });
    if (this.edgeBefore) this.edgeBefore.hidden = !result.leadingHidden;
    if (this.edgeAfter) this.edgeAfter.hidden = !result.trailingHidden;
    this.relocateHiddenTabStop();
    this.applied = applied;
    const leading = result.visibleIds[0];
    if (this.state.leadingId === undefined && leading !== undefined) {
      this.state.leadingId = leading;
    }
  }

  revealForFocus(id: string): boolean {
    if (!this.items.some(item => item.id === id)) return false;
    const current = this.applied;
    if (!current?.result) return false;
    this.apply(this.resolveRequired(
      current.outerWidth,
      { pendingFocusId: id }));
    this.retainAppliedLeading();
    return true;
  }

  slide(direction: SlideStripDirection): boolean {
    const current = this.applied;
    if (!current?.result) return false;
    const windowTarget = adjacentSlideTarget(
      this.items,
      current.result,
      direction);
    if (!windowTarget) return false;
    this.apply(this.resolveRequired(current.outerWidth, { windowTarget }));
    this.retainAppliedLeading();
    return true;
  }

  private measure(
    policy: SlideStripPolicy,
    itemContainer: HTMLElement,
  ): DomMeasurements {
    const priorMode = this.element.dataset.mode;
    const priorVisibility = this.element.style.visibility;
    const hidden = this.buttons.map(button => button.hidden);
    this.element.style.visibility = "hidden";
    this.buttons.forEach(button => button.hidden = false);
    const measurements = new Map<string, Partial<
      Record<SlideStripRepresentation, number>
    >>();
    const gaps = new Map<SlideStripRepresentation, number>();
    for (const mode of policy.modes) {
      this.element.dataset.mode = mode.kind;
      gaps.set(mode.kind, elementGap(itemContainer));
      for (const button of this.buttons) {
        const id = itemId(button);
        if (id === undefined) continue;
        const widths = measurements.get(id) ?? {};
        widths[mode.kind] = elementOuterWidth(button);
        measurements.set(id, widths);
      }
    }
    this.buttons.forEach((button, index) => {
      button.hidden = hidden[index] ?? false;
    });
    if (priorMode === undefined) delete this.element.dataset.mode;
    else this.element.dataset.mode = priorMode;
    this.element.style.visibility = priorVisibility;
    return {
      itemMeasurements: this.items.map(item => ({
        id: item.id,
        widths: measurements.get(item.id) ?? {},
      })),
      gaps,
    };
  }

  private focusedId(): string | undefined {
    const active = this.element.ownerDocument.activeElement;
    return active instanceof HTMLElement && this.element.contains(active)
      ? itemId(active)
      : undefined;
  }

  private button(id: string): HTMLButtonElement | null {
    return this.buttons.find(
      button => button.dataset.slideStripId === id) ?? null;
  }

  private retainAppliedLeading(): void {
    const leading = this.applied?.result?.visibleIds[0];
    if (leading !== undefined) this.state.leadingId = leading;
  }

  private relocateHiddenTabStop(): void {
    const active = this.element.ownerDocument.activeElement;
    if (active instanceof HTMLElement && this.element.contains(active)) return;
    const holder = this.buttons.find(button => button.tabIndex === 0);
    if (!holder?.hidden) return;
    const visible = this.buttons.filter(button => !button.hidden);
    if (visible.length === 0) return;
    const selected = visible.find(
      button => button.getAttribute("aria-selected") === "true");
    const priorIndex = holder ? this.buttons.indexOf(holder) : -1;
    const replacement = selected ?? visible.reduce((best, button) => {
      if (priorIndex < 0) return best;
      const bestDistance = Math.abs(this.buttons.indexOf(best) - priorIndex);
      const distance = Math.abs(this.buttons.indexOf(button) - priorIndex);
      if (distance < bestDistance) return button;
      if (distance > bestDistance) return best;
      return this.policy.preferredDirection === "after"
        ? (this.buttons.indexOf(button) > this.buttons.indexOf(best)
          ? button
          : best)
        : (this.buttons.indexOf(button) < this.buttons.indexOf(best)
          ? button
          : best);
    }, visible[0]!);
    this.buttons.forEach(button => {
      button.tabIndex = button === replacement ? 0 : -1;
    });
  }
}
