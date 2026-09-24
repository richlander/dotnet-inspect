import { assertNever, sourceRequestNeedsLoad } from "./data.ts";
import {
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
} from "./annotated-source-session.ts";
import type {
  AnnotatedSourceResult,
  AnnotatedSourceSession,
} from "./annotated-source-session.ts";
import type {
  CompiledDocumentationOutcome,
  DocumentationQueryOutcome,
  DocumentationQueryTextFieldEvidence,
} from "./facades/inspect-web-package.d.ts";
import type {
  BrowserMemberDeclaration,
} from "./facades/inspect-web-metadata.d.ts";
import type { BrowserMemberFacts } from "./facades/inspect-web-analysis.d.ts";
import {
  createMemberFindingInteraction,
  type MemberFindingCensus,
  type MemberFindingInteraction,
} from "./finding-interaction.ts";
import type { MemberFocusSnapshot } from "./member-focus.ts";
import type { AppMemberSurface } from "./package-acquisition.ts";

export type DocumentableMemberSurface = AppMemberSurface;

export type MemberFacts = BrowserMemberFacts;

interface MemberCoordinates {
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  type: string;
  member: string;
  memberSignature: string;
}

export interface MemberDocumentationRequest {
  signature: string;
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  platformPack: string;
  overload: DocumentableMemberSurface;
  isRuntimePack: boolean;
  isCurrent(): boolean;
}

export interface MemberFindingCensusRequest extends MemberCoordinates {
  signature: string;
  typeIdentity: string;
  selectorKey: string;
  metadataToken: number;
  taste: string;
  embeddedSession?: boolean;
  isCurrent(): boolean;
}

export interface MemberFactsRequest extends MemberCoordinates {
  signature: string;
  typeIdentity: string;
  selectorKey: string;
  metadataToken: number;
  implementationBodySelected: boolean;
  isCurrent(): boolean;
}

export interface MemberDeclarationRequest {
  signature: string;
  packageId: string;
  version: string;
  framework: string;
  assembly: string;
  isRuntimePack: boolean;
  platformPack: string;
  typeIdentity: string;
  member: string;
  selectorKey: string;
  metadataToken: number;
  implementationMember: boolean;
  isCurrent(): boolean;
}

export interface MemberDetailInspectionState {
  memberAnnotated: AnnotatedSourceResult | null;
  memberAnnotatedLoading: boolean;
  memberAnnotatedError: string;
  memberAnnotatedKey: string;
  memberAnnotatedEmbedded: AnnotatedSourceSession | null;
  memberAnnotatedModal: AnnotatedSourceSession | null;
  memberFindingInteraction: MemberFindingInteraction | null;
  memberFindingSelectionError: string;
  memberFacts: MemberFacts | null;
  memberFactsLoading: boolean;
  memberFactsError: string;
  memberFactsKey: string;
  memberDocumentationLoading: boolean;
  memberDocumentationError: string;
  memberDocumentationKey: string;
  memberDeclaration: BrowserMemberDeclaration | null;
  memberDeclarationLoading: boolean;
  memberDeclarationError: string;
  memberDeclarationKey: string;
}

export function cancelFindingCensusRequest(
  state: Pick<
    MemberDetailInspectionState,
    "memberAnnotatedLoading" | "memberAnnotatedKey" | "memberAnnotatedError"
  >,
): boolean {
  if (!state.memberAnnotatedLoading) return false;
  state.memberAnnotatedLoading = false;
  state.memberAnnotatedKey = "";
  state.memberAnnotatedError = "";
  return true;
}

export interface MemberDetailInspectionDependencies {
  state: MemberDetailInspectionState;
  queryDocumentation(
    request: MemberDocumentationRequest,
    documentationId: string,
  ): Promise<CompiledDocumentationOutcome | DocumentationQueryOutcome>;
  queryDeclaration(
    request: MemberDeclarationRequest,
  ): Promise<BrowserMemberDeclaration>;
  queryFindingCensus(
    request: MemberFindingCensusRequest,
  ): Promise<MemberFindingCensus>;
  queryFacts(request: MemberFactsRequest): Promise<MemberFacts>;
  describeError(error: unknown): string;
  render(): void;
  renderPreservingMemberFocus(
    fallback?: MemberFocusSnapshot | null,
  ): MemberFocusSnapshot;
}

