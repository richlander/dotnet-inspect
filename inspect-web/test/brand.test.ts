import assert from "node:assert/strict";
import test from "node:test";
import { renderBrand } from "../src/brand.ts";

test("brand renders the ordered product navigation inventory", () => {
  const html = renderBrand({ id: "test-product" });

  assert.match(
    html,
    /id="test-product" class="brand" type="button"[\s\S]*aria-controls="test-product-menu"[\s\S]*aria-expanded="false" aria-haspopup="menu"/);
  assert.match(
    html,
    /data-product-destination="home">Home[\s\S]*data-product-destination="query">Query[\s\S]*data-product-destination="workspace">Workspace[\s\S]*data-product-destination="activity">Activity[\s\S]*class="product-navigation-separator" role="separator"[\s\S]*data-product-action="open-library">Open Library…/);
  assert.match(
    html,
    /id="test-product-menu" class="product-navigation-menu" role="menu"[\s\S]*hidden[\s\S]*role="menuitem" tabindex="-1"/);
  assert.doesNotMatch(
    html,
    /data-product-action="open-library"[^>]*aria-current/);
});
