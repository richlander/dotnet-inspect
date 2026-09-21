import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceConsumerCompletionResult,
  BrowserRetainedWorkspaceDeactivationResult,
  BrowserRetainedWorkspacePosting,
  BrowserRetainedWorkspacePreparedPosting,
  BrowserRetainedWorkspacePreparationResult,
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
  prepareRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
    label: string,
    canonicalLocation: string,
    canonicalPacket: string,
  ): Promise<BrowserRetainedWorkspacePreparationResult>;
  commitRetainedWorkspaceActivation(
    receipt: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  cancelRetainedWorkspaceActivation(
    receipt: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  completeRetainedWorkspaceActivation(
    receipt: string,
    succeeded: boolean,
    failure: string | null,
  ): Promise<BrowserRetainedWorkspaceConsumerCompletionResult>;
  deactivateRetainedWorkspaceDefinition(
    retainedDefinitionId: string,
  ): Promise<BrowserRetainedWorkspaceDeactivationResult>;
  completeRetainedWorkspaceDeactivation(
    receipt: string,
    succeeded: boolean,
    failure: string | null,
  ): Promise<BrowserRetainedWorkspaceConsumerCompletionResult>;
  observeRetainedWorkspaceSettlement(
    settlementId: string,
  ): Promise<BrowserRetainedWorkspaceSettlementResult>;
  validateRetainedWorkspaceNavigationAuthority(
    realizationId: string,
    publicationOrdinal: number,
    session: string,
    revision: string,
    intent: string,
    epoch: string,
  ): boolean | Promise<boolean>;
  recordRetainedWorkspaceNavigationPosting(
    realizationId: string,
    publicationOrdinal: number,
    session: string,
    revision: string,
    intent: string,
    epoch: string,
  ): string | Promise<string>;
  acknowledgeRetainedWorkspaceNavigation(
    realizationId: string,
    publicationOrdinal: number,
    session: string,
    revision: string,
    intent: string,
    epoch: string,
  ): string | Promise<string>;
  abandonRetainedWorkspaceNavigation(
    realizationId: string,
    publicationOrdinal: number,
    session: string,
    revision: string,
    intent: string,
    epoch: string,
  ): string | Promise<string>;
}

export interface RetainedWorkspacePredecessorObservation {
  readonly retainedDefinitionId: string;
  readonly realizationId: string;
  readonly settlementId: string;
}

export interface RetainedWorkspaceActivationHooks {
  post(posting: BrowserRetainedWorkspacePosting): void;
  clear(): void;
  predecessorSettled(
    observation: RetainedWorkspacePredecessorObservation,
    result: BrowserRetainedWorkspaceSettlementResult,
  ): void;
  predecessorObservationFailed(
    observation: RetainedWorkspacePredecessorObservation,
    error: unknown,
  ): void;
}

export interface RetainedWorkspaceActivationController {
  readonly state: RetainedWorkspaceActivationState;
  retain(input: RetainedWorkspaceDefinitionInput): RetainedWorkspaceDefinition;
  activate(
    retainedDefinitionId: string,
    accept?: (
      preparation: BrowserRetainedWorkspacePreparedPosting,
    ) => boolean | Promise<boolean>,
    complete?: (
      posting: BrowserRetainedWorkspacePosting,
    ) => void | Promise<void>,
  ): Promise<BrowserRetainedWorkspaceActivationResult>;
  cancelPending(): boolean;
  waitForPendingCommit(): Promise<void> | null;
  delete(
    retainedDefinitionId: string,
    options?: RetainedWorkspaceDeletionOptions,
  ): Promise<void>;
}

interface RetainedWorkspaceDeletionOptions {
  readonly successorDefinitionId?: string | null;
  readonly acceptSuccessor?: (
    preparation: BrowserRetainedWorkspacePreparedPosting,
  ) => boolean | Promise<boolean>;
  readonly completeSuccessor?: (
    posting: BrowserRetainedWorkspacePosting,
  ) => void | Promise<void>;
  readonly completeDeactivation?: () => void | Promise<void>;
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
  let postedPublicationOrdinal = 0;
  let currentActivationReceipt: string | null = null;
  const uncertainCancellations = new Map<string, {
    readonly retainedDefinitionId: string;
    readonly receipt: string;
    retry: Promise<void> | null;
  }>();
  let committingDefinitionId: string | null = null;
  let commitBarrier: Promise<void> | null = null;
  let settleCommit: (() => void) | null = null;
  let soleDeactivationIntent: SoleDeactivationIntent | null = null;
  const cancellationRequests = new Map<
    string,
    Promise<BrowserRetainedWorkspaceActivationResult>
  >();
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
    posting: BrowserRetainedWorkspacePosting,
  ): void {
    const predecessor = posting.predecessor;
    if (predecessor === null
      || observedSettlementIds.has(predecessor.settlementId)) {
      return;
    }
    observedSettlementIds.add(predecessor.settlementId);
    const observation: RetainedWorkspacePredecessorObservation = {
      retainedDefinitionId: posting.retainedDefinitionId,
      realizationId: posting.realizationId,
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

  function authorityArguments(
    posting: BrowserRetainedWorkspacePosting,
  ): readonly [string, number, string, string, string, string] {
    const authority = posting.navigation.authority;
    if (authority === null) {
      throw new Error(
        "A retained Workspace posting requires Navigation effect authority.",
      );
    }
    return [
      posting.realizationId,
      posting.publicationOrdinal,
      authority.session,
      authority.revision,
      authority.intent,
      authority.epoch,
    ];
  }

  async function abandonPosting(
    posting: BrowserRetainedWorkspacePosting,
  ): Promise<void> {
    const status = await client.abandonRetainedWorkspaceNavigation(
      ...authorityArguments(posting),
    );
    if (status !== "accepted" && status !== "invalidAuthority") {
      throw new Error(
        `Navigation abandonment returned '${status}'.`,
      );
    }
  }

  async function postActivation(
    posting: BrowserRetainedWorkspacePosting,
    complete: (
      posting: BrowserRetainedWorkspacePosting,
    ) => void | Promise<void>,
  ): Promise<boolean> {
    const authority = authorityArguments(posting);
    if (posting.publicationOrdinal <= postedPublicationOrdinal) {
      if (posting.publicationOrdinal < postedPublicationOrdinal) {
        await abandonPosting(posting);
      }
      return false;
    }

    try {
      if (!await client.validateRetainedWorkspaceNavigationAuthority(
        ...authority,
      )) {
        hooks.clear();
        await abandonPosting(posting);
        return false;
      }
      postedPublicationOrdinal = posting.publicationOrdinal;
      hooks.post(posting);
      const recorded =
        await client.recordRetainedWorkspaceNavigationPosting(
          ...authority,
        );
      if (recorded === "invalidAuthority") {
        throw new Error(
          "Navigation posting record rejected the committed authority.",
        );
      }
      if (recorded !== "accepted") {
        throw new Error(
          `Navigation posting record returned '${recorded}'.`,
        );
      }
      await complete(posting);
      const acknowledged =
        await client.acknowledgeRetainedWorkspaceNavigation(...authority);
      if (acknowledged === "invalidAuthority") {
        throw new Error(
          "Navigation acknowledgement rejected the committed authority.",
        );
      }
      if (acknowledged !== "accepted") {
        throw new Error(
          `Navigation acknowledgement returned '${acknowledged}'.`,
        );
      }
      return true;
    } catch (error) {
      let clearFailure: unknown = null;
      try {
        hooks.clear();
      } catch (clearError) {
        clearFailure = clearError;
      }
      try {
        await abandonPosting(posting);
      } catch (abandonmentError) {
        throw new AggregateError(
          clearFailure === null
            ? [error, abandonmentError]
            : [error, clearFailure, abandonmentError],
          "Retained Workspace posting reconciliation and Navigation "
            + "abandonment failed.",
          { cause: abandonmentError },
        );
      }
      if (clearFailure !== null) {
        throw new AggregateError(
          [error, clearFailure],
          "Retained Workspace posting and presentation reconciliation failed.",
          { cause: error },
        );
      }
      throw error;
    }
  }

  function requestCancellation(
    receipt: string,
  ): Promise<BrowserRetainedWorkspaceActivationResult> {
    const existing = cancellationRequests.get(receipt);
    if (existing !== undefined) return existing;
    const cancellation =
      client.cancelRetainedWorkspaceActivation(receipt);
    cancellationRequests.set(receipt, cancellation);
    return cancellation;
  }

  function requireCompleted(
    result: BrowserRetainedWorkspaceConsumerCompletionResult,
    expectedSucceeded: boolean,
  ): void {
    if (result.status !== "completed"
      || result.succeeded !== expectedSucceeded) {
      throw new Error(
        result.message
          ?? result.failure
          ?? "Retained Workspace consumer completion was not accepted.",
      );
    }
  }

  async function completeActivationReceipt(
    receipt: string,
    succeeded: boolean,
    failure: string | null,
  ): Promise<void> {
    requireCompleted(
      await client.completeRetainedWorkspaceActivation(
        receipt,
        succeeded,
        failure,
      ),
      succeeded,
    );
  }

  async function completeDeactivationReceipt(
    receipt: string,
    succeeded: boolean,
    failure: string | null,
  ): Promise<void> {
    requireCompleted(
      await client.completeRetainedWorkspaceDeactivation(
        receipt,
        succeeded,
        failure,
      ),
      succeeded,
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
    accept: (
      preparation: BrowserRetainedWorkspacePreparedPosting,
    ) => boolean | Promise<boolean> = () => true,
    complete: (
      posting: BrowserRetainedWorkspacePosting,
    ) => void | Promise<void> = () => {},
    committed: () => void = () => {},
  ): Promise<BrowserRetainedWorkspaceActivationResult> {
    const definition = find(retainedDefinitionId);
    if (soleDeactivationIntent !== null) {
      throw new Error(
        "A retained Workspace cannot be activated while the active Workspace is being deactivated.",
      );
    }
    if (uncertainCancellations.size > 0) {
      throw new Error(
        "A retained Workspace activation cannot begin while cancellation settlement is unknown.",
      );
    }
    if (committingDefinitionId !== null) {
      throw new Error(
        "A retained Workspace activation is awaiting consumer completion.",
      );
    }
    const generation = ++selectionGeneration;
    pendingDefinitionId = retainedDefinitionId;
    lastFailure = null;
    beginActivation(definition.id);
    let receipt: string | null = null;
    let commitStarted = false;
    let commitOutcomeConfirmed = false;
    let cancellationOutcomeConfirmed = false;
    let requiresConsumerCompletion = false;
    let completionAttempted = false;
    let completionAccepted = false;

    try {
      let result: BrowserRetainedWorkspaceActivationResult;
      try {
        const preparation = await client.prepareRetainedWorkspaceDefinition(
          definition.id,
          definition.label,
          definition.canonicalLocation,
          definition.canonicalPacket,
        );
        switch (preparation.status) {
          case "prepared": {
            if (preparation.preparation === null
              || preparation.receipt === null) {
              throw new Error(
                "Retained Workspace preparation omitted candidate evidence or receipt.",
              );
            }
            const preparedReceipt = preparation.receipt;
            receipt = preparedReceipt;
            if (generation === selectionGeneration) {
              currentActivationReceipt = preparedReceipt;
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
              let cancellation:
                BrowserRetainedWorkspaceActivationResult;
              try {
                cancellation =
                  await requestCancellation(preparedReceipt);
                cancellationOutcomeConfirmed = true;
              } catch (cancellationError) {
                throw new AggregateError(
                  [error, cancellationError],
                  "Retained Workspace acceptance and cancellation failed.",
                  { cause: cancellationError },
                );
              }
              if (cancellation.status === "failed") {
                const cleanupFailure = new Error(
                  cancellation.failure?.message
                    ?? "Retained Workspace cancellation cleanup failed.",
                  { cause: error },
                );
                if (generation === selectionGeneration) {
                  lastFailure = cleanupFailure.message;
                }
                throw new AggregateError(
                  [error, cleanupFailure],
                  "Retained Workspace acceptance failed and its candidate "
                    + "could not be cleaned up.",
                  { cause: error },
                );
              }
              throw error;
            }
            if (!accepted || generation !== selectionGeneration) {
              result = await requestCancellation(preparedReceipt);
              cancellationOutcomeConfirmed = true;
              break;
            }

            committingDefinitionId = definition.id;
            commitBarrier = new Promise<void>(resolve => {
              settleCommit = resolve;
            });
            commitStarted = true;
            requiresConsumerCompletion = true;
            result = await client.commitRetainedWorkspaceActivation(
              preparedReceipt,
            );
            commitOutcomeConfirmed = true;
            requiresConsumerCompletion = result.status === "activated";
            break;
          }
          case "noEffect":
            result = {
              status: "noEffect",
              posting: preparation.posting,
              failure: null,
            };
            break;
          case "failed":
            result = {
              status: "failed",
              posting: null,
              failure: preparation.failure,
            };
            break;
          case "superseded":
            result = {
              status: "superseded",
              posting: null,
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
          if (lastFailure === null) {
            lastFailure = error instanceof Error
              ? error.message
              : "Retained Workspace activation failed.";
          }
        }
        throw error;
      }

      switch (result.status) {
        case "activated":
        case "noEffect": {
          const posting = result.posting;
          if (posting === null) {
            throw new Error(
              `Retained Workspace ${result.status} omitted posting evidence.`,
            );
          }
          observePredecessorOnce(posting);
          if (result.status === "activated") {
            if (receipt === null) {
              throw new Error(
                "Activated retained Workspace omitted its consumer completion receipt.",
              );
            }
            activeDefinitionId = posting.retainedDefinitionId;
            committed();
            try {
              const posted = await postActivation(posting, complete);
              if (!posted) {
                throw new Error(
                  "The committed retained Workspace posting is no longer current.",
                );
              }
              completionAttempted = true;
              await completeActivationReceipt(receipt, true, null);
              completionAccepted = true;
            } catch (error) {
              const message = error instanceof Error
                ? error.message
                : "Retained Workspace consumer completion failed.";
              try {
                completionAttempted = true;
                await completeActivationReceipt(receipt, false, message);
                completionAccepted = true;
              } catch (completionError) {
                throw new AggregateError(
                  [error, completionError],
                  "Retained Workspace consumer work and completion reporting failed.",
                  { cause: completionError },
                );
              }
              if (generation === selectionGeneration) {
                lastFailure = message;
              }
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
    } catch (error) {
      if (generation === selectionGeneration) {
        pendingDefinitionId = null;
        if (lastFailure === null) {
          lastFailure = error instanceof Error
            ? error.message
            : "Retained Workspace activation failed.";
        }
      }
      if (commitStarted
        && !commitOutcomeConfirmed
        && !completionAttempted
        && receipt !== null) {
        const message = error instanceof Error
          ? error.message
          : "Retained Workspace activation ended before consumer completion.";
        activeDefinitionId = null;
        pendingDefinitionId = null;
        let reconciliationError: unknown = null;
        try {
          hooks.clear();
        } catch (clearError) {
          reconciliationError = clearError;
        }
        try {
          completionAttempted = true;
          await completeActivationReceipt(receipt, false, message);
          completionAccepted = true;
        } catch (completionError) {
          throw new AggregateError(
            reconciliationError === null
              ? [error, completionError]
              : [error, reconciliationError, completionError],
            "Retained Workspace activation and completion reporting failed.",
            { cause: completionError },
          );
        }
        activeDefinitionId = definition.id;
        committed();
        if (reconciliationError !== null) {
          const reconciliationMessage =
            reconciliationError instanceof Error
              ? reconciliationError.message
              : "Retained Workspace presentation reconciliation failed.";
          lastFailure =
            `${message} Presentation reconciliation failed: ${
              reconciliationMessage
            }`;
          throw new AggregateError(
            [error, reconciliationError],
            "Retained Workspace commit response and presentation "
              + "reconciliation failed.",
            { cause: error },
          );
        }
      }
      throw error;
    } finally {
      const releaseTransaction = (!commitStarted
          && (receipt === null || cancellationOutcomeConfirmed))
        || (commitOutcomeConfirmed && !requiresConsumerCompletion)
        || completionAccepted;
      if (!releaseTransaction
        && !commitStarted
        && receipt !== null) {
        uncertainCancellations.set(receipt, {
          retainedDefinitionId: definition.id,
          receipt,
          retry: null,
        });
      }
      if (commitStarted && releaseTransaction) {
        committingDefinitionId = null;
        settleCommit?.();
        settleCommit = null;
        commitBarrier = null;
      }
      if (releaseTransaction && currentActivationReceipt === receipt) {
        currentActivationReceipt = null;
      }
      if (receipt !== null) cancellationRequests.delete(receipt);
      if (releaseTransaction) endActivation(definition.id);
    }
  }

  function cancelPending(): boolean {
    if (committingDefinitionId !== null
      || soleDeactivationIntent !== null) return false;
    const cancelledGeneration = selectionGeneration;
    selectionGeneration++;
    pendingDefinitionId = null;
    const receipt = currentActivationReceipt;
    currentActivationReceipt = null;
    const receiptHasUncertainCancellation =
      receipt !== null && uncertainCancellations.has(receipt);
    if (uncertainCancellations.size > 0) {
      for (const intent of uncertainCancellations.values()) {
        if (intent.retry !== null) continue;
        intent.retry = requestCancellation(intent.receipt).then(
          result => {
            cancellationRequests.delete(intent.receipt);
            if (uncertainCancellations.get(intent.receipt) !== intent) {
              return undefined;
            }
            uncertainCancellations.delete(intent.receipt);
            if (currentActivationReceipt === intent.receipt) {
              currentActivationReceipt = null;
            }
            endActivation(intent.retainedDefinitionId);
            if (result.status === "failed") {
              lastFailure = result.failure?.message
                ?? "Retained Workspace cancellation failed.";
            }
            return undefined;
          },
          (error: unknown) => {
            cancellationRequests.delete(intent.receipt);
            if (uncertainCancellations.get(intent.receipt) === intent) {
              intent.retry = null;
              lastFailure = error instanceof Error
                ? error.message
                : "Retained Workspace cancellation failed.";
            }
            return undefined;
          },
        );
      }
    }
    if (receipt === null || receiptHasUncertainCancellation) return true;
    void requestCancellation(receipt).then(
      result => {
        if (selectionGeneration === cancelledGeneration + 1
          && result.status === "failed") {
          lastFailure = result.failure?.message
            ?? "Retained Workspace cancellation failed.";
        }
        return undefined;
      },
      (error: unknown) => {
        if (selectionGeneration === cancelledGeneration + 1) {
          lastFailure = error instanceof Error
            ? error.message
            : "Retained Workspace cancellation failed.";
        }
        return undefined;
      },
    );
    return true;
  }

  async function deleteDefinition(
    retainedDefinitionId: string,
    options: RetainedWorkspaceDeletionOptions = {},
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
    if (committingDefinitionId !== null) {
      throw new Error(
        "The active retained Workspace cannot be deleted while an activation "
          + "is awaiting consumer completion.",
      );
    }
    if (unsettledActivationCounts.size > 0) {
      throw new Error(
        "The active retained Workspace cannot be deleted until pending "
          + "activations settle.",
      );
    }
    if (options.successorDefinitionId === retainedDefinitionId) {
      throw new Error(
        "A retained Workspace definition cannot be its own deletion successor.",
      );
    }

    const successor = options.successorDefinitionId === undefined
      ? definitions[removedIndex + 1] ?? definitions[removedIndex - 1]
      : options.successorDefinitionId === null
        ? undefined
        : find(options.successorDefinitionId);
    if (successor !== undefined) {
      const completeCommittedDeletion = (): void => {
        definitions = definitions.filter(
          definition => definition.id !== retainedDefinitionId,
        );
      };
      const result = await activate(
        successor.id,
        options.acceptSuccessor,
        options.completeSuccessor,
        completeCommittedDeletion,
      );
      if (result.status !== "activated" && result.status !== "noEffect") {
        return;
      }
      completeCommittedDeletion();
      return;
    }

    const intent: SoleDeactivationIntent = {
      generation: ++nextDeactivationGeneration,
      retainedDefinitionId,
    };
    soleDeactivationIntent = intent;
    commitBarrier = new Promise<void>(resolve => {
      settleCommit = resolve;
    });
    let requiresConsumerCompletion = false;
    let completionAccepted = false;
    try {
      let result: BrowserRetainedWorkspaceDeactivationResult;
      try {
        requiresConsumerCompletion = true;
        result = await client.deactivateRetainedWorkspaceDefinition(
          retainedDefinitionId,
        );
      } catch (error) {
        lastFailure = error instanceof Error
          ? error.message
          : "Retained Workspace deactivation outcome is unknown.";
        activeDefinitionId = null;
        try {
          hooks.clear();
        } catch (clearError) {
          const clearMessage = clearError instanceof Error
            ? clearError.message
            : "Retained Workspace presentation reconciliation failed.";
          lastFailure += ` Presentation reconciliation failed: ${clearMessage}`;
          throw new AggregateError(
            [error, clearError],
            "Retained Workspace deactivation response and presentation "
              + "reconciliation failed.",
            { cause: clearError },
          );
        }
        throw error;
      }
      if (soleDeactivationIntent?.generation !== intent.generation) {
        return;
      }
      requiresConsumerCompletion = result.status === "deactivated"
        || result.status === "cleanupFailed";
      switch (result.status) {
        case "deactivated": {
          const receipt = result.completionReceipt;
          if (receipt === null) {
            throw new Error(
              "Retained Workspace deactivation omitted its completion receipt.",
            );
          }
          definitions = definitions.filter(
            definition => definition.id !== retainedDefinitionId,
          );
          activeDefinitionId = null;
          lastFailure = null;
          try {
            hooks.clear();
            await options.completeDeactivation?.();
            await completeDeactivationReceipt(receipt, true, null);
            completionAccepted = true;
          } catch (error) {
            const message = error instanceof Error
              ? error.message
              : "Retained Workspace deactivation completion failed.";
            lastFailure = message;
            try {
              await completeDeactivationReceipt(receipt, false, message);
              completionAccepted = true;
            } catch (completionError) {
              throw new AggregateError(
                [error, completionError],
                "Retained Workspace deactivation and completion reporting failed.",
                { cause: completionError },
              );
            }
            throw error;
          }
          return;
        }
        case "noEffect":
          definitions = definitions.filter(
            definition => definition.id !== retainedDefinitionId,
          );
          activeDefinitionId = null;
          hooks.clear();
          return;
        case "cleanupFailed": {
          const receipt = result.completionReceipt;
          if (receipt === null) {
            throw new Error(
              "Retained Workspace cleanup failure omitted its completion receipt.",
            );
          }
          definitions = definitions.filter(
            definition => definition.id !== retainedDefinitionId,
          );
          activeDefinitionId = null;
          const managedFailure = result.settlement?.failure
            ?? result.message
            ?? "The active Workspace could not be settled.";
          lastFailure = managedFailure;
          try {
            hooks.clear();
            await options.completeDeactivation?.();
            await completeDeactivationReceipt(receipt, true, null);
            completionAccepted = true;
          } catch (error) {
            const message = error instanceof Error
              ? error.message
              : "Retained Workspace deactivation completion failed.";
            const combinedFailure =
              `${managedFailure} Consumer completion failed: ${message}`;
            lastFailure = combinedFailure;
            try {
              await completeDeactivationReceipt(
                receipt,
                false,
                combinedFailure,
              );
              completionAccepted = true;
            } catch (completionError) {
              throw new AggregateError(
                [
                  new Error(managedFailure),
                  error,
                  completionError,
                ],
                "Retained Workspace cleanup, consumer completion, and "
                  + "completion reporting failed.",
                { cause: completionError },
              );
            }
            throw new AggregateError(
              [new Error(managedFailure), error],
              "Retained Workspace cleanup and consumer completion failed.",
              { cause: error },
            );
          }
          return;
        }
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
      if ((!requiresConsumerCompletion || completionAccepted)
        && soleDeactivationIntent?.generation === intent.generation) {
        soleDeactivationIntent = null;
        settleCommit?.();
        settleCommit = null;
        commitBarrier = null;
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
    delete: deleteDefinition,
  };
}
