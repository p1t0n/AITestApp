import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router";
import { AxiosError, AxiosHeaders } from "axios";
import CatalogPage from "./CatalogPage";
import type { Category, SkillDto } from "../types";

/**
 * The skill catalog as a category list beside a skills table (EXP-48, Variant C on
 * `prototype/table-conventions`).
 *
 * Two rules this page used to break, and the reason most of the assertions below exist:
 *
 * * **The row is never an input.** Clicking a skill's or a category's name used to turn the row
 *   into text fields, so a stray click mid-journey was one keystroke from a write. Every change now
 *   goes through `EditDialog`, reached by an explicit ✎.
 * * **The tree is gone.** ~80 skills in ~13 near-flat categories is a dictionary, so it filters,
 *   sorts and pages like every other dictionary (`DictionaryTable`), with the categories as the
 *   master list rather than as indentation.
 *
 * The table's own toolbar behaviour is asserted once in `DictionaryTable.test.tsx`; what is tested
 * here is what this page owes on top of it — the selection, the columns, the popups and the failure
 * message.
 */

const CATEGORIES: Category[] = [
  { id: "c-front", name: "Frontend", parentId: null },
  { id: "c-react", name: "React", parentId: "c-front" },
  { id: "c-hooks", name: "Hooks", parentId: "c-react" },
  { id: "c-data", name: "Data", parentId: null },
];

const SKILLS: SkillDto[] = [
  { id: "s-1", name: "useMemo", categoryId: "c-hooks", categoryName: "Hooks", rank: 0 },
  { id: "s-2", name: "Suspense", categoryId: "c-react", categoryName: "React", rank: 0 },
  { id: "s-3", name: "Postgres", categoryId: "c-data", categoryName: "Data", rank: 0 },
  { id: "s-4", name: "Arrays", categoryId: "c-front", categoryName: "Frontend", rank: 0 },
];

const createCategory = vi.fn();
const updateCategory = vi.fn();
const deleteCategory = vi.fn();
const createSkill = vi.fn();
const updateSkill = vi.fn();
const deleteSkill = vi.fn();

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null };
  return {
    ...actual,
    useCategories: () => ({ data: CATEGORIES, isLoading: false, isError: false, error: null }),
    useSkills: () => ({ data: SKILLS, isLoading: false, isError: false, error: null }),
    useCreateCategory: () => ({ mutateAsync: createCategory, ...idle }),
    useUpdateCategory: () => ({ mutateAsync: updateCategory, ...idle }),
    useDeleteCategory: () => ({ mutateAsync: deleteCategory, ...idle }),
    useCreateSkill: () => ({ mutateAsync: createSkill, ...idle }),
    useUpdateSkill: () => ({ mutateAsync: updateSkill, ...idle }),
    useDeleteSkill: () => ({ mutateAsync: deleteSkill, ...idle }),
  };
});

