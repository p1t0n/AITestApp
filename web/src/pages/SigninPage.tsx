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

export default function SigninPage() {
  const [email, setEmail] = useState("");
  const navigate = useNavigate();
  const signin = useSignin();
  const supported = isPasskeySupported();

  const canSubmit = email.trim().length > 0 && supported;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    signin.mutate({ email: email.trim() }, { onSuccess: () => navigate("/") });
  };

  return (
    <Box sx={{ display: "flex", justifyContent: "center", pt: 6 }}>
      <Paper elevation={2} sx={{ p: 4, width: "100%", maxWidth: 440 }}>
        <Stack spacing={3} component="form" onSubmit={handleSubmit}>
          <Box>
            <Typography variant="h5" gutterBottom>
              Sign in
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Enter your email, then approve with your passkey.
            </Typography>
          </Box>

          {!supported && (
            <Alert severity="error">
              This browser doesn't support passkeys. Use a current version of Chrome, Safari, Edge,
              or Firefox.
            </Alert>
          )}

          {signin.isError && <Alert severity="error">{apiErrorMessage(signin.error)}</Alert>}

          <TextField
            label="Email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username webauthn"
            required
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
