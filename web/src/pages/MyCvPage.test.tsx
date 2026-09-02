import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import MyCvPage from "./MyCvPage";
import ExpertDetailPage from "./ExpertDetailPage";
import type { ExpertDetail } from "../types";
import { readFileSync } from "node:fs";

/** Reads a source file relative to `web/` — vitest runs from there — so a structural claim about
 * imports can be asserted rather than described. */
function readSource(path: string): string {
  return readFileSync(`${process.cwd()}/${path}`, "utf8");
}

type Visibility = { expertId: string; hidden: boolean; hiddenSince: string | null };

let visibility: Visibility | undefined = { expertId: "e1", hidden: false, hiddenSince: null };
let visibilityFailed = false;

function expert(over: Partial<ExpertDetail> = {}): ExpertDetail {
  return {
    id: "e1",
    firstName: "Ada",
    lastName: "Lovelace",
    title: "Engineer",
    email: "ada@lovelace.dev",
    phone: null,
    location: "London",
    summary: null,
    photoUrl: null,
    currentCapacityPercent: 80,
    status: "Active",
    spokenLanguages: [],
    availabilityEntries: [],
    skills: [],
    qualifications: [],
    experiences: [],
    ...over,
  };
}

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false, isError: false, error: null };
  return {
    ...actual,
    useMyVisibility: () => ({
      data: visibilityFailed ? undefined : visibility,
      isLoading: false,
      isError: visibilityFailed,
      error: null,
    }),
    useExpert: () => ({ data: expert(), isLoading: false, isError: false, error: null }),
    useUpdateExpert: () => idle,
    useExportExpertOnBehalf: () => idle,
    useExpertOwnership: () => ({
      data: { expertId: "e1", ownerUserId: null, ownerEmail: null },
      isLoading: false,
      isError: false,
      error: null,
    }),
    useIssueClaimCode: () => idle,
    useRevokeOwnership: () => idle,
    useRedeemClaimCode: () => ({ ...idle, isSuccess: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useAddExpertSkill: () => idle,
    useUpdateExpertSkill: () => idle,
    useDeleteExpertSkill: () => idle,
    useAddAvailability: () => idle,
    useUpdateAvailability: () => idle,
    useDeleteAvailability: () => idle,
    useAddLanguage: () => idle,
    useUpdateLanguage: () => idle,
    useDeleteLanguage: () => idle,
    useAddQualification: () => idle,
    useUpdateQualification: () => idle,
    useDeleteQualification: () => idle,
    useAddExperience: () => idle,
    useUpdateExperience: () => idle,
    useDeleteExperience: () => idle,
  };
});

function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/me/cv" element={<MyCvPage />} />
          <Route path="/me/claim" element={<div>the claim-status page</div>} />
          <Route path="/experts/:id" element={<ExpertDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  visibility = { expertId: "e1", hidden: false, hiddenSince: null };
  visibilityFailed = false;
  vi.clearAllMocks();
});

describe("the Expert's own CV page (P1T-190)", () => {
  it("is the landing, and shows the record", () => {
    renderAt("/me/cv");

    expect(screen.getByRole("heading", { name: "My CV" })).toBeInTheDocument();
    expect(screen.getByText("ada@lovelace.dev")).toBeInTheDocument();
  });

  /**
   * Somebody who owns no record has nothing to edit, and a form full of blank fields reads as
   * "fill this in" rather than "you are waiting on somebody".
   */
  it("sends somebody who owns no record to claim status", () => {
    visibilityFailed = true;

    renderAt("/me/cv");

    expect(screen.getByText("the claim-status page")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "My CV" })).not.toBeInTheDocument();
  });

  it("says so when they are paused, and only then", () => {
    renderAt("/me/cv");
    expect(screen.queryByText(/You are paused/)).not.toBeInTheDocument();

    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T00:00:00Z" };
    renderAt("/me/cv");
    expect(screen.getByText(/You are paused/)).toBeInTheDocument();
  });

  /**
   * Their email is login identifier, claim key and CV contact at once, so its owner is exactly who
   * must not be able to move it. The field is shown and frozen rather than hidden — the server
   * refuses the change either way, and a missing field invites the question of where it went.
   */
  it("locks the email field in their own edit dialog", async () => {
    renderAt("/me/cv");

    await userEvent.click(screen.getByRole("button", { name: "Edit details" }));

    const email = screen.getByLabelText("Email");
    expect(email).toBeDisabled();
    expect(screen.getByText(/can only be changed by a Service Manager/)).toBeInTheDocument();
  });

  /** Offers none of the staff affordances — those belong to the page that administers the bench. */
  it("offers no on-behalf export and no ownership controls", () => {
    renderAt("/me/cv");

    expect(screen.queryByRole("button", { name: "Export their data" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Issue claim code" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Ownership" })).not.toBeInTheDocument();
  });
});

describe("the child forms are shared, not copied (P1T-190)", () => {
  /**
   * The acceptance criterion asks that the child forms be genuinely shared rather than two copies.
   * Asserted structurally: both pages render the same module, so a change to a form reaches both by
   * construction. Two copies would pass any behavioural test and fail this one.
   */
  it("both pages import the one module, and neither keeps a copy", () => {
    const shared = "components/ExpertRecordSections";
    const forms = [
      "ExperienceFormDialog",
      "ExpertSkillFormDialog",
      "QualificationFormDialog",
      "LanguageFormDialog",
      "AvailabilityFormDialog",
    ];

    const myCv = readSource("src/pages/MyCvPage.tsx");
    const staff = readSource("src/pages/ExpertDetailPage.tsx");

    expect(myCv).toContain(shared);
    expect(staff).toContain(shared);

    // And neither page reaches past it to a form directly, which is how a "shared" component
    // quietly becomes two: one page starts importing a dialog itself, then diverges in how it
    // wires it. The staff page keeps only the root-details dialog, which is not a child form.
    for (const form of forms) {
      expect(myCv, `MyCvPage imports ${form} directly`).not.toContain(form);
      expect(staff, `ExpertDetailPage imports ${form} directly`).not.toContain(form);
    }
  });

  it("and both actually render it", () => {
    renderAt("/me/cv");
    expect(screen.getByRole("heading", { name: "Skills" })).toBeInTheDocument();

    renderAt("/experts/e1");
    expect(screen.getAllByRole("heading", { name: "Skills" }).length).toBeGreaterThan(0);
  });

  /**
   * The catalog is a curated taxonomy that semantic search and shortlist ranking depend on, and a
   * proposal queue would be a third human queue after claims and contested scores.
   */
  it("offers no way to add a skill to the catalog", async () => {
    renderAt("/me/cv");

    await userEvent.click(screen.getByRole("button", { name: "Add skill" }));

    expect(screen.queryByRole("button", { name: /propose/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /new skill/i })).not.toBeInTheDocument();
  });
});
