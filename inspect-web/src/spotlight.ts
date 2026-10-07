import { publicationDateText, type PublicationDate } from "./package-publication.ts";
import {
  commandPaletteResults,
  commandPaletteRowHtml,
  type CommandContext,
  type CommandPaletteResult,
} from "./command-bar.ts";
import type { KeybindingRegistry } from "./keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";
import { packageRemoveButton } from "./package-removal.ts";
import {
  replaceChildrenPreservingRenderedInteractions,
} from "./rendered-interaction.ts";
import type {
  BrowserCapabilityCatalogSearchResult,
  BrowserCapabilityCatalogSearchResourceKind,
} from "./facades/inspect-web-package.d.ts";

type LensDefinition = readonly [id: string, label: string];
type SpotlightFocus = "input" | "chips";

type HighlightRange = readonly [start: number, end: number];

interface SpotlightPackage {
  id: string;
  version: string;
  activeFramework?: string;
  isRuntimePack?: boolean;
}

interface SpotlightType {
  id: string;
  name: string;
  namespace?: string;
  kind: string;
}

export interface SpotlightEcosystemAnnotation {
  id: string;
  title: string;
  isPruned: boolean | null;
  traversalTfm: string;
  platformVersion: string | null;
  platformVersionComparison?: number | null;
}

interface PackageLoadedResult {
  publication?: PublicationDate;
  ecosystem?: SpotlightEcosystemAnnotation;
  kind: "pkg-loaded";
  pkg: SpotlightPackage;
  ranges: readonly HighlightRange[];
}

export interface SpotlightPackageHit {
  id: string;
  version?: string;
  exact?: boolean;
}

interface PackageNugetResult {
  publication?: PublicationDate;
  ecosystem?: SpotlightEcosystemAnnotation;
  kind: "pkg-nuget";
  hit: SpotlightPackageHit;
  ranges: readonly HighlightRange[];
}

interface PackageRecentResult {
  publication?: PublicationDate;
  ecosystem?: SpotlightEcosystemAnnotation;
  kind: "pkg-recent";
  entry: { id: string; version?: string; framework?: string; nugetOrg?: boolean };
  ranges: readonly HighlightRange[];
}

interface PackageQueryResult {
  kind: "package-query";
  prefix: string;
}

interface PackageActivityResult {
  kind: "package-activity";
}

export interface SpotlightCapabilityResult {
  kind: "capability";
  query: string;
  capability: BrowserCapabilityCatalogSearchResult;
  ranges: readonly HighlightRange[];
}

interface FrameworkLibraryResult {
  inReferencePack?: boolean;
  publication?: PublicationDate;
  kind: "framework-lib";
  assembly: string;
  pack: string;
  publicTypes: number;
  loaded?: boolean;
  ranges: readonly HighlightRange[];
  tfm?: string;
  version?: string;
  role?: string;
}

interface TypeResult {
  kind: "type";
  pkg: SpotlightPackage;
  type: SpotlightType;
  ranges: readonly HighlightRange[];
}

export interface ManagedTypeResult {
  kind: "managed-type";
  identity: string;
  action: string | null;
  reason: string | null;
  name: string;
  namespace: string;
  library: string;
  source: string;
  typeKind: string;
  ranges: readonly HighlightRange[];
}

interface MemberResult {
  kind: "member";
  pkg: SpotlightPackage;
  type: SpotlightType;
  memberKey: string;
  name: string;
  ranges: readonly HighlightRange[];
}

export type SpotlightPackageResult =
  | PackageLoadedResult
  | PackageNugetResult
  | PackageRecentResult;

export type SpotlightResult =
  | CommandPaletteResult
  | SpotlightPackageResult
  | PackageQueryResult
  | PackageActivityResult
  | SpotlightCapabilityResult
  | FrameworkLibraryResult
  | TypeResult
  | ManagedTypeResult
  | MemberResult;

export type RemovableSpotlightResult = PackageLoadedResult | PackageRecentResult;

export interface SpotlightState {
  spotlightOpen: boolean;
  spotlightQuery: string;
  spotlightIndex: number;
  spotlightScope: SpotlightScope;
  spotlightFocus: SpotlightFocus;
  spotlightChipIndex: number;
}

interface SpotlightOptions {
  keybindings: KeybindingRegistry;
  state: SpotlightState;
  lenses: () => readonly LensDefinition[];
  escapeHtml: (value: unknown) => string;
  highlightRanges: (
    value: string,
    ranges: readonly HighlightRange[],
  ) => string;
  kindIcon: (kind: string) => string;
  searchResults: () => SpotlightResult[];
  pickResult: (result: SpotlightResult) => void;
  removeResult?: (result: RemovableSpotlightResult) => boolean;
  executeCommand: (
    command: string,
    result: CommandPaletteResult,
  ) => Promise<unknown> | undefined;
  reportCommandError: (error: unknown) => void;
  commandContext: () => CommandContext | null;
  prepareResults?: () => void;
  schedulePackageFetch: () => void;
  resetPackageSearch: () => void;
  resetTypeSearch?: () => void;
  packageSearchLoading: () => boolean;
  packageSearchError?: () => string;
  ecosystemError?: () => string;
  typeSearchLoading?: () => boolean;
  typeSearchError?: () => string;
  typeSearchNotice?: () => string;
  scheduleCapabilitySearch: () => void;
  resetCapabilitySearch: () => void;
  capabilitySearchMessage?: () => string;
  packageCount: () => number;
  render: () => void;
  focusAfterDismiss?: () => void;
  captureFocusAfterDismiss?: () => () => void;
}

interface PackageAdditionOptions {
  pickResult: (result: SpotlightPackageResult) => void;
  focusAfterDismiss: () => void;
}

