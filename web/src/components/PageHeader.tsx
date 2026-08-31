import { useEffect, useRef, useState } from "react";
import type { ReactNode, RefObject } from "react";
import { Box, Container, IconButton, Stack, Tooltip, Typography } from "@mui/material";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import { Link as RouterLink } from "react-router-dom";
import { RAIL_TOP_INSET_VAR } from "./useAppRail";

/**
 * How wide a page is allowed to get. Two values, because the app only has two kinds of page.
 *
 * `Container maxWidth="lg"` used to answer this once for everybody from `App.tsx`, and that stopped
 * being right the moment the shell grew two pushing edges: 1200px centred inside whatever the rail
 * and the dock leave over squeezes a nine-column table while the whitespace sits *outside* it.
 * A single global answer cannot be right for both a roster and a sign-in form
 * (`manuals/spa-design-system.md` §5).
 */
export type PageWidth = "wide" | "content";

/**
 * `wide` is for the three tables — roster, catalog, users — which read better the more columns
 * they can spread; the cap only stops a line of table becoming unscannable on a 4K monitor.
 * `content` is for a page that is read rather than scanned: the employee detail form and the CV
 * page, whose sheet caps itself tighter still (820px, §7) and centres inside this.
 */
export const PAGE_MAX_WIDTH: Record<PageWidth, number> = { wide: 1440, content: 1000 };

/**
 * The page's own box: capped by {@link PAGE_MAX_WIDTH}, centred, with the gutters and vertical
 * rhythm that used to come from `App.tsx`'s one `Container`.
 *
 * Exported because the routed-area error fallback is a page too — it renders instead of one — and
 * a `Paper` with no box around it would sit flush against the rail.
 */
export function PageContainer({ width, children }: { width: PageWidth; children: ReactNode }) {
  return (
    <Container
      maxWidth={false}
      sx={{
        maxWidth: PAGE_MAX_WIDTH[width],
        pt: 3,
        pb: 5,
        // On paper the document *is* the page. The cap and the gutters are screen furniture, and
        // leaving them on would inset a printed CV inside margins the printer already applies —
        // the same reasoning that zeroes the shell's two edge paddings in print (P1T-161).
        "@media print": { maxWidth: "none", p: 0 },
      }}
    >
      {children}
    </Container>
  );
}

/**
 * Whether the header has stopped moving — i.e. `position: sticky` has caught it against the top.
 *
 * Measured as the gap between the header and a zero-height sentinel left behind at its place in
 * the flow, rather than as `scrollY > 0`. The two tops agree until the header is actually pinned,
 * so the border appears at the exact moment it means something, and this component never has to
 * know what the top inset currently is — or that the rail's mobile bar exists at all.
 */
function usePinned(
  sentinel: RefObject<HTMLElement>,
  header: RefObject<HTMLElement>,
): boolean {
  const [pinned, setPinned] = useState(false);

  useEffect(() => {
    const read = () => {
      const marker = sentinel.current;
      const strip = header.current;
      if (!marker || !strip) return;
      setPinned(strip.getBoundingClientRect().top - marker.getBoundingClientRect().top > 1);
    };

    read();
    window.addEventListener("scroll", read, { passive: true });
    window.addEventListener("resize", read);
    return () => {
      window.removeEventListener("scroll", read);
      window.removeEventListener("resize", read);
    };
  }, [sentinel, header]);

  return pinned;
}

export interface PageHeaderProps {
  /** Rendered as the page's `<h1>`. A real heading element: the e2e suite asserts by role + name. */
  title: string;
  /** One line under the title — a job title, a count. Not a paragraph; that belongs in the body. */
  subtitle?: ReactNode;
  /** Where "back" goes. Absent on a top-level page, which has the rail instead. */
  backTo?: string;
  backLabel?: string;
  /** The page's primary actions, right-aligned. Pass bare buttons — the slot lays them out. */
  actions?: ReactNode;
  width: PageWidth;
  /** The page body. It shares the header's width declaration, which is the point — see below. */
  children?: ReactNode;
}

