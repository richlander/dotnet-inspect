export type SlideStripRepresentation =
  | "label"
  | "short-label"
  | "icon"
  | "index";

export type SlideStripDirection = "before" | "after";

export interface SlideStripItem<TId extends string = string> {
  id: TId;
  label: string;
  shortLabel?: string;
  icon?: string;
}

export interface SlideStripMode {
  kind: SlideStripRepresentation;
  minimumVisible: number;
  gap: number;
}

export interface SlideStripPolicy<TId extends string = string> {
  modes: readonly SlideStripMode[];
  initialAnchor: TId;
  preferredDirection: SlideStripDirection;
  continuityKey: string;
  windowContinuity?: "retain-leading" | "anchor-until-slide";
  fallbackVisibilityFloor: number;
  oversizedAlignment: "start" | "end";
}

export interface SlideStripItemMeasurement<TId extends string = string> {
  id: TId;
  widths: Readonly<Partial<Record<SlideStripRepresentation, number>>>;
}

export interface SlideStripWindowTarget<TId extends string = string> {
  id: TId;
  edge: SlideStripDirection;
}

export interface ResolveSlideStripOptions<TId extends string = string> {
  items: readonly SlideStripItem<TId>[];
  measurements: readonly SlideStripItemMeasurement<TId>[];
  policy: SlideStripPolicy<TId>;
  viewportWidth: number;
  retainedLeadingId?: TId;
  focusedId?: TId;
  pendingFocusId?: TId;
  windowTarget?: SlideStripWindowTarget<TId>;
}

export type SlideStripRequiredIdentity<TId extends string = string> = Pick<
  ResolveSlideStripOptions<TId>,
  "retainedLeadingId" | "focusedId" | "pendingFocusId"
>;

export interface SlideStripResult<TId extends string = string> {
  mode: SlideStripRepresentation;
  modeIndex: number;
  visibleIds: readonly TId[];
  startIndex: number;
  endIndex: number;
  requiredWidth: number;
  visibleCount: number;
  leadingHidden: boolean;
  trailingHidden: boolean;
  fallback: boolean;
  oversizedAlignment: "start" | "end";
  pendingFocusId?: TId;
}

interface CandidateWindow {
  startIndex: number;
  endIndex: number;
  requiredWidth: number;
  visibleCount: number;
}

interface ModeResolution {
  mode: SlideStripMode;
  modeIndex: number;
  candidate: CandidateWindow | null;
}

function representationAvailable<TId extends string>(
  item: SlideStripItem<TId>,
  kind: SlideStripRepresentation,
): boolean {
  if (kind === "short-label") return Boolean(item.shortLabel);
  if (kind === "icon") return Boolean(item.icon);
  return true;
}

function viableModes<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  policy: SlideStripPolicy<TId>,
): readonly { mode: SlideStripMode; modeIndex: number }[] {
  return policy.modes.flatMap((mode, modeIndex) =>
    items.every(item => representationAvailable(item, mode.kind))
      ? [{ mode, modeIndex }]
      : []);
}

function itemIndex<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  id: TId | undefined,
): number {
  return id === undefined ? -1 : items.findIndex(item => item.id === id);
}

function measurementWidths<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  measurements: readonly SlideStripItemMeasurement<TId>[],
  kind: SlideStripRepresentation,
): readonly number[] {
  const byId = new Map(measurements.map(item => [item.id, item.widths]));
  return items.map(item => {
    const width = byId.get(item.id)?.[kind];
    if (width === undefined || !Number.isFinite(width) || width <= 0) {
      throw new Error(
        `SlideStrip is missing a positive ${kind} width for ${JSON.stringify(item.id)}.`);
    }
    return width;
  });
}

function windowWidth(
  widths: readonly number[],
  startIndex: number,
  endIndex: number,
  gap: number,
): number {
  let width = 0;
  for (let index = startIndex; index <= endIndex; index++) {
    width += widths[index] ?? 0;
  }
  return width + Math.max(0, endIndex - startIndex) * gap;
}

