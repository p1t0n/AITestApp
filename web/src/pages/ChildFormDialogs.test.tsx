import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import AvailabilityFormDialog from "./AvailabilityFormDialog";
import ExpertSkillFormDialog from "./ExpertSkillFormDialog";
import LanguageFormDialog from "./LanguageFormDialog";
import QualificationFormDialog from "./QualificationFormDialog";
import ExperienceFormDialog from "./ExperienceFormDialog";
import type { SkillDto } from "../types";

const CATALOG: SkillDto[] = [
  { id: "skill-react", name: "React", categoryId: "c1", categoryName: "Frontend", rank: 1 },
  { id: "skill-dotnet", name: ".NET", categoryId: "c2", categoryName: "Backend", rank: 2 },
];

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return { ...actual, useSkills: () => ({ data: CATALOG, isLoading: false }) };
});

/** What the API answers on a FluentValidation failure, in the shape `apiErrorMessage` reads. */
function validationFailure(message: string) {
  return new AxiosError("Request failed", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 400,
    statusText: "Bad Request",
    data: { error: message },
    headers: new AxiosHeaders(),
    config: { headers: new AxiosHeaders() },
  });
}

const save = () => screen.getByRole("button", { name: "Save" });

describe("LanguageFormDialog", () => {
  it("saves the language and its level", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    const onClose = vi.fn();
    render(
      <LanguageFormDialog open title="Add language" onClose={onClose} onSave={onSave} />,
    );

    await userEvent.type(screen.getByLabelText("Language"), "German");
    await userEvent.click(screen.getByLabelText("Level"));
    await userEvent.click(screen.getByRole("option", { name: "Native" }));
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith({ language: "German", level: "Native" });
    expect(onClose).toHaveBeenCalled();
  });

  it("seeds the form from initial so an edit starts from the current row", () => {
    render(
      <LanguageFormDialog
        open
        title="Edit language"
        initial={{ language: "Polish", level: "Fluent" }}
        onClose={() => {}}
        onSave={vi.fn()}
      />,
    );

    expect(screen.getByLabelText("Language")).toHaveValue("Polish");
    expect(screen.getByText("Fluent")).toBeInTheDocument();
  });

  it("shows a server validation failure and keeps the dialog open", async () => {
    const onSave = vi.fn().mockRejectedValue(validationFailure("Language must not be empty."));
    const onClose = vi.fn();
    render(<LanguageFormDialog open title="Add language" onClose={onClose} onSave={onSave} />);

    await userEvent.click(save());

    expect(await screen.findByRole("alert")).toHaveTextContent("Language must not be empty.");
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe("QualificationFormDialog", () => {
  it("sends the degree fields and leaves the certification half null", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(
      <QualificationFormDialog open title="Add qualification" onClose={() => {}} onSave={onSave} />,
    );

    await userEvent.type(screen.getByLabelText("Name"), "BSc Computer Science");
    await userEvent.type(screen.getByLabelText("Institution"), "TU Delft");
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        type: "Degree",
        name: "BSc Computer Science",
        institution: "TU Delft",
        issuer: null,
        credentialId: null,
        issueDate: null,
        expiryDate: null,
      }),
    );
  });

  it("switches to the certification fields and clears the degree half on save", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(
      <QualificationFormDialog
        open
        title="Edit qualification"
        // A record that started life as a Degree, then re-typed as a Certification: its
        // institution must not ride along into the saved certification.
        initial={{ type: "Degree", name: "AZ-204", institution: "TU Delft" }}
        onClose={() => {}}
        onSave={onSave}
      />,
    );

    await userEvent.click(screen.getByLabelText("Type"));
    await userEvent.click(screen.getByRole("option", { name: "Certification" }));
    expect(screen.queryByLabelText("Institution")).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Issuer"), "Microsoft");
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        type: "Certification",
        issuer: "Microsoft",
        institution: null,
        field: null,
        startDate: null,
        endDate: null,
      }),
    );
  });

  it("shows a server validation failure and keeps the dialog open", async () => {
    const onSave = vi.fn().mockRejectedValue(validationFailure("Name must not be empty."));
    const onClose = vi.fn();
    render(
      <QualificationFormDialog open title="Add qualification" onClose={onClose} onSave={onSave} />,
    );

    await userEvent.click(save());

    expect(await screen.findByRole("alert")).toHaveTextContent("Name must not be empty.");
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe("ExperienceFormDialog", () => {
  it("saves the experience with its bullets and catalog skills", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(
      <ExperienceFormDialog open title="Add experience" onClose={() => {}} onSave={onSave} />,
    );

    await userEvent.type(screen.getByLabelText("Company"), "Contoso");
    await userEvent.type(screen.getByLabelText("Job title"), "Staff Engineer");
    await userEvent.type(screen.getByLabelText("Start date"), "2021-03-01");

    await userEvent.click(screen.getByRole("button", { name: "Add bullet" }));
    await userEvent.type(screen.getByLabelText("Bullet 1"), "Cut deploy time in half.");

    await userEvent.click(screen.getByLabelText("Skills"));
    await userEvent.click(screen.getByRole("option", { name: "React" }));

    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith({
      company: "Contoso",
      title: "Staff Engineer",
      location: null,
      startDate: "2021-03-01",
      endDate: null,
      summary: null,
      achievements: [{ order: 1, text: "Cut deploy time in half." }],
      skillIds: ["skill-react"],
    });
  });

  it("renumbers bullets from their position after a move, and drops blank ones", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(
      <ExperienceFormDialog
        open
        title="Edit experience"
        initial={{
          company: "Contoso",
          title: "Staff Engineer",
          startDate: "2021-03-01",
          achievements: [
            { order: 1, text: "First" },
            { order: 2, text: "Second" },
          ],
          skillIds: [],
        }}
        onClose={() => {}}
        onSave={onSave}
      />,
    );

    // An empty row the user added and thought better of: dropped, not sent to fail validation.
    await userEvent.click(screen.getByRole("button", { name: "Add bullet" }));
    await userEvent.click(screen.getByLabelText("Move bullet 2 up"));
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        achievements: [
          { order: 1, text: "Second" },
          { order: 2, text: "First" },
        ],
      }),
    );
  });

  it("removes a bullet without touching its siblings", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(
      <ExperienceFormDialog
        open
        title="Edit experience"
        initial={{
          company: "Contoso",
          title: "Staff Engineer",
          startDate: "2021-03-01",
          achievements: [
            { order: 1, text: "Keep me" },
            { order: 2, text: "Drop me" },
            { order: 3, text: "Keep me too" },
          ],
          skillIds: [],
        }}
        onClose={() => {}}
        onSave={onSave}
      />,
    );

    await userEvent.click(screen.getByLabelText("Remove bullet 2"));
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        achievements: [
          { order: 1, text: "Keep me" },
          { order: 2, text: "Keep me too" },
        ],
      }),
    );
  });

  it("seeds the skill picker from the experience's existing skills", () => {
    render(
      <ExperienceFormDialog
        open
        title="Edit experience"
        initial={{
          company: "Contoso",
          title: "Staff Engineer",
          startDate: "2021-03-01",
          achievements: [],
          skillIds: ["skill-dotnet"],
        }}
        onClose={() => {}}
        onSave={vi.fn()}
      />,
    );

    expect(within(screen.getByRole("dialog")).getByText(".NET")).toBeInTheDocument();
  });

  it("shows a server validation failure and keeps the dialog open", async () => {
    const onSave = vi.fn().mockRejectedValue(validationFailure("Company must not be empty."));
    const onClose = vi.fn();
    render(<ExperienceFormDialog open title="Add experience" onClose={onClose} onSave={onSave} />);

    await userEvent.click(save());

    expect(await screen.findByRole("alert")).toHaveTextContent("Company must not be empty.");
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe("AvailabilityFormDialog", () => {
  it("saves the step: capacity from a date on", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    const onClose = vi.fn();
    render(
      <AvailabilityFormDialog open title="Add availability" onClose={onClose} onSave={onSave} />,
    );

    await userEvent.type(screen.getByLabelText("Effective from"), "2026-04-01");
    await userEvent.clear(screen.getByLabelText("Capacity %"));
    await userEvent.type(screen.getByLabelText("Capacity %"), "60");
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith({ effectiveFrom: "2026-04-01", capacityPercent: 60 });
    expect(onClose).toHaveBeenCalled();
  });

  it("seeds the form from initial so an edit starts from the current entry", () => {
    render(
      <AvailabilityFormDialog
        open
        title="Edit availability"
        initial={{ effectiveFrom: "2026-01-15", capacityPercent: 80 }}
        onClose={() => {}}
        onSave={vi.fn()}
      />,
    );

    expect(screen.getByLabelText("Effective from")).toHaveValue("2026-01-15");
    expect(screen.getByLabelText("Capacity %")).toHaveValue(80);
  });

  it("cannot save without a date, because an empty one fails binding before validation", async () => {
    const onSave = vi.fn();
    render(<AvailabilityFormDialog open title="Add availability" onClose={() => {}} onSave={onSave} />);

    expect(save()).toBeDisabled();
    await userEvent.type(screen.getByLabelText("Effective from"), "2026-04-01");
    expect(save()).toBeEnabled();
  });

  it("shows a server validation failure and keeps the dialog open", async () => {
    const onSave = vi
      .fn()
      .mockRejectedValue(validationFailure("Capacity percent must be between 0 and 100."));
    const onClose = vi.fn();
    render(
      <AvailabilityFormDialog
        open
        title="Edit availability"
        initial={{ effectiveFrom: "2026-04-01", capacityPercent: 140 }}
        onClose={onClose}
        onSave={onSave}
      />,
    );

    await userEvent.click(save());

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Capacity percent must be between 0 and 100.",
    );
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe("ExpertSkillFormDialog", () => {
  it("links a catalog skill with its level and years", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(<ExpertSkillFormDialog open title="Add skill" onClose={() => {}} onSave={onSave} />);

    await userEvent.click(screen.getByLabelText("Skill"));
    await userEvent.click(screen.getByRole("option", { name: "React" }));
    await userEvent.click(screen.getByLabelText("Level"));
    await userEvent.click(screen.getByRole("option", { name: "Expert" }));
    await userEvent.clear(screen.getByLabelText("Years"));
    await userEvent.type(screen.getByLabelText("Years"), "7");
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith({
      skillId: "skill-react",
      level: "Expert",
      yearsExperience: 7,
    });
  });

  it("cannot save until a catalog skill is picked", () => {
    render(<ExpertSkillFormDialog open title="Add skill" onClose={() => {}} onSave={vi.fn()} />);

    expect(save()).toBeDisabled();
  });

  it("locks the skill on edit and sends the row's own id back", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    render(
      <ExpertSkillFormDialog
        open
        title="Edit skill"
        initial={{ skillId: "skill-dotnet", level: "Intermediate", yearsExperience: 3 }}
        lockedSkillName=".NET"
        onClose={() => {}}
        onSave={onSave}
      />,
    );

    // Disabled, not absent: the row is about this skill, and the API assigns the level and the
    // years only — an editable picker would look like it worked and change nothing.
    const skill = screen.getByLabelText("Skill");
    expect(skill).toHaveValue(".NET");
    expect(skill).toBeDisabled();

    await userEvent.click(screen.getByLabelText("Level"));
    await userEvent.click(screen.getByRole("option", { name: "Advanced" }));
    await userEvent.click(save());

    expect(onSave).toHaveBeenCalledWith({
      skillId: "skill-dotnet",
      level: "Advanced",
      yearsExperience: 3,
    });
  });

  it("shows a server validation failure and keeps the dialog open", async () => {
    const onSave = vi.fn().mockRejectedValue(validationFailure("Expert already has this skill."));
    const onClose = vi.fn();
    render(
      <ExpertSkillFormDialog
        open
        title="Add skill"
        initial={{ skillId: "skill-react" }}
        onClose={onClose}
        onSave={onSave}
      />,
    );

    await userEvent.click(save());

    expect(await screen.findByRole("alert")).toHaveTextContent("Expert already has this skill.");
    expect(onClose).not.toHaveBeenCalled();
  });
});
