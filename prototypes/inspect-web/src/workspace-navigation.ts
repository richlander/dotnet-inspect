import {
  graphMemberShareTarget,
  isMemberSection,
  isPackageLens,
  isTypeLens,
  replaceCurrentNavigationEntry,
  type MemberSection,
  type PackageLens,
  type PlatformPack,
  type GraphMemberShareIdentity,
  type TypeLens,
  type WorkspaceTab,
} from "./data.ts";
import { parseNonNegativeInteger } from "./dom-data.ts";
import {
  encodeBodyTarget,
  type BodyTarget,
} from "./member-filtering.ts";
import type {
  BrowserWorkspaceShareContext,
  BrowserWorkspaceShareDecodeResult,
  BrowserWorkspaceShareEncodeResult,
  BrowserWorkspaceShareState,
  BrowserWorkspaceShareTab,
  BrowserWorkspaceShareView,
} from "./inspect-web-engine.d.ts";

// Owns navigation stacks and URL-backed workspace snapshots. The composition root remains
// the sole mutable AppState owner and supplies captures plus explicit transition callbacks.
export interface WorkspaceView {
  package: string;
  packageKey: string;
  lens: TypeLens;
  selectedTypeId: string;
  selectedMemberKey: string;
  memberBrowseTypeId: string;
  memberKindFilter: string;
  memberAccessibilityFilter: string;
  memberTraitFilter: string;
  memberTextFilter: string;
  selectedOverloadIndex: number | null;
  bodyTarget: BodyTarget | null;
  memberSection: MemberSection;
  atPackageRoot: boolean;
  packageLens: PackageLens;
  libraryScope: string[] | null;
}

export function workspaceViewSignature(view: WorkspaceView): string {
  const graphTarget = graphMemberShareTarget(view.bodyTarget);
  return JSON.stringify({
    p: view.packageKey,
    l: view.lens,
    t: view.selectedTypeId,
    m: view.selectedMemberKey,
    mb: view.memberBrowseTypeId,
    mk: view.memberKindFilter,
    ma: view.memberAccessibilityFilter,
    mr: view.memberTraitFilter,
    o: view.selectedOverloadIndex,
    b: graphTarget ? null : encodeBodyTarget(view.bodyTarget),
    g: graphTarget,
    s: view.memberSection,
    pr: view.atPackageRoot,
    pl: view.packageLens,
    ls: view.libraryScope,
  });
}

// A duck-typed subset of a click on an `<a>`, so the interception rule is a pure
// function testable without a DOM. The composition root is the single owner that
// registers one delegated `click` listener and evaluates every in-app anchor click
// against this rule instead of leaving each link to opt in (or be forgotten) one by one.
export interface LinkNavigationClick {
  button: number;
  metaKey: boolean;
  ctrlKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
  defaultPrevented: boolean;
  download: boolean;
  target: string | null;
  href: string | null;
  origin: string | null;
  currentOrigin: string;
}

// True when the owner should treat this as an in-app transition: take over the
// navigation itself (pushState + apply) instead of letting the browser load a new
// document. False for anything that should keep its native behavior — a modified
// click (new tab/window), a download link, a link explicitly targeting another
// browsing context, or a cross-origin destination.
export function shouldInterceptLinkClick(click: LinkNavigationClick): boolean {
  if (click.defaultPrevented) return false;
  if (click.button !== 0) return false;
  if (click.metaKey || click.ctrlKey || click.shiftKey || click.altKey) return false;
  if (click.download) return false;
  if (click.target && click.target !== "_self") return false;
  if (!click.href) return false;
  if (click.origin !== click.currentOrigin) return false;
  return true;
}

export interface WorkspaceLinkNavigationDependencies {
  currentOrigin(): string;
  resolve(href: string): URL;
  navigate(url: URL): void;
}

