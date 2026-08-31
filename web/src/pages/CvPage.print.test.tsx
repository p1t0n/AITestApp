import { readFileSync } from "node:fs";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import CvPage from "./CvPage";
import type { Cv } from "../types";
import { printBlockFor } from "../test/printCss";

// The print rules used to live in `web/src/index.css`, keyed by `#cv-sheet` and `.no-print` —
// strings CvPage owns but never declared, so renaming either would have broken the printed CV
// silently, visible only in a print preview (P1T-154). They now travel in the components' own
// `sx`, and these tests hold that: they read the CSS the page actually emits and check the print
// block is attached to the element's own class.

const cv: Cv = {
  fullName: "Ada Lovelace",
  title: "Principal Engineer",
  email: "ada@example.com",
  phone: null,
  location: null,
  summary: "Analytical engine specialist.",
  photoUrl: null,
  availability: { currentCapacityPercent: 100, schedule: [] },
  skillGroups: [],
  languages: [],
  experiences: [],
  education: [],
  certifications: [],
};

vi.mock("../api", () => ({
  useCv: () => ({ data: cv, isLoading: false }),
  useDownloadCvPdf: () => ({ mutate: vi.fn(), isPending: false }),
}));

beforeEach(() => {
  render(
    <MemoryRouter>
      <CvPage />
    </MemoryRouter>,
  );
});

describe("CV print styling", () => {
  it("flattens the sheet onto the page: no elevation shadow, no centring margin", () => {
    const sheet = document.getElementById("cv-sheet");
    expect(sheet).not.toBeNull();

    const block = printBlockFor(sheet!);

    expect(block).toBeDefined();
    expect(block).toContain("box-shadow:none");
    expect(block).toContain("margin:0");
  });

  it("hides the page's own toolbar, which is chrome rather than document", () => {
    // "Back" is a direct child of the toolbar row; the print/download pair sits in a nested Stack.
    const toolbar = screen.getByRole("link", { name: /back/i }).closest(".MuiStack-root");
    expect(toolbar).not.toBeNull();

    expect(printBlockFor(toolbar!)).toContain("display:none");
  });

  it("leaves the global stylesheet nothing to say about this page's DOM", () => {
    // Read from disk: `index.css` is imported by `main.tsx`, so it is not in this document at all.
    // If a selector CvPage owns reappears there, the silent coupling is back.
    const globalCss = readFileSync("src/index.css", "utf8"); // vitest runs from web/
    const rules = globalCss.replace(/\/\*[\s\S]*?\*\//g, "");

    expect(rules).not.toContain("#cv-sheet");
    expect(rules).not.toContain(".no-print");
  });
});
