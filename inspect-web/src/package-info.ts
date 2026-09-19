import type {
  BrowserPackageInfoMeasurementInspection,
} from "./facades/inspect-web-package.d.ts";

function formatBytes(value: number | null): string {
  if (value === null || !Number.isFinite(value) || value < 0)
    return "Unavailable";
  if (value === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let amount = value;
  let unit = 0;
  while (amount >= 1024 && unit < units.length - 1) {
    amount /= 1024;
    unit += 1;
  }
  const digits = Number.isInteger(amount)
    ? 0
    : amount < 10 && unit > 0
      ? 1
      : 0;
  return `${amount.toFixed(digits)} ${units[unit]}`;
}

function formatStatus(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function renderPackageInfo(
  inspection: BrowserPackageInfoMeasurementInspection,
  escapeHtml: (value: unknown) => string,
): string {
  const content = inspection.content;
  const rows: Array<readonly [string, string]> = [];
  if (content.compressedPackageBytes !== null) {
    rows.push([
      "Package Size (compressed)",
      formatBytes(content.compressedPackageBytes),
    ]);
  }
  if (content.hasSelectedSlice) {
    rows.push(
      ["Selected TFM", content.selectedTargetFramework ?? "Unavailable"],
      [
        "Selected-TFM Folders",
        content.selectedTargetFrameworkFolders?.join(", ") || "None",
      ],
      [
        "Selected-TFM Library Count",
        content.selectedLibraryCount?.toLocaleString("en-US") ?? "Unavailable",
      ],
      [
        "Selected-TFM Size",
        formatBytes(content.selectedLibraryPayloadBytes),
      ],
    );
  } else {
    rows.push(["Status", formatStatus(content.status)]);
    rows.push([
      "Detail",
      content.detail ?? content.unavailableReason ?? "Unavailable",
    ]);
  }
  if (content.availableTargetFrameworks !== null) {
    rows.push([
      "TFMs",
      content.availableTargetFrameworks.join(", ") || "None",
    ]);
  }

  return `<section class="document-section package-info-section">
    <div class="section-title"><h2>Package Info</h2><span>${escapeHtml(formatStatus(content.status))}</span></div>
    <dl class="fact-rows package-info-rows">${rows.map(([label, value]) =>
      `<div><dt>${escapeHtml(label)}</dt><dd><code>${escapeHtml(value)}</code></dd></div>`).join("")}</dl>
  </section>`;
}