// Registers the single delegated click listener that owns every in-app anchor's
// navigation: same-origin, unmodified left clicks take over here (pushState + apply)
// instead of loading a new document. This is the one binder the composition root
// calls, so `dotnet-inspect.ts` gains no new raw `addEventListener` call of its own —
// a link that wants different behavior (new tab, download, cross-origin) opts out
// through ordinary anchor semantics rather than bespoke per-link wiring.
export function bindWorkspaceLinkNavigation(
  root: Document,
  dependencies: WorkspaceLinkNavigationDependencies,
) {
  root.addEventListener("click", event => {
    if (event.defaultPrevented) return;
    const target = event.target instanceof Element ? event.target : null;
    const anchor = target?.closest("a[href]");
    if (!(anchor instanceof HTMLAnchorElement)) return;
    const url = dependencies.resolve(anchor.href);
    if (!shouldInterceptLinkClick({
      button: event.button,
      metaKey: event.metaKey,
      ctrlKey: event.ctrlKey,
      shiftKey: event.shiftKey,
      altKey: event.altKey,
      defaultPrevented: event.defaultPrevented,
      download: anchor.hasAttribute("download"),
      target: anchor.target || null,
      href: anchor.href || null,
      origin: url.origin,
      currentOrigin: dependencies.currentOrigin(),
    })) {
      return;
    }
    event.preventDefault();
    dependencies.navigate(url);
  });
}

export interface NavigationSequence {
  begin(): number;
  invalidate(): void;
  current(): number;
  isCurrent(candidate: number): boolean;
}

export function createNavigationSequence(): NavigationSequence {
  let current = 0;
  return {
    begin() {
      return ++current;
    },
    invalidate() {
      current++;
    },
    current() {
      return current;
    },
    isCurrent(candidate) {
      return candidate === current;
    },
  };
}

export interface NavigationHistorySnapshot<TView> {
  stack: Array<{ sig: string; view: TView }>;
  index: number;
}

export interface NavigationHistory<TView> {
  record(): void;
  normalizeCurrent(): void;
  canBack(): boolean;
  canForward(): boolean;
  back(): boolean;
  forward(): boolean;
  snapshot(): NavigationHistorySnapshot<TView>;
  restore(snapshot: NavigationHistorySnapshot<TView>): void;
}

export interface NavigationHistoryDependencies<TView> {
  capture(): TView | null;
  signature(view: TView): string;
  apply(view: TView): boolean;
  onExhausted(): void;
}

interface NavigationEntry<TView> {
  sig: string;
  view: TView;
}

export function createNavigationHistory<TView>(
  dependencies: NavigationHistoryDependencies<TView>,
): NavigationHistory<TView> {
  const navigation = {
    stack: [] as NavigationEntry<TView>[],
    index: -1,
  };

  return {
    record() {
      const view = dependencies.capture();
      if (!view) return;
      const sig = dependencies.signature(view);
      const current = navigation.index >= 0
        ? navigation.stack[navigation.index]
        : undefined;
      if (current?.sig === sig) {
        current.view = view;
        return;
      }
      navigation.stack = navigation.stack.slice(0, navigation.index + 1);
      navigation.stack.push({ sig, view });
      navigation.index = navigation.stack.length - 1;
    },
    normalizeCurrent() {
      const view = dependencies.capture();
      if (!view) return;
      replaceCurrentNavigationEntry(
        navigation,
        {
          sig: dependencies.signature(view),
          view,
        });
    },
    canBack() {
      return navigation.index > 0;
    },
    canForward() {
      return navigation.index < navigation.stack.length - 1;
    },
    back() {
      while (navigation.index > 0) {
        const candidate = navigation.index - 1;
        navigation.index = candidate;
        const entry = navigation.stack[candidate];
        if (entry && dependencies.apply(entry.view)) return true;
        navigation.stack.splice(candidate, 1);
      }
      dependencies.onExhausted();
      return false;
    },
    forward() {
      while (navigation.index < navigation.stack.length - 1) {
        const candidate = navigation.index + 1;
        navigation.index = candidate;
        const entry = navigation.stack[candidate];
        if (entry && dependencies.apply(entry.view)) return true;
        navigation.stack.splice(candidate, 1);
        navigation.index--;
      }
      dependencies.onExhausted();
      return false;
    },
    snapshot() {
      return {
        stack: navigation.stack.map(entry => ({ ...entry })),
        index: navigation.index,
      };
    },
    restore(snapshot) {
      navigation.stack = snapshot.stack.map(entry => ({ ...entry }));
      navigation.index = snapshot.index;
    },
  };
}

