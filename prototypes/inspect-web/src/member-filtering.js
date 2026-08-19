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
