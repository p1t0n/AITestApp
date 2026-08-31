import { Link as RouterLink } from "react-router-dom";
import { Box, Link } from "@mui/material";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

// Matches a GUID anywhere in the text. The agents cite employees by name + id, so we turn those
// ids into links to the employee detail page.
const GUID = /[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}/g;

// Turn bare employee ids into markdown links so they render as navigable links in the answer.
function linkifyGuids(markdown: string): string {
  return markdown.replace(GUID, (id) => `[${id}](/employees/${id})`);
}

/** Renders an agent answer as GitHub-flavoured markdown (headings, lists, tables), with employee
 * ids linkified to their detail page.
 *
 * Every rule here is about one thing: an agent's answer is arbitrary-length markdown arriving in a
 * 360px column, and until P1T-163 only inline `code` had been styled at all. A fenced block, a
 * six-column table or a linkified GUID each overflowed the panel horizontally — the answer was
 * there and unreadable, which is the same failure as not having it. */
export function AgentMarkdown({ text }: { text: string }) {
  return (
    <Box
      sx={{
        fontSize: 14,
        lineHeight: 1.5,
        // A linkified GUID is 36 unbreakable characters, and the panel's minimum column is 360px
        // wide with a bubble inside it. Wrapping anywhere is what keeps an id inside the bubble
        // instead of pushing the whole transcript sideways.
        overflowWrap: "anywhere",
        "& p": { mt: 0, mb: 1 },
        "& ul, & ol": { mt: 0, mb: 1, pl: 2.5 },
        "& h1, & h2, & h3": { fontSize: "1rem", fontWeight: 700, mt: 1.5, mb: 0.5 },
        "& th, & td": { border: 1, borderColor: "divider", px: 0.75, py: 0.25, textAlign: "left" },
        // The same head treatment the app's own tables get from `MuiTableCell` — a markdown table
        // in the dock should not look like a different product from the roster behind it.
        "& th": { bgcolor: "surface.raised", color: "text.secondary", fontWeight: 600, whiteSpace: "nowrap" },
        // The mono family itself comes from the baseline's `code, kbd, samp, pre` rule, so this
        // says only what makes a code *span* read as one inside a sentence.
        "& code": { bgcolor: "surface.raised", px: 0.5, borderRadius: 0.75, fontSize: "0.85em" },
        // A fenced block had no rule of its own at all: it inherited the inline span's fill through
        // its `<code>` child and then scrolled the panel, because `pre` does not wrap. It is a well
        // that scrolls *itself* now, and the inner `code` stops double-painting the fill.
        "& pre": {
          my: 1,
          p: 1,
          bgcolor: "surface.raised",
          borderRadius: 0.75,
          overflowX: "auto",
        },
        "& pre code": { bgcolor: "transparent", p: 0, fontSize: "0.8125rem" },
        "& blockquote": {
          my: 1,
          ml: 0,
          pl: 1.5,
          borderLeft: 2,
          borderColor: "divider",
          color: "text.secondary",
        },
        "& hr": { border: 0, borderTop: 1, borderColor: "divider", my: 1.5 },
      }}
    >
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          a: ({ href, children }) =>
            href?.startsWith("/") ? (
              <Link component={RouterLink} to={href}>
                {children}
              </Link>
            ) : (
              <Link href={href} target="_blank" rel="noopener noreferrer">
                {children}
              </Link>
            ),
          // Scrolled by a wrapper rather than by `display: block` on the table itself, which is the
          // usual fix and drops the table's own role in Chrome — a scrollbar bought by deleting the
          // element's semantics is not a fix. `max-content` lets a wide table overflow the wrapper
          // (so it scrolls) while `min-width: 100%` keeps a two-column one full-bleed.
          table: ({ children }) => (
            <Box sx={{ my: 1, overflowX: "auto" }}>
              <Box
                component="table"
                sx={{ borderCollapse: "collapse", width: "max-content", minWidth: "100%" }}
              >
                {children}
              </Box>
            </Box>
          ),
        }}
      >
        {linkifyGuids(text)}
      </ReactMarkdown>
    </Box>
  );
}
