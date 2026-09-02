import { Alert, AlertTitle, Typography } from "@mui/material";
import { useMyAccessView } from "../api";

/**
 * The last-thirty-days warning (P1T-188).
 *
 * <p>It has a property worth knowing about: reading it is itself activity, so for somebody whose
 * own record it is, <b>signing in to see this warning resets the clock it warns about</b>. The
 * banner is therefore usually its own cure — and it says so, because a warning that quietly fixed
 * itself without telling anybody would leave people thinking they still had to act.</p>
 *
 * <p>Somebody who never signs in never sees it. That gap is real and is not solvable here: this
 * service has no email and never will.</p>
 */
export default function ExpiryBanner() {
  const access = useMyAccessView();

  if (access.isError || !access.data?.expiringSoon || !access.data.expiresAt) {
    return null;
  }

  const when = new Date(access.data.expiresAt).toLocaleDateString();

  return (
    <Alert severity="warning" sx={{ mb: 3 }}>
      <AlertTitle>Your record is due to be deleted on {when}</AlertTitle>
      <Typography variant="body2">
        {access.data.retentionClock === "Claimed"
          ? "We delete a record two years after the last time its owner did anything with it. " +
            "Signing in and reading this counts — so the date above has already moved. Nothing " +
            "is required of you."
          : "We delete a record six months after it was entered when nobody has claimed it. " +
            "Claiming this record keeps it, and gives you control over it."}
      </Typography>
    </Alert>
  );
}
