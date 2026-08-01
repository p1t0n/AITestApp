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
 * ids linkified to their detail page. */
export function AgentMarkdown({ text }: { text: string }) {
  return (
    <Box
      sx={{
        fontSize: 14,
        lineHeight: 1.5,
        "& p": { mt: 0, mb: 1 },
        "& ul, & ol": { mt: 0, mb: 1, pl: 2.5 },
        "& h1, & h2, & h3": { fontSize: "1rem", fontWeight: 700, mt: 1.5, mb: 0.5 },
        "& table": { borderCollapse: "collapse", width: "100%", my: 1 },
        "& th, & td": { border: 1, borderColor: "divider", px: 0.75, py: 0.25, textAlign: "left" },
        "& code": { bgcolor: "grey.100", px: 0.5, borderRadius: 0.5 },
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
        }}
      >
        {linkifyGuids(text)}
      </ReactMarkdown>
    </Box>
  );
}
