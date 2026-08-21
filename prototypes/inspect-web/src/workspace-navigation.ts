import {
  lenses,
  normalizeShareTabs,
  packageLenses,
  replaceCurrentNavigationEntry,
  shareStateLengthError,
  type WorkspaceTab,
} from "./data.ts";
import {
  decodeBodyTarget,
  encodeBodyTarget,
  type BodyTarget,
  type EncodedBodyTarget,
} from "./member-filtering.ts";

// Owns navigation stacks and URL-backed workspace snapshots. The composition root remains
// the sole mutable AppState owner and supplies captures plus explicit transition callbacks.
export interface WorkspaceView {
  package: string;
  packageKey: string;
  lens: string;
  selectedTypeId: string;
  selectedMemberKey: string;
  memberBrowseTypeId: string;
  memberKindFilter: string;
  memberAccessibilityFilter: string;
  memberTraitFilter: string;
  memberTextFilter: string;
  selectedOverloadIndex: number | null;
  bodyTarget: BodyTarget | null;
  memberSection: string;
  atPackageRoot: boolean;
  packageLens: string;
  libraryScope: string[] | null;
}

export function workspaceViewSignature(view: WorkspaceView): string {
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
    b: encodeBodyTarget(view.bodyTarget),
    s: view.memberSection,
    pr: view.atPackageRoot,
    pl: view.packageLens,
    ls: view.libraryScope,
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

export interface NavigationHistory<TView> {
  record(): void;
  normalizeCurrent(): void;
  canBack(): boolean;
  canForward(): boolean;
  back(): boolean;
  forward(): boolean;
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
      if (navigation.index >= 0
        && navigation.stack[navigation.index]?.sig === sig) {
        navigation.stack[navigation.index].view = view;
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
        dependencies.signature(view),
        view);
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
        if (dependencies.apply(navigation.stack[candidate].view)) return true;
        navigation.stack.splice(candidate, 1);
      }
      dependencies.onExhausted();
      return false;
    },
    forward() {
      while (navigation.index < navigation.stack.length - 1) {
        const candidate = navigation.index + 1;
        navigation.index = candidate;
        if (dependencies.apply(navigation.stack[candidate].view)) return true;
        navigation.stack.splice(candidate, 1);
        navigation.index--;
      }
      dependencies.onExhausted();
      return false;
    },
  };
}

export interface WorkspaceDeepLink {
  type?: string | null;
  member?: string | null;
  overload?: string | null;
  section?: string | null;
  bodyTarget?: BodyTarget | null;
  memberBrowse?: boolean;
  memberTextFilter?: string;
  memberKindFilter?: string;
  memberAccessibilityFilter?: string;
  memberTraitFilter?: string;
}

export interface WorkspaceUrlState {
  package: string;
  tabs: WorkspaceTab[];
  active: number;
  lens: string;
  atPackageRoot: boolean;
  packageLens: string;
  library: string | null;
  selectedTypeId: string;
  selectedMemberKey: string;
  selectedOverloadIndex: number | null;
  memberSection: string;
  selectedBodyTarget: BodyTarget | null;
  memberBrowse: boolean;
  memberTextFilter: string;
  memberKindFilter: string;
  memberAccessibilityFilter: string;
  memberTraitFilter: string;
}

interface SharePacket {
  t: string[][];
  a: number;
  l?: string;
  v?: string;
  y?: string;
  m?: string;
  o?: number;
  c?: string;
  d?: EncodedBodyTarget;
  b?: 1;
  q?: string;
  k?: string;
  e?: string;
  r?: string;
}

interface DecodedShareState {
  tabs: WorkspaceTab[];
  active: number;
  view: string;
  rich: boolean;
  type: string | null;
  member: string | null;
  overload: string | null;
  section: string | null;
  bodyTarget: BodyTarget | null;
  library: string | null;
  memberBrowse: boolean;
  memberTextFilter: string;
  memberKindFilter: string;
  memberAccessibilityFilter: string;
  memberTraitFilter: string;
}

type ShareStateResult = DecodedShareState | { error: string } | null;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function base64UrlEncode(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function base64UrlDecode(value: string): string {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++)
    bytes[index] = binary.charCodeAt(index);
  return new TextDecoder().decode(bytes);
}

