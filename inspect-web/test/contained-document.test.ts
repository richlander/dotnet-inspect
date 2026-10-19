import assert from "node:assert/strict";
import test from "node:test";

import {
  browserDocumentFragmentPresentationLeaves,
  browserDocumentPresentationLeaves,
  completeContainedDocumentFragment,
  completeDocumentLeafContainment,
  containedDocumentFragmentHtml,
  prepareContainedDocumentFragment,
  prepareDocumentLeafContainment,
  type DocumentPresentationTarget,
} from "../src/contained-document.ts";
import type {
  BrowserDocumentLeafContainmentFailureKind,
  BrowserDocumentLeafContainmentOutcome,
  InertString,
} from "../src/facades/inspect-web-package.d.ts";

function isInertString(value: unknown): value is InertString {
  return typeof value === "string";
}

function inertString(value: string): InertString {
  const wire: unknown = JSON.parse(JSON.stringify(value));
  if (!isInertString(wire)) {
    throw new TypeError("The inert-string fixture must remain a JSON string.");
  }
  return wire;
}

function contained(
  generation = "document-4",
): BrowserDocumentLeafContainmentOutcome {
  return {
    outcome: "contained",
    generation,
    leaves: [
      { identity: 1, policy: "Field", text: inertString("field\\^Jvalue") },
      { identity: 2, policy: "Prose", text: inertString("line\\u202Eone") },
    ],
  };
}

function rejected(
  kind: BrowserDocumentLeafContainmentFailureKind,
): BrowserDocumentLeafContainmentOutcome {
  return {
    outcome: "rejected",
    generation: "document-4",
    failure: {
      kind,
      identity: null,
      actual: 20,
      limit: 10,
    },
  };
}

function prepared() {
  return prepareDocumentLeafContainment(
    "document-4",
    [
      { identity: 2, policy: "Prose", text: "line\u202Eone" },
      { identity: 1, policy: "Field", text: "field\nvalue" },
    ],
  );
}

function rejectionReason(
  completion: ReturnType<typeof completeDocumentLeafContainment>,
) {
  assert.equal(completion.kind, "rejected");
  if (completion.kind !== "rejected") {
    throw new Error("Expected leaf completion to reject.");
  }
  return completion.reason;
}

class FakeText {
  data: string;

  constructor(data: string) {
    this.data = data;
  }
}

class FakeElement {
  private readonly attributes = new Map<string, string>();

  constructor(attributes: Readonly<Record<string, string>>) {
    for (const [name, value] of Object.entries(attributes))
      this.attributes.set(name, value);
  }

  getAttribute(name: string) {
    return this.attributes.get(name) ?? null;
  }

  setAttribute(name: string, value: string) {
    this.attributes.set(name, value);
  }
}

function fragmentFixture() {
  const text = new FakeText("line\u202Eone");
  const title = new FakeElement({
    title: "field\nvalue",
    href: "https://example.test/exact-target",
  });
  const members = new Set<object>([text, title]);
  const template = {
    content: {
      contains: (value: object) => members.has(value),
    },
    get innerHTML() {
      return `<p title="${title.getAttribute("title")}">${text.data}</p>`;
    },
  };
  // These fixtures implement only the DOM operations used by the owner.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const typedTemplate = template as unknown as HTMLTemplateElement;
  const targets: DocumentPresentationTarget[] = [
    {
      kind: "text",
      // oxlint-disable-next-line typescript/no-unsafe-type-assertion
      node: text as unknown as Text,
      policy: "Prose",
    },
    {
      kind: "attribute",
      // oxlint-disable-next-line typescript/no-unsafe-type-assertion
      element: title as unknown as Element,
      name: "title",
      policy: "Field",
    },
  ];
  return { template: typedTemplate, targets, text, title };
}

