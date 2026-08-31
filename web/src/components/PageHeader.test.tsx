import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ThemeProvider } from "@mui/material";
import PageHeader, { PAGE_MAX_WIDTH, PageContainer } from "./PageHeader";
import { RAIL_TOP_INSET_VAR } from "./useAppRail";
import { darkTheme, lightTheme } from "../theme";

// One heading strip for all five pages (P1T-162, `manuals/spa-design-system.md` §5). What is worth
// holding here is the part a page cannot see: that the title is a *heading element* (three e2e
// specs assert `getByRole("heading", { name })`), that the strip is print-hidden as a strip so
// everything inside it goes with it, and that the width a page declares once reaches both the
// header and the body it heads.

function renderHeader(ui: React.ReactElement, theme = lightTheme) {
  return render(
    <ThemeProvider theme={theme}>
      <MemoryRouter>{ui}</MemoryRouter>
    </ThemeProvider>,
  );
}

/** A hex token as jsdom reports a resolved colour, exactly as `CvSheet.lightLock.test.tsx` does. */
function rgb(hex: string): string {
  const h = hex.replace("#", "");
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
  return `rgb(${r}, ${g}, ${b})`;
}

/** The page's own box — the element carrying the width cap. */
function container(): HTMLElement {
  return document.querySelector(".MuiContainer-root") as HTMLElement;
}

/** The sticky heading strip: the root of the header, which is a `Stack`. */
function strip(): HTMLElement {
  return screen.getByRole("heading", { level: 1 }).closest(".MuiStack-root") as HTMLElement;
}

/** Everything emotion has injected so far, whitespace-stripped, as `CvPage.print.test.tsx` reads it. */
function emittedCss(): string {
  return Array.from(document.querySelectorAll("style"))
    .map((s) => s.textContent ?? "")
    .join("\n")
    .replace(/\s+/g, "");
}