/**
 * One heading strip for every routed page: title, optional back link, a right slot for the page's
 * primary actions, sticky with a border that appears once it is pinned.
 *
 * **Why the body comes through here.** The width is a per-page decision, and a header capped at one
 * width above a body capped at another is a defect nobody would notice for months. Passing the body
 * in makes the two physically incapable of disagreeing: one prop, at the top of the page, where it
 * applies. `PageContainer` is exported for the one caller that has a body and no header.
 *
 * The whole strip is print-hidden. It is chrome, not document — printing a CV must not print its
 * own toolbar — and it is the strip that carries the rule, so every control inside it goes with it
 * (`e2e/cv-print.e2e.ts` measures exactly this, in a real Chromium at print media).
 */
export default function PageHeader({
  title,
  subtitle,
  backTo,
  backLabel = "Back",
  actions,
  width,
  children,
}: PageHeaderProps) {
  const sentinel = useRef<HTMLDivElement>(null);
  const header = useRef<HTMLDivElement>(null);
  const pinned = usePinned(sentinel, header);

  return (
    <PageContainer width={width}>
      <Box ref={sentinel} aria-hidden sx={{ height: 0 }} />

      <Stack
        ref={header}
        direction="row"
        alignItems="center"
        spacing={1.5}
        data-pinned={pinned ? "true" : undefined}
        sx={{
          position: "sticky",
          // The rail's narrow-mode top bar covers the first rows of the viewport and publishes how
          // many, exactly as both edges publish how much of the sides they cover. Unset — a rail
          // standing beside the app, or the auth pages with no rail at all — falls back to the top
          // of the viewport on its own, which is the same `var(…, 0px)` contract as the pushes.
          top: `var(${RAIL_TOP_INSET_VAR}, 0px)`,
          // Under the mobile bar, above the page. The bar is the one thing allowed to cover this.
          zIndex: (t) => t.zIndex.appBar - 1,
          // Opaque, or the roster scrolls through it.
          bgcolor: "background.default",
          py: 2,
          mb: 3,
          // Always 1px of border, transparent until pinned: a border that appears on scroll must
          // not also move the page down by a pixel when it does.
          borderBottom: 1,
          borderColor: pinned ? "divider" : "transparent",
          transition: "border-color 150ms ease",
          "@media print": { display: "none" },
        }}
      >
        {/* Back is an icon on the title's own line, not a labelled button stacked above it. Two
            rows would put the page's actions level with the gap between them — visible in slice
            4's first CV-page capture — and stacking would also nest the title inside a second
            `Stack`, which is the element two print specs walk up to from a locator. One flat row
            keeps the strip's outermost element the answer for every control in it. */}
        {backTo && (
          <Tooltip title={backLabel}>
            <IconButton
              component={RouterLink}
              to={backTo}
              aria-label={backLabel}
              // `inherit`, not the default. `MuiIconButton`'s own default is `action.active`, which
              // this palette resolves to flat white in dark mode — louder than the title it sits
              // beside, and not a colour anybody chose. Caught by `CvSheet.lightLock.test.tsx`,
              // which asserts the chrome resolves the *app's* `text.primary`: the icon-only Back
              // silently stopped doing that. Neutral chrome, per slice 2's accent rule.
              color="inherit"
              sx={{ ml: -1 }}
            >
              <ArrowBackIcon />
            </IconButton>
          </Tooltip>
        )}

        <Box sx={{ minWidth: 0, flexGrow: 1 }}>
          <Typography variant="h4" component="h1" noWrap>
            {title}
          </Typography>
          {subtitle && (
            <Typography variant="body2" color="text.secondary" noWrap>
              {subtitle}
            </Typography>
          )}
        </Box>

        {actions && (
          <Box sx={{ display: "flex", alignItems: "center", gap: 1, flexShrink: 0 }}>{actions}</Box>
        )}
      </Stack>

      {children}
    </PageContainer>
  );
}
