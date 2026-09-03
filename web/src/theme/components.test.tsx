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
import { contrastRatio, over, rgb, rgbaOver } from "../test/contrast";

const modes: [string, Theme, ThemeModeTokens][] = [
  ["light", lightTheme, tokens.modes.light],
  ["dark", darkTheme, tokens.modes.dark],
];

/**
 * jsdom re-serialises a multi-layer `box-shadow` without the space after the layer comma, so a
 * relief pair never compares equal to the token it came from until both are normalised. The
 * assertion is about the shadow, not about whitespace.
 */
function shadow(value: string): string {
  return value.replace(/,\s*/g, ", ");
}

/** Renders `node` under one of the two real themes and hands back the computed styles reader. */
function styleOf(theme: Theme, node: React.ReactElement, selector: string): CSSStyleDeclaration {
  const { container } = render(<ThemeProvider theme={theme}>{node}</ThemeProvider>);
  // `document` rather than `container`, because an overlay renders into a portal on `body`.
  const el = container.querySelector(selector) ?? document.body.querySelector(selector);
  expect(el, `nothing matched ${selector}`).not.toBeNull();
  return window.getComputedStyle(el as Element);
}

describe.each(modes)("%s mode — surfaces separate with relief, not a border", (_m, theme, t) => {
  it("extrudes a plain Paper out of its ground, with no page asking for it", () => {
    const s = styleOf(theme, <Paper>panel</Paper>, ".MuiPaper-root");
    // The inversion: this used to be `variant="outlined"` and a hairline, on the grounds that a
    // shadow separates nothing on a near-black page. Relief is the mechanism now, so a panel that
    // asks for nothing is lifted — and carries no border, because a rim around an extrusion reads
    // as a sticker on a card.
    expect(shadow(s.boxShadow)).toBe(shadow(t.relief.extruded));
    expect(s.borderWidth === "" || s.borderWidth === "0px").toBe(true);
    // MUI's `elevation` variant paints a white overlay gradient in dark mode on top of whatever
    // shadow it computes. Both are ours now.
    expect(s.backgroundImage).toBe("none");
  });

  it("presses a `well` into its parent — the inset half, and a darker fill to sell it", () => {
    const s = styleOf(theme, <Paper variant="well">bubble</Paper>, ".MuiPaper-root");
    expect(shadow(s.boxShadow)).toBe(shadow(t.relief.inset));
    // The page ground, not `surface.raised`: a pressed surface shows what is *under* the panel, and
    // `raised` is now the flat level-three fill rather than a step on a ramp.
    expect(s.backgroundColor).toBe(rgb(t.surface.page));
    expect(s.borderWidth === "" || s.borderWidth === "0px").toBe(true);
    expect(s.backgroundImage).toBe("none");
  });

  it("flattens the third level, which is Relief Depth as a rule the DOM enforces", () => {
    // The ceiling the ADR priced: two levels, then a flat fill and a hairline. MUI cannot tell a
    // component its own nesting depth — but a descendant selector can, so the theme carries the
    // rule instead of every call site remembering it. This is the check that a Paper three deep
    // stops being relief no matter which variants got it there.
    const { container } = render(
      <ThemeProvider theme={theme}>
        <Paper data-testid="one">
          <Paper data-testid="two">
            <Paper data-testid="three">deep</Paper>
          </Paper>
        </Paper>
      </ThemeProvider>,
    );
    const at = (id: string) =>
      window.getComputedStyle(container.querySelector(`[data-testid="${id}"]`) as Element);

    expect(shadow(at("one").boxShadow)).toBe(shadow(t.relief.extruded));
    expect(shadow(at("two").boxShadow)).toBe(shadow(t.relief.extruded));

    const third = at("three");
    expect(third.boxShadow).toBe("none");
    expect(third.backgroundColor).toBe(rgb(t.surface.raised));
    expect(third.borderColor).toBe(rgb(t.divider));
    expect(third.borderWidth).toBe("1px");
  });

  it("keeps a Well from carrying relief inside it, whatever the depth says", () => {
    // Inset inside inset has no physical reading at all — it is the case the two-level rule exists
    // for, and it is reachable at level two, before the descendant count would catch it.
    const { container } = render(
      <ThemeProvider theme={theme}>
        <Paper variant="well" data-testid="outer">
          <Paper data-testid="inner">nested</Paper>
        </Paper>
      </ThemeProvider>,
    );
    const inner = window.getComputedStyle(
      container.querySelector('[data-testid="inner"]') as Element,
    );
    expect(inner.boxShadow).toBe("none");
    expect(inner.borderWidth).toBe("1px");
  });

  it("counts a Float as a level, which is what decides the dock's own Wells", () => {
    // The audit the ADR asked for, as a rule rather than a list. The agent dock is a Float
    // (`elevation={8}` floating, `4` docked), and it is a surface — so a Well sitting directly on
    // it is level two and stays pressed in, while a Well inside a tab's own card is level three
    // and goes flat. That is the whole difference between the dock's chat bubbles, which keep
    // their relief, and the proposal inbox's package views, which do not.
    const { container } = render(
      <ThemeProvider theme={theme}>
        <Paper elevation={8}>
          <Paper variant="well" data-testid="bubble">
            on the dock itself
          </Paper>
          <Paper data-testid="card">
            <Paper variant="well" data-testid="inside-card">
              inside a tab's card
            </Paper>
          </Paper>
        </Paper>
      </ThemeProvider>,
    );
    const at = (id: string) =>
      window.getComputedStyle(container.querySelector(`[data-testid="${id}"]`) as Element);

    expect(shadow(at("bubble").boxShadow)).toBe(shadow(t.relief.inset));
    expect(at("inside-card").boxShadow).toBe("none");
    expect(at("inside-card").borderWidth).toBe("1px");
  });

  it("floats a dialog on the largest relief, plus the backdrop that says `above`", () => {
    const s = styleOf(
      theme,
      <Dialog open>
        <DialogContent>above everything</DialogContent>
      </Dialog>,
      ".MuiDialog-paper",
    );
    expect(shadow(s.boxShadow)).toBe(shadow(t.relief.float));
    // Extrusion is ordinary now — every panel has it — so Float needs a second signal.
    expect(
      s.getPropertyValue("backdrop-filter") || s.getPropertyValue("-webkit-backdrop-filter"),
    ).toBe(t.relief.floatBackdrop);
  });

  it("speaks only the relief vocabulary, so no override can invent a shadow", () => {
    // The rule that replaces "there is one shadow and it is the overlay": every `boxShadow` this
    // theme declares is one of the mode's own relief tokens or an explicit removal. A hand-rolled
    // `0 2px 4px rgba(0,0,0,.2)` in some override is exactly what this catches.
    const declared: string[] = [];
    const walk = (value: unknown) => {
      if (!value || typeof value !== "object") return;
      for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
        if (key === "boxShadow" && typeof child === "string") declared.push(child);
        else walk(child);
      }
    };
    walk(theme.components);
    const vocabulary = [
      t.relief.extruded,
      t.relief.extrudedSmall,
      t.relief.inset,
      t.relief.insetSmall,
      t.relief.float,
      "none",
      // The print floor in `baseline.ts`, which is the one shadow declaration that has to outrank
      // a component's own — it is a floor, not a look.
      "none !important",
    ];
    expect(declared.length).toBeGreaterThan(0);
    for (const value of declared) expect(vocabulary).toContain(value);
  });

  it("prints nothing raised, because relief on paper is a grey smudge", () => {
    // The floor joins the others in `baseline.ts` rather than being repeated per component: the
    // rail and the dock are already print-hidden, but every page's own cards are not. jsdom can
    // only prove the rule is emitted — `e2e/print.e2e.ts` is where the cascade is watched winning.
    const baseline = theme.components?.MuiCssBaseline?.styleOverrides as Record<
      string,
      Record<string, Record<string, unknown>>
    >;
    expect(baseline["@media print"]["*, *::before, *::after"].boxShadow).toBe("none !important");
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

  it("sets the table header as an Eyebrow: mono, uppercase, and still readable", () => {
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
    // The Eyebrow treatment, which is what promoted mono to a UI role in slice 1: a column name is
    // a label for a column of values, not prose.
    expect(s.fontFamily).toBe(tokens.type.fontFamilyMono);
    expect(s.textTransform).toBe("uppercase");
    // Header text is still text: the pairing has to clear 4.5:1 on the surface it has.
    expect(contrastRatio(t.text.secondary, t.surface.raised)).toBeGreaterThanOrEqual(4.5);
  });

  it("washes a hovered row in the accent rather than in grey, and keeps the row legible", () => {
    // The one place the accent is allowed to touch a whole row. It is a wash, so what matters is
    // the composite: `text.primary` has to survive amber at low alpha over the panel.
    const hover = (
      theme.components?.MuiTableRow?.styleOverrides?.root as Record<string, Record<string, string>>
    )["&.MuiTableRow-hover:hover"];
    expect(hover.backgroundColor).toContain("245, 158, 11");
    expect(
      contrastRatio(t.text.primary, rgbaOver(hover.backgroundColor, t.surface.surface)),
    ).toBeGreaterThanOrEqual(4.5);
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

  it("extrudes a button at rest and presses it in on `:active`", () => {
    const s = styleOf(theme, <Button variant="contained">Save</Button>, ".MuiButton-root");
    expect(shadow(s.boxShadow)).toBe(shadow(t.relief.extrudedSmall));

    // jsdom resolves no `:active`, so the press is read off the override the theme declares. The
    // transform is the half that makes it feel physical; the shadow alone reads as a colour change.
    const root = theme.components?.MuiButton?.styleOverrides?.root as Record<
      string,
      Record<string, string>
    >;
    expect(root["&:active"].boxShadow).toBe(t.relief.insetSmall);
    expect(root["&:active"].transform).toBe("translateY(2px)");
  });

  it("sinks an input into the panel, and keeps a boundary that can be measured", () => {
    // Neumorphism's 1.4.11 failure, refused: the prototype's own inputs are
    // `border: 1px solid transparent` and rely on the shadow, which has no measurable edge.
    const s = styleOf(theme, <TextField label="Email" />, ".MuiOutlinedInput-root");
    expect(shadow(s.boxShadow)).toBe(shadow(t.relief.insetSmall));
    expect(s.backgroundColor).toBe(rgb(t.surface.page));
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
    // Plus the one thing relief added to the list of things it must not inherit: a bar spanning
    // the viewport is part of the ground, not a card lifted off it.
    expect(lightTheme.components?.MuiAppBar?.styleOverrides?.root).toEqual({
      border: 0,
      boxShadow: "none",
    });
  });

  it("gives a Chip the tag treatment — mono, pill, and its role's colour kept", () => {
    const s = styleOf(
      lightTheme,
      <Chip label="Strong" color="success" variant="outlined" />,
      ".MuiChip-root",
    );
    // Still keyed per role: a blanket override here would flatten all five colours to one grey.
    expect(s.borderColor).not.toBe(rgb(tokens.modes.light.divider));
    expect(s.fontFamily).toBe(tokens.type.fontFamilyMono);
    expect(s.textTransform).toBe("uppercase");
    // A pill, which is what a tag is — the radius steps are for panels and controls.
    expect(s.borderRadius).toBe("999px");
  });

  it.each(modes)("%s mode fills a tag with its role at low alpha, inked at AA", (_m, theme, t) => {
    // The prototype's tag: a wash of the role, and the role's own readable step as the label. The
    // pairing is a composite, so it is asserted as one — amber in light mode is the case that
    // forces the readable step to be `dark` rather than `main`.
    for (const name of ["primary", "success", "warning", "error"] as const) {
      const s = styleOf(
        theme,
        <Chip label={name} color={name} />,
        ".MuiChip-root",
      );
      // Both are read back as `rgb()`/`rgba()` and resolved over the panel: a tag's label sits on
      // its own wash, so the pairing that matters is ink-over-wash-over-surface.
      const ground = rgbaOver(s.backgroundColor, t.surface.surface);
      expect(
        contrastRatio(rgbaOver(s.color, ground), ground),
        `${name} tag label`,
      ).toBeGreaterThanOrEqual(4.5);
    }
  });
});