export interface MemberDetailInspectionCoordinator {
  invalidate(): void;
  loadDeclaration(request: MemberDeclarationRequest): Promise<void>;
  loadDocumentation(request: MemberDocumentationRequest): Promise<void>;
  loadFindingCensus(request: MemberFindingCensusRequest): Promise<void>;
  loadFacts(request: MemberFactsRequest): Promise<void>;
}

export function createMemberDetailInspectionCoordinator(
  dependencies: MemberDetailInspectionDependencies,
): MemberDetailInspectionCoordinator {
  const { state } = dependencies;
  const memberFactsQueries = new Map<string, Promise<MemberFacts>>();
  let memberDocumentationRequestId = 0;
  let memberDeclarationRequestId = 0;
  let memberFindingCensusRequestId = 0;
  let memberFactsRequestId = 0;

  return {
    invalidate() {
      memberDocumentationRequestId++;
      memberDeclarationRequestId++;
      memberFindingCensusRequestId++;
      memberFactsRequestId++;
      memberFactsQueries.clear();
      if (state.memberDocumentationLoading) {
        state.memberDocumentationLoading = false;
        state.memberDocumentationKey = "";
        state.memberDocumentationError = "";
      }
      if (state.memberDeclarationLoading) {
        state.memberDeclarationLoading = false;
        state.memberDeclarationKey = "";
        state.memberDeclarationError = "";
      }
      if (state.memberAnnotatedLoading) {
        state.memberAnnotatedLoading = false;
        state.memberAnnotatedKey = "";
        state.memberAnnotatedError = "";
      }
      if (state.memberFactsLoading) {
        state.memberFactsLoading = false;
        state.memberFactsKey = "";
        state.memberFactsError = "";
      }
    },

    async loadDeclaration(request) {
      if (state.memberDeclarationKey === request.signature
        && !state.memberDeclarationLoading
        && (state.memberDeclaration || state.memberDeclarationError)) {
        dependencies.render();
        return;
      }

      const requestId = ++memberDeclarationRequestId;
      state.memberDeclarationKey = request.signature;
      state.memberDeclaration = null;
      state.memberDeclarationLoading = true;
      state.memberDeclarationError = "";
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      try {
        const declaration = await dependencies.queryDeclaration(request);
        if (request.isCurrent()
          && state.memberDeclarationKey === request.signature
          && memberDeclarationRequestId === requestId) {
          state.memberDeclaration = declaration;
        }
      } catch (error) {
        if (request.isCurrent()
          && state.memberDeclarationKey === request.signature
          && memberDeclarationRequestId === requestId) {
          state.memberDeclarationError = dependencies.describeError(error);
        }
      } finally {
        if (state.memberDeclarationKey === request.signature
          && memberDeclarationRequestId === requestId) {
          state.memberDeclarationLoading = false;
          if (request.isCurrent()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },

    async loadDocumentation(request) {
      const { overload } = request;
      const documentationId = overload.documentationId;
      if (!documentationId || overload.documentationLoaded) {
        state.memberDocumentationKey = request.signature;
        state.memberDocumentationLoading = false;
        state.memberDocumentationError = "";
        dependencies.render();
        return;
      }

      if (state.memberDocumentationKey === request.signature
        && state.memberDocumentationLoading) {
        return;
      }
      state.memberDocumentationKey = request.signature;
      state.memberDocumentationLoading = true;
      state.memberDocumentationError = "";
      const requestId = ++memberDocumentationRequestId;
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      try {
        const outcome =
          await dependencies.queryDocumentation(request, documentationId);
        if (!request.isCurrent()
          || memberDocumentationRequestId !== requestId) return;
        switch (outcome.kind) {
          case "completed": {
            const { fields } = outcome;
            const parameters = new Map(
              fields.parameters.map(
                parameter => [
                  parameter.name ?? "",
                  firstTextContribution(parameter.evidence),
                ]));
            overload.summary = firstTextContribution(fields.summary);
            overload.returns = firstTextContribution(fields.returns);
            overload.exceptions =
              (fields.exceptions.contributions[0]?.value ?? [])
                .map(exception => ({
                  type: documentationExceptionType(exception.reference),
                  description: exception.description ?? "",
                }));
            overload.parameters = overload.parameters.map(parameter => ({
              ...parameter,
              description: parameters.get(parameter.name) ?? null,
            }));
            const completedError = completedDocumentationError(outcome);
            if (completedError) {
              state.memberDocumentationError = completedError;
            } else {
              overload.documentationLoaded = true;
            }
            break;
          }
          case "available": {
            const { documentation } = outcome;
            const parameters = new Map(
              documentation.parameters.map(
                parameter => [parameter.name, parameter.description]));
            overload.summary = documentation.summary ?? null;
            overload.returns = documentation.returns ?? null;
            overload.exceptions = documentation.exceptions.map(exception => ({
              type: documentationExceptionType(exception.reference),
              description: exception.description ?? "",
            }));
            overload.parameters = overload.parameters.map(parameter => ({
              ...parameter,
              description: parameters.get(parameter.name) ?? null,
            }));
            overload.documentationLoaded = true;
            break;
          }
          case "absent":
            overload.documentationLoaded = true;
            break;
          case "unavailable":
            state.memberDocumentationError =
              "Compiled documentation is unavailable.";
            break;
          case "ambiguous":
            state.memberDocumentationError =
              "The compiled documentation source is ambiguous.";
            break;
          case "contributionsRejected":
            state.memberDocumentationError =
              "Compiled documentation sources were rejected.";
            break;
          case "malformedOrUnreadableDocument":
            state.memberDocumentationError =
              "The compiled documentation could not be read.";
            break;
          case "incomplete":
            state.memberDocumentationError =
              "The documentation query did not complete.";
            break;
          case "requestRejected":
            state.memberDocumentationError =
              "The documentation request was rejected.";
            break;
          case "contentAccessFailed":
            state.memberDocumentationError =
              "The compiled documentation content could not be read.";
            break;
          case "failed":
            state.memberDocumentationError =
              "The documentation query failed.";
            break;
          default:
            assertNever(outcome, "documentation outcome");
        }
      } catch (error) {
        if (request.isCurrent()
          && memberDocumentationRequestId === requestId) {
          state.memberDocumentationError = dependencies.describeError(error);
        }
      } finally {
        if (state.memberDocumentationKey === request.signature
          && memberDocumentationRequestId === requestId) {
          state.memberDocumentationLoading = false;
          if (request.isCurrent()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },

    async loadFindingCensus(request) {
      if (!sourceRequestNeedsLoad(
          state.memberAnnotatedKey === request.signature,
          state.memberAnnotatedLoading,
          state.memberFindingInteraction,
          state.memberAnnotatedError)) {
        dependencies.render();
        return;
      }

      const requestId = ++memberFindingCensusRequestId;
      state.memberAnnotatedKey = request.signature;
      state.memberAnnotated = null;
      state.memberAnnotatedLoading = true;
      state.memberAnnotatedError = "";
      state.memberAnnotatedEmbedded = null;
      state.memberAnnotatedModal = null;
      state.memberFindingInteraction = null;
      state.memberFindingSelectionError = "";
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      try {
        const census = await dependencies.queryFindingCensus(request);
        const interaction = createMemberFindingInteraction(census);
        const annotated = census.annotatedSource;
        const embedded = request.embeddedSession === false
          ? null
          : createEmbeddedSession(
              createAnnotatedSourceViewerModel(annotated),
            );
        if (request.isCurrent()
          && state.memberAnnotatedKey === request.signature
          && memberFindingCensusRequestId === requestId) {
          state.memberFindingInteraction = interaction;
          state.memberAnnotated = annotated;
          state.memberAnnotatedEmbedded = embedded;
        }
      } catch (error) {
        if (request.isCurrent()
          && state.memberAnnotatedKey === request.signature
          && memberFindingCensusRequestId === requestId) {
          state.memberAnnotatedError = dependencies.describeError(error);
        }
      } finally {
        if (state.memberAnnotatedKey === request.signature
          && memberFindingCensusRequestId === requestId) {
          state.memberAnnotatedLoading = false;
          if (request.isCurrent()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },

    async loadFacts(request) {
      if (state.memberFactsKey === request.signature
        && !state.memberFactsLoading
        && (state.memberFacts || state.memberFactsError)) {
        dependencies.render();
        return;
      }

      const requestId = ++memberFactsRequestId;
      state.memberFactsKey = request.signature;
      state.memberFacts = null;
      state.memberFactsLoading = true;
      state.memberFactsError = "";
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      let query = memberFactsQueries.get(request.signature);
      if (!query) {
        query = (async () => dependencies.queryFacts(request))();
        memberFactsQueries.set(request.signature, query);
      }
      try {
        const result = await query;
        if (request.isCurrent()
          && state.memberFactsKey === request.signature
          && memberFactsRequestId === requestId) {
          state.memberFacts = result;
        }
      } catch (error) {
        if (request.isCurrent()
          && state.memberFactsKey === request.signature
          && memberFactsRequestId === requestId) {
          state.memberFactsError = dependencies.describeError(error);
        }
      } finally {
        if (memberFactsQueries.get(request.signature) === query) {
          memberFactsQueries.delete(request.signature);
        }
        if (state.memberFactsKey === request.signature
          && memberFactsRequestId === requestId) {
          state.memberFactsLoading = false;
          if (request.isCurrent()) {
            dependencies.renderPreservingMemberFocus(preservedFocus);
          }
        }
      }
    },
  };
}

function firstTextContribution(
  evidence: DocumentationQueryTextFieldEvidence,
): string | null {
  return evidence.contributions[0]?.value ?? null;
}

function completedDocumentationError(
  outcome: Extract<DocumentationQueryOutcome, { readonly kind: "completed" }>,
): string | null {
  if (outcome.fields.summary.contributions.length > 0
    || outcome.fields.remarks.contributions.length > 0
    || outcome.fields.returns.contributions.length > 0
    || outcome.fields.parameters.some(
      parameter => parameter.evidence.contributions.length > 0)
    || outcome.fields.exceptions.contributions.length > 0
    || outcome.fields.samples.contributions.length > 0) {
    return null;
  }

  const authoredFailure = authoredDocumentationError(
    outcome.authoredSource,
    false,
  );
  if (authoredFailure) return authoredFailure;

  switch (outcome.compiledXml?.kind) {
    case "available":
    case "absent":
      return null;
    case "unavailable":
      return "Compiled and authored documentation are unavailable.";
    case "ambiguous":
      return "The compiled documentation source is ambiguous.";
    case "contributionsRejected":
      return "Compiled documentation sources were rejected.";
    case "malformedOrUnreadableDocument":
      return "The compiled documentation could not be read.";
    case "incomplete":
      return "The documentation query did not complete.";
    case "requestRejected":
      return "The documentation request was rejected.";
    case "contentAccessFailed":
      return "The compiled documentation content could not be read.";
    case undefined:
      break;
    default:
      return assertNever(
        outcome.compiledXml,
        "compiled documentation outcome",
      );
  }

  return authoredDocumentationError(outcome.authoredSource, true);
}

function authoredDocumentationError(
  outcome: Extract<
    DocumentationQueryOutcome,
    { readonly kind: "completed" }
  >["authoredSource"],
  reportUnavailable: boolean,
): string | null {
  switch (outcome?.kind) {
    case "available":
    case "absent":
      return null;
    case "unavailable":
      return reportUnavailable
        ? "Authored documentation is unavailable."
        : null;
    case "ambiguous":
      return "The authored documentation source is ambiguous.";
    case "rejected":
      return "The authored documentation source was rejected.";
    case "failed":
      return "The authored documentation source failed.";
    case "incomplete":
      return "The authored documentation query did not complete.";
    case undefined:
      return null;
    default:
      return assertNever(
        outcome,
        "authored documentation outcome",
      );
  }
}

function documentationExceptionType(
  reference: string | null | undefined,
): string {
  if (!reference?.trim()) return "";
  const value = reference.length > 2 && reference[1] === ":"
    ? reference.slice(2)
    : reference;
  return value.replace(/#/g, ".");
}
