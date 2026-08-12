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

export const MAX_WORKSPACE_PACKAGES = 12;
export const MAX_SHARE_STATE_CHARACTERS = 65536;

export function packageIdentityKey(pkg) {
  if (!pkg) return "";
  return [pkg.id, pkg.version, pkg.activeFramework]
    .map(value => encodeURIComponent(String(value || "").toLowerCase()))
    .join("|");
}

export function assemblyDescriptorForType(assemblies, type) {
  if (type?.assemblyId) {
    return assemblies?.find(assembly => assembly.id === type.assemblyId) ?? null;
  }

  const name = type?.assembly || "";
  const bare = name.endsWith(".dll") ? name.slice(0, -4) : name;
  return assemblies?.find(assembly =>
    assembly.name === name
    || assembly.name === bare
    || assembly.name === `${bare}.dll`) ?? null;
}

export function normalizeShareTabs(list) {
  if (!Array.isArray(list)) {
    return {
      tabs: [],
      sourceIndexes: [],
      error: "The shared workspace state is invalid and was ignored."
    };
  }
  if (list.length > MAX_WORKSPACE_PACKAGES) {
    return {
      tabs: [],
      sourceIndexes: [],
      error: `The shared workspace exceeds the ${MAX_WORKSPACE_PACKAGES}-package limit and was ignored.`
    };
  }

  const tabs = [];
  const sourceIndexes = [];
  const identityIndexes = new Map();
  for (let sourceIndex = 0; sourceIndex < list.length; sourceIndex++) {
    const tuple = list[sourceIndex];
    if (!Array.isArray(tuple)
      || tuple.length < 1
      || tuple.length > 3
      || tuple.some(value => typeof value !== "string")
      || !tuple[0].trim()) {
      return {
        tabs: [],
        sourceIndexes: [],
        error: "The shared workspace state is invalid and was ignored."
      };
    }
    const tab = {
      id: tuple[0],
      version: tuple[1] || "latest",
      framework: tuple[2] || ""
    };
    const identity = packageIdentityKey({
      id: tab.id,
      version: tab.version,
      activeFramework: tab.framework
    });
    if (!identityIndexes.has(identity)) {
      identityIndexes.set(identity, tabs.length);
      tabs.push(tab);
    }
    sourceIndexes[sourceIndex] = identityIndexes.get(identity);
  }
  return { tabs, sourceIndexes, error: "" };
}

export function shareStateLengthError(value) {
  return String(value || "").length > MAX_SHARE_STATE_CHARACTERS
    ? `The shared workspace state exceeds the ${MAX_SHARE_STATE_CHARACTERS}-character limit and was ignored.`
    : "";
}

export function retainWorkspacePackage(
  packages,
  activePackage,
  packageModel,
  replacedPackage = null) {
  const evicted = [];
  const next = packages.filter(item => {
    if (item !== replacedPackage) return true;
    evicted.push(item);
    return false;
  });
  const existing = next.findIndex(item =>
    packageIdentityKey(item) === packageIdentityKey(packageModel));
  if (existing >= 0)
    next[existing] = packageModel;
  else
    next.push(packageModel);

  while (next.length > MAX_WORKSPACE_PACKAGES) {
    const eviction = next.findIndex(item =>
      packageIdentityKey(item) !== packageIdentityKey(activePackage)
      && packageIdentityKey(item) !== packageIdentityKey(packageModel));
    if (eviction < 0) break;
    evicted.push(...next.splice(eviction, 1));
  }
  return { packages: next, evicted };
}

export function removeWorkspacePackage(packages, activePackage, packageKey) {
  const index = packages.findIndex(item => packageIdentityKey(item) === packageKey);
  if (index < 0 || packages[index].isRuntimePack) {
    return { packages, active: activePackage, closed: null };
  }

  const closed = packages[index];
  const remaining = packages.filter((_, candidate) => candidate !== index);
  const active = packageIdentityKey(activePackage) === packageKey
    ? remaining[Math.min(index, remaining.length - 1)] ?? null
    : activePackage;
  return { packages: remaining, active, closed };
}

