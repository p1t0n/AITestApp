import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { selectAgentSurface } from "../test/agentSurface";
import type { AgentDock } from "./useAgentDock";
import type { IngestionResponse } from "../api";
import type { EmployeeDetail } from "../types";

// ---- api module mock ----
// Hooks only; apiErrorMessage stays real (same pattern as the other AgentWidget suites).

const DRAFT_ID = "dddddddd-1111-2222-3333-444444444444";

const ingestState = {
  mutateAsync: vi.fn<(text: string) => Promise<IngestionResponse>>(),
  isPending: false,
};
const promoteState = { mutateAsync: vi.fn(), isPending: false };
const updateState = { mutateAsync: vi.fn(), isPending: false };
const deleteState = { mutateAsync: vi.fn(), isPending: false };
const addSkillState = { mutateAsync: vi.fn(), isPending: false };
const createSkillState = { mutateAsync: vi.fn(), isPending: false };

let draftEmployee: EmployeeDetail;

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useResumeIngestion: () => ingestState,
    usePromoteEmployee: () => promoteState,
    useUpdateEmployee: () => updateState,
    useDeleteEmployee: () => deleteState,
    useAddEmployeeSkill: () => addSkillState,
    useCreateSkill: () => createSkillState,
    useEmployee: () => ({ data: draftEmployee, isLoading: false }),
    useEmployees: () => ({ data: [], isLoading: false }),
    useSkills: () => ({
      data: [{ id: "s-react", name: "React", categoryId: "c1", categoryName: "Frontend", rank: 1 }],
      isLoading: false,
    }),
    useCategories: () => ({ data: [{ id: "c1", name: "Frontend", parentId: null }], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

function response(overrides: Partial<IngestionResponse> = {}): IngestionResponse {
  return {
    employeeId: DRAFT_ID,
    created: { languages: 2, skills: 3, qualifications: 1, experiences: 2 },
    proposals: [],
    notes: [],
    duplicateWarning: null,
    degraded: false,
    ...overrides,
  };
}

function employee(overrides: Partial<EmployeeDetail> = {}): EmployeeDetail {
  return {
    id: DRAFT_ID,
    firstName: "Wren",
    lastName: "Ashgrove",
    title: "Platform Engineer",
    email: "wren@example.com",
    phone: null,
    location: "Tallinn",
    summary: null,
    photoUrl: null,
    currentCapacityPercent: 0,
    status: "Draft",
    spokenLanguages: [],
    availabilityEntries: [],
    skills: [],
    qualifications: [],
    experiences: [],
    ...overrides,
  };
}

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 460,
  isNarrow: false,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

async function openIngestTab() {
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
  await selectAgentSurface(userEvent, "Resume ingest");
}

async function stage(text = "some resume text") {
  await userEvent.type(screen.getByPlaceholderText(/Paste the raw resume/), text);
  await userEvent.click(screen.getByRole("button", { name: "Stage as draft" }));
}

beforeEach(() => {
  vi.clearAllMocks();
  draftEmployee = employee();
});

describe("Ingestion tab", () => {
  it("stages a draft and shows the review with the Draft chip and counts", async () => {
    ingestState.mutateAsync.mockResolvedValue(response());

    await openIngestTab();
    await stage();

    const review = await screen.findByTestId("ingestion-review");
    expect(within(review).getByTestId("draft-status-chip")).toHaveTextContent("Draft");
    expect(review).toHaveTextContent("Wren Ashgrove");
    expect(review).toHaveTextContent("2 language(s), 3 skill(s), 1 qualification(s), 2 experience(s)");
  });

  it("surfaces the duplicate warning and degradation notes prominently", async () => {
    ingestState.mutateAsync.mockResolvedValue(
      response({
        duplicateWarning: "An employee named Wren Ashgrove already exists.",
        notes: ["experience_add failed 2 time(s); some items were skipped."],
        degraded: true,
      }),
    );

    await openIngestTab();
    await stage();

    expect(await screen.findByTestId("ingestion-dupe-warning")).toHaveTextContent("already exists");
    expect(screen.getByTestId("ingestion-notes")).toHaveTextContent("experience_add failed");
  });

  it("rejecting a proposal creates nothing and strikes the row through", async () => {
    ingestState.mutateAsync.mockResolvedValue(response({ proposals: ["LabVIEW"] }));

    await openIngestTab();
    await stage();

    const row = await screen.findByTestId("proposal-LabVIEW");
    await userEvent.click(within(row).getByRole("button", { name: "Reject" }));

    expect(createSkillState.mutateAsync).not.toHaveBeenCalled();
    expect(addSkillState.mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByTestId("proposal-LabVIEW")).toHaveTextContent("LabVIEW");
  });

  it("mapping a proposal to an existing skill adds it to the employee", async () => {
    ingestState.mutateAsync.mockResolvedValue(response({ proposals: ["ReactJS"] }));
    addSkillState.mutateAsync.mockResolvedValue({});

    await openIngestTab();
    await stage();

    const row = await screen.findByTestId("proposal-ReactJS");
    await userEvent.click(within(row).getByLabelText("Existing skill"));
    await userEvent.click(await screen.findByRole("option", { name: "React" }));
    await userEvent.click(within(row).getByRole("button", { name: "Map to existing" }));

    await waitFor(() =>
      expect(addSkillState.mutateAsync).toHaveBeenCalledWith({
        skillId: "s-react",
        level: "Intermediate",
        yearsExperience: 0,
      }),
    );
    expect(createSkillState.mutateAsync).not.toHaveBeenCalled();
  });

  it("adding a proposal as a new skill creates it then attaches it", async () => {
    ingestState.mutateAsync.mockResolvedValue(response({ proposals: ["LabVIEW"] }));
    createSkillState.mutateAsync.mockResolvedValue({ id: "s-new" });
    addSkillState.mutateAsync.mockResolvedValue({});

    await openIngestTab();
    await stage();

    const row = await screen.findByTestId("proposal-LabVIEW");
    await userEvent.click(within(row).getByLabelText("Category"));
    await userEvent.click(await screen.findByRole("option", { name: "Frontend" }));
    await userEvent.click(within(row).getByRole("button", { name: "Add as new" }));

    await waitFor(() =>
      expect(createSkillState.mutateAsync).toHaveBeenCalledWith({ name: "LabVIEW", categoryId: "c1" }),
    );
    expect(addSkillState.mutateAsync).toHaveBeenCalledWith({
      skillId: "s-new",
      level: "Intermediate",
      yearsExperience: 0,
    });
  });

  it("promotes directly when the draft already has an email", async () => {
    ingestState.mutateAsync.mockResolvedValue(response());
    promoteState.mutateAsync.mockResolvedValue({});

    await openIngestTab();
    await stage();

    await userEvent.click(await screen.findByRole("button", { name: "Promote" }));

    await waitFor(() => expect(promoteState.mutateAsync).toHaveBeenCalledWith(DRAFT_ID));
    expect(updateState.mutateAsync).not.toHaveBeenCalled();
  });

  it("demands an email before promoting an email-less draft, then saves it first", async () => {
    draftEmployee = employee({ email: "" });
    ingestState.mutateAsync.mockResolvedValue(response());
    updateState.mutateAsync.mockResolvedValue({});
    promoteState.mutateAsync.mockResolvedValue({});

    await openIngestTab();
    await stage();

    const promoteButton = await screen.findByRole("button", { name: "Promote" });
    expect(promoteButton).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Email (required to promote)"), "wren@example.com");
    await userEvent.click(promoteButton);

    await waitFor(() => expect(promoteState.mutateAsync).toHaveBeenCalledWith(DRAFT_ID));
    expect(updateState.mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ email: "wren@example.com" }),
    );
  });

  it("discarding deletes the draft and returns to the paste form", async () => {
    ingestState.mutateAsync.mockResolvedValue(response());
    deleteState.mutateAsync.mockResolvedValue({});

    await openIngestTab();
    await stage();

    await userEvent.click(await screen.findByRole("button", { name: "Discard draft" }));

    await waitFor(() => expect(deleteState.mutateAsync).toHaveBeenCalledWith(DRAFT_ID));
    expect(screen.queryByTestId("ingestion-review")).not.toBeInTheDocument();
    expect(screen.getByPlaceholderText(/Paste the raw resume/)).toHaveValue("");
  });
});
