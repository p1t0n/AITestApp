import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type { ApplyRewriteInput, CvTailoringResponse } from "../api";

// ---- api module mock ----
// Only the hooks are mocked; apiErrorMessage stays real so error shapes go through the same
// extraction path production uses (mirrors AgentWidget.shortlist.test.tsx).

const tailoringState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<CvTailoringResponse>>(),
  isPending: false,
};

// Stateful per-card fake for useApplyRewrite: each card mounts its own hook instance, so the fake
// keeps real per-instance state (pending/success/error) and records every mutate() input. Tests
// steer outcomes through applyImpl (resolve = success, reject = error, never-settle = pending).
const applyCalls: ApplyRewriteInput[] = [];
let applyImpl: (input: ApplyRewriteInput) => Promise<unknown> = () => Promise.resolve({});

const EMPLOYEE_ID = "11111111-2222-3333-4444-555555555555";
const EXPERIENCE_A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const EXPERIENCE_B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const { useState } = await import("react");
  return {
    ...actual,
    useCvTailoring: () => tailoringState,
    useApplyRewrite: () => {
      const [state, setState] = useState<{
        status: "idle" | "pending" | "success" | "error";
        error: unknown;
      }>({ status: "idle", error: null });
      return {
        isPending: state.status === "pending",
        isSuccess: state.status === "success",
        isError: state.status === "error",
        error: state.error,
        mutate: (input: ApplyRewriteInput) => {
          applyCalls.push(input);
          setState({ status: "pending", error: null });
          applyImpl(input).then(
            () => setState({ status: "success", error: null }),
            (error: unknown) => setState({ status: "error", error }),
          );
        },
      };
    },
    useEmployees: () => ({
      data: [
        {
          id: EMPLOYEE_ID,
          firstName: "Ada",
          lastName: "Lovelace",
          title: "Senior Engineer",
          location: null,
          email: "ada@example.com",
          currentCapacityPercent: 100,
  status: "Active",
        },
      ],
      isLoading: false,
    }),
    useSkills: () => ({ data: [], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useJdMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const ANSWER = "## Tailored CV\n\nA **tailored** summary of Ada's experience.";

const LONG_TEXT =
  "Shippedaverylongunbrokenachievementtokenthatwouldotherwiseoverflowthecardhorizontally" +
  "becauseithasnospacesatallanywhereinsideit";

const RESPONSE: CvTailoringResponse = {
  answer: ANSWER,
  rewrites: [
    {
      experienceId: EXPERIENCE_A,
      achievementId: "a1a1a1a1-1111-1111-1111-111111111111",
      original: "Worked on React apps",
      rewritten: "Delivered three production React/TypeScript apps serving 40k users",
    },
    {
      experienceId: EXPERIENCE_A,
      achievementId: "a2a2a2a2-2222-2222-2222-222222222222",
      original: "Did some performance work",
      rewritten: "Cut page-load time 45% by code-splitting and memoizing hot paths",
    },
    {
      experienceId: EXPERIENCE_B,
      achievementId: "b1b1b1b1-3333-3333-3333-333333333333",
      original: LONG_TEXT,
      rewritten: LONG_TEXT + " rewritten",
    },
  ],
};

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 420,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

async function runTailoring(response: CvTailoringResponse) {
  tailoringState.mutateAsync.mockResolvedValue(response);
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} isNarrow={false} />
    </MemoryRouter>,
  );
  await user.click(screen.getByRole("tab", { name: "Tailor CV" }));
  await user.click(screen.getByLabelText("Employee"));
  await user.click(await screen.findByRole("option", { name: "Ada Lovelace — Senior Engineer" }));
  await user.type(
    screen.getByPlaceholderText(/paste a job description/i),
    "Senior React engineer",
  );
  await user.click(screen.getByRole("button", { name: "Tailor CV" }));
  await screen.findByRole("heading", { name: "Tailored CV" });
  return user;
}

beforeEach(() => {
  tailoringState.mutateAsync = vi.fn();
  tailoringState.isPending = false;
  applyCalls.length = 0;
  applyImpl = () => Promise.resolve({});
});

describe("Tailor CV tab — rewritten bullets", () => {
  it("renders the section with before/after blocks grouped by experienceId", async () => {
    await runTailoring(RESPONSE);

    expect(screen.getByText("Rewritten bullets")).toBeInTheDocument();

    // Two groups, one per distinct experienceId, in response order with neutral headers.
    const groupA = screen.getByTestId(`rewrite-group-${EXPERIENCE_A}`);
    const groupB = screen.getByTestId(`rewrite-group-${EXPERIENCE_B}`);
    expect(within(groupA).getByText("Experience 1")).toBeInTheDocument();
    expect(within(groupB).getByText("Experience 2")).toBeInTheDocument();

    // Group A holds both of its bullets, before and after.
    expect(within(groupA).getByText("Worked on React apps")).toBeInTheDocument();
    expect(
      within(groupA).getByText(
        "Delivered three production React/TypeScript apps serving 40k users",
      ),
    ).toBeInTheDocument();
    expect(within(groupA).getByText("Did some performance work")).toBeInTheDocument();
    expect(
      within(groupA).getByText("Cut page-load time 45% by code-splitting and memoizing hot paths"),
    ).toBeInTheDocument();
    expect(within(groupA).getAllByText("Before")).toHaveLength(2);
    expect(within(groupA).getAllByText("After")).toHaveLength(2);

    // Group B holds only its own bullet.
    expect(within(groupB).getByText(LONG_TEXT)).toBeInTheDocument();
    expect(within(groupB).queryByText("Worked on React apps")).not.toBeInTheDocument();
  });

  it("copies the rewritten text (not the original) per bullet", async () => {
    const user = await runTailoring(RESPONSE);
    const writeText = vi.spyOn(navigator.clipboard, "writeText");

    const groupA = screen.getByTestId(`rewrite-group-${EXPERIENCE_A}`);
    const copyButtons = within(groupA).getAllByRole("button", {
      name: "Copy rewritten bullet",
    });
    expect(copyButtons).toHaveLength(2);

    await user.click(copyButtons[1]);
    expect(writeText).toHaveBeenCalledWith(
      "Cut page-load time 45% by code-splitting and memoizing hot paths",
    );
  });

  it("hides the section entirely when rewrites are empty (degrade path)", async () => {
    await runTailoring({ answer: ANSWER, rewrites: [] });

    expect(screen.queryByText("Rewritten bullets")).not.toBeInTheDocument();
    expect(screen.queryByTestId(/rewrite-group-/)).not.toBeInTheDocument();
  });

  it("keeps the markdown answer pane unchanged in both cases", async () => {
    await runTailoring(RESPONSE);
    expect(screen.getByRole("heading", { name: "Tailored CV" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copy answer" })).toBeInTheDocument();
  });

  it("keeps the markdown answer pane when rewrites are empty", async () => {
    await runTailoring({ answer: ANSWER, rewrites: [] });
    expect(screen.getByRole("heading", { name: "Tailored CV" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copy answer" })).toBeInTheDocument();
  });

  it("wraps long unbroken text instead of overflowing horizontally", async () => {
    await runTailoring(RESPONSE);

    const groupB = screen.getByTestId(`rewrite-group-${EXPERIENCE_B}`);
    for (const el of [
      within(groupB).getByText(LONG_TEXT),
      within(groupB).getByText(LONG_TEXT + " rewritten"),
    ]) {
      expect(getComputedStyle(el).overflowWrap).toBe("anywhere");
    }
  });
});

describe("Tailor CV tab — apply flow", () => {
  const CARD_1 = RESPONSE.rewrites[0]; // experience A, bullet 1
  const CARD_2 = RESPONSE.rewrites[1]; // experience A, bullet 2
  const CARD_3 = RESPONSE.rewrites[2]; // experience B

  function card(achievementId: string) {
    return screen.getByTestId(`rewrite-card-${achievementId}`);
  }

  it("applies with the selected employee, the rewrite's ids, and text = rewritten", async () => {
    const user = await runTailoring(RESPONSE);

    await user.click(within(card(CARD_2.achievementId)).getByRole("button", { name: "Apply" }));

    expect(applyCalls).toEqual([
      {
        employeeId: EMPLOYEE_ID,
        experienceId: EXPERIENCE_A,
        achievementId: CARD_2.achievementId,
        original: CARD_2.original,
        rewritten: CARD_2.rewritten,
      },
    ]);
  });

  it("disables the button while the apply is in flight", async () => {
    applyImpl = () => new Promise(() => {}); // never settles
    const user = await runTailoring(RESPONSE);

    await user.click(within(card(CARD_1.achievementId)).getByRole("button", { name: "Apply" }));

    expect(
      within(card(CARD_1.achievementId)).getByRole("button", { name: /applying/i }),
    ).toBeDisabled();
  });

  it("flips to an Applied indicator on success and removes the button", async () => {
    const user = await runTailoring(RESPONSE);

    await user.click(within(card(CARD_1.achievementId)).getByRole("button", { name: "Apply" }));

    expect(await within(card(CARD_1.achievementId)).findByText("Applied")).toBeInTheDocument();
    expect(
      within(card(CARD_1.achievementId)).queryByRole("button", { name: /apply/i }),
    ).not.toBeInTheDocument();
  });

  it("shows the error and re-enables the button on failure", async () => {
    applyImpl = () => Promise.reject(new Error("The bullet no longer exists."));
    const user = await runTailoring(RESPONSE);

    await user.click(within(card(CARD_1.achievementId)).getByRole("button", { name: "Apply" }));

    expect(
      await within(card(CARD_1.achievementId)).findByText("The bullet no longer exists."),
    ).toBeInTheDocument();
    expect(
      within(card(CARD_1.achievementId)).getByRole("button", { name: "Apply" }),
    ).toBeEnabled();
    expect(within(card(CARD_1.achievementId)).queryByText("Applied")).not.toBeInTheDocument();
  });

  it("keeps sibling cards untouched when one rewrite is applied", async () => {
    const user = await runTailoring(RESPONSE);

    await user.click(within(card(CARD_1.achievementId)).getByRole("button", { name: "Apply" }));
    await within(card(CARD_1.achievementId)).findByText("Applied");

    // The sibling in the same experience and the card in the other experience still offer Apply.
    for (const sibling of [CARD_2, CARD_3]) {
      expect(
        within(card(sibling.achievementId)).getByRole("button", { name: "Apply" }),
      ).toBeEnabled();
      expect(within(card(sibling.achievementId)).queryByText("Applied")).not.toBeInTheDocument();
    }
    expect(applyCalls).toHaveLength(1);
  });
});
