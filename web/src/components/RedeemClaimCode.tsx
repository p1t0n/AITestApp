import { useState } from "react";
import { Alert, Button, Paper, Stack, TextField, Typography } from "@mui/material";
import { apiErrorMessage, useRedeemClaimCode } from "../api";
import { ErrorNotice } from "./ErrorNotice";

/**
 * Where an Expert spends a claim code (P1T-184). The counterpart of the Service Manager's "issue
 * claim code": a code handed over in person is the only proof this service can offer that is
 * stronger than an unverified email match, so redeeming binds the record with no approval step.
 *
 * <p>Lives on the workspace because it is the way out of owning nothing — the state a person is in
 * while a claim waits, and the state their session is deliberately indistinguishable from.</p>
 */
export default function RedeemClaimCode() {
  const redeem = useRedeemClaimCode();
  const [code, setCode] = useState("");

  return (
    <Paper variant="outlined" sx={{ p: 3, mt: 3 }}>
      <Stack spacing={2}>
        <Typography variant="h6" component="h2">
          Have a claim code?
        </Typography>
        <Typography variant="body2" color="text.secondary">
          If a Service Manager gave you a code for your record — in person or by phone — enter it
          here. It works once, and it links that record to this account straight away.
        </Typography>

        <ErrorNotice message={redeem.isError ? apiErrorMessage(redeem.error) : null} />
        {redeem.isSuccess && (
          <Alert severity="success">
            That record is now yours. It is held because you asked to be considered for work, and it
            is scanned for Jobs.
          </Alert>
        )}

        <Stack direction="row" spacing={2} alignItems="flex-start">
          <TextField
            label="Claim code"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            fullWidth
            inputProps={{ style: { fontFamily: "monospace" } }}
          />
          <Button
            variant="contained"
            sx={{ mt: 1 }}
            disabled={code.trim() === "" || redeem.isPending}
            onClick={() => redeem.mutate(code.trim(), { onSuccess: () => setCode("") })}
          >
            Redeem
          </Button>
        </Stack>
      </Stack>
    </Paper>
  );
}
