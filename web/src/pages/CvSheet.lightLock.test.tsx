// The light-lock (P1T-164, pinned for real in P1T-197): the CV Sheet renders under its **own**
// theme whatever mode the app is in, because what a client receives cannot depend on which Theme
// Mode the operator happened to be using — or on what the app's palette did this quarter. These
// tests hold that boundary from *inside* a dark app; the failure they prevent is silent and
// client-facing.
//
// **Every colour below is a literal.** That is the whole repair. This file used to assert against
// `tokens.modes.light.*`, which made it a mirror rather than a lock: the neumorphic reversal
// (P1T-193) re-pointed the light surface at `#EEF1F8` and the accent at amber, which would have
// turned a client-facing document grey with amber headings — and every assertion here would have
// stayed green while it happened. A lock asserts colours, not roles (`CONTEXT.md`, **Light Lock**).
//
// Asserted on resolved colour, not on the emitted CSS. jsdom does not implement media queries
// (which is why `CvPage.print.test.tsx` reads emitted CSS strings for the print rules), but it does
// resolve class-based declarations and inheritance through `getComputedStyle` — so the question
// "what colour is this text actually" is answerable here, and it is the only question that matters.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { CssBaseline, ThemeProvider } from "@mui/material";
import CvPage from "./CvPage";
import { darkTheme } from "../theme";
import { tokens } from "../theme/tokens";
import type { Cv } from "../types";

/** `#RRGGBB` → the `rgb(r, g, b)` form every browser and jsdom report computed colours in. */
function rgb(hex: string): string {
  const h = hex.replace("#", "");
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
  return `rgb(${r}, ${g}, ${b})`;
}

/**
 * What the sheet is, stated once and by hand. These are `cvSheetTheme`'s values written out again
 * rather than imported from it: a spec that reads the constant it is checking cannot fail when that
 * constant moves, which is exactly the hole this file had. If a change to `cvSheet.ts` is
 * deliberate, this list is the second signature it has to collect.
 */
const SHEET = {
  paper: "#FFFFFF",
  text: "#101418",
  secondary: "#566070",
  accent: "#2453D4",
};

const dark = tokens.modes.dark;

// Enough on the CV that every colour path in the sheet is exercised: an unstyled heading, a
// `text.secondary` line, an accented section label, a bullet list, and both Chip variants.
const cv: Cv = {
  fullName: "Ada Lovelace",
  title: "Principal Engineer",
  email: "ada@example.com",
  phone: null,
  location: "London",
  summary: "Analytical engine specialist.",
  photoUrl: null,
  availability: { currentCapacityPercent: 100, schedule: [] },
  skillGroups: [],
  languages: [],
  experiences: [
    {
      title: "Mathematician",
      company: "Analytical Engine Programme",
      period: "1842 – 1843",
      summary: "Wrote the notes.",
      achievements: [{ id: "a1", text: "Described the first algorithm intended for a machine." }],
      skills: ["Mathematics"],
    },
  ],
  education: [],
  certifications: [],
} as unknown as Cv;

vi.mock("../api", () => ({
  useCv: () => ({ data: cv, isLoading: false }),
  useDownloadCvPdf: () => ({ mutate: vi.fn(), isPending: false }),
}));

/** The app in dark mode, which is the only mode this file is interested in. */
beforeEach(() => {
  render(
    <ThemeProvider theme={darkTheme}>
      <CssBaseline />
      <MemoryRouter>
        <CvPage />
      </MemoryRouter>
    </ThemeProvider>,
  );
});

function sheet(): HTMLElement {
  const el = document.getElementById("cv-sheet");
  expect(el, "the sheet is located by the id the e2e suite also uses").not.toBeNull();
  return el as HTMLElement;
}

describe("the CV sheet is light-locked while the app is dark", () => {
  it("puts white paper under the sheet — not the app's dark surface, and not its light one", () => {
    expect(getComputedStyle(sheet()).backgroundColor).toBe(rgb(SHEET.paper));
    // The second half is the pin: light mode's own paper is `#EEF1F8` now, and a sheet that
    // followed it would be a grey CV. The sheet reads as a whiter card on a grey page, on purpose.
    expect(getComputedStyle(sheet()).backgroundColor).not.toBe(
      rgb(tokens.modes.light.surface.surface),
    );
  });

  it("carries the light text colour on the sheet itself, so its contents inherit it", () => {
    // The whole change rests on this one declaration. MUI's `Paper` root sets `color:
    // text.primary` alongside `background.paper`, so the nested provider re-colours the text that
    // names no palette key — which is most of the sheet.
    expect(getComputedStyle(sheet()).color).toBe(rgb(SHEET.text));
  });

  it("actually resolves dark text on the unstyled heading, not the app's near-white", () => {
    // The failure mode this exists for: a nested provider that re-themed only the elements naming
    // a palette key would leave this heading inheriting `body`, and near-white on white paper is
    // an invisible CV rather than a merely wrong-looking one.
    const heading = screen.getByRole("heading", { name: "Ada Lovelace" });

    expect(getComputedStyle(heading).color).toBe(rgb(SHEET.text));
    expect(getComputedStyle(heading).color).not.toBe(rgb(dark.text.primary));
  });

  it("resolves the light secondary and accent roles inside the sheet", () => {
    expect(getComputedStyle(screen.getByText("Principal Engineer")).color).toBe(
      rgb(SHEET.secondary),
    );
    // The section labels are `color="primary"` — and this is the other thing the pin holds. The
    // app's accent is amber in both modes now; the sheet's stays the blue the document was made
    // with, because a client-facing artifact does not get restyled by a theme decision.
    expect(getComputedStyle(screen.getByText("Experience")).color).toBe(rgb(SHEET.accent));
    expect(getComputedStyle(screen.getByText("Experience")).color).not.toBe(
      rgb(tokens.modes.light.primary.main),
    );
  });

  it("leaves the page chrome in the app's dark theme", () => {
    // The other half of the boundary. The toolbar is chrome, it never reaches paper (it is
    // print-hidden), and re-theming it would make a dark app look broken around a light sheet.
    // Asserted on `text.primary` rather than the accent: P1T-160 made a `text` button neutral
    // chrome (accent belongs to the primary action alone), so the accent is no longer what
    // distinguishes the two themes here — the text role is, and it still differs per mode.
    const back = screen.getByRole("link", { name: /back/i });

    expect(getComputedStyle(back).color).toBe(rgb(dark.text.primary));
    expect(getComputedStyle(back).color).not.toBe(rgb(SHEET.text));
  });

  it("keeps the sheet's own theme out of the app's, and the app's out of the sheet", () => {
    // The pin is a nested provider, not a global: nothing about the sheet may leak upward. This is
    // the assertion that would fail if `cvSheetTheme` were ever applied at the root "for
    // consistency" — which would silently light-lock the whole app.
    expect(getComputedStyle(document.body).backgroundColor).not.toBe(rgb(SHEET.paper));
  });

  it("confirms the app around it really is dark — otherwise this file proves nothing", () => {
    // A guard on the fixture rather than on the app: if `CssBaseline` or the dark theme stopped
    // applying, every assertion above would pass for the wrong reason.
    expect(getComputedStyle(document.body).color).toBe(rgb(dark.text.primary));
    expect(getComputedStyle(document.body).backgroundColor).toBe(rgb(dark.surface.page));
  });
});
