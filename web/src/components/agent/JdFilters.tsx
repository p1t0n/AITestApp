// The collapsible roster filters shared by Staffing, Shortlist and Roster Scan, and the single
// rule for turning them into a request body: only a filter the user actually set is sent, because
// the server owns every default. Shortlist hangs its own "Top K" off `trailing`, which is the only
// field any tab adds.
import { useMemo, useState, type ReactNode } from "react";
import { Autocomplete, Box, Button, Collapse, Stack, TextField } from "@mui/material";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import FilterListIcon from "@mui/icons-material/FilterList";
import { useSkills } from "../../api";

/** The filter fields of a shortlist / staffing / roster-scan request — all optional by design. */
export interface JdFilterFields {
  availableOn?: string;
  skillIds?: string[];
  location?: string;
  minYears?: number;
}

export interface JdFiltersState {
  showFilters: boolean;
  setShowFilters: (open: boolean | ((open: boolean) => boolean)) => void;
  availableOn: string;
  setAvailableOn: (value: string) => void;
  skillIds: string[];
  setSkillIds: (value: string[]) => void;
  location: string;
  setLocation: (value: string) => void;
  minYears: string;
  setMinYears: (value: string) => void;
  skillOptions: { id: string; label: string }[];
  selectedSkills: { id: string; label: string }[];
  skillsLoading: boolean;
  /** Just the filters that are set, ready to spread into a request body. */
  toRequest: () => JdFilterFields;
}

export function useJdFilters(): JdFiltersState {
  const skills = useSkills();

  const [showFilters, setShowFilters] = useState(false);
  const [availableOn, setAvailableOn] = useState("");
  const [skillIds, setSkillIds] = useState<string[]>([]);
  const [location, setLocation] = useState("");
  const [minYears, setMinYears] = useState("");

  const skillOptions = useMemo(
    () => (skills.data ?? []).map((s) => ({ id: s.id, label: s.name })),
    [skills.data],
  );
  const selectedSkills = skillOptions.filter((o) => skillIds.includes(o.id));

  function toRequest(): JdFilterFields {
    const req: JdFilterFields = {};
    if (availableOn) req.availableOn = availableOn;
    if (skillIds.length > 0) req.skillIds = skillIds;
    if (location.trim()) req.location = location.trim();
    if (minYears !== "") req.minYears = Number(minYears);
    return req;
  }

  return {
    showFilters,
    setShowFilters,
    availableOn,
    setAvailableOn,
    skillIds,
    setSkillIds,
    location,
    setLocation,
    minYears,
    setMinYears,
    skillOptions,
    selectedSkills,
    skillsLoading: skills.isLoading,
    toRequest,
  };
}

export function JdFilters({ filters, trailing }: { filters: JdFiltersState; trailing?: ReactNode }) {
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
        endIcon={filters.showFilters ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        onClick={() => filters.setShowFilters((v) => !v)}
      >
        Filters (optional)
      </Button>
      <Collapse in={filters.showFilters} unmountOnExit>
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
            options={filters.skillOptions}
            value={filters.selectedSkills}
            onChange={(_, v) => filters.setSkillIds(v.map((o) => o.id))}
            loading={filters.skillsLoading}
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
