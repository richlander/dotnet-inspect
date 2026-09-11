import assert from "node:assert/strict";
import test from "node:test";

import {
  clearFindingSelection,
  createMemberFindingInteraction,
  selectAnnotatedSourceFact,
  selectedAnnotatedSourceFactId,
  selectFindingInstance,
} from "../src/finding-interaction.ts";
import { memberFindingCensusFixture } from "./member-finding-census-fixture.ts";

test("display-identical Findings retain distinct bidirectional identity", () => {
  const interaction =
    createMemberFindingInteraction(memberFindingCensusFixture());

  const secondFact = selectFindingInstance(
    interaction,
    interaction.census.factCensusReceipt,
    42,
  );
  assert.equal(secondFact.accepted, true);
  assert.equal(secondFact.factId, 1);
  assert.equal(secondFact.interaction.selectedInstanceKey, 42);
  assert.equal(selectedAnnotatedSourceFactId(secondFact.interaction), 1);

  const firstFact = selectAnnotatedSourceFact(secondFact.interaction, 0);
  assert.equal(firstFact.accepted, true);
  assert.equal(firstFact.factId, 0);
  assert.equal(firstFact.interaction.selectedInstanceKey, 41);
});

test("wrong receipts and missing identities do not replace active selection", () => {
  const interaction = selectFindingInstance(
    createMemberFindingInteraction(memberFindingCensusFixture()),
    "11111111-1111-1111-1111-111111111111",
    41,
  ).interaction;

  const stale = selectFindingInstance(
    interaction,
    "22222222-2222-2222-2222-222222222222",
    42,
  );
  assert.equal(stale.accepted, false);
  assert.match(stale.error, /stale census/);
  assert.equal(stale.interaction.selectedInstanceKey, 41);

  const unknownKey = selectFindingInstance(
    interaction,
    interaction.census.factCensusReceipt,
    99,
  );
  assert.equal(unknownKey.accepted, false);
  assert.match(unknownKey.error, /not present/);
  assert.equal(unknownKey.interaction.selectedInstanceKey, 41);

  const unknownFact = selectAnnotatedSourceFact(interaction, 99);
  assert.equal(unknownFact.accepted, false);
  assert.match(unknownFact.error, /no Finding instance identity/);
  assert.equal(unknownFact.interaction.selectedInstanceKey, 41);
});

test("selection clears without changing the active census", () => {
  const selected = selectAnnotatedSourceFact(
    createMemberFindingInteraction(memberFindingCensusFixture()),
    0,
  ).interaction;
  const cleared = clearFindingSelection(selected);

  assert.equal(cleared.census, selected.census);
  assert.equal(cleared.selectedInstanceKey, null);
  assert.equal(selectedAnnotatedSourceFactId(cleared), null);
});

test("malformed sidecars and key sets are rejected without shape fallback", () => {
  const census = memberFindingCensusFixture();
  assert.throws(
    () => createMemberFindingInteraction({
      ...census,
      sourceFactInstances: [{ factId: 0, instanceKey: 41 }],
    }),
    /does not cover every Annotated Source body fact/,
  );
  assert.throws(
    () => createMemberFindingInteraction({
      ...census,
      sourceFactInstances: [
        { factId: 0, instanceKey: 41 },
        { factId: 1, instanceKey: 41 },
      ],
    }),
    /appears more than once/,
  );
  assert.throws(
    () => createMemberFindingInteraction({
      ...census,
      facts: census.facts.map(fact =>
        fact.instanceKey === 42 ? { ...fact, instanceKey: 43 } : fact),
    }),
    /do not carry the same Finding instance keys/,
  );
});