const BASE_SCOPES = [
  { id: "all", label: "All" },
  { id: "packages", label: "Packages" },
  { id: "libraries", label: "Libraries" },
  { id: "types", label: "Types" },
  { id: "members", label: "Members" },
] as const;

const COMMAND_SCOPE = { id: "commands", label: "Commands" } as const;

export type SpotlightScope =
  | (typeof BASE_SCOPES)[number]["id"]
  | typeof COMMAND_SCOPE.id;
const PLATFORM_PACK_LABEL: Readonly<Record<string, string>> = {
  "netcore.app": ".NET Runtime",
  "netstandard": ".NET Runtime",
  "aspnetcore.app": "ASP.NET Core",
};
const GROUP_LABELS: Readonly<Record<SpotlightResult["kind"], string>> = {
  command: "Commands",
  "pkg-recent": "Recent",
  "package-query": "Query",
  "package-activity": "Query",
  capability: "Capabilities",
  "pkg-loaded": "Packages",
  "pkg-nuget": "Packages",
  type: "Types",
  "managed-type": "Types",
  member: "Members",
  "framework-lib": "Ecosystem",
};

export function nextSpotlightSelection(
  current: number,
  delta: number,
  count: number,
): number | null {
  if (count <= 0) return null;
  const next = current + delta;
  return next < 0 ? null : Math.min(count - 1, next);
}

export function nextSpotlightScope(
  current: number,
  count: number,
  backward: boolean,
): number {
  if (count <= 0) return 0;
  return backward
    ? (current - 1 + count) % count
    : (current + 1) % count;
}

export function spotlightResultIdentity(result: SpotlightResult): string {
  switch (result.kind) {
    case "command":
      return JSON.stringify([
        result.kind,
        result.action,
        result.command,
        result.targetTypeId ?? "",
      ]);
    case "pkg-loaded":
      return JSON.stringify([
        result.kind,
        result.pkg.id,
        result.pkg.version,
        result.pkg.activeFramework ?? "",
      ]);
    case "pkg-nuget":
      return JSON.stringify([
        result.kind,
        result.hit.id,
        result.hit.version ?? "",
        result.hit.exact === true,
      ]);
    case "pkg-recent":
      return JSON.stringify([
        result.kind,
        result.entry.id,
        result.entry.version ?? "",
        result.entry.framework ?? "",
      ]);
    case "package-query":
      return JSON.stringify([result.kind, result.prefix]);
    case "package-activity":
      return JSON.stringify([result.kind]);
    case "capability":
      return JSON.stringify([result.kind, result.capability.resourcePath]);
    case "framework-lib":
      return JSON.stringify([result.kind, result.tfm ?? "", result.version ?? "", result.pack, result.assembly]);
    case "type":
      return JSON.stringify([
        result.kind,
        result.pkg.id,
        result.pkg.version,
        result.pkg.activeFramework ?? "",
        result.type.id,
      ]);
    case "managed-type":
      return JSON.stringify([result.kind, result.identity]);
    case "member":
      return JSON.stringify([
        result.kind,
        result.pkg.id,
        result.pkg.version,
        result.pkg.activeFramework ?? "",
        result.type.id,
        result.memberKey,
      ]);
    default:
      throw new Error("Unknown Spotlight result.");
  }
}

export function spotlightCapabilityDraftValue(
  result: SpotlightCapabilityResult,
): string {
  return result.capability.matchSource === "ExampleValue"
    ? result.query
    : "";
}

export function distinctSpotlightResults(
  results: readonly SpotlightResult[],
): SpotlightResult[] {
  const identities = new Set<string>();
  return results.filter(result => {
    const identity = spotlightResultIdentity(result);
    if (identities.has(identity)) return false;
    identities.add(identity);
    return true;
  });
}

function isTextInputTarget(value: EventTarget | null): value is HTMLInputElement {
  return value !== null
    && "selectionStart" in value
    && "selectionEnd" in value
    && "value" in value;
}

function hasElementId(value: EventTarget | null): value is EventTarget & { id: string } {
  return value !== null && "id" in value && typeof value.id === "string";
}

function isPackageAdditionResult(result: SpotlightResult): result is SpotlightPackageResult {
  return result.kind === "pkg-nuget"
    || result.kind === "pkg-recent"
    || (result.kind === "pkg-loaded" && !result.pkg.isRuntimePack);
}

function capabilityKindLabel(
  kind: BrowserCapabilityCatalogSearchResourceKind,
): string {
  switch (kind) {
    case "InspectionDocument":
      return "Inspection document";
    case "HostNeutralRoute":
      return "Route";
    case "QuerySpace":
      return "Query space";
    case "QueryFacet":
      return "Query facet";
    case "ConsumerBinding":
      return "Consumer binding";
    default:
      return "Capability";
  }
}

function isEcosystemResult(result: SpotlightResult): boolean {
  return result.kind === "framework-lib"
    || ((result.kind === "pkg-loaded" || result.kind === "pkg-nuget" || result.kind === "pkg-recent")
      && result.ecosystem !== undefined);
}

function ecosystemName(result: SpotlightResult): string | null {
  switch (result.kind) {
    case "framework-lib": return result.assembly.toLowerCase();
    case "pkg-loaded": return result.pkg.id.toLowerCase();
    case "pkg-nuget": return result.hit.id.toLowerCase();
    case "pkg-recent": return result.entry.id.toLowerCase();
    default: return null;
  }
}