/** The `@media print{…}` blocks carrying this element's own generated class. */
function printRulesFor(el: Element): string {
  const classes = Array.from(el.classList).filter((c) => c.startsWith("css-"));
  const css = emittedCss();
  const blocks: string[] = [];
  const opener = /@mediaprint\{/g;
  let match: RegExpExecArray | null;
  while ((match = opener.exec(css)) !== null) {
    let depth = 1;
    let i = match.index + match[0].length;
    for (; i < css.length && depth > 0; i++) {
      if (css[i] === "{") depth++;
      else if (css[i] === "}") depth--;
    }
    const block = css.slice(match.index, i);
    if (classes.some((c) => block.includes(`.${c}`))) blocks.push(block);
  }
  return blocks.join("\n");
}

afterEach(() => vi.restoreAllMocks());

describe("PageHeader", () => {
  it("renders the title as the page's h1, not as styled text", () => {
    renderHeader(<PageHeader title="CVs" width="wide" />);

    // The frozen accessible name, reached the way `e2e/auth.e2e.ts` reaches it.
    expect(screen.getByRole("heading", { level: 1, name: "CVs" })).toBeInTheDocument();
  });

  it("puts the actions in the strip and the body under it", () => {
    renderHeader(
      <PageHeader title="CVs" width="wide" actions={<button>New CV</button>}>
        <div>the roster</div>
      </PageHeader>,
    );

    expect(strip()).toContainElement(screen.getByRole("button", { name: "New CV" }));
    expect(strip()).not.toContainElement(screen.getByText("the roster"));
  });

  it("renders the back link as a link, with a subtitle beside the title", () => {
    renderHeader(
      <PageHeader title="CV" subtitle="Principal Engineer" backTo="/employees/7" width="content" />,
    );

    // `e2e/cv-print.e2e.ts` finds this by role + /back/i and then walks up to the strip, so both
    // halves of that locator are asserted here: it is a link, and the strip is its Stack ancestor.
    const back = screen.getByRole("link", { name: /back/i });
    expect(back).toHaveAttribute("href", "/employees/7");
    expect(back.closest(".MuiStack-root")).toBe(strip());
    expect(screen.getByText("Principal Engineer")).toBeInTheDocument();
  });

  it("hides the whole strip in print, which is what takes every control in it off the paper", () => {
    renderHeader(
      <PageHeader title="CV" backTo="/employees/7" width="content" actions={<button>Print</button>}>
        <div>the sheet</div>
      </PageHeader>,
    );

    // The rule is on the strip, not on each control: `display` does not inherit, but layout does —
    // a button inside a `display: none` parent takes no space on the page (P1T-154, P1T-164).
    expect(printRulesFor(strip())).toContain("display:none");
    // …and the page's box is not hidden with it, or the sheet would never print at all.
    expect(printRulesFor(container())).not.toContain("display:none");
  });

  it("sticks under whatever the rail says it is covering, rather than guessing", () => {
    renderHeader(<PageHeader title="CVs" width="wide" />);

    expect(getComputedStyle(strip()).position).toBe("sticky");
    // Read from the rail's published property with a zero fallback — the same contract as the two
    // side pushes, so the auth pages and a rail standing beside the app both need no special case.
    expect(getComputedStyle(strip()).top).toBe(`var(${RAIL_TOP_INSET_VAR}, 0px)`);
  });

  it("shows no border at rest and one once the strip is actually pinned", () => {
    renderHeader(<PageHeader title="CVs" width="wide" />);

    // Transparent rather than absent: the border is always 1px, so gaining it moves nothing.
    expect(getComputedStyle(strip()).borderBottomColor).toBe("rgba(0, 0, 0, 0)");
    expect(strip()).not.toHaveAttribute("data-pinned");

    // Pinned is measured as the gap between the strip and the zero-height sentinel left at its
    // place in the flow — so jsdom only has to disagree about two rects, and the component never
    // needs to know what the current top inset is.
    vi.spyOn(Element.prototype, "getBoundingClientRect").mockImplementation(function (
      this: Element,
    ) {
      const top = this.classList.contains("MuiStack-root") ? 0 : -120;
      return { top } as DOMRect;
    });
    act(() => {
      window.dispatchEvent(new Event("scroll"));
    });

    expect(strip()).toHaveAttribute("data-pinned", "true");
    expect(getComputedStyle(strip()).borderBottomColor).toBe(rgb(lightTheme.palette.divider));
  });

  it("is opaque in both modes, or the page scrolls through the pinned strip", () => {
    const { unmount } = renderHeader(<PageHeader title="CVs" width="wide" />);
    expect(getComputedStyle(strip()).backgroundColor).toBe(
      rgb(lightTheme.palette.background.default),
    );
    unmount();

    renderHeader(<PageHeader title="CVs" width="wide" />, darkTheme);
    expect(getComputedStyle(strip()).backgroundColor).toBe(
      rgb(darkTheme.palette.background.default),
    );
  });
});

describe("per-page width", () => {
  it("caps a table page at the wide measure", () => {
    renderHeader(<PageHeader title="CVs" width="wide" />);

    expect(getComputedStyle(container()).maxWidth).toBe(`${PAGE_MAX_WIDTH.wide}px`);
  });

  it("caps a read-me page tighter", () => {
    renderHeader(<PageHeader title="Ada Lovelace" width="content" />);

    expect(getComputedStyle(container()).maxWidth).toBe(`${PAGE_MAX_WIDTH.content}px`);
  });

  it("is one declaration for the header and the body, which is why the body comes through here", () => {
    renderHeader(
      <PageHeader title="CVs" width="wide">
        <div>the roster</div>
      </PageHeader>,
    );

    // The header cannot be capped at one width and the table it heads at another: there is one
    // container, and both are inside it.
    expect(container()).toContainElement(strip());
    expect(container()).toContainElement(screen.getByText("the roster"));
  });

  it("gives a page with no header the same box, for the error fallback that replaces one", () => {
    renderHeader(
      <PageContainer width="content">
        <div>this page stopped working</div>
      </PageContainer>,
    );

    expect(getComputedStyle(container()).maxWidth).toBe(`${PAGE_MAX_WIDTH.content}px`);
    expect(container()).toContainElement(screen.getByText("this page stopped working"));
  });
});