export function encodeWorkspaceShareState(state: WorkspaceUrlState): string {
  const packet: SharePacket = {
    t: state.tabs.map(item => [item.id, item.version, item.framework || ""]),
    a: Math.max(0, state.active),
  };
  if (state.library) packet.l = state.library;
  if (state.atPackageRoot) {
    packet.v = state.packageLens && state.packageLens !== "overview"
      ? `pkg:${state.packageLens}`
      : "pkg";
  } else {
    if (state.lens && state.lens !== "api") packet.v = state.lens;
    if (state.selectedTypeId) packet.y = state.selectedTypeId;
    if (state.selectedMemberKey) packet.m = state.selectedMemberKey;
    if (state.selectedOverloadIndex != null) packet.o = state.selectedOverloadIndex;
    if (state.memberSection && state.memberSection !== "overview")
      packet.c = state.memberSection;
    if (state.selectedBodyTarget) packet.d = encodeBodyTarget(state.selectedBodyTarget) ?? undefined;
    if (state.memberBrowse) packet.b = 1;
    if (state.memberTextFilter) packet.q = state.memberTextFilter;
    if (state.memberKindFilter !== "all") packet.k = state.memberKindFilter;
    if (state.memberAccessibilityFilter !== "all")
      packet.e = state.memberAccessibilityFilter;
    if (state.memberTraitFilter) packet.r = state.memberTraitFilter;
  }
  return base64UrlEncode(JSON.stringify(packet));
}

function decodeWorkspaceShareState(value: string | null): ShareStateResult {
  if (!value) return null;
  const lengthError = shareStateLengthError(value);
  if (lengthError) return { error: lengthError };
  try {
    const raw: unknown = JSON.parse(base64UrlDecode(value));
    if (Array.isArray(raw)) {
      const normalized = normalizeShareTabs(raw);
      if (normalized.error) return { error: normalized.error };
      return {
        tabs: normalized.tabs,
        active: 0,
        view: "",
        rich: false,
        type: null,
        member: null,
        overload: null,
        section: null,
        bodyTarget: null,
        library: null,
        memberBrowse: false,
        memberTextFilter: "",
        memberKindFilter: "all",
        memberAccessibilityFilter: "all",
        memberTraitFilter: "",
      };
    }
    if (isRecord(raw) && Array.isArray(raw.t)) {
      const normalized = normalizeShareTabs(raw.t);
      if (normalized.error) return { error: normalized.error };
      return {
        tabs: normalized.tabs,
        active: typeof raw.a === "number" && Number.isInteger(raw.a)
          ? (normalized.sourceIndexes[raw.a] ?? 0)
          : 0,
        view: typeof raw.v === "string" ? raw.v : "",
        rich: true,
        type: raw.y != null ? String(raw.y) : null,
        member: raw.m != null ? String(raw.m) : null,
        overload: raw.o != null ? String(raw.o) : null,
        section: raw.c != null ? String(raw.c) : null,
        bodyTarget: decodeBodyTarget(raw.d),
        library: raw.l != null ? String(raw.l) : null,
        memberBrowse: raw.b === 1,
        memberTextFilter: raw.q != null ? String(raw.q) : "",
        memberKindFilter: raw.k != null ? String(raw.k) : "all",
        memberAccessibilityFilter: raw.e != null ? String(raw.e) : "all",
        memberTraitFilter: raw.r != null ? String(raw.r) : "",
      };
    }
    return { error: "The shared workspace state is invalid and was ignored." };
  } catch {
    return { error: "The shared workspace state is invalid and was ignored." };
  }
}

function resolveView(token: string) {
  const atPackageRoot = token === "pkg" || token.startsWith("pkg:");
  return {
    lens: lenses.some(([id]) => id === token) ? token : null,
    atPackageRoot,
    packageLens: atPackageRoot
      ? (packageLenses.some(([id]) => id === token.split(":")[1])
        ? token.split(":")[1]
        : "overview")
      : null,
  };
}

export interface WorkspaceLocationSnapshot {
  href: string;
  pathname: string;
  search: string;
  hash: string;
}