beforeEach(() => {
  vi.clearAllMocks();
  for (const fn of [createCategory, updateCategory, deleteCategory, createSkill, updateSkill, deleteSkill]) {
    fn.mockResolvedValue(undefined);
  }
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <CatalogPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const skillsTable = () => screen.getByRole("table", { name: "Skills" });

/** The Skill column of every rendered row, in order. */
const skillNames = () =>
  within(skillsTable())
    .getAllByRole("row")
    .slice(1)
    .map((r) => (r as HTMLTableRowElement).cells[0].textContent);

/** The Category column of every rendered row, in order. */
const categoryPaths = () =>
  within(skillsTable())
    .getAllByRole("row")
    .slice(1)
    .map((r) => (r as HTMLTableRowElement).cells[1].textContent);

/**
 * Clicks a category in the master list by its exact path — "Frontend", not the "Frontend / React"
 * it is a prefix of. The count chip is part of the row's accessible name, so the row is found by
 * its label text and clicked through its button ancestor.
 */
const chooseCategory = (path: string) =>
  userEvent.click(
    within(screen.getByRole("list", { name: "Categories" }))
      .getByText(path)
      .closest('[role="button"]') as HTMLElement,
  );

/** Opens a skill's edit popup the only way the page offers — the row's ✎. */
async function editSkill(name: string) {
  const row = within(skillsTable()).getByText(name).closest("tr") as HTMLElement;
  await userEvent.click(within(row).getByRole("button", { name: `Edit ${name}` }));
  return screen.getByRole("dialog");
}

/** Picks an option out of a MUI select, which renders a listbox rather than a native menu. */
async function choose(dialog: HTMLElement, field: string, option: string | RegExp) {
  await userEvent.click(within(dialog).getByRole("combobox", { name: field }));
  await userEvent.click(screen.getByRole("option", { name: option }));
}

describe("the tree is gone", () => {
  it("lists every category by its full path, with its own skill count", () => {
    renderPage();

    const list = screen.getByRole("list", { name: "Categories" });
    expect(within(list).getAllByRole("button").map((b) => b.textContent)).toEqual([
      "All skills4",
      "Data1",
      "Frontend1",
      "Frontend / React1",
      "Frontend / React / Hooks1",
    ]);
  });

  it("shows a skill's category as a path, not as indentation", () => {
    renderPage();

    expect(categoryPaths()).toContain("Frontend / React / Hooks");
  });

  it("does not turn a row into an input when a skill's name is clicked", async () => {
    renderPage();

    await userEvent.click(within(skillsTable()).getByText("Suspense"));

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(within(skillsTable()).queryByRole("textbox")).toBeNull();
  });
});

describe("choosing a category", () => {
  it("filters the table to that category alone", async () => {
    renderPage();
    expect(skillNames()).toHaveLength(4);

    await chooseCategory("Frontend / React");

    expect(skillNames()).toEqual(["Suspense"]);
  });

  it("does not sweep in the chosen category's subcategories", async () => {
    renderPage();

    await chooseCategory("Frontend");

    expect(skillNames()).toEqual(["Arrays"]);
  });

  it('is reset by "All skills"', async () => {
    renderPage();
    await chooseCategory("Data");
    expect(skillNames()).toEqual(["Postgres"]);

    await chooseCategory("All skills");

    expect(skillNames()).toHaveLength(4);
  });

  it("heads the results with the category's path and an Edit category… button", async () => {
    renderPage();

    await chooseCategory("Frontend / React / Hooks");

    expect(screen.getByRole("heading", { name: "Frontend / React / Hooks" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit category…" })).toBeInTheDocument();
  });

  it("offers no category heading with the whole catalog shown", () => {
    renderPage();

    expect(screen.queryByRole("button", { name: "Edit category…" })).toBeNull();
  });
});

describe("the skills table", () => {
  it("searches over the skill name and its category path", async () => {
    renderPage();

    await userEvent.type(screen.getByRole("textbox", { name: "Search skills" }), "hooks");

    expect(skillNames()).toEqual(["useMemo"]);
  });

  it("sorts by skill name, and reverses on a second click", async () => {
    renderPage();
    expect(skillNames()).toEqual(["Arrays", "Postgres", "Suspense", "useMemo"]);

    await userEvent.click(screen.getByRole("button", { name: "Skill" }));

    expect(skillNames()).toEqual(["useMemo", "Suspense", "Postgres", "Arrays"]);
  });

  it("sorts by category path", async () => {
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "Category" }));

    expect(categoryPaths()).toEqual([
      "Data",
      "Frontend",
      "Frontend / React",
      "Frontend / React / Hooks",
    ]);
  });
});

describe("editing a skill", () => {
  it("saves the name and the category the popup was left with", async () => {
    renderPage();
    const dialog = await editSkill("Suspense");

    await userEvent.clear(within(dialog).getByRole("textbox", { name: "Name" }));
    await userEvent.type(within(dialog).getByRole("textbox", { name: "Name" }), "Suspense boundary");
    await choose(dialog, "Category", "Data");
    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(updateSkill).toHaveBeenCalledWith({
      id: "s-2",
      name: "Suspense boundary",
      categoryId: "c-data",
    });
  });

  it("refuses to close on Esc once it is dirty, and asks instead", async () => {
    renderPage();
    const dialog = await editSkill("Suspense");

    await userEvent.type(within(dialog).getByRole("textbox", { name: "Name" }), "!");
    await userEvent.keyboard("{Escape}");

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Discard your changes?")).toBeInTheDocument();
  });

  it("deletes only behind its own confirmation, inside the popup", async () => {
    renderPage();
    const dialog = await editSkill("Postgres");

    await userEvent.click(within(dialog).getByRole("button", { name: /Delete skill/ }));
    expect(deleteSkill).not.toHaveBeenCalled();
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete" }));

    expect(deleteSkill).toHaveBeenCalledWith("s-3");
  });
});

describe("creating", () => {
  it("opens a New skill popup with the chosen category already filled in", async () => {
    renderPage();
    await chooseCategory("Data");

    await userEvent.click(screen.getByRole("button", { name: "New skill" }));
    const dialog = screen.getByRole("dialog");
    await userEvent.type(within(dialog).getByRole("textbox", { name: "Name" }), "SQL");
    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(createSkill).toHaveBeenCalledWith({ name: "SQL", categoryId: "c-data" });
  });

  it("will not save a skill that names no category", async () => {
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "New skill" }));
    const dialog = screen.getByRole("dialog");
    await userEvent.type(within(dialog).getByRole("textbox", { name: "Name" }), "SQL");

    expect(within(dialog).getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("creates a top-level category from the list's + button", async () => {
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "New category" }));
    const dialog = screen.getByRole("dialog");
    await userEvent.type(within(dialog).getByRole("textbox", { name: "Name" }), "Platform");
    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(createCategory).toHaveBeenCalledWith({ name: "Platform", parentId: null });
  });
});