export interface WorkspaceDeepLink {
  type?: string | null;
  member?: string | null;
  memberAnchor?: string | null;
  memberSignature?: string | null;
  overload?: number | null;
  section?: MemberSection | null;
  bodyTarget?: BodyTarget | null;
  memberBrowse?: boolean;
  memberTextFilter?: string;
  memberKindFilter?: string;
  memberAccessibilityFilter?: string;
  memberTraitFilter?: string;
  graphTarget?: GraphMemberShareIdentity | null;
}

export interface WorkspaceMemberFilterContext {
  kinds: readonly string[];
  accessibilities: readonly string[];
  traits: readonly string[];
}

export interface ResolvedWorkspaceMemberFilters {
  kind: string;
  accessibility: string;
  trait: string;
  rejectedFields: string[];
}

export function unavailableWorkspaceMemberContextFields(
  deep: WorkspaceDeepLink,
  includeFilters: boolean,
): string[] {
  const fields: string[] = [];
  if (deep.member) fields.push("member");
  if (deep.overload != null) fields.push("overload");
  if (deep.section && deep.section !== "overview") {
    fields.push("member section");
  }
  if (deep.bodyTarget) fields.push("member body");
  if (includeFilters) {
    if (deep.memberBrowse) fields.push("member browse");
    if (deep.memberTextFilter) fields.push("member text filter");
    if (deep.memberKindFilter && deep.memberKindFilter !== "all") {
      fields.push("member kind filter");
    }
    if (deep.memberAccessibilityFilter
      && deep.memberAccessibilityFilter !== "all") {
      fields.push("member accessibility filter");
    }
    if (deep.memberTraitFilter) fields.push("member trait filter");
  }
  return fields;
}

export function resolveWorkspaceMemberFilters(
  deep: WorkspaceDeepLink,
  context: WorkspaceMemberFilterContext,
): ResolvedWorkspaceMemberFilters {
  const rejectedFields: string[] = [];
  const resolve = (
    value: string | null | undefined,
    fallback: string,
    allowed: readonly string[],
    field: string,
  ) => {
    if (!value || value === fallback) return fallback;
    if (allowed.includes(value)) return value;
    rejectedFields.push(field);
    return fallback;
  };
  const kind = resolve(
    deep.memberKindFilter,
    "all",
    context.kinds,
    "member kind filter");
  const accessibility = resolve(
    deep.memberAccessibilityFilter,
    "all",
    context.accessibilities,
    "member accessibility filter");
  const trait = resolve(
    deep.memberTraitFilter,
    "",
    context.traits,
    "member trait filter");
  return { kind, accessibility, trait, rejectedFields };
}

export function resolveWorkspaceMemberOverload(
  overload: number | null | undefined,
  overloadCount: number,
): { overload: number | null; rejected: boolean } {
  if (overload == null) return { overload: null, rejected: false };
  return Number.isSafeInteger(overload)
    && overload >= 0
    && !Object.is(overload, -0)
    && overload < overloadCount
    ? { overload, rejected: false }
    : { overload: null, rejected: true };
}

export function resolveWorkspaceMemberSection(
  section: MemberSection | null | undefined,
  availableSections: readonly MemberSection[],
): { section: MemberSection; rejected: boolean } {
  if (section == null || section === "overview") {
    return { section: "overview", rejected: false };
  }
  return availableSections.includes(section)
    ? { section, rejected: false }
    : { section: "overview", rejected: true };
}

export interface WorkspaceUrlState {
  package: string;
  tabs: BrowserWorkspaceShareTab[];
  contexts: BrowserWorkspaceShareContext[];
  activeTabId: string;
  selectedContextId: string;
  view: BrowserWorkspaceShareView;
}

export interface PackageRootUrlState {
  package: string;
  version: string;
  framework: string;
  lens: PackageLens;
}

export interface WorkspaceShareCaptureTopology {
  contexts: BrowserWorkspaceShareContext[];
  selectedContextId: string;
}

export function selectedBrowserCallGraphPackageTabIds(
  basis: BrowserWorkspaceShareState,
): string[] {
  const selected = basis.contexts.find(
    context => context.id === basis.selectedContextId);
  if (!selected) {
    throw new Error(
      "The selected workspace context is no longer available.");
  }
  const selectedTabs = selected.tabIds.map(id =>
    basis.tabs.find(tab => tab.id === id));
  if (selectedTabs.some(tab => !tab)) {
    throw new Error(
      "The selected Call Graph context contains an unknown tab identity.");
  }
  if (selectedTabs.some(tab => tab?.kind !== "package")) {
    throw new Error(
      "The selected Call Graph context contains a Platform participant that this browser cannot realize.");
  }
  return [...selected.tabIds];
}

