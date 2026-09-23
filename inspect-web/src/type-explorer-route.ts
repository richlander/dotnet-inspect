import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";
import {
  historyEntryId,
  withHistoryEntryId,
} from "./package-query-route.ts";

export type TypeExplorerBodyMode =
  | "Bodies"
  | "Skeleton"
  | "SelectedBody";
export type TypeExplorerPlacement = "All" | "Instance" | "Static";
export type TypeExplorerAccessibility =
  | "Unknown"
  | "Private"
  | "PrivateProtected"
  | "Protected"
  | "Internal"
  | "ProtectedInternal"
  | "Public";

export interface TypeExplorerMemberIdentity {
  readonly stableSelector: string;
  readonly canonicalSignature: string;
  readonly fingerprint: string;
  readonly typeFullName: string;
  readonly memberName: string;
}

export interface TypeExplorerIntent {
  readonly version: 1;
  readonly bodyMode: TypeExplorerBodyMode;
  readonly placement: TypeExplorerPlacement;
  readonly accessibilities: readonly TypeExplorerAccessibility[];
  readonly includeGenerated: boolean;
  readonly includeDocumentation: boolean;
  readonly includeAttributes: boolean;
  readonly selectedMember: TypeExplorerMemberIdentity | null;
  readonly documentRevision: string | null;
}

export interface TypeExplorerHistory {
  readonly predecessorEntryId: string;
  readonly returnFocus: "explore-source";
}

const TYPE_EXPLORER_PATH = ROUTED_ENTRY_PATHS.typeExplorer;
const TYPE_EXPLORER_INTENT_PARAMETER = "te";

const TYPE_EXPLORER_HISTORY_KEY = "dotnetInspectTypeExplorer";
const MAXIMUM_INTENT_CHARACTERS = 32 * 1024;
const ACCESSIBILITIES: readonly TypeExplorerAccessibility[] = [
  "Unknown",
  "Private",
  "PrivateProtected",
  "Protected",
  "Internal",
  "ProtectedInternal",
  "Public",
];

export function defaultTypeExplorerIntent(): TypeExplorerIntent {
  return {
    version: 1,
    bodyMode: "Bodies",
    placement: "All",
    accessibilities: ACCESSIBILITIES,
    includeGenerated: false,
    includeDocumentation: true,
    includeAttributes: true,
    selectedMember: null,
    documentRevision: null,
  };
}

export function isTypeExplorerPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, TYPE_EXPLORER_PATH);
}

export function typeExplorerHistoryState(
  value: unknown,
  entryId: string,
  route: TypeExplorerHistory,
): Record<string, unknown> {
  return {
    ...withHistoryEntryId(value, entryId),
    [TYPE_EXPLORER_HISTORY_KEY]: route,
  };
}

export function readTypeExplorerHistory(
  value: unknown,
): TypeExplorerHistory | null {
  if (!isRecord(value)) return null;
  const route = ownData(value, TYPE_EXPLORER_HISTORY_KEY);
  if (!isRecord(route)) return null;
  const predecessorEntryId = ownData(route, "predecessorEntryId");
  const returnFocus = ownData(route, "returnFocus");
  return typeof predecessorEntryId === "string"
    && predecessorEntryId.length > 0
    && returnFocus === "explore-source"
    ? { predecessorEntryId, returnFocus }
    : null;
}

export function isTypeExplorerPredecessor(
  value: unknown,
  predecessorEntryId: string | null,
): boolean {
  return predecessorEntryId !== null
    && historyEntryId(value) === predecessorEntryId;
}

export function encodeTypeExplorerIntent(
  intent: TypeExplorerIntent,
): string {
  const bytes = new TextEncoder().encode(JSON.stringify(intent));
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary)
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/u, "");
}

export function decodeTypeExplorerIntent(
  value: string | null,
): TypeExplorerIntent | null {
  if (value === null || value.length === 0
    || value.length > MAXIMUM_INTENT_CHARACTERS) {
    return null;
  }
  try {
    const base64 = value.replaceAll("-", "+").replaceAll("_", "/");
    const padded = base64 + "=".repeat((4 - base64.length % 4) % 4);
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, character =>
      character.charCodeAt(0));
    const parsed: unknown =
      JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
    return parseIntent(parsed);
  } catch {
    return null;
  }
}

