import {
  graphMemberShareTarget,
  graphMemberTargetFromPacket,
  isMemberSection,
  isPackageLens,
  isTypeLens,
  normalizeShareTabs,
  platformPackToken,
  replaceCurrentNavigationEntry,
  shareStateLengthError,
  type MemberSection,
  type PackageLens,
  type PlatformPack,
  type GraphMemberShareIdentity,
  type GraphMemberShareTarget,
  type TypeLens,
  type WorkspaceTab,
} from "./data.ts";
import { parseNonNegativeInteger } from "./dom-data.ts";
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

export interface NavigationHistory {
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
): NavigationHistory {
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
  };
}

export interface WorkspaceDeepLink {
  type?: string | null;
  member?: string | null;
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

export interface WorkspaceUrlState {
  package: string;
  tabs: WorkspaceTab[];
  active: number;
  lens: TypeLens;
  atPackageRoot: boolean;
  packageLens: PackageLens;
  library: string | null;
  libraryPack: PlatformPack | null;
  selectedTypeId: string;
  selectedMemberKey: string;
  selectedOverloadIndex: number | null;
  memberSection: MemberSection;
  selectedBodyTarget: BodyTarget | null;
  graphTarget: GraphMemberShareIdentity | null;
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
  p?: PlatformPack;
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
  g?: GraphMemberShareTarget;
}

interface DecodedShareState {
  tabs: WorkspaceTab[];
  active: number;
  view: string;
  rich: boolean;
  type: string | null;
  member: string | null;
  overload: number | null;
  section: MemberSection | null;
  rejectedFields: string[];
  bodyTarget: BodyTarget | null;
  library: string | null;
  libraryPack: PlatformPack | null;
  memberBrowse: boolean;
  memberTextFilter: string;
  memberKindFilter: string;
  memberAccessibilityFilter: string;
  memberTraitFilter: string;
  graphTarget: GraphMemberShareIdentity | null;
}

type ShareStateResult = DecodedShareState | { error: string } | null;

const invalidShareState =
  "The shared workspace state is invalid and was ignored.";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function hasOwn(record: Record<string, unknown>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(record, key);
}

function richSharePacketIsValid(
  raw: Record<string, unknown>,
  sourceIndexes: readonly number[],
): raw is Record<string, unknown> & { a: number } {
  if (typeof raw.a !== "number"
    || !Number.isInteger(raw.a)
    || raw.a < 0
    || Object.is(raw.a, -0)
    || raw.a >= sourceIndexes.length) {
    return false;
  }

  const optionalStrings = ["l", "v", "y", "m", "c", "q", "k", "e", "r"];
  if (optionalStrings.some(key => hasOwn(raw, key) && typeof raw[key] !== "string"))
    return false;
  if (hasOwn(raw, "p") && platformPackToken(raw.p) === null) return false;
  if (hasOwn(raw, "o")
    && (typeof raw.o !== "number"
      || !Number.isInteger(raw.o)
      || raw.o < 0)) {
    return false;
  }
  if (hasOwn(raw, "d") && decodeBodyTarget(raw.d) === null) return false;
  if (hasOwn(raw, "b") && raw.b !== 1) return false;
  return true;
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
  if (state.libraryPack) packet.p = state.libraryPack;
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
    const graphTarget = graphMemberShareTarget(state.graphTarget);
    if (graphTarget
      && state.selectedTypeId
      && state.selectedMemberKey
      && state.selectedOverloadIndex != null
      && Number.isInteger(state.selectedOverloadIndex)
      && state.selectedOverloadIndex >= 0) {
      packet.g = graphTarget;
    } else if (state.selectedBodyTarget) {
      const encodedBodyTarget = encodeBodyTarget(state.selectedBodyTarget);
      if (encodedBodyTarget) packet.d = encodedBodyTarget;
    }
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
        rejectedFields: [],
        bodyTarget: null,
        library: null,
        libraryPack: null,
        memberBrowse: false,
        memberTextFilter: "",
        memberKindFilter: "all",
        memberAccessibilityFilter: "all",
        memberTraitFilter: "",
        graphTarget: null,
      };
    }
    if (isRecord(raw) && Array.isArray(raw.t)) {
      const normalized = normalizeShareTabs(raw.t);
      if (normalized.error) return { error: normalized.error };
      const graphMember = graphMemberTargetFromPacket(raw);
      if (graphMember.error) return { error: graphMember.error };
      if (!richSharePacketIsValid(raw, normalized.sourceIndexes))
        return { error: invalidShareState };
      const active = normalized.sourceIndexes[raw.a];
      if (active === undefined) return { error: invalidShareState };
      return {
        tabs: normalized.tabs,
        active,
        view: typeof raw.v === "string" ? raw.v : "",
        rich: true,
        type: typeof raw.y === "string" ? raw.y : null,
        member: typeof raw.m === "string" ? raw.m : null,
        overload: typeof raw.o === "number" ? raw.o : null,
        section: typeof raw.c === "string" && isMemberSection(raw.c)
          ? raw.c
          : null,
        rejectedFields: Object.hasOwn(raw, "c")
          && (typeof raw.c !== "string" || !isMemberSection(raw.c))
          ? ["section"]
          : [],
        bodyTarget: decodeBodyTarget(raw.d),
        library: typeof raw.l === "string" ? raw.l : null,
        libraryPack: platformPackToken(raw.p),
        memberBrowse: raw.b === 1,
        memberTextFilter: typeof raw.q === "string" ? raw.q : "",
        memberKindFilter: typeof raw.k === "string" ? raw.k : "all",
        memberAccessibilityFilter: typeof raw.e === "string" ? raw.e : "all",
        memberTraitFilter: typeof raw.r === "string" ? raw.r : "",
        graphTarget: graphMember.target,
      };
    }
    return { error: invalidShareState };
  } catch {
    return { error: invalidShareState };
  }
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

