// The component overrides (P1T-160), held two ways.
//
// **Rendered, not configured.** Half of these could be written as `expect(theme.components.MuiX
// .defaultProps)…` and would then pass while the app looked wrong — slice 1's focus ring emitted
// perfect CSS and rendered nothing, because a competing class won on source order. So the checks
// that matter render the real component under the real theme and read `getComputedStyle`, the same
// approach `CvSheet.lightLock.test.tsx` takes and for the same reason. jsdom does not implement
// media queries, but it does resolve class declarations and inheritance.
//
// **Contrast against composites.** The overrides introduce colour pairings the token layer could
// not have checked, because they are computed: an `Alert`'s panel is 14% of a semantic role over
// whichever of the three surfaces it lands on. `tokens.contrast.test.ts` asserts the tokens; this
// asserts what the overrides make out of them. The WCAG helpers were copied into this file rather
// than imported, because importing a `*.test.ts` module re-registers its assertions inside this
// file's suite; P1T-163 needed them a third time and moved them to `src/test/contrast.ts`, which is
// a plain module with no suite to leak. Nothing about the assertions below changed.
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  Alert,
  Button,
  Chip,
  Dialog,
  DialogContent,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  ThemeProvider,
} from "@mui/material";
import type { Theme } from "@mui/material/styles";
import { darkTheme, lightTheme } from "./index";
import { tokens } from "./tokens";
import type { ThemeModeTokens } from "./tokens";
import { contrastRatio, over, rgb } from "../test/contrast";

const modes: [string, Theme, ThemeModeTokens][] = [
  ["light", lightTheme, tokens.modes.light],
  ["dark", darkTheme, tokens.modes.dark],
];

/** Renders `node` under one of the two real themes and hands back the computed styles reader. */
function styleOf(theme: Theme, node: React.ReactElement, selector: string): CSSStyleDeclaration {
  const { container } = render(<ThemeProvider theme={theme}>{node}</ThemeProvider>);
  // `document` rather than `container`, because an overlay renders into a portal on `body`.
  const el = container.querySelector(selector) ?? document.body.querySelector(selector);
  expect(el, `nothing matched ${selector}`).not.toBeNull();
  return window.getComputedStyle(el as Element);
}

describe.each(modes)("%s mode — surfaces separate with a border, not a shadow", (_m, theme, t) => {
  it("gives a plain Paper the hairline and no elevation, with no page asking for it", () => {
    const s = styleOf(theme, <Paper>panel</Paper>, ".MuiPaper-root");
    expect(s.borderColor).toBe(rgb(t.divider));
    expect(s.borderWidth).toBe("1px");
    // `variant="outlined"` emits no `box-shadow` at all, which is the point: there is nothing to
    // override later and nothing to un-animate.
    expect(s.boxShadow === "" || s.boxShadow === "none").toBe(true);
  });

  it("makes a `well` a flat fill — no hairline on a coloured surface, no dark-mode overlay", () => {
    const s = styleOf(theme, <Paper variant="well">bubble</Paper>, ".MuiPaper-root");
    expect(s.backgroundColor).toBe(rgb(t.surface.raised));
    expect(s.borderWidth === "" || s.borderWidth === "0px").toBe(true);
    expect(s.boxShadow).toBe("none");
    expect(s.backgroundImage).toBe("none");
  });

  it("reserves the one shadow in the system for a surface that genuinely floats", () => {
    const s = styleOf(
      theme,
      <Dialog open>
        <DialogContent>above everything</DialogContent>
      </Dialog>,
      ".MuiDialog-paper",
    );
    expect(s.boxShadow).toBe(t.overlayShadow);
  });

  it("never emits a second shadow anywhere else in the theme", () => {
    // "Borders separate, shadows are the exception" as a rule the theme cannot break silently:
    // every `boxShadow` any override declares is either the overlay or an explicit removal.
    const shadows: string[] = [];
    const walk = (value: unknown) => {
      if (!value || typeof value !== "object") return;
      for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
        if (key === "boxShadow" && typeof child === "string") shadows.push(child);
        else walk(child);
      }
    };
    walk(theme.components);
    expect(shadows.length).toBeGreaterThan(0);
    for (const shadow of shadows) {
      expect([t.overlayShadow, "none"]).toContain(shadow);
    }
  });
});

describe.each(modes)("%s mode — density is the theme's, not the page's", (_m, theme, t) => {
  it.each([
    ["MuiButton", theme.components?.MuiButton?.defaultProps],
    ["MuiIconButton", theme.components?.MuiIconButton?.defaultProps],
    ["MuiTextField", theme.components?.MuiTextField?.defaultProps],
    ["MuiChip", theme.components?.MuiChip?.defaultProps],
    ["MuiTable", theme.components?.MuiTable?.defaultProps],
    ["MuiAutocomplete", theme.components?.MuiAutocomplete?.defaultProps],
  ])("defaults %s to the small size the pages were writing by hand", (_name, defaults) => {
    expect((defaults as { size?: string } | undefined)?.size).toBe("small");
  });

  it("carries a Table's compact size into its cells through context, not through props", () => {
    const s = styleOf(
      theme,
      <Table>
        <TableBody>
          <TableRow>
            <TableCell>Ada</TableCell>
          </TableRow>
        </TableBody>
      </Table>,
      ".MuiTableCell-root",
    );
    expect(s.padding).toBe("6px 12px");
  });

  it("tokenises the table header rather than leaving it identical to a body row", () => {
    const s = styleOf(
      theme,
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Email</TableCell>
          </TableRow>
        </TableHead>
      </Table>,
      ".MuiTableCell-head",
    );
    expect(s.backgroundColor).toBe(rgb(t.surface.raised));
    expect(s.color).toBe(rgb(t.text.secondary));
    // Header text is text: the pairing has to clear 4.5:1 on the surface it now has.
    expect(contrastRatio(t.text.secondary, t.surface.raised)).toBeGreaterThanOrEqual(4.5);
  });
});

