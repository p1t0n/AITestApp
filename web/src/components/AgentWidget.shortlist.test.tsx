import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type { ShortlistResponse } from "../api";

// ---- api module mock ----
// Only the hooks are mocked; apiErrorMessage stays real so error shapes (429 JSON body) go through
// the same extraction path production uses.

const shortlistState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<ShortlistResponse>>(),
  isPending: false,
};

const EMPLOYEE_ID = "11111111-2222-3333-4444-555555555555";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useShortlist: () => shortlistState,
    useSkills: () => ({
      data: [
        { id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", name: "React", categoryId: "c1", categoryName: "Frontend", rank: 1 },
        { id: "99999999-8888-7777-6666-555555555555", name: "Postgres", categoryId: "c2", categoryName: "Data", rank: 2 },
      ],
      isLoading: false,
    }),
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
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const RESPONSE: ShortlistResponse = {
  requirements: ["React expertise", "5+ years experience", "Team leadership"],
  candidates: [
    {
      employeeId: EMPLOYEE_ID,
      name: "Ada Lovelace",
      title: "Senior Engineer",
      score: 0.9234,
      coverage: { matched: 2, total: 3 },
      requirements: [
        { text: "React expertise", matched: true, snippet: "Built React apps for 6 years" },
        { text: "5+ years experience", matched: true, snippet: "8 years total" },
        { text: "Team leadership", matched: false },
      ],
      rationale: "Strong React background with long tenure.",
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

async function openShortlistTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} isNarrow={false} />
    </MemoryRouter>,
  );
  await user.click(screen.getByRole("tab", { name: "Shortlist" }));
  return user;
}

function jdField() {
  return screen.getByPlaceholderText(/paste a job description/i);
}

function submitButton() {
  return screen.getByRole("button", { name: /build shortlist/i });
}

beforeEach(() => {
  shortlistState.mutateAsync = vi.fn();
  shortlistState.isPending = false;
});

describe("Shortlist tab", () => {
  it("renders the JD input with filters collapsed until toggled", async () => {
    const user = await openShortlistTab();

    expect(jdField()).toBeInTheDocument();
    expect(screen.queryByLabelText("Available on")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /filters/i }));

    expect(screen.getByLabelText("Available on")).toBeInTheDocument();
    expect(screen.getByLabelText("Skills")).toBeInTheDocument();
    expect(screen.getByLabelText("Location")).toBeInTheDocument();
    expect(screen.getByLabelText("Min years")).toBeInTheDocument();
    expect(screen.getByLabelText("Top K")).toBeInTheDocument();
  });

  it("disables submit while the JD is empty and while a request is pending", async () => {
    const user = await openShortlistTab();

    expect(submitButton()).toBeDisabled();
    await user.type(jdField(), "Senior React engineer");
    expect(submitButton()).toBeEnabled();
  });

  it("keeps submit disabled while pending", async () => {
    shortlistState.isPending = true;
    const user = await openShortlistTab();
    await user.type(jdField(), "Senior React engineer");
    expect(screen.getByRole("button", { name: /shortlisting/i })).toBeDisabled();
  });

  it("submits only the JD when no filters are set", async () => {
    shortlistState.mutateAsync.mockResolvedValue(RESPONSE);
    const user = await openShortlistTab();

    await user.type(jdField(), "  Senior React engineer  ");
    await user.click(submitButton());

    expect(shortlistState.mutateAsync).toHaveBeenCalledWith({
      jobDescription: "Senior React engineer",
    });
  });

  it("submits the set filters (skill GUIDs, date, location, minYears, topK)", async () => {
    shortlistState.mutateAsync.mockResolvedValue(RESPONSE);
    const user = await openShortlistTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(screen.getByRole("button", { name: /filters/i }));

    fireEvent.change(screen.getByLabelText("Available on"), { target: { value: "2026-08-01" } });
    await user.click(screen.getByLabelText("Skills"));
    await user.click(await screen.findByRole("option", { name: "React" }));
    await user.type(screen.getByLabelText("Location"), "Berlin");
    await user.type(screen.getByLabelText("Min years"), "5");
    await user.type(screen.getByLabelText("Top K"), "3");

    await user.click(submitButton());

    expect(shortlistState.mutateAsync).toHaveBeenCalledWith({
      jobDescription: "Senior React engineer",
      availableOn: "2026-08-01",
      skillIds: ["aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"],
      location: "Berlin",
      minYears: 5,
      topK: 3,
    });
  });

  it("renders requirements and ranked candidate cards from the response", async () => {
    shortlistState.mutateAsync.mockResolvedValue(RESPONSE);
    const user = await openShortlistTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());

    // "How the JD was read": one chip per requirement.
    expect(await screen.findByText("How the JD was read")).toBeInTheDocument();
    expect(screen.getByText("Team leadership")).toBeInTheDocument();

    // Candidate card: linked name, title, 2dp score, coverage badge, rationale.
    const nameLink = screen.getByRole("link", { name: "Ada Lovelace" });
    expect(nameLink).toHaveAttribute("href", `/employees/${EMPLOYEE_ID}`);
    expect(screen.getByText("Senior Engineer")).toBeInTheDocument();
    expect(screen.getByText("0.92")).toBeInTheDocument();
    expect(screen.getByText("2/3")).toBeInTheDocument();
    expect(screen.getByText("Strong React background with long tenure.")).toBeInTheDocument();
  });

  it("expands per-candidate evidence, including a missed requirement without a snippet", async () => {
    shortlistState.mutateAsync.mockResolvedValue(RESPONSE);
    const user = await openShortlistTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());
    await user.click(await screen.findByRole("button", { name: /evidence/i }));

    const evidence = screen.getByTestId(`evidence-${EMPLOYEE_ID}`);
    expect(within(evidence).getByText("Built React apps for 6 years")).toBeInTheDocument();
    const missed = within(evidence).getByTestId("evidence-row-2");
    expect(within(missed).getByText("Team leadership")).toBeInTheDocument();
    expect(within(missed).getByTestId("missed-icon")).toBeInTheDocument();
    expect(within(missed).queryByTestId("snippet")).not.toBeInTheDocument();
  });

  it("'Run full Match' switches to the Match tab with the employee and JD pre-filled", async () => {
    shortlistState.mutateAsync.mockResolvedValue(RESPONSE);
    const user = await openShortlistTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());
    await user.click(await screen.findByRole("button", { name: /run full match/i }));

    expect(screen.getByRole("tab", { name: "Match" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByDisplayValue("Ada Lovelace — Senior Engineer")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Senior React engineer")).toBeInTheDocument();
  });

  it("renders the structured 429 cap message the same way other tabs do", async () => {
    shortlistState.mutateAsync.mockRejectedValue({
      isAxiosError: true,
      message: "Request failed with status code 429",
      response: {
        status: 429,
        data: {
          error: "Your daily token cap has been reached.",
          window: "daily",
          used: 1000,
          cap: 1000,
          resetAt: "2026-07-12T00:00:00Z",
        },
      },
    });
    const user = await openShortlistTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());

    expect(await screen.findByText("Your daily token cap has been reached.")).toBeInTheDocument();
  });

  it("shows a clear message when no candidates matched", async () => {
    shortlistState.mutateAsync.mockResolvedValue({ requirements: ["React"], candidates: [] });
    const user = await openShortlistTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());

    expect(await screen.findByText(/no candidates matched/i)).toBeInTheDocument();
  });
});
