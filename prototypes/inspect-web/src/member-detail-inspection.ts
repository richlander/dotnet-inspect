import { sourceRequestNeedsLoad } from "./data.ts";
import {
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
} from "./annotated-source-session.ts";
import type {
  AnnotatedSourceResult,
  AnnotatedSourceSession,
} from "./annotated-source-session.ts";
import type {
  BrowserMemberDocumentation,
} from "./facades/inspect-web-package.d.ts";
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
  ): Promise<BrowserMemberDocumentation>;
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
  loadDocumentation(request: MemberDocumentationRequest): Promise<void>;
  loadFindingCensus(request: MemberFindingCensusRequest): Promise<void>;
  loadFacts(request: MemberFactsRequest): Promise<void>;
}

export function createMemberDetailInspectionCoordinator(
  dependencies: MemberDetailInspectionDependencies,
): MemberDetailInspectionCoordinator {
  const { state } = dependencies;
  const memberFactsQueries = new Map<string, Promise<MemberFacts>>();
  let memberFindingCensusRequestId = 0;
  let memberFactsRequestId = 0;

  return {
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

      // Runtime pseudo-packages have no companion XML-documentation package to query.
      if (request.isRuntimePack) {
        overload.documentationLoaded = true;
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
      const preservedFocus = dependencies.renderPreservingMemberFocus();
      try {
        const documentation =
          await dependencies.queryDocumentation(request, documentationId);
        if (!request.isCurrent()) return;
        overload.summary = documentation.summary;
        overload.returns = documentation.returns;
        overload.exceptions = [...(documentation.exceptions ?? [])];
        overload.parameters = (overload.parameters ?? []).map(parameter => ({
          ...parameter,
          description: documentation.parameters?.[parameter.name] ?? null,
        }));
        overload.documentationLoaded = true;
      } catch (error) {
        if (request.isCurrent()) {
          state.memberDocumentationError = dependencies.describeError(error);
        }
      } finally {
        if (state.memberDocumentationKey === request.signature) {
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
        if (request.isCurrent()
          && state.memberAnnotatedKey === request.signature
          && memberFindingCensusRequestId === requestId) {
          state.memberFindingInteraction = interaction;
          state.memberAnnotated = census.annotatedSource;
          state.memberAnnotatedEmbedded = createEmbeddedSession(
            createAnnotatedSourceViewerModel(census.annotatedSource),
          );
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