test("leaf completion restores request order after exact correspondence", () => {
  const request = prepared();
  assert.deepEqual(
    browserDocumentPresentationLeaves(request),
    [
      { identity: 2, policy: "Prose", text: "line\u202Eone" },
      { identity: 1, policy: "Field", text: "field\nvalue" },
    ],
  );

  const completion = completeDocumentLeafContainment(
    request,
    "document-4",
    contained(),
  );

  assert.equal(completion.kind, "completed");
  if (completion.kind !== "completed") return;
  assert.equal(completion.population.generation, "document-4");
  assert.deepEqual(
    completion.population.leaves,
    [
      { identity: 2, policy: "Prose", text: "line\\u202Eone" },
      { identity: 1, policy: "Field", text: "field\\^Jvalue" },
    ],
  );
});

test("preparation rejects invalid or duplicate identities", () => {
  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [{ identity: -1, policy: "Field", text: "value" }],
    ),
    /non-negative 32-bit integer/,
  );
  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [
        { identity: 1, policy: "Field", text: "first" },
        { identity: 1, policy: "Prose", text: "second" },
      ],
    ),
    /duplicated/,
  );
});

test("preparation closes identity to the managed 32-bit range", () => {
  const maximum = prepareDocumentLeafContainment(
    "document-4",
    [{
      identity: 2_147_483_647,
      policy: "Field",
      text: "maximum",
    }],
  );
  assert.equal(maximum.leaves[0]?.identity, 2_147_483_647);

  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [{
        identity: 2_147_483_648,
        policy: "Field",
        text: "overflow",
      }],
    ),
    /non-negative 32-bit integer/,
  );
});

test("preparation rejects an invalid generation or policy", () => {
  assert.throws(
    () => prepareDocumentLeafContainment(" ", []),
    /must not be empty/,
  );
  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [
        {
          identity: 1,
          // @ts-expect-error Runtime validation closes the generated numeric enum.
          policy: "Code",
          text: "value",
        },
      ],
    ),
    /unsupported text policy/,
  );
});

test("completion rejects a closed or replaced generation", () => {
  assert.deepEqual(
    completeDocumentLeafContainment(prepared(), null, contained()),
    {
      kind: "rejected",
      reason: "stale-generation",
      identity: null,
      failure: null,
    },
  );
  assert.deepEqual(
    completeDocumentLeafContainment(
      prepared(),
      "document-5",
      contained(),
    ),
    {
      kind: "rejected",
      reason: "stale-generation",
      identity: null,
      failure: null,
    },
  );
});

test("completion rejects a result from another generation", () => {
  assert.deepEqual(
    completeDocumentLeafContainment(
      prepared(),
      "document-4",
      contained("document-3"),
    ),
    {
      kind: "rejected",
      reason: "wrong-generation",
      identity: null,
      failure: null,
    },
  );
});

test("completion preserves a managed rejection", () => {
  const outcome = rejected("OutputTextTooLarge");
  if (outcome.outcome !== "rejected") return;
  const completion = completeDocumentLeafContainment(
    prepared(),
    "document-4",
    outcome,
  );

  assert.equal(completion.kind, "rejected");
  if (completion.kind !== "rejected") return;
  assert.equal(completion.reason, "managed-rejection");
  assert.deepEqual(completion.failure, outcome.failure);
});

test("completion rejects duplicate, surplus, and missing leaves", () => {
  const duplicate = contained();
  if (duplicate.outcome !== "contained") return;
  assert.equal(
    rejectionReason(completeDocumentLeafContainment(
      prepared(),
      "document-4",
      { ...duplicate, leaves: [duplicate.leaves[0]!, duplicate.leaves[0]!] },
    )),
    "duplicate-leaf",
  );

  assert.equal(
    rejectionReason(completeDocumentLeafContainment(
      prepared(),
      "document-4",
      {
        ...duplicate,
        leaves: [
          ...duplicate.leaves,
          { identity: 3, policy: "Field", text: inertString("surplus") },
        ],
      },
    )),
    "surplus-leaf",
  );

  assert.equal(
    rejectionReason(completeDocumentLeafContainment(
      prepared(),
      "document-4",
      { ...duplicate, leaves: [duplicate.leaves[0]!] },
    )),
    "missing-leaf",
  );
});

