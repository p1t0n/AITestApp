import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type { InterviewKitResponse } from "../api";

// ---- api module mock ----
// Only the hooks are mocked; apiErrorMessage stays real (mirrors the other widget test files).

const interviewState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<InterviewKitResponse>>(),
  isPending: false,
};

const EMPLOYEE_ID = "11111111-2222-3333-4444-555555555555";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useInterviewKit: () => interviewState,
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useJdMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useApplyRewrite: () => ({ isPending: false, isSuccess: false, isError: false, error: null, mutate: vi.fn() }),
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
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 420,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

async function openInterviewTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} isNarrow={false} />
    </MemoryRouter>,
  );
  await user.click(screen.getByRole("tab", { name: "Interview" }));
  return user;
}

async function submitRun(user: Awaited<ReturnType<typeof openInterviewTab>>) {
  await user.click(screen.getByLabelText(/employee/i));
  await user.click(screen.getByText("Ada Lovelace — Senior Engineer"));
  await user.type(screen.getByPlaceholderText(/paste a job description/i), "Platform engineer.");
  await user.click(screen.getByRole("button", { name: /build interview kit/i }));
}

beforeEach(() => {
  interviewState.mutateAsync.mockReset();
});

describe("Interview kit tab (P1T-102)", () => {
  it("submits employee + JD and renders the kit with vetted questions", async () => {
    interviewState.mutateAsync.mockResolvedValue({
      answer: "## Interview kit\n\nFit summary here.",
      questions: [
        {
          question: "How was the 40% deploy-time cut measured?",
          probes: "claim depth",
          evidence: "Cut deploy time 40%.",
        },
        { question: "Any Kubernetes production experience?", probes: "JD gap", evidence: null },
      ],
    });
    const user = await openInterviewTab();

    await submitRun(user);

    expect(interviewState.mutateAsync).toHaveBeenCalledWith({
      employeeId: EMPLOYEE_ID,
      jobDescription: "Platform engineer.",
    });
    expect(await screen.findByText("Fit summary here.")).toBeInTheDocument();

    const first = screen.getByTestId("interview-question-0");
    expect(first).toHaveTextContent("How was the 40% deploy-time cut measured?");
    expect(first).toHaveTextContent("Probes: claim depth");
    expect(first).toHaveTextContent("“Cut deploy time 40%.”");

    // Gap question: no evidence quote rendered.
    const second = screen.getByTestId("interview-question-1");
    expect(second).toHaveTextContent("Any Kubernetes production experience?");
    expect(second).not.toHaveTextContent("“");
  });

  it("renders the answer alone when the structured questions degraded away", async () => {
    interviewState.mutateAsync.mockResolvedValue({
      answer: "Kit only.",
      questions: [],
    });
    const user = await openInterviewTab();

    await submitRun(user);

    expect(await screen.findByText("Kit only.")).toBeInTheDocument();
    expect(screen.queryByTestId("interview-question-0")).toBeNull();
  });
});
