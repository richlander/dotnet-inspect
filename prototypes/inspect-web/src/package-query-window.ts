const DEFAULT_ROW_HEIGHT_PX = 180;
const ROW_GAP_PX = 10;
const OVERSCAN_PX = 600;

export const PACKAGE_QUERY_MAX_RENDERED_ROWS = 30;

export interface PackageQueryResultWindow {
  start: number;
  end: number;
  topSpacerHeight: number;
  bottomSpacerHeight: number;
}

export interface PackageQueryWindowState {
  readonly rowHeights: Map<number, number>;
  estimatedRowHeight: number;
}

export interface PackageQueryScrollAnchor {
  rowIndex: number;
  viewportOffset: number;
}

interface PackageQueryViewport {
  start: number;
  height: number;
}

export function createPackageQueryWindowState(): PackageQueryWindowState {
  return {
    rowHeights: new Map<number, number>(),
    estimatedRowHeight: DEFAULT_ROW_HEIGHT_PX,
  };
}

export function resetPackageQueryWindow(
  state: PackageQueryWindowState,
): void {
  state.rowHeights.clear();
  state.estimatedRowHeight = DEFAULT_ROW_HEIGHT_PX;
}

export function preparePackageQueryResultWindow(
  root: ParentNode,
  rowCount: number,
  state: PackageQueryWindowState,
  anchor?: PackageQueryScrollAnchor | null,
): PackageQueryResultWindow {
  return calculatePackageQueryResultWindow(
    rowCount,
    readPackageQueryViewport(root),
    state,
    anchor);
}

export function packageQueryResultWindowMatches(
  root: ParentNode,
  window: PackageQueryResultWindow,
): boolean {
  const list = root.querySelector<HTMLElement>(".query-list");
  if (!list) return false;
  return Number(list.dataset.queryWindowStart) === window.start
    && Number(list.dataset.queryWindowEnd) === window.end
    && Number(list.dataset.queryWindowTop) ===
      Math.round(window.topSpacerHeight)
    && Number(list.dataset.queryWindowBottom) ===
      Math.round(window.bottomSpacerHeight);
}

export function observePackageQueryRowHeights(
  root: ParentNode,
  state: PackageQueryWindowState,
): boolean {
  let changed = false;
  root.querySelectorAll<HTMLElement>("[data-query-row-index]")
    .forEach(element => {
      const index = Number.parseInt(element.dataset.queryRowIndex ?? "", 10);
      const height = element.getBoundingClientRect().height;
      if (!Number.isInteger(index) || index < 0
        || !Number.isFinite(height) || height <= 0) return;
      const previous = state.rowHeights.get(index);
      if (previous === undefined || Math.abs(previous - height) > 0.5) {
        changed = true;
      }
      state.rowHeights.set(index, height);
    });
  if (!changed) return false;
  const heights = [...state.rowHeights.values()];
  state.estimatedRowHeight =
    heights.reduce((sum, height) => sum + height, 0) / heights.length;
  return true;
}

export function capturePackageQueryScrollAnchor(
  root: ParentNode,
): PackageQueryScrollAnchor | null {
  const main = root.querySelector<HTMLElement>(".query-main");
  if (!main) return null;
  const mainBounds = main.getBoundingClientRect();
  for (const element of root.querySelectorAll<HTMLElement>(
    "[data-query-row-index]")) {
    const rowIndex = Number.parseInt(
      element.dataset.queryRowIndex ?? "",
      10);
    const bounds = element.getBoundingClientRect();
    if (!Number.isInteger(rowIndex) || rowIndex < 0
      || bounds.bottom <= mainBounds.top
      || bounds.top >= mainBounds.bottom) continue;
    return {
      rowIndex,
      viewportOffset: bounds.top - mainBounds.top,
    };
  }
  return null;
}