export function parseWorkspaceLocation(location: WorkspaceLocationSnapshot) {
  const params = new URLSearchParams(location.search);
  const route = location.pathname.split("/").filter(Boolean);
  const packageAt = route.findIndex(part => part.toLowerCase() === "packages");
  const share = decodeWorkspaceShareState(params.get("w"));

  let pkg = packageAt >= 0
    ? decodeURIComponent(route[packageAt + 1] || "")
    : params.get("package");
  let version = packageAt >= 0
    ? decodeURIComponent(route[packageAt + 2] || "")
    : params.get("version");
  let framework = params.get("framework");
  let type = params.get("type");
  let member = params.get("member");
  let overload = params.get("overload");
  let section = params.get("section");
  let bodyTarget: BodyTarget | null = null;
  let viewToken = location.hash.slice(1);
  let tabs: WorkspaceTab[] = [];
  let active = 0;
  let library: string | null = null;
  let memberBrowse = false;
  let memberTextFilter = "";
  let memberKindFilter = "all";
  let memberAccessibilityFilter = "all";
  let memberTraitFilter = "";
  const workspaceNotice = share && "error" in share ? share.error : "";

  if (share && !("error" in share)) {
    tabs = share.tabs;
    if (share.rich) {
      active = Math.min(Math.max(0, share.active), Math.max(0, tabs.length - 1));
      const target = tabs[active];
      if (target) {
        pkg = target.id;
        version = target.version;
        framework = target.framework;
      }
      if (share.view) viewToken = share.view;
      type = share.type;
      member = share.member;
      overload = share.overload;
      section = share.section;
      bodyTarget = share.bodyTarget;
      library = share.library;
      memberBrowse = share.memberBrowse;
      memberTextFilter = share.memberTextFilter;
      memberKindFilter = share.memberKindFilter;
      memberAccessibilityFilter = share.memberAccessibilityFilter;
      memberTraitFilter = share.memberTraitFilter;
    } else {
      const index = tabs.findIndex(tab =>
        pkg && tab.id.toLowerCase() === pkg.toLowerCase());
      active = index >= 0 ? index : 0;
    }
  }
  if (!pkg && tabs.length) {
    const target = tabs[Math.min(Math.max(0, active), tabs.length - 1)];
    pkg = target.id;
    version = target.version;
    framework = target.framework;
  }

  const view = resolveView(viewToken);
  return {
    package: pkg,
    version,
    framework,
    type,
    member,
    overload,
    section,
    bodyTarget,
    lens: view.lens,
    atPackageRoot: view.atPackageRoot,
    packageLens: view.packageLens,
    tabs,
    active,
    library,
    memberBrowse,
    memberTextFilter,
    memberKindFilter,
    memberAccessibilityFilter,
    memberTraitFilter,
    workspaceNotice,
  };
}

export type ParsedWorkspaceLocation = ReturnType<typeof parseWorkspaceLocation>;

export function buildWorkspaceStateUrl(
  base: string,
  state: WorkspaceUrlState,
): URL {
  const url = new URL(base);
  url.pathname = "/";
  const params = new URLSearchParams();
  params.set("package", state.package);
  const shareState = encodeWorkspaceShareState(state);
  const shareError = shareStateLengthError(shareState);
  if (shareError) throw new Error(shareError);
  params.set("w", shareState);
  url.search = params.toString();
  url.hash = "";
  return url;
}

export interface WorkspaceLocationPersistence {
  parseCurrent(): ParsedWorkspaceLocation;
  build(state: WorkspaceUrlState, base?: string): URL;
  sync(state: WorkspaceUrlState): void;
  push(url: string): void;
}

export interface WorkspaceLocationDependencies {
  current(): WorkspaceLocationSnapshot;
  replace(url: string): void;
  push(url: string): void;
}

export function createWorkspaceLocationPersistence(
  dependencies: WorkspaceLocationDependencies,
): WorkspaceLocationPersistence {
  const build = (state: WorkspaceUrlState, base?: string) =>
    buildWorkspaceStateUrl(
      base ?? dependencies.current().href,
      state);
  return {
    parseCurrent() {
      return parseWorkspaceLocation(dependencies.current());
    },
    build,
    sync(state) {
      try {
        dependencies.replace(build(state).toString());
      } catch {
        // Sandboxed frames and overlong state can reject address-bar persistence.
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
}
