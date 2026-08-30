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
import { apiErrorMessage, useRecover } from "../api";
import { isPasskeySupported } from "../auth/webauthn";
import { ErrorNotice } from "../components/ErrorNotice";

export default function RecoverPage() {
  const [email, setEmail] = useState("");
  const [controlWord, setControlWord] = useState("");
  const navigate = useNavigate();
  const recover = useRecover();
  const supported = isPasskeySupported();

  const canSubmit = email.trim().length > 0 && controlWord.trim().length > 0 && supported;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    recover.mutate(
      { email: email.trim(), controlWord: controlWord.trim() },
      { onSuccess: () => navigate("/") },
    );
  };

  return (
    <Box sx={{ display: "flex", justifyContent: "center", pt: 6 }}>
      <Paper elevation={2} sx={{ p: 4, width: "100%", maxWidth: 440 }}>
        <Stack spacing={3} component="form" onSubmit={handleSubmit}>
          <Box>
            <Typography variant="h5" gutterBottom>
              Recover your account
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Lost your device? Enter your email and control word to register a new passkey on this
              device.
            </Typography>
          </Box>

          {!supported && (
            <Alert severity="error">
              This browser doesn't support passkeys. Use a current version of Chrome, Safari, Edge,
              or Firefox.
            </Alert>
          )}

          <ErrorNotice message={recover.isError ? apiErrorMessage(recover.error) : null} />

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
            helperText="The recovery secret you set when you signed up."
          />

          <Button
            type="submit"
            variant="contained"
            size="large"
            disabled={!canSubmit || recover.isPending}
          >
            {recover.isPending ? "Registering passkey…" : "Recover & register a passkey"}
          </Button>

          <Typography variant="body2" color="text.secondary" align="center">
            Remembered your device?{" "}
            <Link component={RouterLink} to="/signin">
              Sign in
            </Link>
          </Typography>
        </Stack>
      </Paper>
    </Box>
  );
}
