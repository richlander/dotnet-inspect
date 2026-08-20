export const MEMBER_TRAITS = [
  ["isStatic", "static"],
  ["isUnsafe", "unsafe"],
  ["isVirtual", "virtual"],
  ["isAbstract", "abstract"],
  ["isOverride", "override"],
  ["isExtension", "extension"],
  ["isObsolete", "obsolete"],
];

export function memberGroupMatches(group, filters) {
  const query = String(filters.query || "").trim().toLowerCase();
  if (filters.kind && filters.kind !== "all" && group.kind !== filters.kind) {
    return false;
  }

  return group.overloads.some(overload => {
    if (filters.accessibility
        && filters.accessibility !== "all"
        && overload.accessibility !== filters.accessibility) {
      return false;
    }
    if (filters.trait && !overload[filters.trait]) {
      return false;
    }
    return !query
      || group.name.toLowerCase().includes(query)
      || String(overload.signature || "").toLowerCase().includes(query);
  });
}

export function filterMemberGroups(groups, filters) {
  return groups.filter(group => memberGroupMatches(group, filters));
}

export function memberScopeIsActive(state, currentTypeId) {
  return !state.atPackageRoot
    && state.lens === "api"
    && Boolean(state.selectedMemberKey || (
      currentTypeId
      && state.memberBrowseTypeId === currentTypeId
    ));
}

export function memberNavTargetIndex(currentIndex, entryCount, delta) {
  if (!entryCount) return -1;
  if (currentIndex < 0) return delta < 0 ? entryCount - 1 : 0;
  return Math.max(0, Math.min(entryCount - 1, currentIndex + delta));
}

export function invalidateMemberCallGraphWork(state) {
  const incomplete = state.memberCallGraphLoading || state.memberCallGraphExpanding;
  state.memberCallGraphSeq++;
  state.memberCallGraphLoading = false;
  state.memberCallGraphExpanding = false;
  state.platformDrillLoading = false;
  state.platformDrillError = "";
  if (incomplete) state.memberCallGraphKey = "";
}

export function captureLibraryScope(scope) {
  return scope ? [...scope].sort() : null;
}

export function restoreLibraryScope(savedScope, availableLibraries) {
  if (!Array.isArray(savedScope) || !savedScope.length) return null;
  const available = new Set(availableLibraries);
  const restored = new Set(
    savedScope.filter(key => typeof key === "string" && available.has(key)));
  return restored.size > 0 && restored.size < available.size
    ? restored
    : null;
}

export function bodyTargetMatchesOverload(target, member, overload) {
  if (!target || !overload
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

export function encodeBodyTarget(target) {
  if (!target) return null;
  return [
    target.memberName ?? null,
    target.selectorKey ?? null,
    target.metadataToken ?? null,
  ];
}

export function decodeBodyTarget(value) {
  if (!Array.isArray(value) || value.length !== 3) return null;
  const [memberNameValue, selectorKeyValue, metadataTokenValue] = value;
  if ((memberNameValue != null && typeof memberNameValue !== "string")
    || (selectorKeyValue != null && typeof selectorKeyValue !== "string")
    || (metadataTokenValue != null && !Number.isInteger(metadataTokenValue))) {
    return null;
  }
  const target = {
    memberName: memberNameValue || null,
    selectorKey: selectorKeyValue || null,
    metadataToken: metadataTokenValue,
  };
  return target.memberName || target.selectorKey || target.metadataToken != null
    ? target
    : null;
}

export function restoreMemberHistoryState(
  view,
  type,
  member,
  memberSectionIds = []) {
  const restoreMemberScope = Boolean(type)
    && view.memberBrowseTypeId === type.id
    && (!view.selectedMemberKey || member);
  const savedOverloadIndex = view.selectedOverloadIndex;
  const overloadIndex = member
    && Number.isInteger(savedOverloadIndex)
    && savedOverloadIndex >= 0
    && savedOverloadIndex < member.overloads.length
    ? savedOverloadIndex
    : null;
  const invalidOverload =
    savedOverloadIndex != null && overloadIndex == null;
  const overload = member
    ? member.overloads[overloadIndex ?? (member.overloads.length === 1 ? 0 : -1)]
    : null;
  const requestedSection =
    restoreMemberScope && member && !invalidOverload
      ? view.memberSection
      : "overview";

  return {
    selectedMemberKey: restoreMemberScope && member ? member.key : "",
    memberBrowseTypeId: restoreMemberScope ? type.id : "",
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
        ? view.bodyTarget
        : null,
  };
}
