import type { AnnotatedSourceResult } from "./annotated-source-session.ts";
import type {
  BrowserMemberFindingCensus,
} from "./facades/inspect-web-source.d.ts";

export interface MemberFindingCensus
  extends Omit<BrowserMemberFindingCensus, "annotatedSource"> {
  annotatedSource: AnnotatedSourceResult;
}

export interface MemberFindingInteraction {
  census: MemberFindingCensus;
  factIdByInstanceKey: ReadonlyMap<number, number>;
  instanceKeyByFactId: ReadonlyMap<number, number>;
  selectedInstanceKey: number | null;
}

export type FindingSelectionTransition =
  | {
      accepted: true;
      interaction: MemberFindingInteraction;
      factId: number;
      error: null;
    }
  | {
      accepted: false;
      interaction: MemberFindingInteraction;
      factId: null;
      error: string;
    };

export function createMemberFindingInteraction(
  census: MemberFindingCensus,
): MemberFindingInteraction {
  if (!census.factCensusReceipt.trim()) {
    throw new TypeError("The Finding census receipt is missing.");
  }

  const documentFactIds =
    new Set(census.annotatedSource.document.facts.map(fact => fact.id));
  const bodyFactIds = new Set(
    census.annotatedSource.document.facts
      .filter(fact => fact.origin === "Body")
      .map(fact => fact.id),
  );
  const factIdByInstanceKey = new Map<number, number>();
  const instanceKeyByFactId = new Map<number, number>();

  for (const instance of census.sourceFactInstances) {
    requirePositiveInteger(instance.instanceKey, "Finding instance key");
    requireNonNegativeInteger(instance.factId, "Annotated Source fact id");
    if (!documentFactIds.has(instance.factId)) {
      throw new TypeError(
        `Annotated Source fact id ${instance.factId} is not present in the document.`,
      );
    }
    if (!bodyFactIds.has(instance.factId)) {
      throw new TypeError(
        `Annotated Source fact id ${instance.factId} is not a body Finding.`,
      );
    }
    if (factIdByInstanceKey.has(instance.instanceKey)) {
      throw new TypeError(
        `Finding instance key ${instance.instanceKey} appears more than once.`,
      );
    }
    if (instanceKeyByFactId.has(instance.factId)) {
      throw new TypeError(
        `Annotated Source fact id ${instance.factId} appears more than once.`,
      );
    }
    factIdByInstanceKey.set(instance.instanceKey, instance.factId);
    instanceKeyByFactId.set(instance.factId, instance.instanceKey);
  }

  if (instanceKeyByFactId.size !== bodyFactIds.size) {
    throw new TypeError(
      "The Finding census sidecar does not cover every Annotated Source body fact.",
    );
  }

  const factsKeys = new Set<number>();
  for (const fact of census.facts) {
    if (fact.instanceKey === null) continue;
    requirePositiveInteger(fact.instanceKey, "Finding instance key");
    if (factsKeys.has(fact.instanceKey)) {
      throw new TypeError(
        `Finding instance key ${fact.instanceKey} appears more than once in Facts.`,
      );
    }
    factsKeys.add(fact.instanceKey);
  }

  if (!setsEqual(factsKeys, factIdByInstanceKey.keys())) {
    throw new TypeError(
      "Facts and Annotated Source do not carry the same Finding instance keys.",
    );
  }

  return {
    census,
    factIdByInstanceKey,
    instanceKeyByFactId,
    selectedInstanceKey: null,
  };
}

export function selectFindingInstance(
  interaction: MemberFindingInteraction,
  receipt: string,
  instanceKey: number,
): FindingSelectionTransition {
  if (receipt !== interaction.census.factCensusReceipt) {
    return rejected(
      interaction,
      "The selected Finding belongs to a stale census.",
    );
  }
  const factId = interaction.factIdByInstanceKey.get(instanceKey);
  if (factId === undefined) {
    return rejected(
      interaction,
      `Finding instance ${instanceKey} is not present in the active census.`,
    );
  }
  return accepted(interaction, instanceKey, factId);
}

export function selectAnnotatedSourceFact(
  interaction: MemberFindingInteraction,
  factId: number,
): FindingSelectionTransition {
  const instanceKey = interaction.instanceKeyByFactId.get(factId);
  if (instanceKey === undefined) {
    return rejected(
      interaction,
      `Annotated Source fact ${factId} has no Finding instance identity.`,
    );
  }
  return accepted(interaction, instanceKey, factId);
}

export function clearFindingSelection(
  interaction: MemberFindingInteraction,
): MemberFindingInteraction {
  return interaction.selectedInstanceKey === null
    ? interaction
    : { ...interaction, selectedInstanceKey: null };
}

export function selectedAnnotatedSourceFactId(
  interaction: MemberFindingInteraction,
): number | null {
  if (interaction.selectedInstanceKey === null) return null;
  return interaction.factIdByInstanceKey.get(interaction.selectedInstanceKey)
    ?? null;
}

function accepted(
  interaction: MemberFindingInteraction,
  instanceKey: number,
  factId: number,
): FindingSelectionTransition {
  return {
    accepted: true,
    interaction: interaction.selectedInstanceKey === instanceKey
      ? interaction
      : { ...interaction, selectedInstanceKey: instanceKey },
    factId,
    error: null,
  };
}

function rejected(
  interaction: MemberFindingInteraction,
  error: string,
): FindingSelectionTransition {
  return {
    accepted: false,
    interaction,
    factId: null,
    error,
  };
}

function requirePositiveInteger(value: number, label: string): void {
  if (!Number.isSafeInteger(value) || value <= 0) {
    throw new TypeError(`${label} must be a positive integer.`);
  }
}

function requireNonNegativeInteger(value: number, label: string): void {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new TypeError(`${label} must be a non-negative integer.`);
  }
}

function setsEqual(
  left: ReadonlySet<number>,
  rightValues: Iterable<number>,
): boolean {
  const right = new Set(rightValues);
  return left.size === right.size
    && [...left].every(value => right.has(value));
}