describe("editing a category", () => {
  it("offers neither itself nor a descendant as its parent", async () => {
    renderPage();
    await chooseCategory("Frontend / React");
    await userEvent.click(screen.getByRole("button", { name: "Edit category…" }));

    await userEvent.click(within(screen.getByRole("dialog")).getByRole("combobox", { name: "Parent" }));

    const offered = screen.getAllByRole("option").map((o) => o.textContent);
    expect(offered).toEqual(["— none (top level) —", "Data", "Frontend"]);
  });

  it("re-parents through the popup", async () => {
    renderPage();
    await chooseCategory("Frontend / React");
    await userEvent.click(screen.getByRole("button", { name: "Edit category…" }));
    const dialog = screen.getByRole("dialog");

    await choose(dialog, "Parent", "Data");
    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(updateCategory).toHaveBeenCalledWith({ id: "c-react", name: "React", parentId: "c-data" });
  });

  it("deletes only behind its own confirmation", async () => {
    renderPage();
    await chooseCategory("Data");
    await userEvent.click(screen.getByRole("button", { name: "Edit category…" }));
    const dialog = screen.getByRole("dialog");

    await userEvent.click(within(dialog).getByRole("button", { name: /Delete category/ }));
    expect(deleteCategory).not.toHaveBeenCalled();
    await userEvent.click(within(dialog).getByRole("button", { name: "Delete" }));

    expect(deleteCategory).toHaveBeenCalledWith("c-data");
  });
});

describe("when the server refuses", () => {
  it("shows the server's own words and leaves the popup open", async () => {
    deleteCategory.mockRejectedValue(
      new AxiosError("Request failed", "400", undefined, undefined, {
        status: 400,
        statusText: "Bad Request",
        headers: new AxiosHeaders(),
        config: { headers: new AxiosHeaders() },
        data: { error: "Category still has skills." },
      }),
    );
    renderPage();
    await chooseCategory("Data");
    await userEvent.click(screen.getByRole("button", { name: "Edit category…" }));
    const dialog = screen.getByRole("dialog");

    await userEvent.click(within(dialog).getByRole("button", { name: /Delete category/ }));
    await userEvent.click(within(dialog).getByRole("button", { name: "Delete" }));

    expect(await screen.findByTestId("error-notice")).toHaveTextContent("Category still has skills.");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