export interface WorkspaceUrlPreservation {
  url: string;
  projection: string;
}

export function workspaceUrlPreservationApplies(
  preservation: WorkspaceUrlPreservation | null,
  url: string,
  projection: string,
): boolean {
  return preservation?.url === url
    && preservation.projection === projection;
}

export function callGraphCaptureTopology(
  tabs: readonly BrowserWorkspaceShareTab[],
  activeIndex: number,
  participantTabIds: readonly string[],
): WorkspaceShareCaptureTopology {
  const contexts = tabs.map((tab, index) => ({
    id: `g${index}`,
    tabIds: [tab.id],
  }));
  const activeTab = tabs[activeIndex];
  if (!activeTab || !participantTabIds.includes(activeTab.id)) {
    throw new Error(
      "The active package is not part of the Call Graph workspace context.");
  }
  const knownIds = new Set(tabs.map(tab => tab.id));
  if (new Set(participantTabIds).size !== participantTabIds.length
    || participantTabIds.some(id => !knownIds.has(id))) {
    throw new Error(
      "The Call Graph workspace context contains invalid tab identities.");
  }
  if (participantTabIds.length <= 1) {
    return {
      contexts,
      selectedContextId: contexts[activeIndex]?.id ?? "",
    };
  }

  const context = {
    id: `g${contexts.length}`,
    tabIds: [...participantTabIds],
  };
  contexts.push(context);
  return {
    contexts,
    selectedContextId: context.id,
  };
}

export function browserCreatedCallGraphTabIds(
  tabs: readonly BrowserWorkspaceShareTab[],
  activeIndex: number,
): string[] {
  const activeTab = tabs[activeIndex];
  if (activeTab?.kind !== "package") return [];
  const framework = activeTab.framework?.toLowerCase() ?? null;
  const compatible = tabs.filter(tab =>
    tab.kind === "package"
    && (tab.framework?.toLowerCase() ?? null) === framework
    && tab.runtimeIdentifier === activeTab.runtimeIdentifier)
    .map(tab => tab.id);
  return [
    activeTab.id,
    ...compatible.filter(id => id !== activeTab.id),
  ];
}

export function workspaceShareCaptureTopology(
  tabs: readonly BrowserWorkspaceShareTab[],
  activeIndex: number,
  basis: BrowserWorkspaceShareState | null,
  preserveBasis: boolean,
  callGraph: boolean,
): WorkspaceShareCaptureTopology {
  const preservesBasis = basis
    && preserveBasis
    && basis.tabs.length === tabs.length
    && basis.tabs.every((tab, index) => tab.id === tabs[index]?.id);
  const contexts = preservesBasis
    ? basis.contexts.map(context => ({
        id: context.id,
        tabIds: context.tabIds.slice(),
      }))
    : tabs.map((tab, index) => ({
        id: `g${index}`,
        tabIds: [tab.id],
      }));
  let selectedContextId = preservesBasis
    ? basis.selectedContextId
    : contexts[activeIndex]?.id ?? contexts[0]?.id ?? "";

  if (!preservesBasis && callGraph) {
    const packageTabIds = browserCreatedCallGraphTabIds(tabs, activeIndex);
    if (packageTabIds.length > 0) {
      return callGraphCaptureTopology(
        tabs,
        activeIndex,
        packageTabIds);
    }
  }

  return { contexts, selectedContextId };
}

export function retainedPlatformTargetVersion(
  tab: BrowserWorkspaceShareTab | null | undefined,
  runtimePack: {
    version: string;
    activeFramework: string;
  } | null | undefined,
  framework: string,
): string {
  if (!tab
    || tab.kind !== "group"
    || tab.source !== ":Platform"
    || !runtimePack
    || runtimePack.activeFramework.toLowerCase() !== framework.toLowerCase()
    || (tab.framework
      && tab.framework.toLowerCase() !== framework.toLowerCase())
    || (tab.version
      && tab.version.toLowerCase() !== runtimePack.version.toLowerCase())) {
    return "";
  }
  return tab.version ?? "";
}

