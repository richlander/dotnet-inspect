import type {
  BrowserProjectInputDeclaration,
  BrowserProjectInputInspection,
} from "./facades/inspect-web-catalog.d.ts";

export function localProjectInputKind(
  name: string,
): "authoredProject" | "restoredProject" | null {
  const lower = name.toLowerCase();
  return lower.endsWith(".csproj")
    ? "authoredProject"
    : lower === "project.assets.json" ? "restoredProject" : null;
}

export interface ProjectPackageAction {
  readonly packageId: string;
  readonly version: string;
  readonly exact: boolean;
}

function declarationAction(
  declaration: BrowserProjectInputDeclaration,
): ProjectPackageAction | null {
  return declaration.packageId !== null && declaration.versionConstraint !== null
    ? {
      packageId: declaration.packageId,
      version: declaration.versionConstraint,
      exact: false,
    }
    : null;
}

export function projectPackageActions(
  inspection: BrowserProjectInputInspection,
): readonly ProjectPackageAction[] {
  const content = inspection.content;
  return content.kind === "AuthoredProject"
    ? content.declarations.flatMap(declaration => {
      const action = declarationAction(declaration);
      return action === null ? [] : [action];
    })
    : content.packages.map(package_ => ({
      packageId: package_.packageId,
      version: package_.version,
      exact: true,
    }));
}

export function renderProjectInput(
  inspection: BrowserProjectInputInspection,
  inspector: string,
  escapeHtml: (value: unknown) => string,
): string {
  const content = inspection.content;
  const authored = content.kind === "AuthoredProject";
  const table = (headings: readonly string[], rows: readonly (readonly string[])[]) =>
    `<div class="table-wrap"><table><thead><tr>${headings.map(heading =>
      `<th scope="col">${escapeHtml(heading)}</th>`).join("")}</tr></thead>
      <tbody>${rows.map(row => `<tr>${row.map(cell =>
        `<td>${cell}</td>`).join("")}</tr>`).join("")}</tbody></table></div>`;
  const fields = (rows: readonly { readonly name: string; readonly value: string }[]) =>
    table(["Evidence", "Value"], rows.map(field =>
      [escapeHtml(field.name), escapeHtml(field.value)]));
  const failures = content.failures.length === 0 ? "" : `
    <h2>Failures</h2>${table(["Phase", "Reason", "Message", "Occurrences"],
      content.failures.map(failure => [
        escapeHtml(failure.phase), escapeHtml(failure.reason),
        escapeHtml(failure.message), escapeHtml(failure.occurrences),
      ]))}`;
  const diagnostics = inspection.diagnostics.length === 0 ? "" : `
    <h2>Diagnostics</h2>${table(["Severity", "Code", "Message", "Correspondence"],
      inspection.diagnostics.map(diagnostic => [
        escapeHtml(diagnostic.severity), escapeHtml(diagnostic.code),
        escapeHtml(diagnostic.summary), escapeHtml(diagnostic.correspondence ?? ""),
      ]))}`;
  const introduction = `<p>${authored
    ? "Authored declarations only. MSBuild was not evaluated; imports and central versions were not loaded. Constraints are not resolved versions."
    : "Restored dependency evidence from the supplied assets file. No restore or dependency acquisition was performed."}</p>
    <p><strong>Provider outcome:</strong> ${escapeHtml(content.providerOutcome)}
      ${content.traversalCompletion === null ? ""
        : ` · Traversal: ${escapeHtml(content.traversalCompletion)}`}
      · Root set: ${escapeHtml(content.rootSetCompletion)}
      ${content.isTruncated ? " · Truncated" : ""}</p>`;
  if (inspector === "project.overview") {
    return `${introduction}
      <h2>Project evidence</h2>
      ${fields(content.metadata)}
      ${content.roots.map(root => fields([
        { name: "Declarations", value: `${root.declarationState}${root.declarationCompletion === null ? "" : ` (${root.declarationCompletion})`}` },
        { name: "Declaration selection", value: root.selectionStatus },
        { name: "Requested framework", value: root.requestedFramework ?? "Not requested" },
        { name: "Selected framework", value: root.selectedFramework ?? "Unavailable" },
        { name: "Restored graph", value: `${root.graphState}${root.graphCompletion === null ? "" : ` (${root.graphCompletion})`}` },
        { name: "Restored target", value: root.restoredTargetFramework ?? "Not applicable or unavailable" },
        { name: "Runtime identifier", value: root.restoredRuntimeIdentifier ?? "None" },
        { name: "Target selection", value: root.targetProvenance ?? "Not applicable or unavailable" },
      ])).join("")}
      <p>${content.declarations.length} declarations · ${content.packages.length} resolved Packages · ${content.edges.length} restored edges</p>
      <p><strong>Share:</strong> ${escapeHtml(inspection.share.reason ?? inspection.share.fullUrl ?? inspection.share.kind)}</p>
      ${failures}${diagnostics}`;
  }
  if (inspector !== "project.dependencies") {
    return `<p>This Project inspector is unavailable in this Browser.</p>`;
  }
  let actionIndex = 0;
  const button = (label: string) =>
    `<button type="button" class="as-link" data-project-package-index="${actionIndex++}">${escapeHtml(label)}</button>`;
  const declarations = table(
    ["Package declaration", "Version constraint", authored ? "Condition" : "Framework",
      "Occurrences", ...(authored ? ["Inspect"] : [])],
      content.declarations.map(declaration => [
        escapeHtml(declaration.sourcePackageId || declaration.packageId || "Unresolved"),
        escapeHtml(declaration.sourceVersionConstraint ?? "Unresolved"),
        escapeHtml(declaration.condition),
        escapeHtml(declaration.sourceOccurrences),
        ...(authored ? [declarationAction(declaration) === null
          ? "Unresolved declaration"
          : button("Resolve and inspect Package")] : []),
      ]));
  const packages = authored ? "" : `<h2>Resolved Packages</h2>${content.packages.length === 0
    ? "<p>No resolved Package coordinates are available for this Project selection.</p>"
    : table(["Package", "Exact restored version", "Role", "Inspect"],
      content.packages.map(package_ => [
        escapeHtml(package_.packageId), escapeHtml(package_.version),
        escapeHtml(package_.role), button("Inspect Package"),
      ]))}`;
  const edges = content.edges.length === 0 ? "" : `
    <h2>Restored dependency edges</h2>
    ${table(["Parent", "Dependency", "Exact version", "Constraint", "Role"],
      content.edges.map(edge => [
        escapeHtml(edge.parentPackageId === null
          ? edge.parentProjectIdentity ?? edge.parentKind
          : `${edge.parentPackageId}@${edge.parentPackageVersion}`),
        escapeHtml(edge.packageId), escapeHtml(edge.version),
        escapeHtml(edge.sourceVersionConstraint), escapeHtml(edge.role),
      ]))}`;
  return `${introduction}
    <p>Inspecting a Package is an explicit network action and opens a separate Workspace. No vulnerability, license, or trust assessment has been performed.</p>
    <h2>Declared dependencies</h2>${declarations}
    ${packages}${edges}${failures}${diagnostics}`;
}
