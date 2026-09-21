import { expect, test } from "@playwright/test";

test("browser traversal owns location across irreversible Workspace completion", async ({
  page,
}) => {
  await page.goto("/browser/navigation-location-intent.html");

  const result = await page.evaluate(async () => {
    const { createNavigationLocationIntentArbiter } =
      await import("../src/navigation-location-intent.ts");
    const arbiter = createNavigationLocationIntentArbiter();
    const workspaceA = {
      identity: Symbol("System.Text.Json A"),
      canonicalLocation: "/?package=System.Text.Json&version=8.0.5",
      historyState: { workspace: "System.Text.Json A" },
    };
    const workspaceB = {
      identity: Symbol("System.Text.Json B"),
      canonicalLocation: "/?package=System.Text.Json&version=9.0.4",
      historyState: { workspace: "System.Text.Json B" },
    };

    history.replaceState(
      workspaceA.historyState,
      "",
      workspaceA.canonicalLocation);
    history.pushState(
      workspaceB.historyState,
      "",
      workspaceB.canonicalLocation);
    const acceptedCutover =
      arbiter.admitNonBrowser("replace", workspaceA, null);
    const acceptedEffect = arbiter.classify(acceptedCutover, {
      outcome: "applied",
      synchronization: "current",
      association: workspaceB,
    });

    const selected = new Promise<void>(resolve =>
      window.addEventListener("popstate", () => resolve(), { once: true }));
    history.back();
    await selected;
    const traversal = arbiter.selectBrowserEntry({
      url: location.href,
      historyState: history.state,
      retainedDefinitionId: "System.Text.Json A",
      incumbent: workspaceB,
    });

    arbiter.publish(acceptedEffect, history);
    const traversalEffect = arbiter.classify(traversal, {
      outcome: "applied",
      synchronization: "current",
      association: workspaceA,
      browserRestoration: "exact",
    });
    arbiter.publish(traversalEffect, history);
    const selectedLocation = location.href;

    const forwarded = new Promise<void>(resolve =>
      window.addEventListener("popstate", () => resolve(), { once: true }));
    history.forward();
    await forwarded;

    return {
      classifiedCutoverEffect: acceptedEffect.kind,
      traversalEffect: traversalEffect.kind,
      selectedLocation,
      forwardLocation: location.href,
      forwardState: history.state as unknown,
    };
  });

  expect(result).toEqual({
    classifiedCutoverEffect: "replace",
    traversalEffect: "adopt",
    selectedLocation:
      "http://127.0.0.1:4175/?package=System.Text.Json&version=8.0.5",
    forwardLocation:
      "http://127.0.0.1:4175/?package=System.Text.Json&version=9.0.4",
    forwardState: { workspace: "System.Text.Json B" },
  });
});

test("post-cutover history failure keeps the installed successor", async ({
  page,
}) => {
  await page.goto("/browser/navigation-location-intent.html");

  const result = await page.evaluate(async () => {
    const { createNavigationLocationIntentArbiter } =
      await import("../src/navigation-location-intent.ts");
    const arbiter = createNavigationLocationIntentArbiter();
    const workspaceB = {
      identity: Symbol("System.Text.Json B"),
      canonicalLocation: "/?package=System.Text.Json&version=9.0.4",
      historyState: { workspace: "System.Text.Json B" },
    };
    const acceptedCutover =
      arbiter.admitNonBrowser("push", null, null);
    const effect = arbiter.classify(acceptedCutover, {
      outcome: "applied",
      synchronization: "current",
      association: workspaceB,
    });
    const originalPushState = history.pushState.bind(history);
    Object.defineProperty(history, "pushState", {
      configurable: true,
      value: () => {
        throw new DOMException("History blocked");
      },
    });

    let failure = "";
    try {
      arbiter.publish(effect, history);
    } catch (error) {
      failure = error instanceof Error ? error.message : String(error);
    }
    Object.defineProperty(history, "pushState", {
      configurable: true,
      value: originalPushState,
    });

    const next = arbiter.admitNonBrowser(
      "replace",
      workspaceB,
      history,
    );

    return {
      failure,
      installed: workspaceB.identity.description,
      location: location.href,
      state: history.state as unknown,
      nextAdmitted: arbiter.currentIntentId === next.id,
      unresolved: arbiter.unresolved !== null,
    };
  });

  expect(result).toEqual({
    failure: "History blocked",
    installed: "System.Text.Json B",
    location:
      "http://127.0.0.1:4175/?package=System.Text.Json&version=9.0.4",
    state: { workspace: "System.Text.Json B" },
    nextAdmitted: true,
    unresolved: false,
  });
});