function workspaceShareTabMatchesResolved(
  requested: BrowserWorkspaceShareTab,
  resolved: BrowserWorkspaceShareTab,
): boolean {
  return requested.kind === resolved.kind
    && requested.source.toLowerCase() === resolved.source.toLowerCase()
    && (!requested.version
      || requested.version.toLowerCase() === resolved.version?.toLowerCase())
    && (!requested.framework
      || requested.framework.toLowerCase() === resolved.framework?.toLowerCase())
    && requested.runtimeIdentifier === resolved.runtimeIdentifier;
}

export function workspaceShareTabsMatchResolved(
  requested: readonly BrowserWorkspaceShareTab[],
  resolved: readonly BrowserWorkspaceShareTab[],
): boolean {
  return requested.length === resolved.length
    && requested.every((tab, index) => {
      const resolvedTab = resolved[index];
      return resolvedTab
        ? workspaceShareTabMatchesResolved(tab, resolvedTab)
        : false;
    });
}

export interface RetainedMissingPlatformTarget {
  tabIndex: number;
  version: string;
}

export function retainedMissingPlatformTarget(
  basisTabs: readonly BrowserWorkspaceShareTab[] | null | undefined,
  resolvedTabs: readonly BrowserWorkspaceShareTab[],
  framework: string,
): RetainedMissingPlatformTarget | null {
  if (!basisTabs || basisTabs.length !== resolvedTabs.length + 1) return null;
  const matches = basisTabs
    .map((tab, index) => ({ tab, index }))
    .filter(({ tab }) =>
      tab.kind === "group"
      && tab.source === ":Platform"
      && !tab.runtimeIdentifier
      && (!tab.version || Boolean(tab.framework))
      && (!tab.framework
        || tab.framework.toLowerCase() === framework.toLowerCase()));
  if (matches.length !== 1) return null;

  const { tab, index } = matches[0]!;
  const remaining = basisTabs.filter((_, candidate) => candidate !== index);
  if (!workspaceShareTabsMatchResolved(remaining, resolvedTabs)) return null;
  return {
    tabIndex: index,
    version: tab.version ?? "",
  };
}

export interface DecodedShareState {
  state: BrowserWorkspaceShareState;
  tabs: WorkspaceTab[];
  active: number;
  contexts: BrowserWorkspaceShareContext[];
  selectedContextId: string;
  view: string;
  type: string | null;
  memberAnchor: string | null;
  memberSignature: string | null;
  section: MemberSection | null;
  library: string | null;
}

export type ShareStateResult = DecodedShareState | { error: string } | null;
export type WorkspaceShareDecoder =
  (value: string) => BrowserWorkspaceShareDecodeResult;
export type WorkspaceShareEncoder =
  (stateJson: string) => BrowserWorkspaceShareEncodeResult;

const invalidShareState =
  "The shared workspace state is invalid and was ignored.";

export function encodeWorkspaceShareState(
  state: WorkspaceUrlState,
  encode: WorkspaceShareEncoder,
): string {
  const result = encode(JSON.stringify({
    tabs: state.tabs,
    contexts: state.contexts,
    activeTabId: state.activeTabId,
    selectedContextId: state.selectedContextId,
    view: state.view,
  } satisfies BrowserWorkspaceShareState));
  if (!result.succeeded || !result.packet) {
    throw new Error(result.failure?.message
      ?? "The workspace cannot be represented as canonical share state.");
  }
  return result.packet;
}

