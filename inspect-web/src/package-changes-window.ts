import {
  PACKAGE_QUERY_RENDERED_ROW_LIMIT,
  type PackageQueryRowWindow,
} from "./package-query-window.ts";

const PACKAGE_CHANGES_DEFAULT_ROW_EXTENT_PX = 300;
const PACKAGE_CHANGES_MINIMUM_ROW_EXTENT_PX = 120;
const PACKAGE_CHANGES_MAXIMUM_ROW_EXTENT_PX = 1_600;
const PACKAGE_CHANGES_ROW_GAP_PX = 10;
const PACKAGE_CHANGES_ROW_OVERSCAN = 5;

export interface PackageChangesViewportSnapshot {
  readonly scrollTop: number;
  readonly clientHeight: number;
  readonly surfaceTop: number;
  readonly rowExtent: number;
  readonly rowExtents?: readonly (number | null)[];
  readonly anchorRowIndex: number | null;
  readonly anchorOffsetTop: number | null;
}

export type PackageChangesRowWindow = PackageQueryRowWindow;

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

function normalizedEstimatedExtent(value: number): number {
  return Number.isFinite(value)
    ? clamp(
        value,
        PACKAGE_CHANGES_MINIMUM_ROW_EXTENT_PX,
        PACKAGE_CHANGES_MAXIMUM_ROW_EXTENT_PX)
    : PACKAGE_CHANGES_DEFAULT_ROW_EXTENT_PX;
}

function measuredExtent(value: number): number | null {
  return Number.isFinite(value) && value > 0 ? value : null;
}

function extentAt(
  extents: readonly (number | null)[] | undefined,
  index: number,
  fallback: number,
): number {
  const measured = extents?.[index];
  return measured === null || measured === undefined
    ? fallback
    : measuredExtent(measured) ?? fallback;
}

function heightBefore(
  index: number,
  extents: readonly (number | null)[] | undefined,
  fallback: number,
): number {
  let height = 0;
  for (let rowIndex = 0; rowIndex < index; rowIndex++) {
    height += extentAt(extents, rowIndex, fallback);
  }
  return height;
}

function rowAtOffset(
  rowCount: number,
  offset: number,
  extents: readonly (number | null)[] | undefined,
  fallback: number,
): number {
  let low = 0;
  let high = rowCount;
  while (low < high) {
    const middle = Math.floor((low + high) / 2);
    if (heightBefore(middle + 1, extents, fallback) <= offset) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }
  return clamp(low, 0, Math.max(0, rowCount - 1));
}

export function resolvePackageChangesRowWindow(
  rowCount: number,
  viewport: PackageChangesViewportSnapshot | null = null,
): PackageChangesRowWindow {
  const count = Math.max(0, Math.trunc(rowCount));
  const fallback = normalizedEstimatedExtent(
    viewport?.rowExtent ?? PACKAGE_CHANGES_DEFAULT_ROW_EXTENT_PX);
  if (count <= PACKAGE_QUERY_RENDERED_ROW_LIMIT) {
    return {
      start: 0,
      end: count,
      beforeHeight: 0,
      afterHeight: 0,
      rowExtent: fallback,
    };
  }

  const extents = viewport?.rowExtents;
  const firstVisible = viewport?.anchorRowIndex
    ?? rowAtOffset(
      count,
      Math.max(
        0,
        (viewport?.scrollTop ?? 0) - (viewport?.surfaceTop ?? 0)),
      extents,
      fallback);
  const start = clamp(
    firstVisible - PACKAGE_CHANGES_ROW_OVERSCAN,
    0,
    count - PACKAGE_QUERY_RENDERED_ROW_LIMIT);
  const end = start + PACKAGE_QUERY_RENDERED_ROW_LIMIT;
  return {
    start,
    end,
    beforeHeight: heightBefore(start, extents, fallback),
    afterHeight:
      heightBefore(count, extents, fallback)
      - heightBefore(end, extents, fallback),
    rowExtent: fallback,
  };
}

export function capturePackageChangesViewport(
  root: ParentNode,
  previous: PackageChangesViewportSnapshot | null = null,
): PackageChangesViewportSnapshot | null {
  const main = root.querySelector<HTMLElement>(".query-main");
  const surface =
    root.querySelector<HTMLElement>("#package-changes-row-window");
  if (!main || !surface) return null;
  const mainRect = main.getBoundingClientRect();
  const surfaceRect = surface.getBoundingClientRect();
  const rows = [
    ...surface.querySelectorAll<HTMLElement>("[data-changes-row-index]"),
  ];
  const rowCount = Number(surface.dataset.changesRowCount);
  const extents: (number | null)[] = Array.from(
    {
      length: Number.isInteger(rowCount) && rowCount >= 0
        ? rowCount
        : previous?.rowExtents?.length ?? 0,
    },
    () => null);
  previous?.rowExtents?.forEach((extent, index) => {
    if (index < extents.length) extents[index] = extent;
  });

  for (const row of rows) {
    const index = Number(row.dataset.changesRowIndex);
    if (!Number.isInteger(index) || index < 0 || index >= extents.length) {
      continue;
    }
    extents[index] = measuredExtent(
      row.getBoundingClientRect().height + PACKAGE_CHANGES_ROW_GAP_PX);
  }
  const measured = extents.filter(
    (extent): extent is number => extent !== null);
  const rowExtent = measured.length
    ? normalizedEstimatedExtent(
        measured.reduce((total, extent) => total + extent, 0) / measured.length)
    : normalizedEstimatedExtent(
        previous?.rowExtent
        ?? Number(surface.dataset.changesRowExtent));
  const anchor = rows.find(row => {
    const rect = row.getBoundingClientRect();
    return rect.bottom > mainRect.top && rect.top < mainRect.bottom;
  }) ?? null;
  const anchorIndex = Number(anchor?.dataset.changesRowIndex);
  return {
    scrollTop: main.scrollTop,
    clientHeight: main.clientHeight,
    surfaceTop: surfaceRect.top - mainRect.top + main.scrollTop,
    rowExtent,
    rowExtents: extents,
    anchorRowIndex:
      Number.isInteger(anchorIndex) && anchorIndex >= 0 ? anchorIndex : null,
    anchorOffsetTop: anchor
      ? anchor.getBoundingClientRect().top - mainRect.top
      : null,
  };
}

export function restorePackageChangesViewport(
  root: ParentNode,
  snapshot: PackageChangesViewportSnapshot | null,
): void {
  if (!snapshot) return;
  const main = root.querySelector<HTMLElement>(".query-main");
  if (!main) return;
  if (snapshot.anchorRowIndex !== null
      && snapshot.anchorOffsetTop !== null) {
    const anchor = root.querySelector<HTMLElement>(
      `[data-changes-row-index="${snapshot.anchorRowIndex}"]`);
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
