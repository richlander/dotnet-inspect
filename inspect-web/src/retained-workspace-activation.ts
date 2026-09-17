import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceDeactivationResult,
  BrowserRetainedWorkspaceInstallation,
  BrowserRetainedWorkspaceSettlementResult,
} from "./facades/inspect-web-catalog.d.ts";

export const MAX_RETAINED_WORKSPACE_DEFINITIONS = 4;

interface RetainedWorkspaceDefinitionInput {
  readonly label: string;
  readonly canonicalLocation: string;
  readonly canonicalPacket: string;
}

interface RetainedWorkspaceDefinition
  extends RetainedWorkspaceDefinitionInput {
  readonly id: string;
}

interface RetainedWorkspaceActivationState {
  readonly definitions: readonly RetainedWorkspaceDefinition[];
  readonly activeDefinitionId: string | null;
  readonly pendingDefinitionId: string | null;
  readonly deactivatingDefinitionId: string | null;
  readonly unsettledDefinitionIds: readonly string[];
  readonly lastFailure: string | null;
}

interface SoleDeactivationIntent {
  readonly generation: number;
  readonly retainedDefinitionId: string;
}

export interface RetainedWorkspaceActivationClient {
  activateRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
    label: string,
    canonicalLocation: string,
    canonicalPacket: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  deactivateRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceDeactivationResult>;
  observeRetainedWorkspaceSettlement(
    settlementId: string,
  ): Promise<BrowserRetainedWorkspaceSettlementResult>;
}

export interface RetainedWorkspaceActivationHooks {
  install(installation: BrowserRetainedWorkspaceInstallation): void;
  clear(): void;
  predecessorSettled(result: BrowserRetainedWorkspaceSettlementResult): void;
  predecessorObservationFailed(error: unknown): void;
}

