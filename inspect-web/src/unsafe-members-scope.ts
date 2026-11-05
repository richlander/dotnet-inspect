import { assertNever } from "./data.ts";
import type { BrowserUnsafeMembers } from "./facades/inspect-web-analysis.d.ts";
import type { PackageInspectionRequestState, UnsafeMembersScope } from "./package-inspection.ts";

// Which census answers an Unsafe view. A view runs the narrowest census that
// answers it and reuses a wider one already requested for the same Library:
// a Library census covers its Types and members, and a Type census covers
// that Type's members. A covering result is used as-is, whatever its outcome.
export type UnsafeCensusSource = "library" | "type" | "member";

export interface UnsafeCensusCoverage {
  library: boolean;
  type: boolean;
}

export function unsafeCensusSourceFor(
  view: UnsafeCensusSource,
  coverage: UnsafeCensusCoverage,
): UnsafeCensusSource {
  if (view === "library" || coverage.library) return "library";
  if (view === "type" || coverage.type) return "type";
  return "member";
}

// Request keys extend the exact Library key, so a different Library, Type, or
// selector never reuses another key's result.
export function unsafeTypeCensusKey(libraryKey: string, typeIdentity: string): string {
  return `${libraryKey}#type=${encodeURIComponent(typeIdentity)}`;
}

export function unsafeMemberCensusKey(
  libraryKey: string,
  typeIdentity: string,
  stableSelector: string,
): string {
  return `${unsafeTypeCensusKey(libraryKey, typeIdentity)}#member=${encodeURIComponent(stableSelector)}`;
}

export function unsafeCensusScope(
  source: UnsafeCensusSource,
  typeIdentity: string,
  stableSelector: string,
): UnsafeMembersScope {
  switch (source) {
    case "library": return { typeIdentity: "", stableSelector: "" };
    case "type": return { typeIdentity, stableSelector: "" };
    case "member": return { typeIdentity, stableSelector };
    default: return assertNever(source, "Unsafe census source");
  }
}

/** The renderer's view of one census request. */
export interface UnsafeCensusRequestView {
  fresh: boolean;
  loading: boolean;
  error: string;
  data: BrowserUnsafeMembers | null;
}

export function unsafeCensusRequestView(
  state: PackageInspectionRequestState<BrowserUnsafeMembers>,
  key: string,
): UnsafeCensusRequestView {
  switch (state.status) {
    case "idle": return { fresh: false, loading: false, error: "", data: null };
    case "loading":
      return { fresh: state.request.key === key, loading: true, error: "", data: null };
    case "ready":
      return { fresh: state.key === key, loading: false, error: "", data: state.result };
    case "failed":
      return { fresh: state.key === key, loading: false, error: state.error, data: null };
    default: return assertNever(state, "Unsafe census request state");
  }
}
