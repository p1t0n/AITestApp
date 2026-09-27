import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import DictionaryTable, { type DictionaryColumn } from "./DictionaryTable";

/**
 * The shared dictionary table (EXP-46, Variant A on `prototype/table-conventions`).
 *
 * Every small dictionary in the app filters, sorts and pages the same way, so the behaviour is
 * asserted once here rather than a fifth time on each page that adopts it. What the pages then owe
 * is their columns and their popups — not a re-test of the toolbar.
 *
 * The rows are pets rather than users on purpose: a fixture borrowed from a real page would make it
 * tempting to assert that page's rules here, and this component knows nothing about them.
 */

interface Pet {
  id: string;
  name: string;
  kind: string;
  age: number;
}

const PETS: Pet[] = [
  { id: "1", name: "Iris", kind: "Cat", age: 3 },
  { id: "2", name: "Bruno", kind: "Dog", age: 11 },
  { id: "3", name: "Clover", kind: "Rabbit", age: 1 },
  { id: "4", name: "Delta", kind: "Dog", age: 7 },
];

const COLUMNS: DictionaryColumn<Pet>[] = [
  { key: "name", label: "Name", sortValue: (p) => p.name.toLowerCase(), render: (p) => p.name },
  { key: "kind", label: "Kind", sortValue: (p) => p.kind, render: (p) => p.kind },
  { key: "age", label: "Age", align: "right", sortValue: (p) => p.age, render: (p) => p.age },
  // No `sortValue`: ordering a column of buttons means nothing, and the header must say so by
  // offering nothing to click.
  { key: "actions", label: "Actions", align: "right", render: (p) => <button>Edit {p.name}</button> },
];

function renderTable(over: Partial<Parameters<typeof DictionaryTable<Pet>>[0]> = {}) {
  return render(
    <DictionaryTable
      label="Pets"
      rows={PETS}
      columns={COLUMNS}
      rowKey={(p) => p.id}
      search={{ label: "Search names", of: (p) => p.name }}
      filters={[{ key: "kind", label: "Kind", options: ["Cat", "Dog", "Rabbit"], valueOf: (p) => p.kind }]}
      empty="No pets yet."
      emptyFiltered="No pets match."
      {...over}
    />,
  );
}

/** The body rows, in the order the table renders them. */
const bodyNames = () =>
  within(screen.getByRole("table", { name: "Pets" }))
    .getAllByRole("row")
    .slice(1)
    .map((r) => (r as HTMLTableRowElement).cells[0].textContent);

describe("filtering", () => {
  it("narrows the rows to the search text, case-insensitively", async () => {
    renderTable();

    await userEvent.type(screen.getByRole("textbox", { name: "Search names" }), "ru");

    expect(bodyNames()).toEqual(["Bruno"]);
  });

  it("shows the search as a removable chip, and removing it restores the rows", async () => {
    renderTable();

    await userEvent.type(screen.getByRole("textbox", { name: "Search names" }), "ru");
    const chip = screen.getByText('Search: "ru"').closest(".MuiChip-root") as HTMLElement;

    await userEvent.click(within(chip).getByTestId("CancelIcon"));

    expect(bodyNames()).toHaveLength(4);
    expect(screen.queryByText('Search: "ru"')).not.toBeInTheDocument();
  });

  it("narrows the rows to a chosen filter value, and chips it", async () => {
    renderTable();

    await userEvent.click(screen.getByRole("combobox", { name: "Kind" }));
    await userEvent.click(screen.getByRole("option", { name: "Dog" }));

    expect(bodyNames()).toEqual(["Bruno", "Delta"]);
    expect(screen.getByText("Kind: Dog")).toBeVisible();
  });

  it("combines search and filter", async () => {
    renderTable();

    await userEvent.click(screen.getByRole("combobox", { name: "Kind" }));
    await userEvent.click(screen.getByRole("option", { name: "Dog" }));
    await userEvent.type(screen.getByRole("textbox", { name: "Search names" }), "del");

    expect(bodyNames()).toEqual(["Delta"]);
  });

  it("drops every chip at once with Clear all", async () => {
    renderTable();

    await userEvent.type(screen.getByRole("textbox", { name: "Search names" }), "o");
    await userEvent.click(screen.getByRole("combobox", { name: "Kind" }));
    await userEvent.click(screen.getByRole("option", { name: "Dog" }));
    expect(bodyNames()).toEqual(["Bruno"]);

    await userEvent.click(screen.getByRole("button", { name: "Clear all" }));

    expect(bodyNames()).toHaveLength(4);
    expect(screen.queryByRole("button", { name: "Clear all" })).not.toBeInTheDocument();
  });

  it("offers Clear all only while something is filtered", () => {
    renderTable();

    expect(screen.queryByRole("button", { name: "Clear all" })).not.toBeInTheDocument();
  });

  it("says the rows were filtered out rather than that there are none", async () => {
    renderTable();

    await userEvent.type(screen.getByRole("textbox", { name: "Search names" }), "zzz");

    expect(screen.getByText("No pets match.")).toBeVisible();
    expect(screen.queryByText("No pets yet.")).not.toBeInTheDocument();
  });

  it("says the dictionary is empty when it holds nothing at all", () => {
    renderTable({ rows: [] });

    expect(screen.getByText("No pets yet.")).toBeVisible();
  });
});

