import type { MemberSection, TypeLens } from "./data.ts";
import type { MemberGroup, MemberOverloadSummary } from "./type-panel.ts";

export const MEMBER_TRAITS = [
  ["isStatic", "static"],
  ["isUnsafe", "unsafe"],
  ["isVirtual", "virtual"],
  ["isAbstract", "abstract"],
  ["isOverride", "override"],
  ["isExtension", "extension"],
  ["isObsolete", "obsolete"],
] as const;

export interface MemberGroupFilters {
  query?: string;
  kind?: string;
  accessibility?: string;
  trait?: string;
}

/** The overload fields the filter predicates and body-target matching read. */
interface FilterableMemberOverload extends MemberOverloadSummary {
  accessibility?: string;
  isStatic?: boolean;
  isUnsafe?: boolean;
  isVirtual?: boolean;
  isAbstract?: boolean;
  isOverride?: boolean;
  isExtension?: boolean;
  isObsolete?: boolean;
}

export interface FilterableMemberGroup extends MemberGroup {
  overloads: readonly FilterableMemberOverload[];
}

export function memberGroupMatches(
  group: FilterableMemberGroup,
  filters: MemberGroupFilters,
): boolean {
  const query = (filters.query ?? "").trim().toLowerCase();
  if (filters.kind && filters.kind !== "all" && group.kind !== filters.kind) {
    return false;
  }

  return group.overloads.some(overload => {
    if (filters.accessibility
        && filters.accessibility !== "all"
        && overload.accessibility !== filters.accessibility) {
      return false;
    }
    if (filters.trait
        && !MEMBER_TRAITS.some(
          ([property]) => property === filters.trait && overload[property])) {
      return false;
    }
    return !query
      || group.name.toLowerCase().includes(query)
      || overload.signature.toLowerCase().includes(query);
  });
}

export function filterMemberGroups(
  groups: readonly FilterableMemberGroup[],
  filters: MemberGroupFilters,
): FilterableMemberGroup[] {
  return groups.filter(group => memberGroupMatches(group, filters));
}

export interface MemberScopeState {
  atPackageRoot: boolean;
  lens: TypeLens;
  selectedMemberKey: string;
  memberBrowseTypeId: string;
}

export function memberScopeIsActive(
  state: MemberScopeState,
  currentTypeId: string | null | undefined,
): boolean {
  return !state.atPackageRoot
    && state.lens === "api"
    && Boolean(state.selectedMemberKey || (
      currentTypeId
      && state.memberBrowseTypeId === currentTypeId
    ));
}

export function memberNavTargetIndex(
  currentIndex: number,
  entryCount: number,
  delta: number,
): number {
  if (!entryCount) return -1;
  if (currentIndex < 0) return delta < 0 ? entryCount - 1 : 0;
  return Math.max(0, Math.min(entryCount - 1, currentIndex + delta));
}

export function selectedConcreteOverload<T>(
  overloads: readonly T[],
  selectedIndex: number | null | undefined,
): T | undefined {
  if (overloads.length > 1 && selectedIndex == null) return undefined;
  return overloads[selectedIndex ?? 0];
}

export interface MemberCallGraphWorkState {
  memberCallGraphLoading: boolean;
  memberCallGraphExpanding: boolean;
  memberCallGraphSeq: number;
  memberCallGraphKey: string;
  platformDrillLoading: boolean;
  platformDrillError: string;
}

export function invalidateMemberCallGraphWork(state: MemberCallGraphWorkState): void {
  const incomplete = state.memberCallGraphLoading || state.memberCallGraphExpanding;
  state.memberCallGraphSeq++;
  state.memberCallGraphLoading = false;
  state.memberCallGraphExpanding = false;
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  if (incomplete) state.memberCallGraphKey = "";
}

export interface GraphMemberNavigationWorkState {
  graphMemberNavigationSeq: number;
  graphMemberNavigationTitle: string;
  graphMemberNavigationError: string;
  pendingGraphMemberDeepLink: unknown | null;
}

export function invalidateGraphMemberNavigationWork(
  state: GraphMemberNavigationWorkState,
): void {
  state.graphMemberNavigationSeq++;
  state.graphMemberNavigationTitle = "";
  state.graphMemberNavigationError = "";
  state.pendingGraphMemberDeepLink = null;
}

export function invalidateMemberDestinationWork(
  state: MemberCallGraphWorkState & GraphMemberNavigationWorkState,
): void {
  invalidateMemberCallGraphWork(state);
  invalidateGraphMemberNavigationWork(state);
}

export function invalidateSourceDestinationWork(
  state: MemberCallGraphWorkState & GraphMemberNavigationWorkState,
): void {
  if (state.pendingGraphMemberDeepLink) {
    invalidateMemberCallGraphWork(state);
    return;
  }
  invalidateMemberDestinationWork(state);
}

export function captureLibraryScope(scope: Iterable<string> | null | undefined): string[] | null {
  return scope ? [...scope].sort() : null;
}

export function restoreLibraryScope(
  savedScope: unknown,
  availableLibraries: Iterable<string>,
): Set<string> | null {
  if (!Array.isArray(savedScope) || !savedScope.length) return null;
  const available = new Set(availableLibraries);
  const restored = new Set(
    savedScope.filter((key): key is string => typeof key === "string" && available.has(key)));
  return restored.size > 0 && restored.size < available.size
    ? restored
    : null;
}

export interface BodyTarget {
  memberName: string | null;
  selectorKey: string | null;
  metadataToken: number | null;
}