export function dependencyGroupSelectionMessage(data) {
  return data?.dependencyGroupError || "";
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

export function packageCoordinateMatchesLocation(pkg, location) {
  if (!pkg || !location?.package || !location.version || !location.framework) return false;
  return String(pkg.id).toLowerCase() === String(location.package).toLowerCase()
    && String(pkg.version).toLowerCase() === String(location.version).toLowerCase()
    && String(pkg.activeFramework).toLowerCase() === String(location.framework).toLowerCase();
}

export function workspaceCoordinatesMatch(packages, tabs) {
  if (!Array.isArray(packages) || !Array.isArray(tabs) || packages.length !== tabs.length)
    return false;
  return tabs.every((tab, index) =>
    packageIdentityKey(packages[index]) === packageIdentityKey({
      id: tab.id,
      version: tab.version,
      activeFramework: tab.framework
    }));
}

export function callGraphTargetTypeId(target) {
  return target?.typeMetadataId || "";
}

export function uniqueTypeByQueryId(types, queryId) {
  const matches = (types ?? []).filter(type =>
    (type.queryId ?? type.id) === queryId);
  return matches.length === 1 ? matches[0] : null;
}

export function callGraphAssemblyIdentityMatches(target, assembly) {
  const hasVersion = Object.prototype.hasOwnProperty.call(
    target ?? {},
    "assemblyVersion");
  if (!hasVersion) return true;
  if (!target?.assemblyVersion) return false;
  if (!assembly) return false;
  const normalizeCulture = value => {
    const normalized = String(value ?? "").toLowerCase();
    return normalized === "neutral" ? "" : normalized;
  };
  return String(assembly.name ?? "").toLowerCase()
      === String(target.assembly ?? "").toLowerCase()
    && String(assembly.version ?? "") === String(target.assemblyVersion)
    && normalizeCulture(assembly.culture) === normalizeCulture(target.assemblyCulture)
    && String(assembly.publicKeyToken ?? "").toLowerCase()
      === String(target.assemblyPublicKeyToken ?? "").toLowerCase();
}

export function resolveLoadedGraphTargetCandidate(packages, target) {
  const typeId = callGraphTargetTypeId(target);
  if (!typeId || !target?.assembly) return { status: "missing" };
  const matches = [];
  for (const pkg of packages) {
    if (!pkg || pkg.isRuntimePack) continue;
    for (const type of pkg.types ?? []) {
      const assembly = String(
        type.assemblyName ?? type.assembly ?? pkg.assembly ?? "")
        .replace(/\.dll$/i, "");
      const descriptors = pkg.assemblies ?? [];
      const descriptor = type.assemblyId
        ? descriptors.find(candidate => candidate.id === type.assemblyId)
        : descriptors.find(candidate =>
            String(candidate.name ?? "").replace(/\.dll$/i, "").toLowerCase()
              === assembly.toLowerCase());
      if (assembly.toLowerCase() === target.assembly.toLowerCase()
          && callGraphAssemblyIdentityMatches(target, descriptor)
          && (type.metadataId ?? type.queryId ?? type.id) === typeId) {
        matches.push({ pkg, type });
        if (matches.length > 1) return { status: "ambiguous" };
      }
    }
  }
  return matches.length === 1
    ? { status: "unique", ...matches[0] }
    : { status: "missing" };
}

export function graphTargetNavigationDisposition(candidate, target) {
  if (candidate.status === "ambiguous") return "blocked";
  if (candidate.status === "unique") return "loaded";
  if (Object.prototype.hasOwnProperty.call(
      target ?? {},
      "assemblyVersion")
      && !target?.assemblyVersion) {
    return "none";
  }
  return target?.kind === "external"
      && target.assembly
      && callGraphTargetTypeId(target)
    ? "platform"
    : "none";
}

export function callGraphDiagnosticsMessage(diagnostics) {
  if (!diagnostics?.isIncomplete) return "";
  const boundaries = [];
  if (diagnostics.hasUnexploredTraversalBoundary)
    boundaries.push("unexplored traversal");
  if (diagnostics.hasAnalysisFailureBoundary)
    boundaries.push("analysis failure");
  const boundaryText = boundaries.length
    ? ` Boundaries: ${boundaries.join(" and ")}.`
    : "";
  return `Partial call graph: ${diagnostics.incompleteNodes} incomplete node${diagnostics.incompleteNodes === 1 ? "" : "s"}, ${diagnostics.incompleteEdges} incomplete edge${diagnostics.incompleteEdges === 1 ? "" : "s"}, and ${diagnostics.bindingIdentityConflicts} binding identity conflict${diagnostics.bindingIdentityConflicts === 1 ? "" : "s"}.${boundaryText}`;
}

export function parameterTitleHtml(parameters) {
  if (!parameters.length) return "()";
  const escape = value => String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
  return `(${parameters.map(parameter => escape(parameter.type || "")).join(", ")})`;
}

export const MARKDOWN_SANITIZE_OPTIONS = Object.freeze({
  ALLOWED_TAGS: Object.freeze([
    "a", "blockquote", "br", "code", "del", "em", "h1", "h2", "h3", "h4", "h5", "h6",
    "hr", "li", "ol", "p", "pre", "strong", "table", "tbody", "td", "th", "thead", "tr", "ul"
  ]),
  ALLOWED_ATTR: Object.freeze(["title"]),
  ALLOW_ARIA_ATTR: false,
  ALLOW_DATA_ATTR: false
});

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

export function memberRequestKey(parts, taste = []) {
  return [...parts, ...taste].join("\u0000");
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