function decodeWorkspaceShareState(
  value: string | null,
  decode: WorkspaceShareDecoder,
): ShareStateResult {
  if (!value) return null;
  const result = decode(value);
  if (!result.succeeded || !result.state) {
    const failure = result.failure;
    return {
      error: failure
        ? `The shared workspace state was rejected (${failure.kind}): ${failure.message}`
        : invalidShareState,
    };
  }

  const state = result.state;
  const tabs: WorkspaceTab[] = [];
  let platformTabCount = 0;
  for (const tab of state.tabs) {
    if (tab.runtimeIdentifier) {
      return {
        error: "The shared workspace uses a runtime-specific context that this browser cannot activate.",
      };
    }
    if (tab.kind === "package") {
      tabs.push({
        id: tab.source,
        version: tab.version ?? "latest",
        framework: tab.framework ?? "",
        shareId: tab.id,
        shareKind: "package",
        shareSource: tab.source,
        runtimeIdentifier: null,
      });
      continue;
    }
    if (tab.kind === "group" && tab.source === ":Platform") {
      platformTabCount++;
      if (platformTabCount > 1) {
        return {
          error: "The shared workspace contains multiple Platform tabs, which this browser cannot retain independently.",
        };
      }
      tabs.push({
        id: "Microsoft.NETCore.App",
        version: tab.version ?? "latest",
        framework: tab.framework ?? "",
        shareId: tab.id,
        shareKind: "group",
        shareSource: tab.source,
        runtimeIdentifier: null,
      });
      continue;
    }
    return {
      error: `The shared workspace group '${tab.source}' is not supported by this browser.`,
    };
  }

  const active = state.tabs.findIndex(tab => tab.id === state.activeTabId);
  if (active < 0) return { error: invalidShareState };
  const section = state.view.section;
  if (section && !isMemberSection(section)) {
    return {
      error: `The shared workspace view section '${section}' is not supported by this browser.`,
    };
  }
  const memberSection = section && isMemberSection(section)
    ? section
    : null;
  if (state.view.lens && !isTypeLens(state.view.lens)) {
    return {
      error: `The shared workspace view lens '${state.view.lens}' is not supported by this browser.`,
    };
  }
  if (state.view.libraries.length > 1) {
    return {
      error: "The shared workspace selects multiple libraries, which this browser cannot activate.",
    };
  }

  return {
    state,
    tabs,
    active,
    contexts: state.contexts,
    selectedContextId: state.selectedContextId,
    view: state.view.lens ?? "",
    type: state.view.type,
    memberAnchor: state.view.memberAnchor,
    memberSignature: state.view.memberSignature,
    section: memberSection,
    library: state.view.libraries[0] ?? null,
  };
}

function resolveView(token: string): {
  lens: TypeLens | null;
  atPackageRoot: boolean;
  packageLens: PackageLens | null;
  rejected: string | null;
} {
  const atPackageRoot = token === "pkg" || token.startsWith("pkg:");
  const packageMatch = /^(?:pkg|pkg:([^:]+))$/.exec(token);
  const packageLensToken = packageMatch?.[1];
  const lens = isTypeLens(token) ? token : null;
  const packageLens = atPackageRoot
    ? (packageMatch && isPackageLens(packageLensToken) ? packageLensToken : "overview")
    : null;
  // A view token that matches no vocabulary used to become an ordinary Overview or API
  // view, so a stale or mistyped link looked like it had worked while showing something
  // else. Only a token that named nothing is a rejection: an empty hash is "no view".
  let rejected: string | null = null;
  if (token !== "" && lens === null) {
    if (!atPackageRoot) rejected = "view";
    else if (!packageMatch
      || (packageLensToken !== undefined && !isPackageLens(packageLensToken))) {
      rejected = "view";
    }
  }
  return { lens, atPackageRoot, packageLens, rejected };
}

// The browser accepts a pathname containing a malformed escape sequence, but
// `decodeURIComponent` throws `URIError` on one. The first parse runs synchronously during
// module initialization, before there is any state or error surface, so a thrown error there
// leaves the application uninitialized rather than showing a failure.
function decodeRouteComponent(value: string): string | null {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}

export interface WorkspaceLocationSnapshot {
  href: string;
  pathname: string;
  search: string;
  hash: string;
}

function parseOverloadCoordinate(value: string | number): number | null {
  if (typeof value === "string") return parseNonNegativeInteger(value);
  return Number.isSafeInteger(value) && value >= 0 && !Object.is(value, -0)
    ? value
    : null;
}

export interface WorkspaceLocationRoute {
  location: WorkspaceLocationSnapshot;
  encodedWorkspaceState: string | null;
  hasWorkspaceState: boolean;
  visible: ParsedWorkspaceLocation;
}