export function parseWorkspaceLocation(location: WorkspaceLocationSnapshot) {
  const params = new URLSearchParams(location.search);
  const route = location.pathname.split("/");
  const packageAt = route.findIndex(part => part.toLowerCase() === "packages");
  const share = decodeWorkspaceShareState(params.get("w"));
  // Every URL field that names nothing is recorded here rather than silently becoming a
  // default, so a stale or mistyped link fails visibly instead of appearing to have worked.
  const rejectedFields: string[] = [];

  let pkg: string | null;
  let version: string | null;
  if (packageAt >= 0) {
    const packageToken = route[packageAt + 1];
    const versionToken = route[packageAt + 2];
    pkg = packageToken ? decodeRouteComponent(packageToken) : null;
    version = versionToken ? decodeRouteComponent(versionToken) : null;
    if (pkg === null) rejectedFields.push("package");
    if (version === null) rejectedFields.push("version");
  } else {
    pkg = params.get("package");
    version = params.get("version");
  }
  let framework = params.get("framework");
  let type = params.get("type");
  let member = params.get("member");
  const overloadToken = params.get("overload");
  // The overload coordinate used to survive as a raw string and be coerced with `Number()`
  // at its use site, so `"+1"`, `" 1"`, `"1e0"`, `"01"`, and `"-0"` all selected a real
  // overload. It is parsed once here with the canonical validator.
  let overload = overloadToken === null
    ? null
    : parseNonNegativeInteger(overloadToken);
  if (overloadToken !== null && overload === null) rejectedFields.push("overload");
  const sectionToken = params.get("section");
  let section: MemberSection | null = isMemberSection(sectionToken)
    ? sectionToken
    : null;
  if (sectionToken !== null && section === null) rejectedFields.push("section");
  let bodyTarget: BodyTarget | null = null;
  let viewToken = location.hash.slice(1);
  let tabs: WorkspaceTab[] = [];
  let active = 0;
  let library: string | null = null;
  let libraryPack: PlatformPack | null = null;
  let memberBrowse = false;
  let memberTextFilter = "";
  let memberKindFilter = "all";
  let memberAccessibilityFilter = "all";
  let memberTraitFilter = "";
  let graphTarget: GraphMemberShareIdentity | null = null;
  const shareError = share && "error" in share ? share.error : "";

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
      // A share packet is untrusted input from the URL just like a query parameter, so its
      // overload takes the same canonical parse rather than a raw `String(...)`.
      overload = share.overload === null
        ? null
        : parseOverloadCoordinate(share.overload);
      if (share.overload !== null && overload === null) rejectedFields.push("overload");
      section = share.section;
      rejectedFields.push(...share.rejectedFields);
      bodyTarget = share.bodyTarget;
      library = share.library;
      libraryPack = share.libraryPack;
      memberBrowse = share.memberBrowse;
      memberTextFilter = share.memberTextFilter;
      memberKindFilter = share.memberKindFilter;
      memberAccessibilityFilter = share.memberAccessibilityFilter;
      memberTraitFilter = share.memberTraitFilter;
      graphTarget = share.graphTarget;
    } else {
      const index = tabs.findIndex(tab =>
        pkg && tab.id.toLowerCase() === pkg.toLowerCase());
      active = index >= 0 ? index : 0;
    }
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
    overload,
    section,
    bodyTarget,
    lens: view.lens,
    atPackageRoot: view.atPackageRoot,
    packageLens: view.packageLens,
    tabs,
    active,
    library,
    libraryPack,
    memberBrowse,
    memberTextFilter,
    memberKindFilter,
    memberAccessibilityFilter,
    memberTraitFilter,
    graphTarget,
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