function includesIndex(
  candidate: CandidateWindow,
  index: number,
): boolean {
  return index < 0
    || (candidate.startIndex <= index && index <= candidate.endIndex);
}

function compareCandidates(
  left: CandidateWindow,
  right: CandidateWindow,
  retainedIndex: number,
  anchorIndex: number,
  direction: SlideStripDirection,
): number {
  if (left.visibleCount !== right.visibleCount) {
    return right.visibleCount - left.visibleCount;
  }
  const origin = retainedIndex >= 0 ? retainedIndex : anchorIndex;
  const leftMovement = Math.abs(left.startIndex - origin);
  const rightMovement = Math.abs(right.startIndex - origin);
  if (leftMovement !== rightMovement) return leftMovement - rightMovement;
  return direction === "after"
    ? right.startIndex - left.startIndex
    : left.startIndex - right.startIndex;
}

function enumerateModeWindows<TId extends string>(
  options: ResolveSlideStripOptions<TId>,
  mode: SlideStripMode,
  requireFocusedDuringSlide: boolean,
): readonly CandidateWindow[] {
  const widths = measurementWidths(
    options.items,
    options.measurements,
    mode.kind);
  const pendingIndex = itemIndex(options.items, options.pendingFocusId);
  const focusedIndex = itemIndex(options.items, options.focusedId);
  const constrainedFocusedIndex = pendingIndex >= 0 ? -1 : focusedIndex;
  const targetIndex = itemIndex(options.items, options.windowTarget?.id);
  const retainedIndex = itemIndex(options.items, options.retainedLeadingId);
  const anchorIndex = itemIndex(options.items, options.policy.initialAnchor);
  const requiresInitialAnchor = pendingIndex < 0
    && focusedIndex < 0
    && targetIndex < 0
    && retainedIndex < 0;
  const candidates: CandidateWindow[] = [];

  for (let startIndex = 0; startIndex < options.items.length; startIndex++) {
    for (
      let endIndex = startIndex;
      endIndex < options.items.length;
      endIndex++
    ) {
      const requiredWidth = windowWidth(
        widths,
        startIndex,
        endIndex,
        mode.gap);
      if (requiredWidth > options.viewportWidth) break;
      const candidate = {
        startIndex,
        endIndex,
        requiredWidth,
        visibleCount: endIndex - startIndex + 1,
      };
      if (!includesIndex(candidate, pendingIndex)) continue;
      if (requiresInitialAnchor && !includesIndex(candidate, anchorIndex)) {
        continue;
      }
      if (targetIndex >= 0) {
        if (options.windowTarget?.edge === "before"
          && startIndex !== targetIndex) {
          continue;
        }
        if (options.windowTarget?.edge === "after"
          && endIndex !== targetIndex) {
          continue;
        }
        if (requireFocusedDuringSlide
          && !includesIndex(candidate, constrainedFocusedIndex)) {
          continue;
        }
      } else if (constrainedFocusedIndex >= 0
        && !includesIndex(candidate, constrainedFocusedIndex)) {
        continue;
      }
      candidates.push(candidate);
    }
  }

  return candidates;
}

function requestedCount<TId extends string>(
  mode: SlideStripMode,
  items: readonly SlideStripItem<TId>[],
): number {
  return Math.min(mode.minimumVisible, items.length);
}

function fallbackId<TId extends string>(
  options: ResolveSlideStripOptions<TId>,
  pendingFocusId: TId | undefined,
): TId {
  const candidates = [
    pendingFocusId,
    options.windowTarget?.id,
    options.focusedId,
    options.retainedLeadingId,
    options.policy.initialAnchor,
  ];
  for (const id of candidates) {
    if (id !== undefined && itemIndex(options.items, id) >= 0) return id;
  }
  const first = options.items[0];
  if (!first) throw new Error("SlideStrip fallback requires an installed item.");
  return first.id;
}

