import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUpAsExpert } from "./passkey";

/**
 * The privacy page at the real surface (P1T-191). Three of its rights cannot be shown by a unit
 * suite at all: a download is a browser event, pausing round-trips through the API and back into
 * the page's own prose, and deleting ends the session on both hosts — which is only observable by
 * being signed out afterwards.
 *
 * <p>A self-serve signup matches nothing, so the person owns their record immediately and is on
 * 6(1)(b): the export reads as a right, and there is no <em>Object</em> row. The legitimate-interest
 * combination is covered by the unit suite, which can put the page in that state without arranging
 * a Service Manager to create the record first.</p>
 */
test.describe("privacy and data", () => {
  test("the page states what is held, who sees it, and how long", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);

    await page.getByRole("link", { name: "Privacy & data" }).click();

    await expect(page.getByRole("heading", { name: "Privacy and data" })).toBeVisible();
    await expect(page.getByText(/Your record is active and can be offered for work/)).toBeVisible();
    // The disclosure that is new information rather than a restatement (P1T-187).
    await expect(page.getByText(/Google \(Gemini\)/)).toBeVisible();
    await expect(page.getByTestId("row-How long we keep it")).toContainText(/due to be deleted on/);
  });

  test("the export downloads as a file, labelled a right", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);
    await page.goto("/me/privacy");

    await expect(page.getByText(/right to data portability/)).toBeVisible();

    const download = page.waitForEvent("download");
    await page.getByRole("button", { name: "Download JSON" }).click();
    const file = await download;

    expect(file.suggestedFilename()).toMatch(/experttojob-export.*\.json/);
  });

  /**
   * Pausing and resuming, read back off the page's own sentence rather than off a chip — which is
   * the property Variant A was chosen for: one source of truth about state, so there is nothing
   * that can disagree with it.
   */
  test("pausing and resuming changes the sentence at the top", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);
    await page.goto("/me/privacy");

    await page.getByRole("button", { name: "Pause" }).click();
    await expect(page.getByText(/Your record is paused/)).toBeVisible();
    await expect(page.getByText(/is active and can be offered/)).toHaveCount(0);

    await page.getByRole("button", { name: "Resume" }).click();
    await expect(page.getByText(/Your record is active and can be offered for work/)).toBeVisible();
  });

  /**
   * The delete path, and the two things about it that matter: the control word is required, and a
   * wrong one changes nothing. Both are the server's rules — the page only carries them.
   */
  test("deleting refuses a wrong control word and keeps the session", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);
    await page.goto("/me/privacy");

    const remove = page.getByRole("button", { name: "Delete everything" });
    await expect(remove).toBeDisabled();

    await page.getByLabel("Your control word").fill("not the control word");
    await remove.click();

    await expect(page.getByText(/control word is not right/)).toBeVisible();
    await expect(page).toHaveURL(/\/me\/privacy$/);
  });

  test("deleting with the right control word ends the session", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsExpert(page);
    await page.goto("/me/privacy");

    // The word every e2e signup uses (see `signUpThroughTheForm`).
    await page.getByLabel("Your control word").fill("correct horse battery staple");
    await page.getByRole("button", { name: "Delete everything" }).click();

    // Signed out, on the gate — the account both hosts re-read every request is gone.
    await page.waitForURL(/\/signin$/, { timeout: 30_000 });
    await expect(page.getByRole("heading", { name: /sign in/i })).toBeVisible();

    // And the record with it: the roster no longer has them.
    await page.goto("/me/privacy");
    await expect(page).toHaveURL(/\/signin$/);
  });
});