function resolveWorkspaceLocation(
  location: WorkspaceLocationSnapshot,
  share: ShareStateResult,
) {
  const params = new URLSearchParams(location.search);
  const route = location.pathname.split("/");
  const packageAt = route.findIndex(part => part.toLowerCase() === "packages");
  const hasWorkspaceState = params.has("w");
  const rejectedFields: string[] = [];

  const packageToken = route[packageAt + 1];
  const versionToken = route[packageAt + 2];
  let pkg = !hasWorkspaceState && packageAt >= 0
    ? packageToken ? decodeRouteComponent(packageToken) : null
    : params.get("package");
  let version = !hasWorkspaceState && packageAt >= 0
    ? versionToken ? decodeRouteComponent(versionToken) : null
    : params.get("version");
  if (!hasWorkspaceState && packageAt >= 0) {
    if (pkg === null && (Boolean(packageToken) || Boolean(versionToken))) {
      rejectedFields.push("package");
    }
    if (version === null && Boolean(versionToken)) {
      rejectedFields.push("version");
    }
  }
  let framework = params.get("framework");
  let type = params.get("type");
  let member = params.get("member");
  let memberAnchor: string | null = null;
  let memberSignature: string | null = null;
  const overloadToken = params.get("overload");
  let overload = overloadToken === null
    ? null
    : parseOverloadCoordinate(overloadToken);
  if (overloadToken !== null && overload === null) {
    rejectedFields.push("overload");
  }
  const sectionToken = params.get("section");
  let section: MemberSection | null = isMemberSection(sectionToken)
    ? sectionToken
    : null;
  if (sectionToken !== null && section === null) rejectedFields.push("section");
  let bodyTarget: BodyTarget | null = null;
  let viewToken = location.hash.slice(1);
  let tabs: WorkspaceTab[] = [];
  let active = 0;
  let contexts: BrowserWorkspaceShareContext[] = [];
  let selectedContextId = "";
  let library: string | null = null;
  let libraryPack: PlatformPack | null = null;
  let memberBrowse = false;
  let memberTextFilter = "";
  let memberKindFilter = "all";
  let memberAccessibilityFilter = "all";
  let memberTraitFilter = "";
  let graphTarget: GraphMemberShareIdentity | null = null;
  let shareState: BrowserWorkspaceShareState | null = null;
  const shareError = share && "error" in share ? share.error : "";

  if (share && !("error" in share)) {
    rejectedFields.length = 0;
    shareState = share.state;
    tabs = share.tabs;
    active = Math.min(Math.max(0, share.active), Math.max(0, tabs.length - 1));
    contexts = share.contexts;
    selectedContextId = share.selectedContextId;
    const target = tabs[active];
    if (target) {
      pkg = target.id;
      version = target.version;
      framework = target.framework;
    }
    viewToken = share.view;
    type = share.type;
    member = null;
    memberAnchor = share.memberAnchor;
    memberSignature = share.memberSignature;
    overload = null;
    section = share.section;
    bodyTarget = null;
    library = share.library;
    libraryPack = null;
    memberBrowse = false;
    memberTextFilter = "";
    memberKindFilter = "all";
    memberAccessibilityFilter = "all";
    memberTraitFilter = "";
    graphTarget = null;
  }
  if (!pkg && tabs.length) {
    const target = tabs[Math.min(Math.max(0, active), tabs.length - 1)];
    if (target) {
      pkg = target.id;
      version = target.version;
      framework = target.framework;
    }
  }

  const view = resolveView(viewToken);
  if (view.rejected) rejectedFields.push(view.rejected);

  const workspaceNotice = shareError || (rejectedFields.length
    ? `Part of this link could not be read and was ignored: ${
      [...new Set(rejectedFields)].join(", ")}.`
    : "");

  return {
    package: pkg,
    version,
    framework,
    type,
    member,
    memberAnchor,
    memberSignature,
    overload,
    section,
    bodyTarget,
    lens: view.lens,
    atPackageRoot: view.atPackageRoot,
    packageLens: view.packageLens,
    tabs,
    active,
    contexts,
    selectedContextId,
    library,
    libraryPack,
    memberBrowse,
    memberTextFilter,
    memberKindFilter,
    memberAccessibilityFilter,
    memberTraitFilter,
    graphTarget,
    shareState,
    hasWorkspaceState,
    workspaceNotice,
  };
}

export type ParsedWorkspaceLocation = ReturnType<typeof resolveWorkspaceLocation>;

