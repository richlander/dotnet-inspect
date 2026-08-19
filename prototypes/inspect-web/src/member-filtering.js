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