export function restorePackageQueryScrollAnchor(
  root: ParentNode,
  anchor: PackageQueryScrollAnchor | null,
): boolean {
  if (!anchor) return false;
  const main = root.querySelector<HTMLElement>(".query-main");
  const row = [...root.querySelectorAll<HTMLElement>(
    "[data-query-row-index]")].find(element =>
      Number(element.dataset.queryRowIndex) === anchor.rowIndex);
  if (!main || !row) return false;
  const offset = row.getBoundingClientRect().top
    - main.getBoundingClientRect().top;
  main.scrollTop += offset - anchor.viewportOffset;
  return true;
}

export function calculatePackageQueryResultWindow(
  rowCount: number,
  viewport: PackageQueryViewport | null,
  state: PackageQueryWindowState,
  anchor?: PackageQueryScrollAnchor | null,
): PackageQueryResultWindow {
  if (rowCount <= PACKAGE_QUERY_MAX_RENDERED_ROWS) {
    return {
      start: 0,
      end: rowCount,
      topSpacerHeight: 0,
      bottomSpacerHeight: 0,
    };
  }

  const strides = Array.from(
    { length: rowCount },
    (_, index) =>
      (state.rowHeights.get(index) ?? state.estimatedRowHeight)
        + (index < rowCount - 1 ? ROW_GAP_PX : 0));
  const totalHeight = strides.reduce((sum, height) => sum + height, 0);
  if (!viewport || viewport.height <= 0) {
    const end = PACKAGE_QUERY_MAX_RENDERED_ROWS;
    const renderedHeight = sumRange(strides, 0, end);
    return {
      start: 0,
      end,
      topSpacerHeight: 0,
      bottomSpacerHeight: totalHeight - renderedHeight,
    };
  }

  const anchoredViewportStart = anchor
    && anchor.rowIndex >= 0
    && anchor.rowIndex < rowCount
    ? Math.max(
        0,
        sumRange(strides, 0, anchor.rowIndex) - anchor.viewportOffset)
    : viewport.start;
  const targetStart = Math.max(0, anchoredViewportStart - OVERSCAN_PX);
  const targetEnd = anchoredViewportStart + viewport.height + OVERSCAN_PX;
  let start = 0;
  let topSpacerHeight = 0;
  while (start < rowCount
    && topSpacerHeight + strides[start]! <= targetStart) {
    topSpacerHeight += strides[start]!;
    start++;
  }

  let end = start;
  let renderedHeight = 0;
  while (end < rowCount
    && end - start < PACKAGE_QUERY_MAX_RENDERED_ROWS
    && (topSpacerHeight + renderedHeight < targetEnd || end === start)) {
    renderedHeight += strides[end]!;
    end++;
  }

  if (end === rowCount && end - start < PACKAGE_QUERY_MAX_RENDERED_ROWS) {
    const adjustedStart = Math.max(0, rowCount - PACKAGE_QUERY_MAX_RENDERED_ROWS);
    if (adjustedStart < start) {
      start = adjustedStart;
      topSpacerHeight = sumRange(strides, 0, start);
      renderedHeight = sumRange(strides, start, end);
    }
  }

  return {
    start,
    end,
    topSpacerHeight,
    bottomSpacerHeight:
      Math.max(0, totalHeight - topSpacerHeight - renderedHeight),
  };
}

function readPackageQueryViewport(
  root: ParentNode,
): PackageQueryViewport | null {
  const main = root.querySelector<HTMLElement>(".query-main");
  const list = root.querySelector<HTMLElement>(".query-list");
  if (!main || !list) return null;
  const mainBounds = main.getBoundingClientRect();
  const listBounds = list.getBoundingClientRect();
  return {
    start: Math.max(0, mainBounds.top - listBounds.top),
    height: main.clientHeight,
  };
}

function sumRange(
  values: readonly number[],
  start: number,
  end: number,
): number {
  let sum = 0;
  for (let index = start; index < end; index++) sum += values[index]!;
  return sum;
}
