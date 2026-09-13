export const PACKAGE_QUERY_RENDERED_ROW_LIMIT = 30;
export const PACKAGE_QUERY_DEFAULT_ROW_EXTENT_PX = 180;

const PACKAGE_QUERY_ROW_OVERSCAN = 5;
const PACKAGE_QUERY_MINIMUM_ROW_EXTENT_PX = 96;
const PACKAGE_QUERY_MAXIMUM_ROW_EXTENT_PX = 480;
const PACKAGE_QUERY_ROW_GAP_PX = 10;

export interface PackageQueryViewportSnapshot {
  scrollTop: number;
  clientHeight: number;
  surfaceTop: number;
  rowExtent: number;
  anchorRowIndex: number | null;
  anchorOffsetTop: number | null;
}

export interface PackageQueryRowWindow {
  start: number;
  end: number;
  beforeHeight: number;
  afterHeight: number;
  rowExtent: number;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

function normalizedRowExtent(value: number): number {
  return Number.isFinite(value)
    ? clamp(
        value,
        PACKAGE_QUERY_MINIMUM_ROW_EXTENT_PX,
        PACKAGE_QUERY_MAXIMUM_ROW_EXTENT_PX)
    : PACKAGE_QUERY_DEFAULT_ROW_EXTENT_PX;
}

function rowIndex(element: HTMLElement): number | null {
  const index = Number(element.dataset.queryRowIndex);
  return Number.isInteger(index) && index >= 0 ? index : null;
}

function measuredRowExtent(rows: readonly HTMLElement[]): number | null {
  if (rows.length === 0) return null;
  if (rows.length === 1) {
    return normalizedRowExtent(
      rows[0]!.getBoundingClientRect().height + PACKAGE_QUERY_ROW_GAP_PX);
  }

  const firstTop = rows[0]!.getBoundingClientRect().top;
  const lastTop = rows.at(-1)!.getBoundingClientRect().top;
  const extent = (lastTop - firstTop) / (rows.length - 1);
  return extent > 0 ? normalizedRowExtent(extent) : null;
}

export function resolvePackageQueryRowWindow(
  rowCount: number,
  viewport: PackageQueryViewportSnapshot | null = null,
): PackageQueryRowWindow {
  const count = Math.max(0, Math.trunc(rowCount));
  const rowExtent = normalizedRowExtent(
    viewport?.rowExtent ?? PACKAGE_QUERY_DEFAULT_ROW_EXTENT_PX);
  if (count <= PACKAGE_QUERY_RENDERED_ROW_LIMIT) {
    return {
      start: 0,
      end: count,
      beforeHeight: 0,
      afterHeight: 0,
      rowExtent,
    };
  }

  const estimatedFirstVisible = viewport?.anchorRowIndex
    ?? Math.floor(Math.max(
      0,
      (viewport?.scrollTop ?? 0) - (viewport?.surfaceTop ?? 0)) / rowExtent);
  const start = clamp(
    estimatedFirstVisible - PACKAGE_QUERY_ROW_OVERSCAN,
    0,
    count - PACKAGE_QUERY_RENDERED_ROW_LIMIT);
  const end = start + PACKAGE_QUERY_RENDERED_ROW_LIMIT;
  return {
    start,
    end,
    beforeHeight: start * rowExtent,
    afterHeight: (count - end) * rowExtent,
    rowExtent,
  };
}

export function capturePackageQueryViewport(
  root: ParentNode,
): PackageQueryViewportSnapshot | null {
  const main = root.querySelector<HTMLElement>(".query-main");
  const surface = root.querySelector<HTMLElement>("#package-query-row-window");
  if (!main || !surface) return null;

  const mainRect = main.getBoundingClientRect();
  const surfaceRect = surface.getBoundingClientRect();
  const rows = [
    ...surface.querySelectorAll<HTMLElement>("[data-query-row-index]"),
  ];
  const storedExtent = Number(surface.dataset.queryRowExtent);
  const rowExtent = measuredRowExtent(rows)
    ?? normalizedRowExtent(storedExtent);
  const anchor = rows.find(row => {
    const rect = row.getBoundingClientRect();
    return rect.bottom > mainRect.top && rect.top < mainRect.bottom;
  }) ?? null;

  return {
    scrollTop: main.scrollTop,
    clientHeight: main.clientHeight,
    surfaceTop: surfaceRect.top - mainRect.top + main.scrollTop,
    rowExtent,
    anchorRowIndex: anchor ? rowIndex(anchor) : null,
    anchorOffsetTop: anchor
      ? anchor.getBoundingClientRect().top - mainRect.top
      : null,
  };
}

export function restorePackageQueryViewport(
  root: ParentNode,
  snapshot: PackageQueryViewportSnapshot | null,
): void {
  if (!snapshot) return;
  const main = root.querySelector<HTMLElement>(".query-main");
  if (!main) return;

  if (snapshot.anchorRowIndex !== null
    && snapshot.anchorOffsetTop !== null) {
    const anchor = root.querySelector<HTMLElement>(
      `[data-query-row-index="${snapshot.anchorRowIndex}"]`);
    if (anchor) {
      const currentOffset =
        anchor.getBoundingClientRect().top
        - main.getBoundingClientRect().top;
      main.scrollTop += currentOffset - snapshot.anchorOffsetTop;
      return;
    }
  }

  main.scrollTop = snapshot.scrollTop;
}
