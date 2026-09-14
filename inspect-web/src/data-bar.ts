import type { BrowserBuildIdentity } from "./facades/inspect-web-host.d.ts";
import { ROUTED_ENTRY_PATHS } from "./entry-routes.ts";

export const CLI_TOOL_URL =
  "https://www.nuget.org/packages/dotnet-inspect";
export const AGENT_SKILL_URL =
  "https://github.com/richlander/dotnet-skills";

export interface DataBarProducer {
  kind: "package" | "acquisition";
  label: string;
}

export interface DataBarModel {
  buildIdentity?: BrowserBuildIdentity | null;
  producer?: DataBarProducer | null;
}

export function fmtBytes(bytes: number | null | undefined): string {
  if (!bytes) return "—";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value < 10 && unit > 0 ? 1 : 0)} ${units[unit]}`;
}

function compactUtcDate(value: string | null): string {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.toLocaleDateString("en-US", {
    day: "numeric",
    month: "short",
    timeZone: "UTC",
    year: "numeric",
  })} UTC`;
}

function buildIdentityItems(
  identity: BrowserBuildIdentity | null | undefined,
  escapeHtml: (value: string) => string,
): string[] {
  const version = identity?.version.trim() ?? "";
  const items = [
    `<span class="data-bar-item data-bar-product">dotnet-inspect${version
      ? ` v${escapeHtml(version)}`
      : ""}</span>`,
  ];
  if (!identity) return items;

  const commit = identity.commit?.trim() ?? "";
  if (commit) {
    const shortCommit = commit.slice(0, 7);
    const commitHtml = identity.commitUrl
      ? `<a href="${escapeHtml(identity.commitUrl)}" target="_blank" rel="noopener noreferrer">${escapeHtml(shortCommit)}</a>`
      : escapeHtml(shortCommit);
    items.push(
      `<span class="data-bar-item data-bar-commit">${commitHtml}</span>`,
    );
  }

  const builtAt = compactUtcDate(identity.builtAtUtc);
  if (builtAt) {
    items.push(
      `<span class="data-bar-item data-bar-date">${escapeHtml(builtAt)}</span>`,
    );
  }
  return items;
}

export function dataBarHtml(
  model: DataBarModel,
  escapeHtml: (value: string) => string,
): string {
  const items = buildIdentityItems(model.buildIdentity, escapeHtml);
  const producerLabel = model.producer?.label.trim() ?? "";
  if (model.producer && producerLabel) {
    items.push(
      `<span class="data-bar-item data-bar-producer">${model.producer.kind === "package"
        ? "Package source: "
        : ""}${escapeHtml(producerLabel)}</span>`,
    );
  }
  items.push(
    `<a class="data-bar-item data-bar-action" href="${CLI_TOOL_URL}" target="_blank" rel="noopener noreferrer">CLI tool</a>`,
    `<a class="data-bar-item data-bar-action" href="${AGENT_SKILL_URL}" target="_blank" rel="noopener noreferrer">Agent skill</a>`,
    `<a class="data-bar-item data-bar-action" href="${ROUTED_ENTRY_PATHS.credits}">Credits</a>`,
  );

  const separator =
    ' <span class="data-bar-separator" aria-hidden="true">·</span> ';
  return `<footer class="data-bar" aria-label="Product information">
    ${items.join(separator)}
  </footer>`;
}
