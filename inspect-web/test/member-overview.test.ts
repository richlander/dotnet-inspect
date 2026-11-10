import assert from "node:assert/strict";
import test from "node:test";
import {
  renderMemberContractSections,
  renderMemberDeclarations,
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

test("shared Member declarations render without resident API rows", () => {
  const html = renderMemberDeclarations({
    name: "GetProperty",
    kind: "method",
    typeName: "JsonElement",
    loading: false,
    error: "",
    inspection: {
      outcome: "Available",
      detail: null,
      diagnostics: [],
      document: {
        typeIdentity: "System.Text.Json.JsonElement",
        memberName: "GetProperty",
        count: 1,
        rows: [{
          metadataToken: 0x06000001,
          baselineOrdinal: 1,
          displaySignature: "JsonElement GetProperty(string propertyName)",
          accessibility: "public",
          receiver: "This",
        }],
      },
    },
    renderSignature: row => row.displaySignature,
  });
  assert.match(html, /1 exact declaration/);
  assert.match(html, /JsonElement GetProperty\(string propertyName\)/);
  assert.doesNotMatch(html, /detail unavailable|disabled|Loading/);
});

test("shared Member declarations distinguish loading from failed inspection", () => {
  const options = {
    name: "GetProperty",
    kind: "method",
    typeName: "JsonElement",
    inspection: null,
    loading: true,
    error: "",
    renderSignature: () => "",
  };
  assert.match(renderMemberDeclarations(options), /role="status"[\s\S]*Loading exact declarations/);
  const failed = renderMemberDeclarations({
    ...options, loading: false, error: "The retained Library was superseded.",
  });
  assert.match(failed, /role="alert"[\s\S]*retained Library was superseded/);
  assert.doesNotMatch(failed, /Loading exact declarations/);
});

test("available declarations do not hide a later rich-detail failure", () => {
  const html = renderMemberDeclarations({
    name: "GetProperty",
    kind: "method",
    typeName: "JsonElement",
    loading: false,
    error: "Member detail exceeded its metadata bound.",
    inspection: {
      outcome: "Available",
      detail: null,
      diagnostics: [],
      document: {
        typeIdentity: "System.Text.Json.JsonElement",
        memberName: "GetProperty",
        count: 1,
        rows: [{
          metadataToken: 0x06000001,
          baselineOrdinal: 1,
          displaySignature: "JsonElement GetProperty(string propertyName)",
          accessibility: "public",
          receiver: "This",
        }],
      },
    },
    renderSignature: row => row.displaySignature,
  });
  assert.match(html, /role="alert"[\s\S]*Member detail exceeded/);
  assert.match(html, /JsonElement GetProperty\(string propertyName\)/);
});