export interface RetainedWorkspaceActivationController {
  readonly state: RetainedWorkspaceActivationState;
  retain(input: RetainedWorkspaceDefinitionInput): RetainedWorkspaceDefinition;
  activate(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  delete(retainedDefinitionId: string): Promise<void>;
}

export function createRetainedWorkspaceActivationController(
  client: RetainedWorkspaceActivationClient,
  hooks: RetainedWorkspaceActivationHooks,
): RetainedWorkspaceActivationController {
  let definitions: RetainedWorkspaceDefinition[] = [];
  let activeDefinitionId: string | null = null;
  let pendingDefinitionId: string | null = null;
  let lastFailure: string | null = null;
  let nextIdentity = 0;
  let selectionGeneration = 0;
  let nextDeactivationGeneration = 0;
  let installedPublicationOrdinal = 0;
  let soleDeactivationIntent: SoleDeactivationIntent | null = null;
  const observedSettlementIds = new Set<string>();
  const unsettledActivationCounts = new Map<string, number>();

  function snapshot(): RetainedWorkspaceActivationState {
    return {
      definitions: [...definitions],
      activeDefinitionId,
      pendingDefinitionId,
      deactivatingDefinitionId:
        soleDeactivationIntent?.retainedDefinitionId ?? null,
      unsettledDefinitionIds: definitions
        .filter(definition => hasUnsettledActivation(definition.id))
        .map(definition => definition.id),
      lastFailure,
    };
  }

  function find(retainedDefinitionId: string): RetainedWorkspaceDefinition {
    const definition = definitions.find(
      candidate => candidate.id === retainedDefinitionId,
    );
    if (definition === undefined) {
      throw new Error(
        `Unknown retained Workspace definition '${retainedDefinitionId}'.`,
      );
    }
    return definition;
  }

  function beginActivation(retainedDefinitionId: string): void {
    unsettledActivationCounts.set(
      retainedDefinitionId,
      (unsettledActivationCounts.get(retainedDefinitionId) ?? 0) + 1,
    );
  }

  function endActivation(retainedDefinitionId: string): void {
    const count = unsettledActivationCounts.get(retainedDefinitionId);
    if (count === undefined) {
      throw new Error(
        `Retained Workspace activation '${retainedDefinitionId}' settled without admission.`,
      );
    }
    if (count === 1) {
      unsettledActivationCounts.delete(retainedDefinitionId);
      return;
    }
    unsettledActivationCounts.set(retainedDefinitionId, count - 1);
  }

  function hasUnsettledActivation(retainedDefinitionId: string): boolean {
    return (unsettledActivationCounts.get(retainedDefinitionId) ?? 0) > 0;
  }

  function observePredecessorOnce(
    installation: BrowserRetainedWorkspaceInstallation,
  ): void {
    const predecessor = installation.predecessor;
    if (predecessor === null
      || observedSettlementIds.has(predecessor.settlementId)) {
      return;
    }
    observedSettlementIds.add(predecessor.settlementId);
    void client.observeRetainedWorkspaceSettlement(
      predecessor.settlementId,
    ).then(
      value => hooks.predecessorSettled(value),
      (error: unknown) => hooks.predecessorObservationFailed(error),
    );
  }

  function retain(
    input: RetainedWorkspaceDefinitionInput,
  ): RetainedWorkspaceDefinition {
    if (definitions.length >= MAX_RETAINED_WORKSPACE_DEFINITIONS) {
      throw new Error(
        `Inspect Web retains at most ${
          MAX_RETAINED_WORKSPACE_DEFINITIONS
        } Workspace definitions. Delete one before retaining another.`,
      );
    }
    if (input.label.trim().length === 0
      || input.canonicalLocation.trim().length === 0
      || input.canonicalPacket.trim().length === 0) {
      throw new Error(
        "A retained Workspace definition requires a label, canonical location, and canonical packet.",
      );
    }

    const definition: RetainedWorkspaceDefinition = {
      id: `workspace-definition-${++nextIdentity}`,
      label: input.label,
      canonicalLocation: input.canonicalLocation,
      canonicalPacket: input.canonicalPacket,
    };
    definitions = [...definitions, definition];
    return definition;
  }

  async function activate(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult> {
    const definition = find(retainedDefinitionId);
    if (soleDeactivationIntent !== null) {
      throw new Error(
        "A retained Workspace cannot be activated while the active Workspace is being deactivated.",
      );
    }
    const generation = ++selectionGeneration;
    pendingDefinitionId = retainedDefinitionId;
    lastFailure = null;
    beginActivation(definition.id);

    try {
      let result: BrowserRetainedWorkspaceActivationResult;
      try {
        result = await client.activateRetainedWorkspaceDefinition(
          definition.id,
          definition.label,
          definition.canonicalLocation,
          definition.canonicalPacket,
        );
      } catch (error) {
        if (generation === selectionGeneration) {
          pendingDefinitionId = null;
          lastFailure = error instanceof Error
            ? error.message
            : "Retained Workspace activation failed.";
        }
        throw error;
      }

      switch (result.status) {
        case "activated":
        case "noEffect": {
          const installation = result.installation;
          if (installation === null) {
            throw new Error(
              `Retained Workspace ${result.status} omitted installation evidence.`,
            );
          }
          observePredecessorOnce(installation);
          if (installation.publicationOrdinal > installedPublicationOrdinal) {
            installedPublicationOrdinal = installation.publicationOrdinal;
            activeDefinitionId = installation.retainedDefinitionId;
            hooks.install(installation);
          }
          if (generation === selectionGeneration) {
            pendingDefinitionId = null;
          }
          return result;
        }
        case "failed":
          if (generation === selectionGeneration
            || result.failure?.kind === "CleanupFailed") {
            lastFailure = result.failure?.message
              ?? "Retained Workspace activation failed.";
          }
          if (generation === selectionGeneration) {
            pendingDefinitionId = null;
          }
          return result;
        case "superseded":
          if (generation === selectionGeneration) {
            pendingDefinitionId = null;
          }
          return result;
        default:
          throw new Error(
            `Unknown retained Workspace activation status '${result.status}'.`,
          );
      }
    } finally {
      endActivation(definition.id);
    }
  }

  async function deleteDefinition(
    retainedDefinitionId: string,
  ): Promise<void> {
    const removedIndex = definitions.findIndex(
      definition => definition.id === retainedDefinitionId,
    );
    if (removedIndex < 0) {
      throw new Error(
        `Unknown retained Workspace definition '${retainedDefinitionId}'.`,
      );
    }
    if (soleDeactivationIntent?.retainedDefinitionId
        === retainedDefinitionId) {
      throw new Error(
        "The retained Workspace definition is already being deactivated.",
      );
    }
    if (hasUnsettledActivation(retainedDefinitionId)) {
      throw new Error(
        "The retained Workspace definition cannot be deleted until its activation settles.",
      );
    }
    if (activeDefinitionId !== retainedDefinitionId) {
      definitions = definitions.filter(
        definition => definition.id !== retainedDefinitionId,
      );
      return;
    }

    const successor = definitions[removedIndex + 1]
      ?? definitions[removedIndex - 1];
    if (successor !== undefined) {
      const successorActivation = activate(successor.id);
      const successorGeneration = selectionGeneration;
      const result = await successorActivation;
      if (result.status !== "activated" && result.status !== "noEffect") {
        return;
      }
      if (selectionGeneration !== successorGeneration
        || activeDefinitionId !== successor.id
        || hasUnsettledActivation(retainedDefinitionId)
        || !definitions.some(
          definition => definition.id === retainedDefinitionId,
        )) {
        return;
      }
      definitions = definitions.filter(
        definition => definition.id !== retainedDefinitionId,
      );
      return;
    }

    const intent: SoleDeactivationIntent = {
      generation: ++nextDeactivationGeneration,
      retainedDefinitionId,
    };
    soleDeactivationIntent = intent;
    try {
      const result = await client.deactivateRetainedWorkspaceDefinition(
        retainedDefinitionId,
      );
      if (soleDeactivationIntent?.generation !== intent.generation) {
        return;
      }
      switch (result.status) {
        case "deactivated":
          definitions = definitions.filter(
            definition => definition.id !== retainedDefinitionId,
          );
          activeDefinitionId = null;
          lastFailure = null;
          hooks.clear();
          return;
        case "noEffect":
          definitions = definitions.filter(
            definition => definition.id !== retainedDefinitionId,
          );
          activeDefinitionId = null;
          hooks.clear();
          return;
        case "rejected":
          lastFailure = result.message
            ?? "Retained Workspace deactivation was rejected.";
          return;
        default:
          throw new Error(
            `Unknown retained Workspace deactivation status '${result.status}'.`,
          );
      }
    } finally {
      if (soleDeactivationIntent?.generation === intent.generation) {
        soleDeactivationIntent = null;
      }
    }
  }

  return {
    get state(): RetainedWorkspaceActivationState {
      return snapshot();
    },
    retain,
    activate,
    delete: deleteDefinition,
  };
}
