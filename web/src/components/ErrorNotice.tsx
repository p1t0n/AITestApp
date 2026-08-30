import { Alert, AlertTitle } from "@mui/material";
import type { SxProps, Theme } from "@mui/material";

/**
 * The one place a failure message renders (P1T-153).
 *
 * Every component that can fail keeps its own local error state — that state is genuinely local.
 * What was decided eleven times over was the *presentation*: six dock panels painted an
 * `error.light` Paper, four dialogs used an `Alert`, the rest an inline `color="error"` caption.
 * This is that decision made once, as an `Alert severity="error"` (which also carries `role="alert"`,
 * so a failure is announced rather than only coloured).
 *
 * Renders nothing when there is no message, so call sites collapse from `{error && (…)}` to a bare
 * `<ErrorNotice message={error} />`.
 *
 * The message itself is not this component's business: `apiErrorMessage` (axios) and `SseHttpError`
 * (streaming) already produce the same string for the same server response, and both keep working
 * exactly as they did — a 429 usage-cap message arrives here as text and is rendered verbatim.
 */
export function ErrorNotice({
  message,
  detail,
  sx,
}: {
  /** The failure. Falsy means "nothing went wrong" and nothing renders. */
  message?: string | null;
  /** A second line under the message, for transports that carry title + detail separately. */
  detail?: string | null;
  sx?: SxProps<Theme>;
}) {
  if (!message) return null;
  return (
    <Alert severity="error" sx={sx} data-testid="error-notice">
      {detail ? (
        <>
          <AlertTitle sx={{ mb: 0 }}>{message}</AlertTitle>
          {detail}
        </>
      ) : (
        message
      )}
    </Alert>
  );
}

export default ErrorNotice;
