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
