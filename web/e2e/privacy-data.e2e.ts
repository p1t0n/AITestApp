import { expect, test } from "@playwright/test";
import { addVirtualAuthenticator, signUpAsUser } from "./passkey";

/**
 * A fresh address on a domain that is <b>not</b> RFC 2606 reserved.
 *
 * The shared `uniqueEmail` helper uses `@example.com`, and retention treats a reserved domain as
 * fabricated data that no clock applies to (P1T-188) — correctly, since no real person has an
 * address there, and without it the demo roster would evaporate. But it means a person created by
 * the shared helper sees "this is sample data rather than a record about a person" where their
 * expiry date should be, so this suite would be asserting the exemption rather than the feature.
 */
function realPersonEmail(): string {
  return `p1t191-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@lovelace.dev`;
}

/**
 * The two providers the stack under test is configured with — `e2e/run.mjs` sets the same values on
 * the API and hands them here, so this asserts what the host was told rather than names that were
 * true when the spec was written (EXP-21, EXP-66). Chat and embeddings move on separate keys, so
 * the page names one recipient when one provider does both jobs and two when they differ.
 *
 * `run.mjs` always sets both variables, so the fallbacks only matter when Playwright is driven at a
 * stack somebody started by hand. Each names Gemini as the exception rather than the default
 * (EXP-61) — a fallback that assumes the provider the shipped settings moved away from is how this
 * spec would go on passing while the page named the wrong company.
 */
const COMPANY = {
  chat: process.env.E2E_CHAT_PROVIDER === "Gemini" ? "Google (Gemini)" : "Microsoft (Azure OpenAI)",
  embeddings:
    process.env.E2E_EMBEDDINGS_PROVIDER === "AzureFoundry"
      ? "Microsoft (Azure OpenAI)"
      : "Google (Gemini)",
};

/**
 * What the recipient list has to read as. One company doing both jobs is a single "AI model
 * provider" entry covering embeddings and scoring; two companies are two entries, each named by the
 * job it does. Escaped for `RegExp`, because every one of these names carries brackets.
 */
const RECIPIENTS =
  COMPANY.chat === COMPANY.embeddings
    ? [`${COMPANY.chat}, as our AI model provider`]
    : [
        `${COMPANY.embeddings}, as our embeddings provider`,
        `${COMPANY.chat}, as our AI model provider`,
      ];

const asPattern = (text: string) =>
  new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));

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
    await signUpAsUser(page, realPersonEmail());

    await page.getByRole("link", { name: "Privacy & data" }).click();

    await expect(page.getByRole("heading", { name: "Privacy and data" })).toBeVisible();
    await expect(page.getByText(/Your record is active and can be offered for work/)).toBeVisible();
    // The disclosure that is new information rather than a restatement (P1T-187), and the one
    // part of this page that can be a false statement if a provider moves under it.
    for (const entry of RECIPIENTS) {
      await expect(page.getByText(asPattern(entry))).toBeVisible();
    }
    await expect(page.getByTestId("row-How long we keep it")).toContainText(/due to be deleted on/);
  });

  test("the export downloads as a file, labelled a right", async ({ context, page }) => {
    await addVirtualAuthenticator(context, page);
    await signUpAsUser(page, realPersonEmail());
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
    await signUpAsUser(page, realPersonEmail());
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
    await signUpAsUser(page, realPersonEmail());
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
    await signUpAsUser(page, realPersonEmail());
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