export function parseWorkspaceRoute(
  location: WorkspaceLocationSnapshot,
): WorkspaceLocationRoute {
  const params = new URLSearchParams(location.search);
  const encodedWorkspaceState = params.get("w");
  return {
    location,
    encodedWorkspaceState,
    hasWorkspaceState: params.has("w"),
    visible: resolveWorkspaceLocation(location, null),
  };
}

export function resolveWorkspaceRoute(
  route: WorkspaceLocationRoute,
  decode: WorkspaceShareDecoder,
): ParsedWorkspaceLocation {
  const encodedWorkspaceState = route.encodedWorkspaceState;
  return resolveWorkspaceLocation(
    route.location,
    encodedWorkspaceState
      ? decodeWorkspaceShareState(encodedWorkspaceState, decode)
      : null);
}

export function parseWorkspaceLocation(
  location: WorkspaceLocationSnapshot,
  decode: WorkspaceShareDecoder,
): ParsedWorkspaceLocation {
  return resolveWorkspaceRoute(parseWorkspaceRoute(location), decode);
}

export function buildWorkspaceStateUrl(
  base: string,
  state: WorkspaceUrlState,
  encode: WorkspaceShareEncoder,
): URL {
  const url = new URL(base);
  url.pathname = "/";
  const params = new URLSearchParams();
  params.set("package", state.package);
  const shareState = encodeWorkspaceShareState(state, encode);
  params.set("w", shareState);
  url.search = params.toString();
  url.hash = "";
  return url;
}

export function buildPackageRootStateUrl(
  base: string,
  state: PackageRootUrlState,
): URL {
  const url = new URL(base);
  url.pathname = "/";
  url.search = "";
  url.searchParams.set("package", state.package);
  url.searchParams.set("version", state.version);
  url.searchParams.set("framework", state.framework);
  url.hash = state.lens === "overview" ? "pkg" : `pkg:${state.lens}`;
  return url;
}

export interface WorkspaceLocationPersistence {
  parseCurrent(): ParsedWorkspaceLocation;
  preflightCurrent(): WorkspaceLocationPreflight;
  build(state: WorkspaceUrlState, base?: string): URL;
  sync(state: WorkspaceUrlState): void;
  replace(url: string): void;
  push(url: string): void;
}

export interface WorkspaceLocationPreflight {
  visible: ParsedWorkspaceLocation;
  hasWorkspaceState: boolean;
  startsAtHome: boolean;
  visibleNotice: string;
  resolve(
    decode?: WorkspaceShareDecoder,
  ): ParsedWorkspaceLocation;
}

export interface WorkspaceLocationDependencies {
  current(): WorkspaceLocationSnapshot;
  replace(url: string): void;
  push(url: string): void;
  decode(value: string): BrowserWorkspaceShareDecodeResult;
  encode(stateJson: string): BrowserWorkspaceShareEncodeResult;
}

export function createWorkspaceLocationPersistence(
  dependencies: WorkspaceLocationDependencies,
): WorkspaceLocationPersistence {
  const decode = (value: string) => dependencies.decode(value);
  const encode = (stateJson: string) => dependencies.encode(stateJson);
  const build = (state: WorkspaceUrlState, base?: string) =>
    buildWorkspaceStateUrl(
      base ?? dependencies.current().href,
      state,
      encode);
  return {
    parseCurrent() {
      return parseWorkspaceLocation(
        dependencies.current(),
        decode);
    },
    preflightCurrent,
    build,
    sync(state) {
      try {
        dependencies.replace(build(state).toString());
      } catch {
        // Sandboxed frames and overlong state can reject address-bar persistence.
      }
    },
    replace(url) {
      try {
        dependencies.replace(url);
      } catch {
        // Sandboxed frames may reject browser-history changes.
      }
    },
    push(url) {
      try {
        dependencies.push(url);
      } catch {
        // Sandboxed frames may reject browser-history changes.
      }
    },
  };

  function preflightCurrent(): WorkspaceLocationPreflight {
    const route = parseWorkspaceRoute(dependencies.current());
    return {
      visible: route.visible,
      hasWorkspaceState: route.hasWorkspaceState,
      startsAtHome: !route.visible.package && !route.hasWorkspaceState,
      visibleNotice: route.hasWorkspaceState
        ? ""
        : route.visible.workspaceNotice,
      resolve(routeDecoder = decode) {
        return resolveWorkspaceRoute(route, routeDecoder);
      },
    };
  }
}
