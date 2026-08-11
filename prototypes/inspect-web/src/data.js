export const lenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"]
];

export const packageLenses = [
  ["overview", "Overview"],
  ["dependencies", "Dependencies"],
  ["integrations", "Integrations"],
  ["opportunities", "Opportunities"],
  ["analysis", "Analysis"],
  ["metadata", "Metadata"]
];

export const rootCommands = [
  ["type", "select a public type"],
  ["types", "filter or group the type index"],
  ["show", "change the active lens"],
  ["framework", "select a target framework"],
  ["find", "search the current package"],
  ["clear", "clear the current filter"],
  ["share", "copy a link to this selection"]
];

export function packageIdentityKey(pkg) {
  if (!pkg) return "";
  return [pkg.id, pkg.version, pkg.activeFramework]
    .map(value => encodeURIComponent(String(value || "").toLowerCase()))
    .join("|");
}

export function spotlightCandidateKey(pkg, typeId) {
  return `${packageIdentityKey(pkg)}\u0000${typeId}`;
}

export function spotlightCandidateSignature(activePackage, packages) {
  return `${packageIdentityKey(activePackage)}#${packages
    .map(pkg => `${packageIdentityKey(pkg)}:${pkg.types?.length ?? 0}`)
    .join("|")}`;
}

export function packageForView(packages, view) {
  if (view.packageKey) {
    return packages.find(pkg => packageIdentityKey(pkg) === view.packageKey) ?? null;
  }
  return packages.find(pkg => pkg.id === view.package) ?? null;
}

export function callGraphTargetTypeId(target) {
  return target?.typeMetadataId || "";
}

export function graphMemberSelection(groups, target) {
  const bodyMatches = [];
  for (let groupIndex = 0; groupIndex < groups.length; groupIndex++) {
    const group = groups[groupIndex];
    for (let overloadIndex = 0; overloadIndex < group.overloads.length; overloadIndex++) {
      const overload = group.overloads[overloadIndex];
      if ((overload.bodySelectors ?? []).some(body =>
        body.memberName === target.memberName
        && body.selectorKey === target.selectorKey
        && (target.metadataToken == null || body.token === target.metadataToken))) {
        bodyMatches.push({ groupIndex, overloadIndex });
      }
    }
  }
  if (bodyMatches.length === 1) return bodyMatches[0];

  const ownerMatches = [];
  for (let groupIndex = 0; groupIndex < groups.length; groupIndex++) {
    const group = groups[groupIndex];
    for (let overloadIndex = 0; overloadIndex < group.overloads.length; overloadIndex++) {
      if (group.overloads[overloadIndex].graphSelectorKey === target.selectorKey)
        ownerMatches.push({ groupIndex, overloadIndex });
    }
  }
  return ownerMatches.length === 1 ? ownerMatches[0] : null;
}

export function scopedRequestState(activeKey, requestKey, loading, error) {
  return activeKey === requestKey
    ? { loading, error }
    : { loading: false, error: "" };
}

export function mermaidLabel(value) {
  let encoded = "";
  for (const character of String(value ?? "")) {
    const scalar = character.codePointAt(0);
    if (character === "&") encoded += "&amp;";
    else if (character === "<") encoded += "&lt;";
    else if (character === ">") encoded += "&gt;";
    else if (character === '"') encoded += "&quot;";
    else if (character === "\\") encoded += "&#92;";
    else if (scalar < 0x20 || (scalar >= 0x7f && scalar <= 0x9f)
      || scalar === 0x2028 || scalar === 0x2029) {
      encoded += `&#92;u${scalar.toString(16).toUpperCase().padStart(4, "0")}`;
    } else encoded += character;
  }
  return encoded;
}
