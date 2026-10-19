import type {
  BrowserDocumentLeafContainmentFailure,
  BrowserDocumentLeafContainmentOutcome,
  BrowserDocumentPresentationLeaf,
  BrowserDocumentTextPolicy,
  InertString,
} from "./facades/inspect-web-package.d.ts";

declare const preparedLeafContainmentBrand: unique symbol;
declare const containedLeafPopulationBrand: unique symbol;
declare const preparedDocumentFragmentBrand: unique symbol;
declare const containedDocumentFragmentBrand: unique symbol;

const maximumDocumentPresentationLeafIdentity = 2_147_483_647;
const preparedDocumentFragments =
  new WeakMap<PreparedContainedDocumentFragment, PreparedDocumentFragmentState>();

export type DocumentTextPolicy =
  Exclude<BrowserDocumentTextPolicy, number>;

export interface DocumentPresentationLeaf {
  readonly identity: number;
  readonly policy: DocumentTextPolicy;
  readonly text: string;
}

export interface PreparedDocumentLeafContainment {
  readonly generation: string;
  readonly leaves: ReadonlyArray<DocumentPresentationLeaf>;
  readonly [preparedLeafContainmentBrand]: "PreparedDocumentLeafContainment";
}

export interface ContainedDocumentPresentationLeaf {
  readonly identity: number;
  readonly policy: DocumentTextPolicy;
  readonly text: InertString;
}

export interface ContainedLeafPopulation {
  readonly generation: string;
  readonly leaves: ReadonlyArray<ContainedDocumentPresentationLeaf>;
  readonly [containedLeafPopulationBrand]: "ContainedLeafPopulation";
}

export interface DocumentTextPresentationTarget {
  readonly kind: "text";
  readonly node: Text;
  readonly policy: DocumentTextPolicy;
}

export interface DocumentAttributePresentationTarget {
  readonly kind: "attribute";
  readonly element: Element;
  readonly name: string;
  readonly policy: DocumentTextPolicy;
}

export type DocumentPresentationTarget =
  | DocumentTextPresentationTarget
  | DocumentAttributePresentationTarget;

export interface PreparedContainedDocumentFragment {
  readonly generation: string;
  readonly [preparedDocumentFragmentBrand]: "PreparedContainedDocumentFragment";
}

export interface ContainedDocumentFragment {
  readonly html: string;
  readonly [containedDocumentFragmentBrand]: "ContainedDocumentFragment";
}

interface PreparedDocumentFragmentState {
  readonly template: HTMLTemplateElement;
  readonly targets: ReadonlyArray<DocumentPresentationTarget>;
  readonly leafContainment: PreparedDocumentLeafContainment;
}

export type DocumentLeafCompletionRejectionReason =
  | "managed-rejection"
  | "stale-generation"
  | "wrong-generation"
  | "duplicate-leaf"
  | "surplus-leaf"
  | "policy-mismatch"
  | "missing-leaf";

export type DocumentLeafCompletion =
  | {
    readonly kind: "completed";
    readonly population: ContainedLeafPopulation;
  }
  | {
    readonly kind: "rejected";
    readonly reason: DocumentLeafCompletionRejectionReason;
    readonly identity: number | null;
    readonly failure: BrowserDocumentLeafContainmentFailure | null;
  };

export type ContainedDocumentFragmentCompletion =
  | {
    readonly kind: "completed";
    readonly fragment: ContainedDocumentFragment;
  }
  | Extract<DocumentLeafCompletion, { readonly kind: "rejected" }>;

export function prepareDocumentLeafContainment(
  generation: string,
  leaves: ReadonlyArray<DocumentPresentationLeaf>,
): PreparedDocumentLeafContainment {
  if (generation.trim().length === 0) {
    throw new TypeError(
      "A document structure generation must not be empty.",
    );
  }

  const identities = new Set<number>();
  const copied: DocumentPresentationLeaf[] = [];
  for (const leaf of leaves) {
    if (
      !Number.isSafeInteger(leaf.identity)
      || leaf.identity < 0
      || leaf.identity > maximumDocumentPresentationLeafIdentity
    ) {
      throw new TypeError(
        "A document presentation-leaf identity must be a non-negative 32-bit integer.",
      );
    }
    if (identities.has(leaf.identity)) {
      throw new TypeError(
        `Document presentation-leaf identity ${leaf.identity} is duplicated.`,
      );
    }
    identities.add(leaf.identity);
    if (leaf.policy !== "Field" && leaf.policy !== "Prose") {
      throw new TypeError(
        `Document presentation leaf ${leaf.identity} has an unsupported text policy.`,
      );
    }
    copied.push(Object.freeze({
      identity: leaf.identity,
      policy: leaf.policy,
      text: leaf.text,
    }));
  }

  const prepared = Object.freeze({
    generation,
    leaves: Object.freeze(copied),
  });
  // This function is the sole construction boundary for the prepared batch.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return prepared as PreparedDocumentLeafContainment;
}

export function browserDocumentPresentationLeaves(
  prepared: PreparedDocumentLeafContainment,
): ReadonlyArray<BrowserDocumentPresentationLeaf> {
  return prepared.leaves;
}

