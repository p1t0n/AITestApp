// The collapsible roster filters shared by Staffing, Shortlist and Roster Scan, and the single
// rule for turning them into a request body: only a filter the user actually set is sent, because
// the server owns every default. Shortlist hangs its own "Top K" off `trailing`, which is the only
// field any tab adds.
import { useState, type ReactNode } from "react";
import { Autocomplete, Box, Button, Collapse, Stack, TextField } from "@mui/material";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import FilterListIcon from "@mui/icons-material/FilterList";
import { useSkills } from "../../api";

/** The filter fields of a shortlist / staffing / roster-scan request — all optional by design. */
interface JdFilterFields {
  availableOn?: string;
  skillIds?: string[];
  location?: string;
  minYears?: number;
}

/**
 * The four filter values and the one rule for sending them. Nothing else: the three tabs that hold
 * this hook read only `toRequest()`, so whether the panel is open and what the skill catalog
 * contains are the panel's own business and live in `JdFilters` (EXP-104).
 */
export function useJdFilters() {
  const [availableOn, setAvailableOn] = useState("");
  const [skillIds, setSkillIds] = useState<string[]>([]);
  const [location, setLocation] = useState("");
  const [minYears, setMinYears] = useState("");

  function toRequest(): JdFilterFields {
    const req: JdFilterFields = {};
    if (availableOn) req.availableOn = availableOn;
    if (skillIds.length > 0) req.skillIds = skillIds;
    if (location.trim()) req.location = location.trim();
    if (minYears !== "") req.minYears = Number(minYears);
    return req;
  }

  return {
    availableOn,
    setAvailableOn,
    skillIds,
    setSkillIds,
    location,
    setLocation,
    minYears,
    setMinYears,
    toRequest,
  };
}

export function JdFilters({
  filters,
  trailing,
}: {
  filters: ReturnType<typeof useJdFilters>;
  trailing?: ReactNode;
}) {
  const { data: catalogSkills, isLoading: skillsLoading } = useSkills();
  const [showFilters, setShowFilters] = useState(false);

  // The catalog rows are the options, as they come: `getOptionLabel` is what names them, so there
  // is no second `{id,label}` shape to keep in step with the first.
  const options = catalogSkills ?? [];
  const selected = options.filter((s) => filters.skillIds.includes(s.id));

  const minYearsField = (
    <TextField
      type="number"
      label="Min years"
      value={filters.minYears}
      onChange={(e) => filters.setMinYears(e.target.value)}
      slotProps={{
        htmlInput: { min: 0 }
      }}
    />
  );

  return (
    <Box>
      <Button
        startIcon={<FilterListIcon />}
        endIcon={showFilters ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        onClick={() => setShowFilters((v) => !v)}
      >
        Filters (optional)
      </Button>
      <Collapse in={showFilters} unmountOnExit>
        <Stack spacing={1.5} sx={{ mt: 1 }}>
          <TextField
            type="date"
            label="Available on"
            value={filters.availableOn}
            onChange={(e) => filters.setAvailableOn(e.target.value)}
            slotProps={{
              inputLabel: { shrink: true }
            }}
          />
          <Autocomplete
            multiple
            options={options}
            value={selected}
            getOptionLabel={(o) => o.name}
            onChange={(_, v) => filters.setSkillIds(v.map((o) => o.id))}
            loading={skillsLoading}
            isOptionEqualToValue={(o, v) => o.id === v.id}
            renderInput={(params) => <TextField {...params} label="Skills" placeholder="Any skill" />}
          />
          <TextField
            label="Location"
            placeholder="Any location"
            value={filters.location}
            onChange={(e) => filters.setLocation(e.target.value)}
          />
          {trailing ? (
            <Stack direction="row" spacing={1.5}>
              {minYearsField}
              {trailing}
            </Stack>
          ) : (
            minYearsField
          )}
        </Stack>
      </Collapse>
    </Box>
  );
}