function orderEcosystemPairs(results: SpotlightResult[]): SpotlightResult[] {
  const buckets = new Map<string, SpotlightResult[]>();
  for (const result of results) {
    const name = ecosystemName(result);
    if (name) buckets.set(name, [...(buckets.get(name) ?? []), result]);
  }
  for (const bucket of buckets.values()) {
    // A pair is two distinct subjects, not a new global search rank.
    if (bucket.length !== 2) continue;
    const library = bucket.find(result => result.kind === "framework-lib");
    const pkg = bucket.find(result => result.kind === "pkg-loaded" || result.kind === "pkg-nuget" || result.kind === "pkg-recent");
    if (!library || library.kind !== "framework-lib" || !pkg || !("ecosystem" in pkg)) continue;
    const evidence = pkg.ecosystem;
    if (!evidence || evidence.traversalTfm !== library.tfm || evidence.platformVersion !== library.version) continue;
    const comparison = evidence.platformVersionComparison;
    if (evidence.isPruned === true || comparison !== undefined && comparison !== null) {
      bucket.splice(0, 2, ...(evidence.isPruned === true || comparison! <= 0 ? [library, pkg] : [pkg, library]));
    }
  }
  // Preserve positions of unrelated names and observations.
  return results.map(result => {
    const name = ecosystemName(result);
    return name ? buckets.get(name)!.shift()! : result;
  });
}

function sentenceCase(value: string): string {
  return value.length === 0
    ? value
    : `${value[0]!.toUpperCase()}${value.slice(1)}`;
}

