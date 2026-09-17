export const MAX_RETAINED_WORKSPACE_DEFINITIONS = 4;

interface RetainedWorkspaceDefinition {
  readonly id: string;
  readonly label: string;
  readonly packet: string;
  readonly canonicalLocation: string;
  readonly failure: string | null;
}

export interface RetainedWorkspaceDefinitionCollection {
  readonly definitions: readonly RetainedWorkspaceDefinition[];
  readonly activeDefinitionId: string | null;
  readonly activatingDefinitionId: string | null;
  readonly activationGeneration: number;
  readonly nextOrdinal: number;
}

export interface RetainedWorkspaceActivationIntent {
  readonly definitionId: string;
  readonly generation: number;
}

export type RetainedWorkspaceActivationStart =
  | {
    readonly kind: "noEffect";
    readonly collection: RetainedWorkspaceDefinitionCollection;
  }
  | {
    readonly kind: "started";
    readonly collection: RetainedWorkspaceDefinitionCollection;
    readonly intent: RetainedWorkspaceActivationIntent;
  };

export function createRetainedWorkspaceDefinitionCollection():
RetainedWorkspaceDefinitionCollection {
  return {
    definitions: [],
    activeDefinitionId: null,
    activatingDefinitionId: null,
    activationGeneration: 0,
    nextOrdinal: 1,
  };
}

export function retainWorkspaceDefinition(
  collection: RetainedWorkspaceDefinitionCollection,
  packet: string,
  canonicalLocation: string,
  label?: string,
): RetainedWorkspaceDefinitionCollection {
  if (collection.definitions.length >= MAX_RETAINED_WORKSPACE_DEFINITIONS) {
    throw new Error(
      `Inspect Web retains at most ${
        MAX_RETAINED_WORKSPACE_DEFINITIONS
      } Workspace definitions. Delete one before opening another.`,
    );
  }
  if (!packet) throw new Error("A retained Workspace requires a packet.");
  if (!canonicalLocation) {
    throw new Error("A retained Workspace requires a canonical location.");
  }

  const ordinal = collection.nextOrdinal;
  const definition: RetainedWorkspaceDefinition = {
    id: `workspace-${ordinal}`,
    label: label ?? `Workspace ${ordinal}`,
    packet,
    canonicalLocation,
    failure: null,
  };
  return {
    ...collection,
    definitions: [...collection.definitions, definition],
    nextOrdinal: ordinal + 1,
  };
}

export function beginRetainedWorkspaceActivation(
  collection: RetainedWorkspaceDefinitionCollection,
  definitionId: string,
): RetainedWorkspaceActivationStart {
  const definition = requireDefinition(collection, definitionId);
  if (definition.id === collection.activeDefinitionId) {
    return {
      kind: "noEffect",
      collection: collection.activatingDefinitionId === null
        ? collection
        : {
          ...collection,
          activatingDefinitionId: null,
          activationGeneration: collection.activationGeneration + 1,
        },
    };
  }

  const generation = collection.activationGeneration + 1;
  return {
    kind: "started",
    collection: {
      ...collection,
      activatingDefinitionId: definitionId,
      activationGeneration: generation,
      definitions: collection.definitions.map(candidate =>
        candidate.id === definitionId
          ? { ...candidate, failure: null }
          : candidate),
    },
    intent: { definitionId, generation },
  };
}

export function commitRetainedWorkspaceActivation(
  collection: RetainedWorkspaceDefinitionCollection,
  intent: RetainedWorkspaceActivationIntent,
): RetainedWorkspaceDefinitionCollection {
  if (!isCurrentIntent(collection, intent)) return collection;
  return {
    ...collection,
    activeDefinitionId: intent.definitionId,
    activatingDefinitionId: null,
  };
}

export function failRetainedWorkspaceActivation(
  collection: RetainedWorkspaceDefinitionCollection,
  intent: RetainedWorkspaceActivationIntent,
  message: string,
): RetainedWorkspaceDefinitionCollection {
  if (!isCurrentIntent(collection, intent)) return collection;
  if (!message) throw new Error("A failed Workspace activation requires a message.");
  return {
    ...collection,
    activatingDefinitionId: null,
    definitions: collection.definitions.map(definition =>
      definition.id === intent.definitionId
        ? { ...definition, failure: message }
        : definition),
  };
}

export function deleteRetainedWorkspaceDefinition(
  collection: RetainedWorkspaceDefinitionCollection,
  definitionId: string,
): RetainedWorkspaceDefinitionCollection {
  requireDefinition(collection, definitionId);
  if (collection.activeDefinitionId === definitionId) {
    throw new Error(
      "Activate a successor or close the active Workspace before deleting its definition.",
    );
  }
  if (collection.activatingDefinitionId === definitionId) {
    throw new Error(
      "A Workspace definition cannot be deleted while its activation is current.",
    );
  }
  return {
    ...collection,
    definitions: collection.definitions.filter(
      definition => definition.id !== definitionId),
  };
}

function isCurrentIntent(
  collection: RetainedWorkspaceDefinitionCollection,
  intent: RetainedWorkspaceActivationIntent,
): boolean {
  return collection.activatingDefinitionId === intent.definitionId
    && collection.activationGeneration === intent.generation;
}

function requireDefinition(
  collection: RetainedWorkspaceDefinitionCollection,
  definitionId: string,
): RetainedWorkspaceDefinition {
  const definition = collection.definitions.find(
    candidate => candidate.id === definitionId);
  if (!definition) throw new Error("That Workspace definition is no longer available.");
  return definition;
}
