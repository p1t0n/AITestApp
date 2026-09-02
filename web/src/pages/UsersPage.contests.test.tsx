import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import UsersPage from "./UsersPage";
import ContestableScores, { CONTEST_RIGHT } from "../components/ContestableScores";
import { CONTEST_QUEUE_NOTE } from "../components/ContestQueue";
import type { ContestQueueItem, DerivedAssessment } from "../api";

const CONTESTED: ContestQueueItem = {
  scoringCandidateId: "cand-1",
  expertId: "expert-1",
  expertName: "Quill Lovelace",
  jobDescription: "Payments platform lead",
  score: 41,
  band: "weak",
  rationale: "Looks like a user of payment platforms rather than a builder of them.",
  view: "I led that platform, not just used it.",
  contestedAt: "2026-09-02T10:00:00Z",
};

const SCORED: DerivedAssessment = {
  source: "Roster scan",
  sourceId: "cand-1",
  at: null,
  score: 41,
  band: "weak",
  rationale: "Looks like a user of payment platforms rather than a builder of them.",
  digest: null,
  matchAnswer: null,
};

let queue: ContestQueueItem[] = [];
let assessments: DerivedAssessment[] = [];
const review = vi.fn();
const contest = vi.fn();

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null, isSuccess: false };
  return {
    ...actual,
    useUsers: () => ({ data: [], isLoading: false, isError: false, error: null }),
    useUpdateUser: () => ({ mutate: vi.fn(), ...idle }),
    useDeleteUser: () => ({ mutate: vi.fn(), ...idle }),
    useClaimQueue: () => ({ data: [], isLoading: false, isError: false, error: null }),
    useApproveClaim: () => ({ mutate: vi.fn(), ...idle }),
    useRejectClaim: () => ({ mutate: vi.fn(), ...idle }),
    useContestQueue: () => ({ data: queue, isLoading: false, isError: false, error: null }),
    useReviewContest: () => ({ mutate: review, ...idle }),
    useContestScore: () => ({ mutate: contest, ...idle }),
    useMyAccessView: () => ({
      data: { derived: { assessments, searchIndexNote: "" } },
      isError: false,
      error: null,
    }),
  };
});

function renderIn(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{node}</MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  queue = [];
  assessments = [];
  vi.clearAllMocks();
});

describe("the Service Manager's contest queue (P1T-189)", () => {
  /**
   * The reviewer is the Art. 22(3) safeguard, not a support desk. If nobody actually reads what the
   * person wrote, the legal basis the whole scan rests on is not being honoured — which is worth
   * one sentence in front of them.
   */
  it("says on the screen why the review matters", () => {
    queue = [CONTESTED];
    renderIn(<UsersPage />);

    expect(screen.getByText(CONTEST_QUEUE_NOTE)).toBeInTheDocument();
    expect(CONTEST_QUEUE_NOTE).toMatch(/not a formality/);
  });

  it("shows the score, the rationale and what the person said about it", () => {
    queue = [CONTESTED];
    renderIn(<UsersPage />);

    const row = screen.getByText("Quill Lovelace").closest("tr")!;
    expect(within(row).getByText("41 · weak")).toBeInTheDocument();
    expect(within(row).getByText(/a user of payment platforms/)).toBeInTheDocument();
    expect(within(row).getByText(/I led that platform/)).toBeInTheDocument();
  });

  it("records an outcome and what the reviewer says back", async () => {
    queue = [CONTESTED];
    renderIn(<UsersPage />);

    await userEvent.click(screen.getByRole("button", { name: "Review" }));
    const dialog = screen.getByRole("dialog");
    await userEvent.type(
      within(dialog).getByLabelText("What you say back to them"), "Agreed, shortlisting by hand.");
    await userEvent.click(within(dialog).getByRole("button", { name: "I disagree with the score" }));

    expect(review).toHaveBeenCalledWith({
      scoringCandidateId: "cand-1",
      outcome: "overturned",
      response: "Agreed, shortlisting by hand.",
    });
  });

  /** Somebody who asked without explaining is still owed a look, and the queue has to say so
   * rather than showing an empty cell that reads like an omission. */
  it("says plainly when somebody asked without explaining", () => {
    queue = [{ ...CONTESTED, view: null }];
    renderIn(<UsersPage />);

    expect(screen.getByText(/without saying more. That is their right on its own/))
      .toBeInTheDocument();
  });
});

describe("the Expert's contest control (P1T-189)", () => {
  it("shows the score written about them and offers a person to look at it", async () => {
    assessments = [SCORED];
    renderIn(<ContestableScores />);

    expect(screen.getByText(CONTEST_RIGHT)).toBeInTheDocument();
    expect(screen.getByText(/a user of payment platforms/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Ask for a person to look at this" }));
    await userEvent.type(screen.getByLabelText(/Why you disagree/), "I led it.");
    await userEvent.click(screen.getByRole("button", { name: "Ask for a person to look" }));

    expect(contest).toHaveBeenCalledWith(
      { scoringCandidateId: "cand-1", view: "I led it." }, expect.anything());
  });

  /** Asking is a right on its own; requiring an explanation first would be a toll on it. */
  it("lets somebody ask without explaining", async () => {
    assessments = [SCORED];
    renderIn(<ContestableScores />);

    await userEvent.click(screen.getByRole("button", { name: "Ask for a person to look at this" }));
    await userEvent.click(screen.getByRole("button", { name: "Ask for a person to look" }));

    expect(contest).toHaveBeenCalledWith(
      { scoringCandidateId: "cand-1", view: undefined }, expect.anything());
  });

  it("renders nothing for somebody software has never scored", () => {
    const { container } = renderIn(<ContestableScores />);

    expect(container).toBeEmptyDOMElement();
  });
});
