// The job-description field every agent tab opens with: preset chips over a grown textarea.
// Four tabs rendered this character for character; the only thing that ever differed is the
// placeholder, which Roster Scan words for its own "whole roster" framing.
import { Box, Chip, Stack, TextField, Typography } from "@mui/material";
import { PRESET_JDS } from "./presets";

export const JD_PLACEHOLDER = "Paste a job description, or pick a preset above…";

export function JdInput({
  value,
  onChange,
  placeholder = JD_PLACEHOLDER,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <Box>
      <Typography variant="caption" sx={{ color: "text.secondary" }}>
        Job description
      </Typography>
      <Stack
        direction="row"
        spacing={0.5}
        useFlexGap
        sx={{ flexWrap: "wrap", mb: 0.5 }}>
        {PRESET_JDS.map((p) => (
          <Chip key={p.label} label={p.label} variant="outlined" onClick={() => onChange(p.text)} />
        ))}
      </Stack>
      <TextField
        fullWidth
        multiline
        minRows={3}
        maxRows={8}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </Box>
  );
}