interface BodySelectorLike {
  memberName: string;
  selectorKey: string;
  token: number;
}

export interface BodyTargetOverload {
  metadataToken?: number | null;
  graphSelectorKey?: string;
  bodySelectors?: readonly BodySelectorLike[] | null;
}

export interface BodyTargetMember {
  name: string;
}

export function bodyTargetMatchesOverload(
  target: Partial<BodyTarget> | null | undefined,
  member: BodyTargetMember | null | undefined,
  overload: BodyTargetOverload | null | undefined,
): boolean {
  if (!target || !overload || !member
    || (target.metadataToken == null && !target.selectorKey && !target.memberName)) {
    return false;
  }
  const candidates = [{
    memberName: member.name,
    selectorKey: overload.graphSelectorKey,
    metadataToken: overload.metadataToken,
  }, ...(overload.bodySelectors ?? []).map(body => ({
    memberName: body.memberName,
    selectorKey: body.selectorKey,
    metadataToken: body.token,
  }))];
  return candidates.some(candidate =>
    (target.memberName == null || target.memberName === candidate.memberName)
    && (target.selectorKey == null || target.selectorKey === candidate.selectorKey)
    && (target.metadataToken == null || target.metadataToken === candidate.metadataToken));
}

export type EncodedBodyTarget = [string | null, string | null, number | null];

export function encodeBodyTarget(target: BodyTarget | null | undefined): EncodedBodyTarget | null {
  if (!target) return null;
  const encoded: EncodedBodyTarget = [
    target.memberName ?? null,
    target.selectorKey ?? null,
    target.metadataToken ?? null,
  ];
  return encoded.some(value => value != null) ? encoded : null;
}

export function decodeBodyTarget(value: unknown): BodyTarget | null {
  if (!Array.isArray(value) || value.length !== 3) return null;
  const values: unknown[] = value;
  const [memberNameValue, selectorKeyValue, metadataTokenValue] = values;
  if ((memberNameValue != null && typeof memberNameValue !== "string")
    || (selectorKeyValue != null && typeof selectorKeyValue !== "string")
    || (metadataTokenValue != null
      && (typeof metadataTokenValue !== "number"
        || !Number.isInteger(metadataTokenValue)))) {
    return null;
  }
  const target: BodyTarget = {
    memberName: memberNameValue || null,
    selectorKey: selectorKeyValue || null,
    metadataToken: metadataTokenValue ?? null,
  };
  return target.memberName || target.selectorKey || target.metadataToken != null
    ? target
    : null;
}

export interface MemberHistoryView {
  memberBrowseTypeId: string;
  selectedMemberKey: string;
  memberKindFilter?: string;
  memberAccessibilityFilter?: string;
  memberTraitFilter?: string;
  memberTextFilter?: string;
  selectedOverloadIndex?: number | null;
  memberSection?: MemberSection;
  bodyTarget?: BodyTarget | null;
}

export interface MemberHistoryType {
  id: string;
}

export interface MemberHistoryMember extends BodyTargetMember {
  key: string;
  overloads: readonly BodyTargetOverload[];
}

export interface RestoredMemberHistoryState {
  selectedMemberKey: string;
  memberBrowseTypeId: string;
  memberKindFilter: string;
  memberAccessibilityFilter: string;
  memberTraitFilter: string;
  memberTextFilter: string;
  selectedOverloadIndex: number | null;
  memberSection: MemberSection;
  selectedBodyTarget: BodyTarget | null;
}

export function restoreMemberHistoryState(
  view: MemberHistoryView,
  type: MemberHistoryType | null | undefined,
  member: MemberHistoryMember | null | undefined,
  memberSectionIds: readonly MemberSection[] = [],
): RestoredMemberHistoryState {
  const restoreMemberScope = Boolean(type)
    && view.memberBrowseTypeId === type!.id
    && (!view.selectedMemberKey || Boolean(member));
  const savedOverloadIndex = view.selectedOverloadIndex;
  const overloadIndex = member
    && Number.isInteger(savedOverloadIndex)
    && savedOverloadIndex! >= 0
    && savedOverloadIndex! < member.overloads.length
    ? savedOverloadIndex!
    : null;
  const invalidOverload =
    savedOverloadIndex != null && overloadIndex == null;
  const overload = member
    ? member.overloads[overloadIndex ?? (member.overloads.length === 1 ? 0 : -1)]
    : null;
  const requestedSection =
    restoreMemberScope && member && !invalidOverload
      ? (view.memberSection ?? "overview")
      : "overview";

  return {
    selectedMemberKey: restoreMemberScope && member ? member.key : "",
    memberBrowseTypeId: restoreMemberScope ? type!.id : "",
    memberKindFilter: type ? (view.memberKindFilter ?? "all") : "all",
    memberAccessibilityFilter:
      type ? (view.memberAccessibilityFilter ?? "all") : "all",
    memberTraitFilter: type ? (view.memberTraitFilter ?? "") : "",
    memberTextFilter: type ? (view.memberTextFilter ?? "") : "",
    selectedOverloadIndex: restoreMemberScope ? overloadIndex : null,
    memberSection: memberSectionIds.includes(requestedSection)
      ? requestedSection
      : "overview",
    selectedBodyTarget:
      restoreMemberScope
        && !invalidOverload
        && bodyTargetMatchesOverload(view.bodyTarget, member, overload)
        ? (view.bodyTarget ?? null)
        : null,
  };
}
