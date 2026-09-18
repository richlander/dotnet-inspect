import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceDeactivationResult,
  BrowserRetainedWorkspaceInstallation,
  BrowserRetainedWorkspacePreparationResult,
  BrowserRetainedWorkspacePreparedInstallation,
  BrowserRetainedWorkspaceSettlementResult,
} from "./facades/inspect-web-catalog.d.ts";

export const MAX_RETAINED_WORKSPACE_DEFINITIONS = 4;

interface RetainedWorkspaceDefinitionInput {
  readonly label: string;
  readonly canonicalLocation: string;
  readonly canonicalPacket: string;
  readonly presentationActiveTabIndex?: number | null;
  readonly coordinateCount?: number;
}

interface RetainedWorkspaceDefinition
  extends RetainedWorkspaceDefinitionInput {
  readonly id: string;
  readonly presentationActiveTabIndex: number | null;
  readonly coordinateCount: number;
}

interface RetainedWorkspaceActivationState {
  readonly definitions: readonly RetainedWorkspaceDefinition[];
  readonly activeDefinitionId: string | null;
  readonly pendingDefinitionId: string | null;
  readonly committingDefinitionId: string | null;
  readonly deactivatingDefinitionId: string | null;
  readonly unsettledDefinitionIds: readonly string[];
  readonly lastFailure: string | null;
}

interface SoleDeactivationIntent {
  readonly generation: number;
  readonly retainedDefinitionId: string;
}