export function completeDocumentLeafContainment(
  prepared: PreparedDocumentLeafContainment,
  currentGeneration: string | null,
  outcome: BrowserDocumentLeafContainmentOutcome,
): DocumentLeafCompletion {
  if (currentGeneration !== prepared.generation) {
    return rejected("stale-generation");
  }
  if (outcome.outcome === "rejected") {
    return rejected(
      "managed-rejection",
      outcome.failure.identity,
      outcome.failure,
    );
  }
  if (outcome.generation !== prepared.generation) {
    return rejected("wrong-generation");
  }

  const expected = new Map(
    prepared.leaves.map(leaf => [leaf.identity, leaf] as const),
  );
  const contained = new Map<
    number,
    ContainedDocumentPresentationLeaf | null
  >(
    prepared.leaves.map(leaf =>
      [leaf.identity, null] as const
    ),
  );
  const seen = new Set<number>();
  for (const leaf of outcome.leaves) {
    if (seen.has(leaf.identity)) {
      return rejected("duplicate-leaf", leaf.identity);
    }
    seen.add(leaf.identity);
    const expectedLeaf = expected.get(leaf.identity);
    if (expectedLeaf === undefined) {
      return rejected("surplus-leaf", leaf.identity);
    }
    if (leaf.policy !== expectedLeaf.policy) {
      return rejected("policy-mismatch", leaf.identity);
    }
    contained.set(leaf.identity, Object.freeze({
      identity: leaf.identity,
      policy: expectedLeaf.policy,
      text: leaf.text,
    }));
  }

  const ordered: ContainedDocumentPresentationLeaf[] = [];
  for (const leaf of prepared.leaves) {
    const result = contained.get(leaf.identity);
    if (result === null || result === undefined) {
      return rejected("missing-leaf", leaf.identity);
    }
    ordered.push(result);
  }

  const population = Object.freeze({
    generation: prepared.generation,
    leaves: Object.freeze(ordered),
  });
  return {
    kind: "completed",
    // Exact current-generation correspondence is the sole construction boundary.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    population: population as ContainedLeafPopulation,
  };
}

export function prepareContainedDocumentFragment(
  generation: string,
  template: HTMLTemplateElement,
  targets: ReadonlyArray<DocumentPresentationTarget>,
): PreparedContainedDocumentFragment {
  const copiedTargets = [...targets];
  const textTargets = new Set<Text>();
  const attributeTargets = new Map<Element, Set<string>>();
  const leaves: DocumentPresentationLeaf[] = [];
  for (const [identity, target] of copiedTargets.entries()) {
    if (target.kind === "text") {
      if (!template.content.contains(target.node)) {
        throw new TypeError(
          `Document presentation leaf ${identity} is outside its structure.`,
        );
      }
      if (textTargets.has(target.node)) {
        throw new TypeError(
          `Document presentation leaf ${identity} duplicates a text target.`,
        );
      }
      textTargets.add(target.node);
      leaves.push({
        identity,
        policy: target.policy,
        text: target.node.data,
      });
      continue;
    }

    if (!template.content.contains(target.element)) {
      throw new TypeError(
        `Document presentation leaf ${identity} is outside its structure.`,
      );
    }
    const text = target.element.getAttribute(target.name);
    if (text === null) {
      throw new TypeError(
        `Document presentation leaf ${identity} targets a missing attribute.`,
      );
    }
    let names = attributeTargets.get(target.element);
    if (!names) {
      names = new Set();
      attributeTargets.set(target.element, names);
    }
    if (names.has(target.name)) {
      throw new TypeError(
        `Document presentation leaf ${identity} duplicates an attribute target.`,
      );
    }
    names.add(target.name);
    leaves.push({
      identity,
      policy: target.policy,
      text,
    });
  }

  const leafContainment = prepareDocumentLeafContainment(generation, leaves);
  const prepared = Object.freeze({ generation });
  // This function is the sole construction boundary for a prepared fragment.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const branded = prepared as PreparedContainedDocumentFragment;
  preparedDocumentFragments.set(branded, {
    template,
    targets: Object.freeze(copiedTargets),
    leafContainment,
  });
  return branded;
}

export function browserDocumentFragmentPresentationLeaves(
  prepared: PreparedContainedDocumentFragment,
): ReadonlyArray<BrowserDocumentPresentationLeaf> {
  return preparedDocumentFragmentState(prepared).leafContainment.leaves;
}

export function completeContainedDocumentFragment(
  prepared: PreparedContainedDocumentFragment,
  currentGeneration: string | null,
  outcome: BrowserDocumentLeafContainmentOutcome,
): ContainedDocumentFragmentCompletion {
  const state = preparedDocumentFragmentState(prepared);
  const completion = completeDocumentLeafContainment(
    state.leafContainment,
    currentGeneration,
    outcome,
  );
  if (completion.kind === "rejected") return completion;

  for (const [index, leaf] of completion.population.leaves.entries()) {
    const target = state.targets[index];
    if (!target || leaf.identity !== index) {
      throw new TypeError(
        "A contained document fragment lost presentation-leaf correspondence.",
      );
    }
    if (target.kind === "text")
      target.node.data = leaf.text;
    else
      target.element.setAttribute(target.name, leaf.text);
  }

  const fragment = Object.freeze({ html: state.template.innerHTML });
  return {
    kind: "completed",
    // DOM replacement after exact completion is the sole publication boundary.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    fragment: fragment as ContainedDocumentFragment,
  };
}

export function containedDocumentFragmentHtml(
  fragment: ContainedDocumentFragment,
): string {
  return fragment.html;
}

function preparedDocumentFragmentState(
  prepared: PreparedContainedDocumentFragment,
): PreparedDocumentFragmentState {
  const state = preparedDocumentFragments.get(prepared);
  if (!state) {
    throw new TypeError(
      "The contained document fragment was not prepared by this owner.",
    );
  }
  return state;
}

function rejected(
  reason: DocumentLeafCompletionRejectionReason,
  identity: number | null = null,
  failure: BrowserDocumentLeafContainmentFailure | null = null,
): DocumentLeafCompletion {
  return {
    kind: "rejected",
    reason,
    identity,
    failure,
  };
}
