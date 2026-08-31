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
import { apiErrorMessage, useSignup } from "../api";
import { isPasskeySupported } from "../auth/webauthn";
import { ErrorNotice } from "../components/ErrorNotice";

export default function SignupPage() {
  const [email, setEmail] = useState("");
  const [controlWord, setControlWord] = useState("");
  const navigate = useNavigate();
  const signup = useSignup();
  const supported = isPasskeySupported();

  const canSubmit = email.trim().length > 0 && controlWord.trim().length > 0 && supported;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    signup.mutate(
      { email: email.trim(), controlWord: controlWord.trim() },
      { onSuccess: () => navigate("/") },
    );
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
              Create your account
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Passwordless — you'll register a passkey on this device. No password to remember.
            </Typography>
          </Box>

          {!supported && (
            <Alert severity="error">
              This browser doesn't support passkeys. Use a current version of Chrome, Safari, Edge,
              or Firefox.
            </Alert>
          )}

          <ErrorNotice message={signup.isError ? apiErrorMessage(signup.error) : null} />

          <TextField
            label="Email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
            required
            fullWidth
          />

          <TextField
            label="Control word"
            value={controlWord}
            onChange={(e) => setControlWord(e.target.value)}
            required
            fullWidth
            helperText="A secret word you'll need to recover your account if you lose this device. Choose something memorable and keep it safe — it cannot be reset for you."
          />

          <Button
            type="submit"
            variant="contained"
            size="large"
            disabled={!canSubmit || signup.isPending}
          >
            {signup.isPending ? "Registering passkey…" : "Sign up with a passkey"}
          </Button>

          <Typography variant="body2" color="text.secondary" align="center">
            Already have an account?{" "}
            <Link component={RouterLink} to="/signin">
              Sign in
            </Link>
          </Typography>
        </Stack>
      </Paper>
    </Box>
  );
}