function validateSlideStrip<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  policy: SlideStripPolicy<TId>,
): void {
  if (!Number.isFinite(policy.fallbackVisibilityFloor)
    || policy.fallbackVisibilityFloor <= 0) {
    throw new Error("SlideStrip fallback visibility floor must be positive.");
  }
  if (policy.modes.length === 0
    || policy.modes[0]?.kind !== "label"
    || policy.modes.filter(mode => mode.kind === "label").length !== 1) {
    throw new Error("SlideStrip policy must begin with exactly one Label mode.");
  }
  const kinds = new Set<SlideStripRepresentation>();
  for (const mode of policy.modes) {
    if (kinds.has(mode.kind)) {
      throw new Error(`SlideStrip policy duplicates ${mode.kind} mode.`);
    }
    if (!Number.isInteger(mode.minimumVisible) || mode.minimumVisible <= 0) {
      throw new Error("SlideStrip mode minimum visible count must be positive.");
    }
    if (!Number.isFinite(mode.gap) || mode.gap < 0) {
      throw new Error("SlideStrip mode gap must be non-negative.");
    }
    kinds.add(mode.kind);
  }
  const ids = new Set<TId>();
  for (const item of items) {
    if (!item.label) {
      throw new Error(
        `SlideStrip item ${JSON.stringify(item.id)} requires a Label.`);
    }
    if (ids.has(item.id)) {
      throw new Error(
        `SlideStrip item identity ${JSON.stringify(item.id)} is duplicated.`);
    }
    ids.add(item.id);
  }
  if (items.length > 0 && !ids.has(policy.initialAnchor)) {
    throw new Error("SlideStrip initial anchor must identify an installed item.");
  }
}

export function resolveSlideStrip<TId extends string>(
  options: ResolveSlideStripOptions<TId>,
): SlideStripResult<TId> | null {
  validateSlideStrip(options.items, options.policy);
  if (!Number.isFinite(options.viewportWidth) || options.viewportWidth < 0) {
    throw new Error("SlideStrip viewport width must be non-negative.");
  }
  if (options.items.length === 0) return null;

  const modes = viableModes(options.items, options.policy);
  const canRetainFocusedDuringSlide = options.windowTarget !== undefined
    && options.focusedId !== undefined
    && modes.some(({ mode }) =>
      enumerateModeWindows(options, mode, true).length > 0);
  const pendingFocusId = options.pendingFocusId
    ?? (options.windowTarget !== undefined
      && options.focusedId !== undefined
      && !canRetainFocusedDuringSlide
      ? options.windowTarget.id
      : undefined);
  const effectiveOptions = pendingFocusId === undefined
    ? options
    : { ...options, pendingFocusId };
  const retainedIndex = itemIndex(
    options.items,
    options.retainedLeadingId);
  const anchorIndex = itemIndex(options.items, options.policy.initialAnchor);
  const resolutions: ModeResolution[] = modes.map(({ mode, modeIndex }) => {
    const candidates = [...enumerateModeWindows(
      effectiveOptions,
      mode,
      canRetainFocusedDuringSlide)];
    candidates.sort((left, right) => compareCandidates(
      left,
      right,
      retainedIndex,
      anchorIndex,
      options.policy.preferredDirection));
    return { mode, modeIndex, candidate: candidates[0] ?? null };
  });
  const preferred = resolutions[0];
  if (!preferred) {
    throw new Error("SlideStrip requires its viable Label mode.");
  }
  const baseline = preferred.candidate?.visibleCount ?? 0;
  let selected = baseline >= requestedCount(preferred.mode, options.items)
    ? preferred
    : resolutions.slice(1).find(resolution => {
        const count = resolution.candidate?.visibleCount ?? 0;
        return count >= requestedCount(resolution.mode, options.items)
          && count > baseline;
      });
  selected ??= resolutions.reduce((best, resolution) => {
    const bestCount = best.candidate?.visibleCount ?? 0;
    const count = resolution.candidate?.visibleCount ?? 0;
    return count > bestCount ? resolution : best;
  }, preferred);

  if (!selected.candidate) {
    const id = fallbackId(options, pendingFocusId);
    const index = itemIndex(options.items, id);
    const widths = measurementWidths(
      options.items,
      options.measurements,
      selected.mode.kind);
    const result: SlideStripResult<TId> = {
      mode: selected.mode.kind,
      modeIndex: selected.modeIndex,
      visibleIds: [id],
      startIndex: index,
      endIndex: index,
      requiredWidth: widths[index] ?? options.policy.fallbackVisibilityFloor,
      visibleCount: 1,
      leadingHidden: index > 0,
      trailingHidden: index < options.items.length - 1,
      fallback: true,
      oversizedAlignment: options.policy.oversizedAlignment,
      ...(pendingFocusId === undefined ? {} : { pendingFocusId }),
    };
    return result;
  }

  const candidate = selected.candidate;
  return {
    mode: selected.mode.kind,
    modeIndex: selected.modeIndex,
    visibleIds: options.items
      .slice(candidate.startIndex, candidate.endIndex + 1)
      .map(item => item.id),
    startIndex: candidate.startIndex,
    endIndex: candidate.endIndex,
    requiredWidth: candidate.requiredWidth,
    visibleCount: candidate.visibleCount,
    leadingHidden: candidate.startIndex > 0,
    trailingHidden: candidate.endIndex < options.items.length - 1,
    fallback: false,
    oversizedAlignment: options.policy.oversizedAlignment,
    ...(pendingFocusId === undefined ? {} : { pendingFocusId }),
  };
}

