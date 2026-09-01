import { Alert, Box, Skeleton } from "@mui/material";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { apiErrorMessage, useTransparencyNotice } from "../api";

/**
 * Renders the current transparency notice (P1T-183) in a well of its own.
 *
 * The text is fetched and rendered in full rather than linked to, because acknowledging a link is
 * not acknowledging a notice — Art. 12(1) asks for the information itself, in an intelligible and
 * easily accessible form, and a person who has to leave the form to read it mostly does not.
 *
 * A load failure surfaces as an error rather than as an empty box: the caller gates its submit on
 * `ready`, so silently rendering nothing would leave somebody staring at a disabled button with no
 * explanation.
 */
export function TransparencyNoticeText() {
  // The caller reads the same query for the version it will record; React Query serves both from
  // one request, which is why the version is not threaded back out of here through a callback.
  const notice = useTransparencyNotice();

  if (notice.isPending) {
    return <Skeleton variant="rectangular" height={220} />;
  }

  if (notice.isError || !notice.data) {
    return (
      <Alert severity="error">
        The transparency notice could not be loaded, so there is nothing here to agree to. Try
        again in a moment. ({apiErrorMessage(notice.error)})
      </Alert>
    );
  }

  return (
    <Box
      // Scrolls itself. The notice is long on purpose — the alternative to length is leaving
      // something out — and a page that grows to fit it pushes the acknowledgment off screen.
      sx={{
        maxHeight: 280,
        overflowY: "auto",
        p: 2,
        border: 1,
        borderColor: "divider",
        borderRadius: 1,
        bgcolor: "surface.raised",
        fontSize: 14,
        lineHeight: 1.55,
        "& h2": { fontSize: "1rem", fontWeight: 700, mt: 0, mb: 1 },
        "& h3": { fontSize: "0.9rem", fontWeight: 700, mt: 2, mb: 0.5 },
        "& p": { mt: 0, mb: 1 },
        "& ul": { mt: 0, mb: 1, pl: 2.5 },
        "& li": { mb: 0.5 },
      }}
    >
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{notice.data.text}</ReactMarkdown>
    </Box>
  );
}

export default TransparencyNoticeText;