export function createSpotlight(options: SpotlightOptions) {
  let boundInput: HTMLInputElement | null = null;
  const { state, escapeHtml } = options;
  let interactionGeneration = 0;
  let renderedResults: readonly SpotlightResult[] = [];
  let renderedResultsByIdentity = new Map<string, SpotlightResult>();
  let selectedResultIdentity: string | null = null;
  const boundResultControls = new WeakSet<HTMLElement>();
  const boundRemoveControls = new WeakSet<HTMLElement>();
  const boundModalBackdrops = new WeakSet<Element>();
  const dismissedPackageIds = new Set<string>();
  let dismissalQuery = state.spotlightQuery;
  let packageAddition: PackageAdditionOptions | null = null;

  function scopes() {
    if (packageAddition) return BASE_SCOPES.filter(scope => scope.id === "packages");
    return options.commandContext()
      ? [...BASE_SCOPES, COMMAND_SCOPE]
      : [...BASE_SCOPES];
  }

  function results(): SpotlightResult[] {
    if (packageAddition) {
      return distinctSpotlightResults(options.searchResults())
        .filter(isPackageAdditionResult);
    }
    if (state.spotlightScope === "commands") {
      const context = options.commandContext();
      return context
        ? distinctSpotlightResults(commandPaletteResults(context, options.lenses()))
        : [];
    }
    const searchResults = distinctSpotlightResults(options.searchResults());
    if (dismissalQuery !== state.spotlightQuery) {
      dismissedPackageIds.clear();
      dismissalQuery = state.spotlightQuery;
    }
    const visible = searchResults.filter(result =>
      result.kind !== "pkg-nuget"
      || !dismissedPackageIds.has(result.hit.id.toLowerCase()));
    if (state.spotlightScope === "libraries") return visible.filter(result => result.kind === "framework-lib");
    if (state.spotlightScope !== "all") return visible;
    // Stable partition: separate Package and Library subjects retain their identities.
    return [
      ...orderEcosystemPairs(visible.filter(isEcosystemResult)),
      ...visible.filter(result => !isEcosystemResult(result)),
    ];
  }

  function removable(result: SpotlightResult): result is RemovableSpotlightResult {
    return !packageAddition && options.removeResult !== undefined
      && (result.kind === "pkg-recent"
        || (result.kind === "pkg-loaded" && !result.pkg.isRuntimePack));
  }

  function withRemoveButton(
    result: SpotlightResult,
    row: string,
  ): string {
    if (!removable(result)) return row;
    const identity = spotlightResultIdentity(result);
    const label = result.kind === "pkg-recent"
      ? `Forget ${result.entry.id} from recent packages`
      : `Remove ${result.pkg.id} ${result.pkg.version} ${result.pkg.activeFramework ?? ""} from Workspace`;
    return `<div class="package-search-row" role="presentation">${row}${packageRemoveButton(
      "data-sl-remove", identity, label, escapeHtml)}</div>`;
  }

  function ecosystemMetadata(result: SpotlightPackageResult): string {
    const annotation = result.ecosystem;
    if (!annotation) return "";
    return `${escapeHtml(annotation.title)} · `;
  }

  function artifactIcon(
    kind: "Package" | "Library",
    ecosystem: { id: string; title: string } | undefined,
    pruning: { traversalTfm: string; platformVersion: string | null } | undefined,
  ): string {
    if (kind === "Package" && pruning) {
      const label = "Package pruned";
      return `<span class="spotlight-svg-icon spotlight-pruned" role="img" aria-label="${label} for ${escapeHtml(pruning.traversalTfm)}" title="${escapeHtml(`Supplied by ${pruning.traversalTfm}${pruning.platformVersion ? ` @ ${pruning.platformVersion}` : ""}; eligible for package pruning`)}"></span>`;
    }
    const classes: Readonly<Record<string, string>> = {
      "ecosystem.runtime": "sl-ecosystem-runtime",
      "ecosystem.aspnetcore": "sl-ecosystem-aspnetcore",
      "ecosystem.microsoft-extensions": "sl-ecosystem-extensions",
      "ecosystem.aspire": "sl-ecosystem-aspire",
    };
    const icon = ecosystem ? classes[ecosystem.id] : undefined;
    return icon
      ? `<span class="spotlight-icon-slot spotlight-ecosystem-icon ${icon}" role="img" aria-label="${kind}: ${escapeHtml(ecosystem?.title)}" title="${escapeHtml(ecosystem?.title)}"></span>`
      : `<span class="spotlight-svg-icon ${kind === "Package" ? "sl-package-icon" : "sl-library-icon"}" role="img" aria-label="${kind}"></span>`;
  }

  function packageIcon(result: SpotlightPackageResult): string {
    return artifactIcon("Package", result.ecosystem,
      result.ecosystem?.isPruned === true ? result.ecosystem : undefined);
  }

  function rowHtml(result: SpotlightResult, index: number): string {
    const selected = index === state.spotlightIndex;
    const identity = spotlightResultIdentity(result);
    if (result.kind === "command") {
      return commandPaletteRowHtml(
        result,
        index,
        selected,
        identity,
        escapeHtml,
      );
    }

    const selectedClass = selected ? "selected" : "";
    const artifactClass = result.kind === "pkg-loaded" || result.kind === "pkg-nuget"
      || result.kind === "pkg-recent" || result.kind === "framework-lib"
      ? ` spotlight-artifact${"publication" in result && result.publication ? " has-publication" : ""}` : "";
    const escapedIdentity = escapeHtml(identity);
    const base = `id="spotlight-result-${index}" class="spotlight-item${artifactClass} ${selectedClass}" role="option" aria-selected="${selected}" data-sl-index="${index}" data-sl-result-identity="${escapedIdentity}" data-rendered-interaction-key="spotlight-result:${escapedIdentity}"${packageAddition ? ' tabindex="-1"' : ""}`;
    const dateHtml = "publication" in result && result.publication
      ? `<span class="spotlight-item-date"${result.publication.status === "unavailable" ? ` title="${escapeHtml(result.publication.reason)}"` : ""}>${result.publication.status === "available" ? `<time datetime="${escapeHtml(result.publication.date)}" aria-label="${escapeHtml(publicationDateText(result.publication))}" title="${escapeHtml(publicationDateText(result.publication))}">${escapeHtml(result.publication.date)}</time>` : escapeHtml(publicationDateText(result.publication))}</span>`
      : "";
    if (result.kind === "pkg-loaded") {
      return withRemoveButton(result, `<button ${base} data-sl-pkg-open="${escapeHtml(result.pkg.id)}">
        ${packageIcon(result)}
        <span class="spotlight-item-name">${options.highlightRanges(result.pkg.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${ecosystemMetadata(result)}${escapeHtml(result.pkg.version)} · ${packageAddition ? "already in Workspace" : "open"}</span>
        ${dateHtml}
      </button>`);
    }
    if (result.kind === "pkg-nuget") {
      const source = result.hit.exact
        ? "exact coordinate · listed or unlisted"
        : "nuget.org";
      return `<button ${base} data-sl-pkg-load="${escapeHtml(result.hit.id)}" data-sl-pkg-version="${escapeHtml(result.hit.version || "")}">
        ${packageIcon(result)}
        <span class="spotlight-item-name">${options.highlightRanges(result.hit.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${ecosystemMetadata(result)}${escapeHtml(result.hit.version || "")} · ${source}</span>
        ${dateHtml}
      </button>`;
    }
    if (result.kind === "pkg-recent") {
      const version = result.entry.version && result.entry.version !== "latest"
        ? result.entry.version
        : "";
      return withRemoveButton(result, `<button ${base} data-sl-pkg-recent="${escapeHtml(result.entry.id)}">
        ${packageIcon(result)}
        <span class="spotlight-item-name">${options.highlightRanges(result.entry.id, result.ranges)}</span>
        <span class="spotlight-item-ns">${ecosystemMetadata(result)}${version ? `${escapeHtml(version)} · ` : ""}recent</span>
        ${dateHtml}
      </button>`);
    }
    if (result.kind === "package-query") {
      const suffix = result.prefix
        ? `Start with “${escapeHtml(result.prefix)}”`
        : "Choose a package ID prefix and inspection facts";
      return `<button ${base} data-sl-package-query="1">
        <span class="kind-icon sl-command">⌕</span>
        <span class="spotlight-item-name">Package query</span>
        <span class="spotlight-item-ns">${suffix}</span>
      </button>`;
    }
    if (result.kind === "package-activity") {
      return `<button ${base} data-sl-package-activity="1">
        <span class="kind-icon sl-command">↻</span>
        <span class="spotlight-item-name">Package Activity</span>
        <span class="spotlight-item-ns">Review product package changes over time</span>
      </button>`;
    }
    if (result.kind === "capability") {
      const capability = result.capability;
      const route = capability.owningRoutes[0]?.name;
      const key = capability.canonicalKeys[0];
      const metadata = [
        capabilityKindLabel(capability.resourceKind),
        route,
        key,
      ].filter(value => value !== undefined && value.length > 0).join(" · ");
      return `<button ${base} data-sl-capability="${escapeHtml(capability.resourcePath)}">
        <span class="kind-icon sl-capability">◇</span>
        <span class="spotlight-item-name">${options.highlightRanges(sentenceCase(capability.resourceName), result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(metadata)}</span>
      </button>`;
    }
    if (result.kind === "framework-lib") {
      const label = PLATFORM_PACK_LABEL[result.pack] || result.pack;
      const types = `${result.publicTypes} type${result.publicTypes === 1 ? "" : "s"}`;
      const meta = `${label}${result.tfm ? ` · ${result.tfm}` : ""}${result.version ? ` · ${result.version}` : ""} · ${result.role ?? types}${result.loaded ? " · loaded" : ""}`;
      return `<button ${base} data-sl-framework-lib="${escapeHtml(result.assembly)}" data-sl-framework-pack="${escapeHtml(result.pack)}">
        ${artifactIcon("Library", result.pack === "netcore.app" || result.pack === "aspnetcore.app" || result.pack === "netstandard"
          ? { id: result.pack === "aspnetcore.app" ? "ecosystem.aspnetcore" : "ecosystem.runtime",
              title: result.pack === "aspnetcore.app" ? "ASP.NET Core" : ".NET Runtime" }
          : undefined, undefined)}
        <span class="spotlight-item-name">${options.highlightRanges(result.assembly, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(meta)}</span>
        ${dateHtml}
      </button>`;
    }
    if (result.kind === "member") {
      const packageName = options.packageCount() > 1
        ? ` · ${escapeHtml(result.pkg.id)}`
        : "";
      return `<button ${base} data-sl-member="${escapeHtml(result.memberKey)}" data-sl-pkg="${escapeHtml(result.pkg.id)}" data-sl-type="${escapeHtml(result.type.id)}">
        <span class="kind-icon sl-member">ƒ</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.name, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(result.type.name)}${packageName}</span>
        ${dateHtml}
      </button>`;
    }
    if (result.kind === "managed-type") {
      const source = [result.namespace, result.library, result.source]
        .filter(Boolean)
        .join(" · ");
      const unavailable = result.action === null
        ? ` aria-disabled="true" title="${escapeHtml(
            result.reason ?? "This Type is unavailable.",
          )}"`
        : "";
      return `<button ${base}${unavailable} data-sl-managed-type="${escapedIdentity}">
        <span class="kind-icon">${options.kindIcon(result.typeKind)}</span>
        <span class="spotlight-item-name">${options.highlightRanges(result.name, result.ranges)}</span>
        <span class="spotlight-item-ns">${escapeHtml(source)}</span>
      </button>`;
    }

    const packageName = options.packageCount() > 1
      ? ` · ${escapeHtml(result.pkg.id)}`
      : "";
    return `<button ${base} data-sl-type="${escapeHtml(result.type.id)}" data-sl-pkg="${escapeHtml(result.pkg.id)}">
      <span class="kind-icon">${options.kindIcon(result.type.kind)}</span>
      <span class="spotlight-item-name">${options.highlightRanges(result.type.name, result.ranges)}</span>
      <span class="spotlight-item-ns">${escapeHtml(result.type.namespace || "")}${packageName}</span>
    </button>`;
  }

  function resultsHtml(items: readonly SpotlightResult[]): string {
    const packageSearch = state.spotlightScope === "all"
      || state.spotlightScope === "packages";
    const typeSearch = state.spotlightScope === "all"
      || state.spotlightScope === "types";
    const packageError = packageSearch ? options.packageSearchError?.() : "";
    const typeError = typeSearch ? options.typeSearchError?.() : "";
    const capabilityMessage = state.spotlightScope === "all"
      ? options.capabilitySearchMessage?.()
      : "";
    const errorHtml = [packageError, typeError, capabilityMessage, packageSearch ? options.ecosystemError?.() : ""]
      .filter(message => Boolean(message))
      .map(message =>
        `<div class="spotlight-hint" role="status">${escapeHtml(message)}</div>`)
      .join("");
    const typeNotice = typeSearch ? options.typeSearchNotice?.() ?? "" : "";
    const noticeHtml = typeNotice
      ? `<div class="spotlight-hint" role="status">${escapeHtml(typeNotice)}</div>`
      : "";
    if (!items.length) {
      if (errorHtml) return errorHtml;
      const query = state.spotlightQuery.trim();
      if (state.spotlightScope === "commands") {
        return `<div class="spotlight-empty">${query
          ? `No command matches “${escapeHtml(query)}”.`
          : "Choose a command to run in the current workspace."}</div>`;
      }
      if (!query) {
        if (packageAddition) {
          return '<div class="spotlight-empty">Search NuGet or enter PackageId@Version to add an exact coordinate.</div>';
        }
        return '<div class="spotlight-empty">Search packages, types, and members, or enter PackageId@Version.</div>';
      }
      if ((packageSearch && options.packageSearchLoading())
        || (typeSearch && options.typeSearchLoading?.())) {
        return '<div class="spotlight-empty">Searching…</div>';
      }
      const empty = typeNotice
        ? `No confirmed matches for “${escapeHtml(query)}”.`
        : `Nothing matches “${escapeHtml(query)}”.`;
      return `<div class="spotlight-empty">${empty}</div>${noticeHtml}`;
    }

    const grouped = state.spotlightScope === "all";
    let html = "";
    let lastGroup = "";
    items.forEach((result, index) => {
      if (grouped) {
        const group = isEcosystemResult(result) ? "Ecosystem" : GROUP_LABELS[result.kind];
        if (group && group !== lastGroup) {
          html += `<div class="spotlight-group">${group}</div>`;
          lastGroup = group;
        }
      }
      html += rowHtml(result, index);
    });
    html += errorHtml;
    html += noticeHtml;
    if (!packageError && options.packageSearchLoading() && packageSearch) {
      html += '<div class="spotlight-hint">Searching nuget.org…</div>';
    }
    if (options.typeSearchLoading?.() && typeSearch) {
      html += '<div class="spotlight-hint">Searching Workspace Types…</div>';
    }
    return html;
  }

  function chipsHtml(): string {
    if (packageAddition) return "";
    return scopes().map((scope, index) => {
      const active = state.spotlightScope === scope.id ? "active" : "";
      const focused = state.spotlightFocus === "chips"
        && state.spotlightChipIndex === index
        ? "focused"
        : "";
      return `<button class="spotlight-chip ${active} ${focused}" data-sl-scope="${scope.id}" data-sl-chip="${index}">${scope.label}</button>`;
    }).join("");
  }

  function clampSelection(items: readonly SpotlightResult[]): void {
    state.spotlightIndex = Math.min(
      state.spotlightIndex,
      Math.max(items.length - 1, 0),
    );
  }

  function rememberSelection(items: readonly SpotlightResult[]): void {
    const selected = items[state.spotlightIndex];
    selectedResultIdentity = selected
      ? spotlightResultIdentity(selected)
      : null;
  }

  function restoreSelection(items: readonly SpotlightResult[]): void {
    if (selectedResultIdentity) {
      const index = items.findIndex(
        item => spotlightResultIdentity(item) === selectedResultIdentity,
      );
      if (index >= 0) state.spotlightIndex = index;
    }
    clampSelection(items);
    rememberSelection(items);
  }

  function resultsForRender(): readonly SpotlightResult[] {
    const items = results();
    restoreSelection(items);
    renderedResults = items;
    renderedResultsByIdentity = new Map(
      items.map(item => [spotlightResultIdentity(item), item]),
    );
    return items;
  }

  function activeDescendantAttribute(items: readonly SpotlightResult[]): string {
    return items.length
      ? ` aria-activedescendant="spotlight-result-${state.spotlightIndex}"`
      : "";
  }

  function syncActiveDescendant(count: number): void {
    const input = document.querySelector<HTMLInputElement>("#spotlight-input");
    if (!input) return;
    if (count > 0) {
      input.setAttribute(
        "aria-activedescendant",
        `spotlight-result-${state.spotlightIndex}`,
      );
    } else {
      input.removeAttribute("aria-activedescendant");
    }
  }

  function modalHtml(): string {
    const items = resultsForRender();
    const commands = state.spotlightScope === "commands";
    const name = packageAddition ? "Add package" : commands ? "Run a command" : "Go to anything";
    const placeholder = packageAddition
      ? "Search NuGet or enter PackageId@Version…"
      : commands
        ? "Run a command…"
        : "Go to anything… package, type, member, or PackageId@Version";
    return `
      <div class="spotlight-backdrop" id="spotlight-backdrop">
        <div class="spotlight" role="dialog" aria-modal="true" aria-label="${name}">
          ${packageAddition ? '<div class="spotlight-foot"><strong>Add package</strong></div>' : ""}
          <div class="spotlight-search">
            <span class="spotlight-glyph">${commands ? "›" : "⌕"}</span>
            <input id="spotlight-input"${packageAddition ? ' aria-label="Add package"' : ""} value="${escapeHtml(state.spotlightQuery)}" placeholder="${placeholder}" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results"${activeDescendantAttribute(items)} />
            <kbd>esc</kbd>
          </div>
          ${packageAddition ? "" : `<div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>`}
          <div class="spotlight-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
          <div class="spotlight-foot">${packageAddition
            ? '<span>↑↓ select</span><span>Add <kbd>Enter</kbd></span><span>esc cancel</span><button type="button" id="spotlight-cancel">Cancel</button>'
            : `<span><kbd>Ctrl P</kbd> search</span><span>↑↓ select</span><span>→ target</span><span>↵ ${commands ? "complete / run" : "open"}</span>${options.removeResult && !commands ? "<span>Shift+Delete remove</span>" : ""}<span>esc close</span>`}</div>
        </div>
      </div>`;
  }

  function inlineHtml(disabled: boolean, showReadyGlint = false): string {
    const items = resultsForRender();
    return `
      <div class="home-search-content" ${disabled ? "inert" : ""}>
        <div class="home-search-box">
          ${showReadyGlint ? `<svg class="home-search-glint" aria-hidden="true">
            <rect class="home-search-glint-glow" pathLength="1"></rect>
            <rect class="home-search-glint-line" pathLength="1"></rect>
          </svg>` : ""}
          <span class="spotlight-glyph">⌕</span>
          <input id="spotlight-input" value="${escapeHtml(state.spotlightQuery)}" placeholder="Search NuGet — package, type, member, or PackageId@Version…" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="spotlight-results"${activeDescendantAttribute(items)} ${disabled ? "disabled" : ""} />
        </div>
        <div class="spotlight-chips" id="spotlight-chips">${chipsHtml()}</div>
        <div class="spotlight-results home-results" id="spotlight-results" role="listbox">${resultsHtml(items)}</div>
      </div>`;
  }

  function bindChipClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-sl-scope]").forEach(button => {
      button.addEventListener("click", () => {
        const scope = availableScope(button.dataset.slScope);
        if (scope !== null) setScope(scope);
      });
    });
  }

  function bindResultClicks(root: ParentNode): void {
    root.querySelectorAll<HTMLElement>("[data-sl-result-identity]").forEach(item => {
      if (boundResultControls.has(item)) return;
      boundResultControls.add(item);
      const identity = item.dataset.slResultIdentity;
      if (!identity) return;
      item.addEventListener("click", () => {
        const result = renderedResultsByIdentity.get(identity);
        if (result) pick(result);
      });
    });
    root.querySelectorAll<HTMLElement>("[data-sl-remove]").forEach(button => {
      if (boundRemoveControls.has(button)) return;
      boundRemoveControls.add(button);
      const identity = button.dataset.slRemove;
      if (!identity) return;
      button.addEventListener("click", () => {
        const result = renderedResultsByIdentity.get(identity);
        if (result) removeResult(result);
      });
    });
  }

  function removeResult(result: SpotlightResult | undefined): boolean {
    if (!result || !removable(result)) return false;
    const input = document.querySelector<HTMLInputElement>("#spotlight-input");
    const start = input?.selectionStart ?? state.spotlightQuery.length;
    const end = input?.selectionEnd ?? start;
    if (!options.removeResult?.(result)) return true;
    dismissedPackageIds.add((result.kind === "pkg-loaded"
      ? result.pkg.id : result.entry.id).toLowerCase());
    updateResults();
    const replacement = document.querySelector<HTMLInputElement>("#spotlight-input");
    replacement?.focus({ preventScroll: true });
    replacement?.setSelectionRange(start, end);
    return true;
  }

  function removeResultAt(index: number): boolean {
    return removeResult(renderedResults[index]);
  }

  function focus(selection?: {
    start: number | null;
    end: number | null;
    direction: "forward" | "backward" | "none" | null;
  }): void {
    requestAnimationFrame(() => {
      const input = document.querySelector<HTMLInputElement>("#spotlight-input");
      if (!input || document.activeElement === input) return;
      input.focus();
      input.setSelectionRange(
        selection?.start ?? input.value.length,
        selection?.end ?? input.value.length,
        selection?.direction ?? "none");
    });
  }

  function updateChips(): void {
    const container = document.querySelector<HTMLElement>("#spotlight-chips");
    if (!container) return;
    container.innerHTML = chipsHtml();
    bindChipClicks(container);
  }

  function updateResults(): void {
    const container = document.querySelector<HTMLElement>("#spotlight-results");
    if (!container) return;
    const items = resultsForRender();
    replaceChildrenPreservingRenderedInteractions(
      container,
      resultsHtml(items),
    );
    bindResultClicks(container);
    syncActiveDescendant(items.length);
    container.querySelector(".spotlight-item.selected")
      ?.scrollIntoView({ block: "nearest" });
  }

  function refresh(): void {
    updateChips();
    updateResults();
  }

  function availableScope(scope: string | undefined): SpotlightScope | null {
    return scopes().find(item => item.id === scope)?.id ?? null;
  }

  function setScope(scope: SpotlightScope): void {
    const available = scopes();
    if (!available.some(item => item.id === scope)) return;
    state.spotlightScope = scope;
    state.spotlightIndex = 0;
    selectedResultIdentity = null;
    options.schedulePackageFetch();
    options.scheduleCapabilitySearch();
    refresh();
    focus();
  }

  function reset(): void {
    packageAddition = null;
    boundInput = null;
    dismissedPackageIds.clear();
    options.resetPackageSearch();
    options.resetTypeSearch?.();
    options.resetCapabilitySearch();
    state.spotlightOpen = false;
    state.spotlightQuery = "";
    state.spotlightScope = "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
    renderedResults = [];
    renderedResultsByIdentity = new Map();
    selectedResultIdentity = null;
  }

  function close(): void {
    const focusAfterDismiss = packageAddition?.focusAfterDismiss ?? options.focusAfterDismiss;
    reset();
    options.render();
    focusAfterDismiss?.();
  }

  function open(seed = "", scope: SpotlightScope = "all"): void {
    openWithPurpose(seed, scope, null);
  }

  function openForPackageAddition(addition: PackageAdditionOptions): void {
    openWithPurpose("", "packages", addition);
  }

  function openWithPurpose(
    seed: string,
    scope: SpotlightScope,
    addition: PackageAdditionOptions | null,
  ): void {
    packageAddition = addition;
    boundInput = null;
    dismissedPackageIds.clear();
    interactionGeneration++;
    options.resetPackageSearch();
    options.resetTypeSearch?.();
    options.resetCapabilitySearch();
    state.spotlightOpen = true;
    state.spotlightQuery = seed;
    state.spotlightScope = availableScope(scope) ?? "all";
    state.spotlightFocus = "input";
    state.spotlightChipIndex = 0;
    state.spotlightIndex = 0;
    renderedResults = [];
    selectedResultIdentity = null;
    options.schedulePackageFetch();
    options.scheduleCapabilitySearch();
    options.render();
    focus();
  }

  function pick(result: SpotlightResult | undefined): void {
    if (packageAddition) {
      if (result && isPackageAdditionResult(result)) packageAddition.pickResult(result);
      return;
    }
    if (!result) {
      close();
      return;
    }
    if (result.kind !== "command") {
      options.pickResult(result);
      return;
    }
    if (result.action === "complete") {
      state.spotlightQuery = `${result.command} `;
      state.spotlightIndex = 0;
      selectedResultIdentity = null;
      options.prepareResults?.();
      const input = document.querySelector<HTMLInputElement>("#spotlight-input");
      if (input) input.value = state.spotlightQuery;
      updateResults();
      focus();
      return;
    }

    const generation = interactionGeneration;
    reset();
    const execution = options.executeCommand(result.command, result);
    const focusAfterDismiss = options.captureFocusAfterDismiss?.()
      ?? options.focusAfterDismiss;
    options.render();
    const focusAfterExecution = () => {
      if (generation === interactionGeneration) focusAfterDismiss?.();
    };
    Promise.resolve(execution).then(
      focusAfterExecution,
      (error: unknown) => {
        options.reportCommandError(error);
        focusAfterExecution();
      });
  }

  function highlightSelection(): number {
    const container = document.querySelector<HTMLElement>("#spotlight-results");
    if (!container) return 0;
    const items = container.querySelectorAll<HTMLElement>(".spotlight-item");
    items.forEach((element, index) => {
      const selected = index === state.spotlightIndex;
      element.classList.toggle("selected", selected);
      element.setAttribute("aria-selected", selected ? "true" : "false");
    });
    syncActiveDescendant(items.length);
    items[state.spotlightIndex]?.scrollIntoView({ block: "nearest" });
    return items.length;
  }

  function moveSelection(delta: number): boolean {
    const container = document.querySelector<HTMLElement>("#spotlight-results");
    const count = container
      ? container.querySelectorAll(".spotlight-item").length
      : 0;
    const next = nextSpotlightSelection(state.spotlightIndex, delta, count);
    if (next === null) return false;
    state.spotlightIndex = next;
    rememberSelection(renderedResults);
    highlightSelection();
    return true;
  }

  function scopeIndex(): number {
    return Math.max(
      0,
      scopes().findIndex(scope => scope.id === state.spotlightScope),
    );
  }

  function moveChip(index: number): void {
    state.spotlightChipIndex = index;
    const scope = scopes()[index];
    if (scope) setScope(scope.id);
  }

  function focusInput(): void {
    state.spotlightFocus = "input";
    updateChips();
    focus();
  }

  function handleModalKeys(event: KeyboardEvent): boolean {
    if (event.key === "Delete" && event.shiftKey) {
      return removeResultAt(state.spotlightIndex);
    }
    if (event.key === "Escape") {
      close();
      return true;
    }
    if (packageAddition) {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        moveSelection(event.key === "ArrowDown" ? 1 : -1);
        return true;
      }
      if (event.key === "Enter") {
        pick(renderedResults[state.spotlightIndex]);
        return true;
      }
      return false;
    }
    if (event.key === "Tab") {
      const available = scopes();
      const current = available.findIndex(scope => scope.id === state.spotlightScope);
      const next = nextSpotlightScope(
        current,
        available.length,
        event.shiftKey,
      );
      const nextScope = available[next];
      if (nextScope) {
        state.spotlightChipIndex = next;
        setScope(nextScope.id);
      }
      return true;
    }

    if (state.spotlightFocus === "chips") {
      const available = scopes();
      if (event.key === "ArrowRight") {
        if (state.spotlightChipIndex < available.length - 1) {
          moveChip(state.spotlightChipIndex + 1);
        }
      } else if (event.key === "ArrowLeft") {
        if (state.spotlightChipIndex === 0) focusInput();
        else moveChip(state.spotlightChipIndex - 1);
      } else if (event.key === "ArrowUp") {
        focusInput();
      } else if (event.key === "ArrowDown" || event.key === "Enter") {
        state.spotlightIndex = 0;
        rememberSelection(renderedResults);
        focusInput();
        highlightSelection();
      } else {
        return false;
      }
      return true;
    }

    if (event.key === "ArrowRight") {
      const input = event.target;
      if (!isTextInputTarget(input)) return false;
      const atEnd = input.selectionStart === input.selectionEnd
        && input.selectionStart === input.value.length;
      if (atEnd) {
        state.spotlightFocus = "chips";
        state.spotlightChipIndex = scopeIndex();
        updateChips();
        return true;
      }
    } else if (event.key === "ArrowDown") {
      moveSelection(1);
      return true;
    } else if (event.key === "ArrowUp") {
      if (!moveSelection(-1)) {
        state.spotlightFocus = "chips";
        state.spotlightChipIndex = scopeIndex();
        updateChips();
      }
      return true;
    } else if (event.key === "Enter") {
      pick(renderedResults[state.spotlightIndex]);
      return true;
    }
    return false;
  }

  function handleInlineKeys(event: KeyboardEvent): boolean {
    if (event.key === "Delete" && event.shiftKey) {
      return removeResultAt(state.spotlightIndex);
    }
    const items = renderedResults;
    if (event.key === "ArrowDown") {
      state.spotlightIndex = nextSpotlightSelection(
        state.spotlightIndex,
        1,
        items.length,
      ) ?? 0;
      rememberSelection(items);
      highlightSelection();
      return true;
    } else if (event.key === "ArrowUp") {
      state.spotlightIndex = nextSpotlightSelection(
        state.spotlightIndex,
        -1,
        items.length,
      ) ?? 0;
      rememberSelection(items);
      highlightSelection();
      return true;
    } else if (event.key === "Enter") {
      pick(items[state.spotlightIndex]);
      return true;
    }
    return false;
  }

  function bind(root: ParentNode, mode: "modal" | "inline"): void {
    const input = root.querySelector<HTMLInputElement>("#spotlight-input");
    const previous = boundInput;
    const selection = previous && input && previous.value === input.value
      ? {
          start: previous.selectionStart,
          end: previous.selectionEnd,
          direction: previous.selectionDirection,
        }
      : undefined;
    boundInput = input;
    if (input) {
      input.addEventListener("input", () => {
        state.spotlightQuery = input.value;
        state.spotlightIndex = 0;
        selectedResultIdentity = null;
        if (state.spotlightFocus === "chips") {
          state.spotlightFocus = "input";
          updateChips();
        }
        options.schedulePackageFetch();
        options.scheduleCapabilitySearch();
        options.prepareResults?.();
        updateResults();
      });
      options.keybindings.register({
        id: mode === "modal"
          ? "spotlight-modal.navigate"
          : "spotlight-inline.navigate",
        key: ["Escape", "Tab", "ArrowRight", "ArrowLeft", "ArrowUp", "ArrowDown", "Enter", "Delete"],
        allowExtraModifiers: true,
        priority: WORKBENCH_KEYBINDING_PRIORITY.element,
        run: mode === "modal" ? handleModalKeys : handleInlineKeys,
      }, input);
    }
    bindChipClicks(root);
    bindResultClicks(root);
    if (mode === "modal") {
      root.querySelector("#spotlight-cancel")?.addEventListener("click", close);
      const backdrop = root.querySelector("#spotlight-backdrop");
      if (backdrop && !boundModalBackdrops.has(backdrop)) {
        boundModalBackdrops.add(backdrop);
        options.keybindings.register({
          id: "spotlight-package-addition.dismiss-or-tab",
          key: ["Tab", "Escape"],
          allowExtraModifiers: true,
          priority: WORKBENCH_KEYBINDING_PRIORITY.element,
          available: () => packageAddition !== null,
          run: event => {
            if (event.key === "Escape") close();
            else {
              const currentInput =
                root.querySelector<HTMLInputElement>("#spotlight-input");
              const cancel =
                root.querySelector<HTMLButtonElement>("#spotlight-cancel");
              const target =
                document.activeElement === cancel ? currentInput : cancel;
              target?.focus();
            }
            return true;
          },
        }, backdrop);
        backdrop.addEventListener(
          "mousedown",
          event => {
            const target = event.target;
            if (hasElementId(target) && target.id === "spotlight-backdrop") close();
          },
        );
      }
      const activeResultIdentity = document.activeElement
        ?.getAttribute("data-sl-result-identity") ?? undefined;
      if (activeResultIdentity === undefined) focus(selection);
    }
  }

  return {
    bind,
    close,
    inlineHtml,
    modalHtml,
    open,
    openForPackageAddition,
    refresh,
    reset,
    results,
    updateResults,
  };
}
