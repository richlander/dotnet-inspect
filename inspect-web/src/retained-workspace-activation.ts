import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceDeactivationResult,
  BrowserRetainedWorkspaceInstallation,
  BrowserRetainedWorkspaceSettlementResult,
} from "./facades/inspect-web-catalog.d.ts";

export const MAX_RETAINED_WORKSPACE_DEFINITIONS = 4;

export interface RetainedWorkspaceDefinitionInput {
  readonly label: string;
  readonly canonicalLocation: string;
  readonly canonicalPacket: string;
}

export interface RetainedWorkspaceDefinition
  extends RetainedWorkspaceDefinitionInput {
  readonly id: string;
}

export interface RetainedWorkspaceActivationState {
  readonly definitions: readonly RetainedWorkspaceDefinition[];
  readonly activeDefinitionId: string | null;
  readonly pendingDefinitionId: string | null;
  readonly lastFailure: string | null;
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
  let installedPublicationOrdinal = 0;

  function snapshot(): RetainedWorkspaceActivationState {
    return {
      definitions: [...definitions],
      activeDefinitionId,
      pendingDefinitionId,
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
    const generation = ++selectionGeneration;
    pendingDefinitionId = retainedDefinitionId;
    lastFailure = null;

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
        if (installation.publicationOrdinal > installedPublicationOrdinal) {
          installedPublicationOrdinal = installation.publicationOrdinal;
          activeDefinitionId = installation.retainedDefinitionId;
          hooks.install(installation);
          if (installation.predecessor !== null) {
            void client.observeRetainedWorkspaceSettlement(
              installation.predecessor.settlementId,
            ).then(
              value => hooks.predecessorSettled(value),
              (error: unknown) =>
                hooks.predecessorObservationFailed(error),
            );
          }
        }
        if (generation === selectionGeneration) {
          pendingDefinitionId = null;
        }
        return result;
      }
      case "failed":
        if (generation === selectionGeneration) {
          pendingDefinitionId = null;
          lastFailure = result.failure?.message
            ?? "Retained Workspace activation failed.";
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
    if (pendingDefinitionId === retainedDefinitionId) {
      throw new Error(
        "The pending retained Workspace definition cannot be deleted.",
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
      const result = await activate(successor.id);
      if (result.status !== "activated" && result.status !== "noEffect") {
        return;
      }
      definitions = definitions.filter(
        definition => definition.id !== retainedDefinitionId,
      );
      return;
    }

    const result = await client.deactivateRetainedWorkspaceDefinition(
      retainedDefinitionId,
    );
    switch (result.status) {
      case "deactivated":
        definitions = [];
        activeDefinitionId = null;
        lastFailure = null;
        hooks.clear();
        return;
      case "noEffect":
        definitions = [];
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