describe.each(modes)("%s mode — the accent is the primary action and nothing else", (_m, theme, t) => {
  it("keeps a contained button accented", () => {
    const s = styleOf(theme, <Button variant="contained">Save</Button>, ".MuiButton-root");
    expect(s.backgroundColor).toBe(rgb(t.primary.main));
    expect(s.color).toBe(rgb(t.primary.contrastText));
  });

  it("turns the secondary actions neutral, and gives them the 3:1 control boundary", () => {
    const outlined = styleOf(theme, <Button variant="outlined">Cancel</Button>, ".MuiButton-root");
    expect(outlined.color).toBe(rgb(t.text.primary));
    expect(outlined.borderColor).toBe(rgb(t.surface.outline));

    const text = styleOf(theme, <Button>Deactivate</Button>, ".MuiButton-root");
    expect(text.color).toBe(rgb(t.text.primary));
  });

  it("leaves a destructive action looking destructive", () => {
    // The neutral treatment is keyed per colour (`outlinedPrimary`), so `color="error"` is untouched
    // — a blanket `outlined` override would have flattened this to grey.
    const s = styleOf(
      theme,
      <Button variant="outlined" color="error">
        Delete
      </Button>,
      ".MuiButton-root",
    );
    expect(s.color).toBe(rgb(t.error.main));
  });

  it("points an input's boundary at `surface.outline`, the token P1T-159 shipped unconsumed", () => {
    // MUI's own notched outline is `rgba(255,255,255,0.23)` — about 2.1:1 — so before this override
    // the app's inputs did not meet WCAG 1.4.11's 3:1, whatever the tokens said.
    const s = styleOf(
      theme,
      <TextField label="Email" />,
      ".MuiOutlinedInput-notchedOutline",
    );
    expect(s.borderColor).toBe(rgb(t.surface.outline));
  });
});

describe.each(modes)("%s mode — a tinted Alert stays readable over any surface", (_m, theme, t) => {
  const surfaces: [string, keyof ThemeModeTokens["surface"]][] = [
    ["background.default", "page"],
    ["background.paper", "surface"],
    ["surface.raised", "raised"],
  ];

  it("paints the panel with the role and leaves the message as body text", () => {
    render(
      <ThemeProvider theme={theme}>
        <Alert severity="error">Something failed.</Alert>
      </ThemeProvider>,
    );
    const alert = screen.getByRole("alert");
    const s = window.getComputedStyle(alert);
    expect(s.color).toBe(rgb(t.text.primary));
    // MUI's standard Alert would have computed this with `lighten(error.light, 0.9)`; this palette's
    // `light` steps are saturated fills, so that formula lands on a colour nobody chose.
    expect(s.backgroundColor).toContain("rgba(");
  });

  it.each(["error", "warning", "info", "success"] as const)(
    "reads the %s message at AA and its icon at 3:1 on all three surfaces",
    (severity) => {
      for (const [, key] of surfaces) {
        const panel = over(t[severity].main, 0.14, t.surface[key]);
        expect(contrastRatio(t.text.primary, panel), `${severity} text on ${key}`).toBeGreaterThanOrEqual(4.5);
        // The icon is the only thing carrying the severity, which makes it a meaningful graphic
        // under 1.4.11 rather than decoration.
        expect(contrastRatio(t[severity].main, panel), `${severity} icon on ${key}`).toBeGreaterThanOrEqual(3);
      }
    },
  );
});

describe("the overrides do not reopen settled decisions", () => {
  it("adds no MuiTabs override, because the app renders no tabs", () => {
    // P1T-152 replaced the dock's ten-tab strip with a grouped Menu. A tab override here would be
    // dead style inviting someone to put tabs back.
    expect(lightTheme.components?.MuiTabs).toBeUndefined();
    expect(darkTheme.components?.MuiTabs).toBeUndefined();
  });

  it("leaves the app bar unstyled beyond not inheriting the outlined box", () => {
    // P1T-161 replaces it with the left rail; restyling it now is work with a half-life.
    expect(lightTheme.components?.MuiAppBar?.styleOverrides?.root).toEqual({ border: 0 });
  });

  it("keeps a Chip's colours alone while taking its size and radius", () => {
    const s = styleOf(
      lightTheme,
      <Chip label="Strong" color="success" variant="outlined" />,
      ".MuiChip-root",
    );
    expect(s.borderColor).not.toBe(rgb(tokens.modes.light.divider));
    expect(s.borderRadius).toBe(`${tokens.radiusSmall}px`);
  });
});