export interface RetainedWorkspaceActivationClient {
  prepareRetainedWorkspaceDefinition(
    activationIntentId: string,
    retainedDefinitionId: string,
    label: string,
    canonicalLocation: string,
    canonicalPacket: string,
    presentationActiveTabIndex: number | null,
  ): Promise<BrowserRetainedWorkspacePreparationResult>;
  commitRetainedWorkspaceActivation(
    activationIntentId: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  cancelRetainedWorkspaceActivation(
    activationIntentId: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  deactivateRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceDeactivationResult>;
  observeRetainedWorkspaceSettlement(
    settlementId: string,
  ): Promise<BrowserRetainedWorkspaceSettlementResult>;
}

export interface RetainedWorkspacePredecessorObservation {
  readonly retainedDefinitionId: string;
  readonly realizationId: string;
  readonly settlementId: string;
}

export interface RetainedWorkspaceActivationHooks {
  install(installation: BrowserRetainedWorkspaceInstallation): void;
  clear(): void;
  predecessorSettled(
    observation: RetainedWorkspacePredecessorObservation,
    result: BrowserRetainedWorkspaceSettlementResult,
  ): void;
  predecessorObservationFailed(
    observation: RetainedWorkspacePredecessorObservation,
    error: unknown,
  ): void;
  commitStarted?(): void;
  commitSettled?(): void;
}

export interface RetainedWorkspaceActivationController {
  readonly state: RetainedWorkspaceActivationState;
  retain(input: RetainedWorkspaceDefinitionInput): RetainedWorkspaceDefinition;
  activate(
    retainedDefinitionId: string,
    accept?: (
      preparation: BrowserRetainedWorkspacePreparedInstallation,
    ) => boolean | Promise<boolean>,
    install?: (
      installation: BrowserRetainedWorkspaceInstallation,
    ) => void | Promise<void>,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  cancelPending(): boolean;
  waitForPendingCommit(): Promise<void>;
  deactivate(
    retainedDefinitionId: string,
    clearPresentation?: boolean,
  ): Promise<void>;
  delete(
    retainedDefinitionId: string,
    successorDefinitionId?: string | null,
  ): Promise<void>;
}

export function createRetainedWorkspaceActivationController(
  client: RetainedWorkspaceActivationClient,
  hooks: RetainedWorkspaceActivationHooks,
): RetainedWorkspaceActivationController {
  let definitions: RetainedWorkspaceDefinition[] = [];
  let activeDefinitionId: string | null = null;
  let pendingDefinitionId: string | null = null;
  let committingDefinitionId: string | null = null;
  let lastFailure: string | null = null;
  let nextIdentity = 0;
  let nextActivationIdentity = 0;
  let selectionGeneration = 0;
  let nextDeactivationGeneration = 0;
  let installedPublicationOrdinal = 0;
  let soleDeactivationIntent: SoleDeactivationIntent | null = null;
  let currentActivationIntentId: string | null = null;
  let commitBarrier: Promise<void> = Promise.resolve();
  let settleCommit: (() => void) | null = null;
  const observedSettlementIds = new Set<string>();
  const unsettledActivationCounts = new Map<string, number>();

  function snapshot(): RetainedWorkspaceActivationState {
    return {
      definitions: [...definitions],
      activeDefinitionId,
      pendingDefinitionId,
      committingDefinitionId,
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
    const observation: RetainedWorkspacePredecessorObservation = {
      retainedDefinitionId: installation.retainedDefinitionId,
      realizationId: installation.realizationId,
      settlementId: predecessor.settlementId,
    };
    void client.observeRetainedWorkspaceSettlement(
      predecessor.settlementId,
    ).then(
      value => hooks.predecessorSettled(observation, value),
      (error: unknown) =>
        hooks.predecessorObservationFailed(observation, error),
    );
  }

  function retain(
    input: RetainedWorkspaceDefinitionInput,
  ): RetainedWorkspaceDefinition {
    const presentationActiveTabIndex =
      input.presentationActiveTabIndex ?? null;
    const coordinateCount = input.coordinateCount ?? 0;
    if (definitions.length >= MAX_RETAINED_WORKSPACE_DEFINITIONS) {
      throw new Error(
        `Inspect Web retains at most ${
          MAX_RETAINED_WORKSPACE_DEFINITIONS
        } Workspace definitions. Delete one before retaining another.`,
      );
    }
    if (input.label.trim().length === 0
      || input.canonicalLocation.trim().length === 0
      || input.canonicalPacket.trim().length === 0
      || (presentationActiveTabIndex !== null
        && (!Number.isInteger(presentationActiveTabIndex)
          || presentationActiveTabIndex < 0))
      || !Number.isInteger(coordinateCount)
      || coordinateCount < 0) {
      throw new Error(
        "A retained Workspace definition requires a label, canonical location, and canonical packet.",
      );
    }

    const definition: RetainedWorkspaceDefinition = {
      id: `workspace-definition-${++nextIdentity}`,
      label: input.label,
      canonicalLocation: input.canonicalLocation,
      canonicalPacket: input.canonicalPacket,
      presentationActiveTabIndex,
      coordinateCount,
    };
    definitions = [...definitions, definition];
    return definition;
  }

  async function activate(
    retainedDefinitionId: string,
    accept: (
      preparation: BrowserRetainedWorkspacePreparedInstallation,
    ) => boolean | Promise<boolean> = () => true,
    install: (
      installation: BrowserRetainedWorkspaceInstallation,
    ) => void | Promise<void> = () => {},
  ): Promise<BrowserRetainedWorkspaceActivationResult> {
    const definition = find(retainedDefinitionId);
    if (soleDeactivationIntent !== null) {
      throw new Error(
        "A retained Workspace cannot be activated while the active Workspace is being deactivated.",
      );
    }
    if (committingDefinitionId !== null) {
      throw new Error(
        "A retained Workspace activation is committing.",
      );
    }
    const generation = ++selectionGeneration;
    const activationIntentId =
      `workspace-activation-${++nextActivationIdentity}`;
    currentActivationIntentId = activationIntentId;
    pendingDefinitionId = retainedDefinitionId;
    lastFailure = null;
    beginActivation(definition.id);
    let commitStarted = false;

    try {
      let result: BrowserRetainedWorkspaceActivationResult;
      try {
        const preparation =
          await client.prepareRetainedWorkspaceDefinition(
            activationIntentId,
            definition.id,
            definition.label,
            definition.canonicalLocation,
            definition.canonicalPacket,
            definition.presentationActiveTabIndex,
          );
        switch (preparation.status) {
          case "prepared": {
            if (preparation.preparation === null) {
              throw new Error(
                "Retained Workspace preparation omitted candidate evidence.",
              );
            }
            let accepted = false;
            try {
              if (generation === selectionGeneration) {
                const decision = accept(preparation.preparation);
                accepted = typeof decision === "boolean"
                  ? decision
                  : await decision;
              }
            } catch (error) {
              await client.cancelRetainedWorkspaceActivation(
                activationIntentId,
              );
              throw error;
            }
            if (!accepted || generation !== selectionGeneration) {
              result = await client.cancelRetainedWorkspaceActivation(
                activationIntentId,
              );
              break;
            }
            committingDefinitionId = definition.id;
            commitBarrier = new Promise<void>(resolve => {
              settleCommit = resolve;
            });
            hooks.commitStarted?.();
            commitStarted = true;
            result = await client.commitRetainedWorkspaceActivation(
              activationIntentId,
            );
            break;
          }
          case "noEffect":
            result = {
              status: "noEffect",
              installation: preparation.installation,
              failure: null,
            };
            break;
          case "failed":
            result = {
              status: "failed",
              installation: null,
              failure: preparation.failure,
            };
            break;
          case "superseded":
            result = {
              status: "superseded",
              installation: null,
              failure: null,
            };
            break;
          default:
            throw new Error(
              `Unknown retained Workspace preparation status '${preparation.status}'.`,
            );
        }
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
          if (generation === selectionGeneration
            && activeDefinitionId === installation.retainedDefinitionId) {
            try {
              await install(installation);
            } catch (error) {
              pendingDefinitionId = null;
              lastFailure = error instanceof Error
                ? error.message
                : "Retained Workspace presentation installation failed.";
              throw error;
            }
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
      if (commitStarted) {
        committingDefinitionId = null;
        settleCommit?.();
        settleCommit = null;
        hooks.commitSettled?.();
      }
      if (currentActivationIntentId === activationIntentId) {
        currentActivationIntentId = null;
      }
      endActivation(definition.id);
    }
  }

  function cancelPending(): boolean {
    if (committingDefinitionId !== null) return false;
    const activationIntentId = currentActivationIntentId;
    if (activationIntentId === null) return true;
    selectionGeneration++;
    currentActivationIntentId = null;
    pendingDefinitionId = null;
    void client.cancelRetainedWorkspaceActivation(
      activationIntentId,
    ).catch((error: unknown) => {
      lastFailure = error instanceof Error
        ? error.message
        : "Retained Workspace cancellation failed.";
    });
    return true;
  }

  async function deactivateDefinition(
    retainedDefinitionId: string,
    clearPresentation = true,
  ): Promise<void> {
    find(retainedDefinitionId);
    if (activeDefinitionId !== retainedDefinitionId) return;
    if (hasUnsettledActivation(retainedDefinitionId)) {
      throw new Error(
        "The retained Workspace definition cannot be deactivated until its activation settles.",
      );
    }
    const result = await client.deactivateRetainedWorkspaceDefinition(
      retainedDefinitionId,
    );
    switch (result.status) {
      case "deactivated":
      case "noEffect":
        activeDefinitionId = null;
        lastFailure = null;
        if (clearPresentation) hooks.clear();
        return;
      case "cleanupFailed":
        activeDefinitionId = null;
        lastFailure = result.settlement?.failure
          ?? result.message
          ?? "The active Workspace could not be settled.";
        if (clearPresentation) hooks.clear();
        return;
      case "rejected":
        lastFailure = result.message
          ?? "Retained Workspace deactivation was rejected.";
        throw new Error(lastFailure);
      default:
        throw new Error(
          `Unknown retained Workspace deactivation status '${result.status}'.`,
        );
    }
  }

  async function deleteDefinition(
    retainedDefinitionId: string,
    successorDefinitionId?: string | null,
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

    const successor = successorDefinitionId === undefined
      ? definitions[removedIndex + 1] ?? definitions[removedIndex - 1]
      : successorDefinitionId === null
        ? undefined
        : find(successorDefinitionId);
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
        case "cleanupFailed":
          definitions = definitions.filter(
            definition => definition.id !== retainedDefinitionId,
          );
          activeDefinitionId = null;
          lastFailure = result.settlement?.failure
            ?? result.message
            ?? "The active Workspace could not be settled.";
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
    cancelPending,
    waitForPendingCommit: () => commitBarrier,
    deactivate: deactivateDefinition,
    delete: deleteDefinition,
  };
}
