import { useState } from "react";
import {
  Alert,
  Box,
  Button,
  Link,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { Link as RouterLink, useNavigate } from "react-router-dom";
import { apiErrorMessage, useSignin } from "../api";
import { isPasskeySupported } from "../auth/webauthn";
import { ErrorNotice } from "../components/ErrorNotice";

export default function SigninPage() {
  const [email, setEmail] = useState("");
  const navigate = useNavigate();
  const signin = useSignin();
  const supported = isPasskeySupported();

  const canSubmit = supported;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    signin.mutate({ email: email.trim() || undefined }, { onSuccess: () => navigate("/") });
  };

  // The auth pages cap themselves — 440px of form, centred — which is why they never needed the
  // shell's container and do not miss it (P1T-162). The gutters are theirs now rather than `App`'s,
  // so the card does not touch the edges of a phone.
  return (
    <Box sx={{ display: "flex", justifyContent: "center", px: { xs: 2, sm: 3 }, pt: 6, pb: 6 }}>
      <Paper sx={{ p: 4, width: "100%", maxWidth: 440 }}>
        <Stack spacing={3} component="form" onSubmit={handleSubmit}>
          <Box>
            <Typography variant="h5" gutterBottom>
              Sign in
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Approve with your passkey — your browser will show your saved accounts. Email is only
              needed if your device doesn't offer one.
            </Typography>
          </Box>

          {!supported && (
            <Alert severity="error">
              This browser doesn't support passkeys. Use a current version of Chrome, Safari, Edge,
              or Firefox.
            </Alert>
          )}

          <ErrorNotice message={signin.isError ? apiErrorMessage(signin.error) : null} />

          <TextField
            label="Email (optional)"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username webauthn"
            fullWidth
          />

          <Button
            type="submit"
            variant="contained"
            size="large"
            disabled={!canSubmit || signin.isPending}
          >
            {signin.isPending ? "Waiting for passkey…" : "Sign in with a passkey"}
          </Button>

          <Stack spacing={0.5} alignItems="center">
            <Typography variant="body2" color="text.secondary">
              No account?{" "}
              <Link component={RouterLink} to="/signup">
                Sign up
              </Link>
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Lost your device?{" "}
              <Link component={RouterLink} to="/recover">
                Recover with your control word
              </Link>
            </Typography>
          </Stack>
        </Stack>
      </Paper>
    </Box>
  );
}
