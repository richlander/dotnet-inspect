import type { BrowserPackageDependencies } from "./facades/inspect-web-package.d.ts";
import { assemblyReferenceGraphLegendHtml } from "./graph-legends.ts";

export interface AssemblyReferenceIdentity {
  readonly name: string;
  readonly version: string;
  readonly culture: string | null;
  readonly publicKeyToken: string | null;
}

export interface WorkspaceLibraryReferenceCandidate
  extends AssemblyReferenceIdentity {
  readonly packageKey: string;
  readonly libraryId: string;
}

export interface AdmittedLibraryReferenceSubject {
  readonly id: string;
}

export interface AssemblyReferenceDescriptor
  extends AssemblyReferenceIdentity {
  readonly id: string;
}

export interface LibraryReferenceDestination {
  readonly packageKey: string;
  readonly libraryId: string;
}

export interface LibraryReferencesOptions {
  assemblyIdentity: string;
  assetPath: string;
  coordinate: string;
  loading: boolean;
  error: string;
  data: BrowserPackageDependencies | null;
  referenceDestinations: readonly (LibraryReferenceDestination | null)[];
  escapeHtml: (value: unknown) => string;
}

function normalizeIdentityValue(value: string): string {
  return value.toLowerCase();
}

function normalizeCulture(value: string | null): string {
  const normalized = value?.toLowerCase() ?? "";
  return normalized === "neutral" ? "" : normalized;
}

function normalizePublicKeyToken(value: string | null): string {
  return value?.toLowerCase() ?? "";
}

function assemblyReferenceIdentityKey(
  identity: AssemblyReferenceIdentity,
): string {
  return [
    normalizeIdentityValue(identity.name),
    normalizeIdentityValue(identity.version),
    normalizeCulture(identity.culture),
    normalizePublicKeyToken(identity.publicKeyToken),
  ].join("\u0000");
}

export function createWorkspaceLibraryReferenceCandidates(
  packageKey: string,
  admittedLibraries: readonly AdmittedLibraryReferenceSubject[],
  descriptors: readonly AssemblyReferenceDescriptor[],
): WorkspaceLibraryReferenceCandidate[] {
  const admittedIds = new Set(admittedLibraries.map(library => library.id));
  return descriptors
    .filter(descriptor => admittedIds.has(descriptor.id))
    .map(descriptor => ({
      packageKey,
      libraryId: descriptor.id,
      name: descriptor.name,
      version: descriptor.version,
      culture: descriptor.culture,
      publicKeyToken: descriptor.publicKeyToken,
    }));
}

export function resolveLibraryReferenceDestinations(
  references: readonly AssemblyReferenceIdentity[],
  candidates: readonly WorkspaceLibraryReferenceCandidate[],
): (LibraryReferenceDestination | null)[] {
  const destinations =
    new Map<string, LibraryReferenceDestination | null>();
  for (const candidate of candidates) {
    const key = assemblyReferenceIdentityKey(candidate);
    destinations.set(
      key,
      destinations.has(key)
        ? null
        : {
            packageKey: candidate.packageKey,
            libraryId: candidate.libraryId,
          });
  }
  return references.map(reference =>
    destinations.get(assemblyReferenceIdentityKey(reference)) ?? null);
}

export function renderLibraryReferencesSurface(options: LibraryReferencesOptions): string {
  const {
    loading,
    error,
    data,
    escapeHtml,
  } = options;
  let status: string;
  let content: string;
  if (loading) {
    status = "Reading references\u2026";
    content = `<section class="document-section source-progress"><span class="loader"></span><h2>Reading references&hellip;</h2><p>Reading direct AssemblyRef rows.</p></section>`;
  } else if (error) {
    status = "Query failed";
    content = `<section class="document-section empty-document"><span class="large-glyph">&#x2318;</span><h2>Reference query failed</h2><p>${escapeHtml(error)}</p></section>`;
  } else if (!data) {
    status = "Loading\u2026";
    content = `<section class="document-section empty-document"><span class="loader"></span><h2>Loading&hellip;</h2></section>`;
  } else if (data.assemblyReferences === null
    || typeof data.assemblyReferences === "string") {
    const message = data.assemblyReferences === null
      ? "The engine returned no assembly-reference result."
      : data.assemblyReferences || "No failure details were provided.";
    status = "Inspection failed";
    content = `<section class="document-section empty-document"><h2>Reference inspection failed</h2><p>${escapeHtml(message)}</p></section>`;
  } else {
    const references = data.assemblyReferences.references;
    status = `${references.length.toLocaleString()} direct reference${references.length === 1 ? "" : "s"}`;
    content = references.length
      ? `<section class="document-section reference-graph-section">
          <div class="section-title"><h2>Reference graph</h2><span>assembly above \u00b7 direct references below</span></div>
          <div id="library-reference-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph&hellip;</p></div>
          ${assemblyReferenceGraphLegendHtml()}
        </section>
        <section class="document-section reference-list-section">
          <div class="section-title"><h2>Assembly references</h2><span>${references.length.toLocaleString()} direct reference${references.length === 1 ? "" : "s"}</span></div>
          <ul class="dep-list" aria-label="Assembly references">${references.map((reference, index) => {
            const destination = options.referenceDestinations[index];
            const name = destination
              ? `<button type="button" class="dep-name as-link" data-library-reference-package="${escapeHtml(destination.packageKey)}" data-library-reference-library="${escapeHtml(destination.libraryId)}" title="Open ${escapeHtml(reference.name)} Library">${escapeHtml(reference.name)}</button>`
              : `<span class="dep-name">${escapeHtml(reference.name)}</span>`;
            return `<li>${name}<code class="dep-version">${escapeHtml(`${reference.version} \u00b7 ${reference.culture || "neutral"} \u00b7 ${reference.publicKeyToken ? `pkt ${reference.publicKeyToken}` : "unsigned"}`)}</code></li>`;
          }).join("")}</ul>
        </section>`
      : `<section class="document-section empty-document"><h2>No direct references</h2><p>This assembly declares no direct AssemblyRef rows.</p></section>`;
  }
  return `<section class="library-references-surface" aria-labelledby="library-references-title">
    <header class="api-surface-head">
      <h1 id="library-references-title">References</h1>
      <p>${escapeHtml(status)}</p>
    </header>
    <div class="library-references-scroll">${content}</div>
  </section>`;
}