test("completion rejects a policy mismatch", () => {
  const outcome = contained();
  if (outcome.outcome !== "contained") return;

  assert.deepEqual(
    completeDocumentLeafContainment(
      prepared(),
      "document-4",
      {
        ...outcome,
        leaves: [
          { ...outcome.leaves[0]!, policy: "Prose" },
          outcome.leaves[1]!,
        ],
      },
    ),
    {
      kind: "rejected",
      reason: "policy-mismatch",
      identity: 1,
      failure: null,
    },
  );
});

test("fragment completion applies exact leaves only after correspondence", () => {
  const fixture = fragmentFixture();
  const fragmentPrepared = prepareContainedDocumentFragment(
    "document-5",
    fixture.template,
    fixture.targets,
  );

  assert.deepEqual(
    browserDocumentFragmentPresentationLeaves(fragmentPrepared),
    [
      { identity: 0, policy: "Prose", text: "line\u202Eone" },
      { identity: 1, policy: "Field", text: "field\nvalue" },
    ],
  );
  assert.equal(fixture.text.data, "line\u202Eone");
  assert.equal(fixture.title.getAttribute("title"), "field\nvalue");

  const completion = completeContainedDocumentFragment(
    fragmentPrepared,
    "document-5",
    {
      outcome: "contained",
      generation: "document-5",
      leaves: [
        { identity: 1, policy: "Field", text: inertString("field\\^Jvalue") },
        { identity: 0, policy: "Prose", text: inertString("line\\u202Eone") },
      ],
    },
  );

  assert.equal(completion.kind, "completed");
  if (completion.kind !== "completed") return;
  assert.equal(fixture.text.data, "line\\u202Eone");
  assert.equal(fixture.title.getAttribute("title"), "field\\^Jvalue");
  assert.equal(
    fixture.title.getAttribute("href"),
    "https://example.test/exact-target",
  );
  assert.equal(
    containedDocumentFragmentHtml(completion.fragment),
    '<p title="field\\^Jvalue">line\\u202Eone</p>',
  );
});

test("fragment rejection does not mutate prepared structure", () => {
  const fixture = fragmentFixture();
  const fragmentPrepared = prepareContainedDocumentFragment(
    "document-5",
    fixture.template,
    fixture.targets,
  );
  const completion = completeContainedDocumentFragment(
    fragmentPrepared,
    null,
    {
      outcome: "contained",
      generation: "document-5",
      leaves: [
        { identity: 0, policy: "Prose", text: inertString("changed") },
        { identity: 1, policy: "Field", text: inertString("changed") },
      ],
    },
  );

  assert.deepEqual(completion, {
    kind: "rejected",
    reason: "stale-generation",
    identity: null,
    failure: null,
  });
  assert.equal(fixture.text.data, "line\u202Eone");
  assert.equal(fixture.title.getAttribute("title"), "field\nvalue");
});

test("fragment preparation rejects outside, missing, or duplicate targets", () => {
  const fixture = fragmentFixture();
  const outside = new FakeText("outside");
  assert.throws(
    () => prepareContainedDocumentFragment(
      "document-5",
      fixture.template,
      [{
        kind: "text",
        // oxlint-disable-next-line typescript/no-unsafe-type-assertion
        node: outside as unknown as Text,
        policy: "Prose",
      }],
    ),
    /outside its structure/,
  );
  assert.throws(
    () => prepareContainedDocumentFragment(
      "document-5",
      fixture.template,
      [fixture.targets[0]!, fixture.targets[0]!],
    ),
    /duplicates a text target/,
  );
  assert.throws(
    () => prepareContainedDocumentFragment(
      "document-5",
      fixture.template,
      [{
        kind: "attribute",
        element: fixture.targets[1]!.kind === "attribute"
          ? fixture.targets[1]!.element
          : assert.fail("Expected an attribute target"),
        name: "missing",
        policy: "Field",
      }],
    ),
    /missing attribute/,
  );
});