export function typeExplorerUrl(
  current: URL,
  intent: TypeExplorerIntent,
): URL {
  const destination = new URL(current);
  destination.pathname = TYPE_EXPLORER_PATH;
  destination.searchParams.set(
    TYPE_EXPLORER_INTENT_PARAMETER,
    encodeTypeExplorerIntent(intent));
  return destination;
}

export function typeSourceUrl(current: URL): URL {
  const destination = new URL(current);
  destination.pathname = "/";
  destination.searchParams.delete(TYPE_EXPLORER_INTENT_PARAMETER);
  return destination;
}

function parseIntent(value: unknown): TypeExplorerIntent | null {
  if (!isRecord(value)
    || !hasExactKeys(value, [
      "version",
      "bodyMode",
      "placement",
      "accessibilities",
      "includeGenerated",
      "includeDocumentation",
      "includeAttributes",
      "selectedMember",
      "documentRevision",
    ])
    || value.version !== 1
    || !isBodyMode(value.bodyMode)
    || !isPlacement(value.placement)
    || !Array.isArray(value.accessibilities)
    || value.accessibilities.length > ACCESSIBILITIES.length
    || !value.accessibilities.every(isAccessibility)
    || new Set(value.accessibilities).size !== value.accessibilities.length
    || typeof value.includeGenerated !== "boolean"
    || typeof value.includeDocumentation !== "boolean"
    || typeof value.includeAttributes !== "boolean"
    || (value.documentRevision !== null
      && (typeof value.documentRevision !== "string"
        || !/^[0-9a-f]{64}$/iu.test(value.documentRevision)))) {
    return null;
  }
  const selectedMember = value.selectedMember === null
    ? null
    : parseMemberIdentity(value.selectedMember);
  if (value.selectedMember !== null && selectedMember === null) return null;
  return {
    version: 1,
    bodyMode: value.bodyMode,
    placement: value.placement,
    accessibilities: value.accessibilities,
    includeGenerated: value.includeGenerated,
    includeDocumentation: value.includeDocumentation,
    includeAttributes: value.includeAttributes,
    selectedMember,
    documentRevision: value.documentRevision,
  };
}

function parseMemberIdentity(
  value: unknown,
): TypeExplorerMemberIdentity | null {
  if (!isRecord(value)
    || !hasExactKeys(value, [
      "stableSelector",
      "canonicalSignature",
      "fingerprint",
      "typeFullName",
      "memberName",
    ])) {
    return null;
  }
  const stableSelector = value.stableSelector;
  const canonicalSignature = value.canonicalSignature;
  const fingerprint = value.fingerprint;
  const typeFullName = value.typeFullName;
  const memberName = value.memberName;
  if (!validMemberIdentityText(stableSelector)
    || !validMemberIdentityText(canonicalSignature)
    || !validMemberIdentityText(fingerprint)
    || !validMemberIdentityText(typeFullName)
    || !validMemberIdentityText(memberName)) {
    return null;
  }
  if (!/^[0-9a-f]{10}$/iu.test(fingerprint)) return null;
  return {
    stableSelector,
    canonicalSignature,
    fingerprint,
    typeFullName,
    memberName,
  };
}

function validMemberIdentityText(value: unknown): value is string {
  return typeof value === "string"
    && value.length > 0
    && value.length <= 16 * 1024;
}

function isRecord(
  value: unknown,
): value is Record<string, unknown> {
  return typeof value === "object"
    && value !== null
    && !Array.isArray(value);
}

function ownData(
  value: Record<string, unknown>,
  name: string,
): unknown {
  const property = Object.getOwnPropertyDescriptor(value, name);
  return property !== undefined && "value" in property
    ? property.value
    : undefined;
}

function hasExactKeys(
  value: Record<string, unknown>,
  expected: readonly string[],
): boolean {
  const keys = Object.keys(value);
  return keys.length === expected.length
    && expected.every(key => Object.hasOwn(value, key));
}

function isBodyMode(value: unknown): value is TypeExplorerBodyMode {
  return value === "Bodies"
    || value === "Skeleton"
    || value === "SelectedBody";
}

function isPlacement(value: unknown): value is TypeExplorerPlacement {
  return value === "All" || value === "Instance" || value === "Static";
}

function isAccessibility(
  value: unknown,
): value is TypeExplorerAccessibility {
  return value === "Unknown"
    || value === "Private"
    || value === "PrivateProtected"
    || value === "Protected"
    || value === "Internal"
    || value === "ProtectedInternal"
    || value === "Public";
}
