import assert from "node:assert/strict";
import test from "node:test";
import {
  renderMemberContractSections,
  type MemberContractModel,
} from "../src/member-overview.ts";

function model(
  overrides: Partial<MemberContractModel> = {},
): MemberContractModel {
  return {
    parameters: [],
    returnType: null,
    returns: null,
    exceptions: [],
    activeFramework: "net10.0",
    documentationStatus: "loaded",
    ...overrides,
  };
}

function parameter(name: string) {
  return {
    name,
    type: "System.Collections.Generic.IReadOnlyDictionary<string, TValue>",
    modifier: null,
    hasDefault: false,
    defaultValue: null,
    description: "The value to inspect.",
  };
}

test("member contract renders zero, one, and many parameter shapes", () => {
  assert.doesNotMatch(renderMemberContractSections(model()), /Parameters/);
  assert.match(
    renderMemberContractSections(model({ parameters: [parameter("value")] })),
    /<span>1 parameter<\/span>/);
  assert.match(
    renderMemberContractSections(model({
      parameters: [parameter("first"), parameter("second")],
    })),
    /<span>2 parameters<\/span>/);
});

test("member contract distinguishes parameter and exception documentation states", () => {
  const input = model({ parameters: [parameter("value")] });
  assert.match(
    renderMemberContractSections({
      ...input,
      documentationStatus: "loading",
    }),
    /Loading parameter documentation…[\s\S]*?Loading documented exceptions…/);
  assert.match(
    renderMemberContractSections({
      ...input,
      documentationStatus: "error",
    }),
    /Parameter documentation is unavailable\.[\s\S]*?Exception documentation is unavailable\./);
  assert.match(
    renderMemberContractSections({
      ...input,
      parameters: [{ ...parameter("value"), description: null }],
    }),
    /No parameter documentation was found[\s\S]*?No exceptions are documented/);
});

test("member contract does not invent a missing return type", () => {
  const html = renderMemberContractSections(model({
    returns: "A documented return value.",
  }));
  assert.match(
    html,
    /member-contract-identity-unavailable">Type unavailable<\/span>/);
  assert.doesNotMatch(html, /<code[^>]*>Return value<\/code>/);
});

test("member contract escapes producer-supplied identity and prose", () => {
  const html = renderMemberContractSections(model({
    parameters: [{
      ...parameter("<value>"),
      defaultValue: "\"<default>\"",
      hasDefault: true,
      description: "<description>",
    }],
    exceptions: [{
      type: "Example.<Failure>",
      description: "<condition>",
    }],
    activeFramework: "<net10.0>",
  }));
  assert.doesNotMatch(
    html,
    /<value>|<default>|<description>|<Failure>|<net10\.0>/);
  assert.match(html, /&lt;value&gt;/);
  assert.match(html, /Example\.&lt;Failure&gt;/);
});