export function slideStripCandidateWidths<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  measurements: readonly SlideStripItemMeasurement<TId>[],
  policy: SlideStripPolicy<TId>,
): readonly number[] {
  validateSlideStrip(items, policy);
  if (items.length === 0) return [0];
  const widths = new Set<number>([policy.fallbackVisibilityFloor]);
  for (const { mode } of viableModes(items, policy)) {
    const itemWidths = measurementWidths(items, measurements, mode.kind);
    for (let startIndex = 0; startIndex < items.length; startIndex++) {
      for (
        let endIndex = startIndex;
        endIndex < items.length;
        endIndex++
      ) {
        widths.add(windowWidth(itemWidths, startIndex, endIndex, mode.gap));
      }
    }
  }
  return [...widths].sort((left, right) => left - right);
}

export function slideStripMinimumWidth<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  measurements: readonly SlideStripItemMeasurement<TId>[],
  policy: SlideStripPolicy<TId>,
  requiredIdentity: SlideStripRequiredIdentity<TId> = {},
): number {
  if (items.length === 0) return 0;
  const requiredId = requiredIdentity.pendingFocusId
    ?? requiredIdentity.focusedId
    ?? requiredIdentity.retainedLeadingId;
  for (const viewportWidth of slideStripCandidateWidths(
    items,
    measurements,
    policy)) {
    const result = resolveSlideStrip({
      items,
      measurements,
      policy,
      viewportWidth,
      ...(requiredId === undefined ? {} : { focusedId: requiredId }),
    });
    if (!result) continue;
    const mode = policy.modes[result.modeIndex];
    if (mode
      && result.visibleCount >= requestedCount(mode, items)
      && !result.fallback) {
      return viewportWidth;
    }
  }
  return policy.fallbackVisibilityFloor;
}

export function slideStripPreferredWidth<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  measurements: readonly SlideStripItemMeasurement<TId>[],
  policy: SlideStripPolicy<TId>,
): number {
  if (items.length === 0) return 0;
  validateSlideStrip(items, policy);
  const labelMode = policy.modes[0];
  if (!labelMode) throw new Error("SlideStrip requires a Label mode.");
  const widths = measurementWidths(items, measurements, "label");
  return windowWidth(widths, 0, items.length - 1, labelMode.gap);
}

export function adjacentSlideTarget<TId extends string>(
  items: readonly SlideStripItem<TId>[],
  current: SlideStripResult<TId> | null,
  direction: SlideStripDirection,
): SlideStripWindowTarget<TId> | null {
  if (!current) return null;
  const index = direction === "after"
    ? current.endIndex + 1
    : current.startIndex - 1;
  const item = items[index];
  return item ? { id: item.id, edge: direction } : null;
}
