// The "How the JD was read" chips (P1T-120). When the response carries the structured extraction
// the chips gain honesty badges: must-have color, an "inferred" marker where the evidence quote
// couldn't be verified verbatim, evidence/kind tooltips, and the model's explicit ambiguities
// note. Without an extraction (degraded runs, older payloads) they fall back to the plain
// requirement strings unchanged.
import { Box, Chip, Stack, Tooltip, Typography } from "@mui/material";
import HelpOutlineIcon from "@mui/icons-material/HelpOutline";
import type { JdExtractedRequirement, JdExtraction } from "../../api";

function chipLabel(r: JdExtractedRequirement): string {
  return r.minYears != null ? `${r.text} · ${r.minYears}+ yrs` : r.text;
}

function chipTooltip(r: JdExtractedRequirement): string {
  const priority =
    r.priority === "MustHave" ? "Must-have" : r.priority === "NiceToHave" ? "Nice-to-have" : "Priority unspecified";
  if (r.inferred) {
    return `${priority} · ${r.kind} — inferred: the evidence quote couldn't be verified verbatim in the JD`;
  }
  return r.evidenceSpan ? `${priority} · ${r.kind} — "${r.evidenceSpan}"` : `${priority} · ${r.kind}`;
}

export default function RequirementChips({
  requirements,
  extraction,
}: {
  requirements: string[];
  extraction?: JdExtraction | null;
}) {
  return (
    <Box>
      <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
        {extraction
          ? extraction.requirements.map((r) => (
              <Tooltip key={r.text} title={chipTooltip(r)}>
                <Chip
                  size="small"
                  label={chipLabel(r)}
                  color={r.priority === "MustHave" ? "primary" : "default"}
                  variant={r.priority === "NiceToHave" ? "outlined" : "filled"}
                  icon={r.inferred ? <HelpOutlineIcon data-testid={`inferred-${r.text}`} /> : undefined}
                />
              </Tooltip>
            ))
          : requirements.map((r) => <Chip key={r} label={r} size="small" />)}
      </Stack>
      {extraction && extraction.ambiguities.length > 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ display: "block", mt: 0.5 }}>
          JD is unclear about: {extraction.ambiguities.join("; ")}
        </Typography>
      )}
    </Box>
  );
}
