// The dock's chrome in a real browser (P1T-163). Three questions the unit suite structurally
// cannot answer, and each of them is the one the ticket's "done when" is actually about:
//
//   * does the resize handle *render* its affordance — a `::after` pseudo-element has no DOM node,
//     so jsdom has nothing to hand back and an `sx` block that emits it is not evidence it paints.
//     This is slice 1's focus ring exactly: perfect CSS, nothing on screen.
//   * does the header survive `DOCK_MIN_WIDTH` without wrapping or clipping — layout, which jsdom
//     does not implement at all.
//   * does the keyboard path move the real panel, not just call a spy.
//
// The e2e stack runs no Agents service (`run.mjs` starts the database, the API and the SPA), so
// nothing here asks an agent anything: every assertion is about the panel.
//
// The dock's *print* behaviour is deliberately not re-asserted here. It arrived in P1T-166
// (`print.e2e.ts`, `AgentWidget.print.test.tsx`) and that branch is still in review, so this one
// left all three `sx` sites it touches — the Fab, the panel root, and the `hideInPrint` constant
// between them — exactly as they are on `main`, and the chrome work went around them. Whichever of
// the two lands second keeps both rules; nothing here competes for those lines.
import { expect, test } from "@playwright/test";
import type { BrowserContext, Locator, Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

/** `DOCK_MIN_WIDTH` from `src/components/useAgentDock.ts`. */
const MIN_WIDTH = 360;

async function signInAndDock(context: BrowserContext, page: Page) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/signin");
  await addVirtualAuthenticator(context, page);
  await signUp(page);

  await page.getByRole("button", { name: "Open the agents assistant" }).click();
  const dockIt = page.getByRole("button", { name: "Dock to side" });
  if (await dockIt.count()) await dockIt.click();
  await expect(page.getByRole("button", { name: "Float" })).toBeVisible();
}

/** What the dock publishes as covered — the one number both edges of the shell agree on. */
function dockPush(page: Page) {
  return page.evaluate(() =>
    Number.parseFloat(
      getComputedStyle(document.documentElement).getPropertyValue("--agent-dock-push"),
    ));
}

/** A pseudo-element's computed value. The only way to see an affordance that has no DOM node. */
function gripStyle(handle: Locator, property: string) {
  return handle.evaluate(
    (el, p) => getComputedStyle(el, "::after").getPropertyValue(p),
    property,
  );
}

test.describe("the agent dock's chrome", () => {
  test("the resize handle draws a grip at rest and lights it on keyboard focus", async ({
    context,
    page,
  }) => {
    await signInAndDock(context, page);
    const handle = page.getByRole("separator", { name: "Resize the agents dock" });
    await expect(handle).toBeVisible();

    // At rest it is *there*: the old strip declared nothing but `cursor: col-resize`, so a person
    // who does not hover had no way to know the dock resizes at all.
    const restHeight = Number.parseFloat(await gripStyle(handle, "height"));
    const restColour = await gripStyle(handle, "background-color");
    expect(restHeight).toBeGreaterThan(0);
    expect(restColour).not.toBe("rgba(0, 0, 0, 0)");

    // Reached by an actual keyboard move, not by `element.focus()`. `:focus-visible` is modality
    // dependent: Chrome does not match it for a programmatic focus when the last thing the user
    // did was click, so the first run of this test read a grip that had not lit up — and the app
    // would have behaved identically for a mouse user tabbing in. Shift+Tab from the control that
    // follows it in DOM order is also the assertion the ticket actually wants: the handle is *in*
    // the tab order, which is what "discoverable without a mouse hover" means.
    await page.getByRole("button", { name: "Token usage" }).focus();
    await page.keyboard.press("Shift+Tab");
    await expect(handle).toBeFocused();

    // Polled, not read instantly: the grip transitions over the theme's 150ms ceiling, and an
    // instant read returns an interpolation of exactly the two values being compared (P1T-164).
    await expect.poll(async () => Number.parseFloat(await gripStyle(handle, "height"))).toBeGreaterThan(restHeight);
    expect(await gripStyle(handle, "background-color")).not.toBe(restColour);

    // And the app's own focus ring reaches it, which is what makes it findable without a mouse.
    expect(await handle.evaluate((el) => getComputedStyle(el).outlineWidth)).toBe("2px");
  });

  test("the keyboard moves the real edge, and the hook's clamp still holds it", async ({
    context,
    page,
  }) => {
    await signInAndDock(context, page);
    const handle = page.getByRole("separator", { name: "Resize the agents dock" });
    await handle.focus();

    const before = await dockPush(page);
    await page.keyboard.press("ArrowLeft");
    await expect.poll(() => dockPush(page)).toBeGreaterThan(before);

    // Home asks for the minimum; End asks for half the viewport, which the hook clamps.
    await page.keyboard.press("Home");
    await expect.poll(() => dockPush(page)).toBe(MIN_WIDTH);
    await expect(handle).toHaveAttribute("aria-valuenow", String(MIN_WIDTH));

    await page.keyboard.press("End");
    await expect.poll(() => dockPush(page)).toBe(1440 / 2);

    // Right shrinks it back, because the key moves the handle rather than the width.
    await page.keyboard.press("Home");
    await expect.poll(() => dockPush(page)).toBe(MIN_WIDTH);
    await page.keyboard.press("ArrowRight");
    await expect.poll(() => dockPush(page)).toBe(MIN_WIDTH);
  });

  test("the header stays one row at the dock's minimum width, and nothing clips", async ({
    context,
    page,
  }) => {
    await signInAndDock(context, page);
    await page.getByRole("separator", { name: "Resize the agents dock" }).focus();
    await page.keyboard.press("Home");
    await expect.poll(() => dockPush(page)).toBe(MIN_WIDTH);

    const panel = page.locator(".MuiPaper-root", { hasText: "Agents" }).first();
    const title = page.getByText("Agents", { exact: true });
    const controls = [
      page.getByRole("button", { name: "Token usage" }),
      page.getByRole("button", { name: "Float" }),
      page.getByRole("button", { name: "Close the agents assistant" }),
    ];

    const titleBox = (await title.boundingBox())!;
    const panelBox = (await panel.boundingBox())!;

    for (const control of controls) {
      const box = (await control.boundingBox())!;
      // One row: every control's vertical centre is on the title's line. If the bar had wrapped,
      // one of them would be a row below — the failure this whole assertion exists for.
      expect(Math.abs(box.y + box.height / 2 - (titleBox.y + titleBox.height / 2))).toBeLessThan(4);
      // And inside the panel: a control pushed past the right edge is clipped, not wrapped.
      expect(box.x + box.width).toBeLessThanOrEqual(panelBox.x + panelBox.width + 1);
      expect(box.x).toBeGreaterThanOrEqual(panelBox.x - 1);
    }

    // The picker under them is the full width of the bar and still names where it points.
    const picker = page.getByRole("button", { name: /^Agent surface: / });
    const pickerBox = (await picker.boundingBox())!;
    expect(pickerBox.y).toBeGreaterThan(titleBox.y);
    expect(pickerBox.width).toBeGreaterThan(panelBox.width * 0.8);
  });
});