describe("sorting", () => {
  it("sorts ascending on the first click of a header, and descending on the second", async () => {
    renderTable();

    await userEvent.click(screen.getByRole("button", { name: "Name" }));
    expect(bodyNames()).toEqual(["Bruno", "Clover", "Delta", "Iris"]);

    await userEvent.click(screen.getByRole("button", { name: "Name" }));
    expect(bodyNames()).toEqual(["Iris", "Delta", "Clover", "Bruno"]);
  });

  it("sorts numbers as numbers, not as text", async () => {
    renderTable();

    await userEvent.click(screen.getByRole("button", { name: "Age" }));

    // 11 last, which a string sort would put second.
    expect(bodyNames()).toEqual(["Clover", "Iris", "Delta", "Bruno"]);
  });

  it("starts a new column ascending rather than inheriting the previous direction", async () => {
    renderTable();

    await userEvent.click(screen.getByRole("button", { name: "Name" }));
    await userEvent.click(screen.getByRole("button", { name: "Name" }));
    await userEvent.click(screen.getByRole("button", { name: "Age" }));

    expect(bodyNames()).toEqual(["Clover", "Iris", "Delta", "Bruno"]);
  });

  it("marks the sorted column for assistive technology", async () => {
    renderTable();

    await userEvent.click(screen.getByRole("button", { name: "Kind" }));

    const header = screen.getByRole("columnheader", { name: "Kind" });
    expect(header).toHaveAttribute("aria-sort", "ascending");
  });

  it("offers nothing to click on a column with no order", () => {
    renderTable();

    expect(screen.queryByRole("button", { name: "Actions" })).not.toBeInTheDocument();
  });

  it("honours the initial sort a page asks for", () => {
    renderTable({ initialSort: { key: "name", dir: "desc" } });

    expect(bodyNames()).toEqual(["Iris", "Delta", "Clover", "Bruno"]);
  });
});

describe("paging", () => {
  const many: Pet[] = Array.from({ length: 23 }, (_, i) => ({
    id: String(i),
    name: `Pet ${String(i).padStart(2, "0")}`,
    kind: i % 2 === 0 ? "Cat" : "Dog",
    age: i,
  }));

  it("shows the first ten of a long dictionary, and says so", () => {
    renderTable({ rows: many });

    expect(bodyNames()).toHaveLength(10);
    expect(screen.getByText("1–10 of 23")).toBeVisible();
  });

  it("walks to the next page", async () => {
    renderTable({ rows: many });

    await userEvent.click(screen.getByRole("button", { name: "Go to next page" }));

    expect(bodyNames()[0]).toBe("Pet 10");
    expect(screen.getByText("11–20 of 23")).toBeVisible();
  });

  it("stops at the last page, which is short", async () => {
    renderTable({ rows: many });

    await userEvent.click(screen.getByRole("button", { name: "Go to last page" }));

    expect(bodyNames()).toHaveLength(3);
    expect(screen.getByText("21–23 of 23")).toBeVisible();
    expect(screen.getByRole("button", { name: "Go to next page" })).toBeDisabled();
  });

  it("resizes the page from the footer", async () => {
    renderTable({ rows: many });

    await userEvent.click(screen.getByRole("combobox", { name: /rows per page/i }));
    await userEvent.click(screen.getByRole("option", { name: "50" }));

    expect(bodyNames()).toHaveLength(23);
  });

  // The bound that a naive `slice` gets wrong: page 3 of an unfiltered list is past the end of a
  // filtered one, and the person is left staring at an empty table with rows they can see counted
  // in the footer.
  it("falls back to the last page that exists when a filter shortens the list", async () => {
    renderTable({ rows: many });

    await userEvent.click(screen.getByRole("button", { name: "Go to last page" }));
    await userEvent.type(screen.getByRole("textbox", { name: "Search names" }), "Pet 0");

    expect(bodyNames()).toHaveLength(10);
    expect(screen.getByText("1–10 of 10")).toBeVisible();
  });

  it("returns to the first page when the filter changes", async () => {
    renderTable({ rows: many });

    await userEvent.click(screen.getByRole("button", { name: "Go to next page" }));
    await userEvent.click(screen.getByRole("combobox", { name: "Kind" }));
    await userEvent.click(screen.getByRole("option", { name: "Cat" }));

    expect(screen.getByText("1–10 of 12")).toBeVisible();
  });
});

describe("while the dictionary is loading", () => {
  it("says so in the body, in the word the screenshot pass waits on", () => {
    renderTable({ rows: undefined, loading: true });

    expect(screen.getByText("Loading…")).toBeVisible();
    expect(screen.queryByText("No pets yet.")).not.toBeInTheDocument();
  });
});

describe("the toolbar's action slot", () => {
  it("renders the table's own writes beside the filters", async () => {
    const onNew = vi.fn();
    renderTable({ actions: <button onClick={onNew}>New pet</button> });

    await userEvent.click(screen.getByRole("button", { name: "New pet" }));

    expect(onNew).toHaveBeenCalled();
  });

  // A table with nothing to search and nothing to filter still has a toolbar when it has a New
  // button — otherwise the one way to create a row would have nowhere to render.
  it("brings the toolbar with it on a table that has neither search nor filters", () => {
    renderTable({ search: undefined, filters: undefined, actions: <button>New pet</button> });

    expect(screen.getByRole("button", { name: "New pet" })).toBeVisible();
  });
});
