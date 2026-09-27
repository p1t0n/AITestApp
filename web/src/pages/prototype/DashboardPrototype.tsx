// PROTOTYPE — throwaway (branch prototype/dashboard). Not production code.
//
// Question: what should the Administrator's main dashboard look like?
// Plan: three structurally different variants on the throwaway route `/prototype/dashboard`,
// switchable via `?variant=A|B|C` (← / → keys or the floating bar). Data is the real local
// roster; see dashboardData.ts for how it is fetched and why that is not how a real build would.
//
//   A — Command center: KPI row → map + side gauges → full-width per-expert Gantt.
//   B — Geography first: the map is the page; a location drives an aggregate location×month
//       heatmap and that city's per-expert Gantt.
//   C — Time first: a 12-month capacity forecast is the primary control; picking a month re-drives
//       the KPIs and the map; Gantt swimlanes grouped by location.
//
// FINDINGS to carry into the real issue:
//   - No bulk availability read exists. A real dashboard needs one server aggregate
//     (e.g. GET /dashboard?from&months=12 → per-location × month FTE + per-expert monthly capacity),
//     computed with CapacityCalculator, not 500 detail fetches.
//   - Location is free text. A map needs a geocode (a Location dictionary with lat/lon, or a
//     country code) and an explicit "Remote" notion — it cannot be drawn from the string.
import { useMemo, useState, type ReactNode } from "react";
import {
  alpha,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Collapse,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
  useTheme,
} from "@mui/material";
import ExpandMore from "@mui/icons-material/ExpandMore";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import PageHeader from "../../components/PageHeader";
import PrototypeSwitcher, { useVariant, type VariantDef } from "../../components/PrototypeSwitcher";
import ExpertMap, { type MapPoint } from "./ExpertMap";
import {
  ADMIN_MONTHS,
  band,
  benchSkills,
  byLocation,
  fteByMonth,
  freeingSoon,
  staffable,
  useDashboardRoster,
  type DashExpert,
} from "./dashboardData";

const VARIANTS: VariantDef[] = [
  { key: "A", name: "Command center" },
  { key: "B", name: "Geography first" },
  { key: "C", name: "Time first" },
];

// ---- shared primitives (marks only — every variant owns its own layout) ---------------------

export function useCapacityFill() {
  const t = useTheme();
  return (c: number) => (c <= 0 ? "transparent" : alpha(t.palette.info.main, 0.18 + 0.82 * (c / 100)));
}

export function CapacityLegend() {
  const fill = useCapacityFill();
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
      <Typography variant="caption" color="text.secondary">
        Available
      </Typography>
      {[0, 25, 50, 75, 100].map((c) => (
        <Stack key={c} direction="row" spacing={0.5} sx={{ alignItems: "center" }}>
          <Box sx={{ width: 14, height: 14, borderRadius: 0.5, bgcolor: fill(c), border: 1, borderColor: "divider" }} />
          <Typography variant="caption" color="text.secondary">
            {c}%
          </Typography>
        </Stack>
      ))}
    </Stack>
  );
}

/** One row per expert, one cell per month, cell depth = average capacity that month. */
export function Gantt({
  rows,
  highlight,
  labelWidth = 220,
  dense,
}: {
  rows: DashExpert[];
  highlight?: number | null;
  labelWidth?: number;
  dense?: boolean;
}) {
  const fill = useCapacityFill();
  const h = dense ? 16 : 22;
  return (
    <Box sx={{ overflowX: "auto" }}>
      <Box sx={{ display: "grid", gridTemplateColumns: `${labelWidth}px repeat(${ADMIN_MONTHS.length}, minmax(34px, 1fr))`, columnGap: "2px", rowGap: "2px", minWidth: labelWidth + 12 * 36 }}>
        <Box />
        {ADMIN_MONTHS.map((m, i) => (
          <Typography key={i} variant="caption" color={highlight === i ? "text.primary" : "text.secondary"} sx={{ textAlign: "center", fontWeight: highlight === i ? 700 : 400 }}>
            {m.label}
          </Typography>
        ))}
        {rows.map((e) => (
          <GanttRow key={e.id} e={e} h={h} fill={fill} highlight={highlight} dense={dense} />
        ))}
      </Box>
    </Box>
  );
}

function GanttRow({ e, h, fill, highlight, dense }: { e: DashExpert; h: number; fill: (c: number) => string; highlight?: number | null; dense?: boolean }) {
  return (
    <>
      <Box sx={{ minWidth: 0, display: "flex", alignItems: "center", gap: 1 }}>
        <Typography variant={dense ? "caption" : "body2"} noWrap title={`${e.name} — ${e.title}, ${e.location}`}>
          {e.name}
        </Typography>
        {!dense && (
          <Typography variant="caption" color="text.secondary" noWrap>
            {e.title}
          </Typography>
        )}
      </Box>
      {e.monthly.map((c, i) => (
        <Tooltip key={i} title={`${e.name} · ${ADMIN_MONTHS[i].label}: ${c}% available`} disableInteractive>
          <Box
            sx={{
              height: h,
              borderRadius: 0.5,
              bgcolor: fill(c),
              border: 1,
              borderColor: c <= 0 ? "divider" : "transparent",
              outline: highlight === i ? "1px solid" : "none",
              outlineColor: "text.secondary",
            }}
          />
        </Tooltip>
      ))}
    </>
  );
}

function Kpi({ label, value, sub }: { label: string; value: ReactNode; sub?: ReactNode }) {
  return (
    <Card variant="outlined" sx={{ flex: "1 1 150px" }}>
      <CardContent sx={{ py: 1.5, "&:last-child": { pb: 1.5 } }}>
        <Typography variant="caption" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5" sx={{ fontVariantNumeric: "tabular-nums" }}>
          {value}
        </Typography>
        {sub && (
          <Typography variant="caption" color="text.secondary">
            {sub}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}

function Panel({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Card variant="outlined">
      <CardContent>
        <Stack direction="row" sx={{ alignItems: "center", mb: 1.5, gap: 1 }}>
          <Typography variant="subtitle1" sx={{ flexGrow: 1 }}>
            {title}
          </Typography>
          {action}
        </Stack>
        {children}
      </CardContent>
    </Card>
  );
}

/** Available FTE per month as columns; one series, so no legend box. */
function Forecast({ experts, selected, onSelect, height = 120 }: { experts: DashExpert[]; selected?: number | null; onSelect?: (i: number) => void; height?: number }) {
  const t = useTheme();
  const fte = fteByMonth(experts);
  const max = Math.max(1, ...fte);
  return (
    <Box sx={{ display: "grid", gridTemplateColumns: `repeat(${fte.length}, 1fr)`, gap: "2px", alignItems: "end" }}>
      {fte.map((v, i) => (
        <Tooltip key={i} title={`${ADMIN_MONTHS[i].label}: ${v.toFixed(1)} FTE available`} disableInteractive>
          <Box onClick={() => onSelect?.(i)} sx={{ cursor: onSelect ? "pointer" : "default", textAlign: "center" }}>
            <Typography variant="caption" color={selected === i ? "text.primary" : "text.secondary"} sx={{ fontVariantNumeric: "tabular-nums", fontWeight: selected === i ? 700 : 400 }}>
              {v.toFixed(0)}
            </Typography>
            <Box
              sx={{
                height: Math.max(2, (v / max) * height),
                bgcolor: selected === i ? t.palette.primary.main : t.palette.info.main,
                borderRadius: "4px 4px 0 0",
              }}
            />
            <Typography variant="caption" color={selected === i ? "text.primary" : "text.secondary"}>
              {ADMIN_MONTHS[i].label}
            </Typography>
          </Box>
        </Tooltip>
      ))}
    </Box>
  );
}

function mapPoints(experts: DashExpert[], month: number | null): MapPoint[] {
  return byLocation(experts).map(({ location, list }) => ({
    location,
    count: list.length,
    availableShare: list.filter((e) => (month === null ? e.today : e.monthly[month]) > 0).length / list.length,
  }));
}

function sortByFreeSoonest(rows: DashExpert[]) {
  // Most available now first, then by how soon capacity arrives.
  const firstFree = (e: DashExpert) => {
    const i = e.monthly.findIndex((c) => c > 0);
    return i < 0 ? 99 : i;
  };
  return [...rows].sort((a, b) => firstFree(a) - firstFree(b) || b.today - a.today || a.name.localeCompare(b.name));
}

// ---- Variant A: Command center --------------------------------------------------------------

function VariantA({ experts }: { experts: DashExpert[] }) {
  const [loc, setLoc] = useState<string | null>(null);
  const [showAll, setShowAll] = useState(false);
  const bench = experts.filter(staffable);
  const bands = { full: 0, partial: 0, none: 0 };
  bench.forEach((e) => bands[band(e.today)]++);
  const fte = fteByMonth(experts);
  const util = bench.length ? 100 - bench.reduce((s, e) => s + e.today, 0) / bench.length : 0;
  const soon = freeingSoon(experts, 30);
  const rows = sortByFreeSoonest(bench.filter((e) => !loc || e.location === loc));

  return (
    <Stack spacing={2}>
      <Stack direction="row" sx={{ flexWrap: "wrap", gap: 2 }}>
        <Kpi label="On the bench" value={bench.length} sub={`of ${experts.length} in roster`} />
        <Kpi label="Available now" value={`${fte[0].toFixed(0)} FTE`} sub={`${bands.full} full · ${bands.partial} partial · ${bands.none} booked`} />
        <Kpi label="Utilisation today" value={`${util.toFixed(0)}%`} sub="1 − avg capacity" />
        <Kpi label="Freeing in 30 days" value={soon.length} sub="capacity steps up" />
        <Kpi label="FTE in 3 months" value={`${fte[3].toFixed(0)}`} sub={`${fte[3] >= fte[0] ? "▲" : "▼"} ${Math.abs(fte[3] - fte[0]).toFixed(0)} vs now`} />
        <Kpi label="Drafts at the gate" value={experts.filter((e) => e.status === "Draft").length} sub="awaiting publication" />
        <Kpi label="Paused" value={experts.filter((e) => e.paused).length} sub="hidden by themselves" />
      </Stack>

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, gap: 2 }}>
        <Panel title="Experts by location" action={loc && <Chip size="small" label={loc} onDelete={() => setLoc(null)} />}>
          <ExpertMap points={mapPoints(experts, null)} selected={loc} onSelect={setLoc} height={380} />
        </Panel>
        <Stack spacing={2}>
          <Panel title="Capacity forecast (FTE)">
            <Forecast experts={experts} height={90} />
          </Panel>
          <Panel title="Coming free in 30 days">
            <List dense disablePadding sx={{ maxHeight: 180, overflow: "auto" }}>
              {soon.slice(0, 12).map((s) => (
                <ListItem key={s.expert.id} disableGutters secondaryAction={<Typography variant="caption">{s.on.slice(5)} → {s.to}%</Typography>}>
                  <ListItemText primary={s.expert.name} secondary={`${s.expert.title} · ${s.expert.location}`} />
                </ListItem>
              ))}
              {soon.length === 0 && <Typography variant="body2" color="text.secondary">Nobody.</Typography>}
            </List>
          </Panel>
          <Panel title="Top skills on the bench today">
            <Stack direction="row" sx={{ flexWrap: "wrap", gap: 0.75 }}>
              {benchSkills(experts, 14).map(([s, n]) => (
                <Chip key={s} size="small" variant="outlined" label={`${s} · ${n}`} />
              ))}
            </Stack>
          </Panel>
        </Stack>
      </Box>

      <Panel title={`Availability by month${loc ? ` — ${loc}` : ""} (${rows.length})`} action={<CapacityLegend />}>
        <Gantt rows={showAll ? rows : rows.slice(0, 30)} />
        {rows.length > 30 && (
          <Button size="small" sx={{ mt: 1 }} onClick={() => setShowAll(!showAll)}>
            {showAll ? "Show first 30" : `Show all ${rows.length}`}
          </Button>
        )}
      </Panel>
    </Stack>
  );
}

// ---- Variant B: Geography first -------------------------------------------------------------

function LocationHeatmap({ experts, selected, onSelect }: { experts: DashExpert[]; selected: string | null; onSelect: (l: string) => void }) {
  const fill = useCapacityFill();
  const groups = byLocation(experts.filter(staffable));
  return (
    <Box sx={{ display: "grid", gridTemplateColumns: `170px repeat(${ADMIN_MONTHS.length}, minmax(30px, 1fr))`, gap: "2px" }}>
      <Box />
      {ADMIN_MONTHS.map((m, i) => (
        <Typography key={i} variant="caption" color="text.secondary" sx={{ textAlign: "center" }}>
          {m.label}
        </Typography>
      ))}
      {groups.map(({ location, list }) => {
        const on = location === selected;
        return (
          <Box key={location} sx={{ display: "contents", cursor: "pointer" }} onClick={() => onSelect(location)}>
            <Typography variant="body2" noWrap sx={{ fontWeight: on ? 700 : 400 }}>
              {location} <Typography component="span" variant="caption" color="text.secondary">{list.length}</Typography>
            </Typography>
            {ADMIN_MONTHS.map((_, i) => {
              const fte = list.reduce((s, e) => s + e.monthly[i] / 100, 0);
              const share = (fte / list.length) * 100;
              return (
                <Tooltip key={i} title={`${location} · ${ADMIN_MONTHS[i].label}: ${fte.toFixed(1)} FTE of ${list.length}`} disableInteractive>
                  <Box sx={{ height: 22, borderRadius: 0.5, bgcolor: fill(share), border: 1, borderColor: on ? "text.primary" : share <= 0 ? "divider" : "transparent", display: "grid", placeItems: "center" }}>
                    <Typography variant="caption" sx={{ fontSize: 10, color: share > 55 ? "info.contrastText" : "text.secondary" }}>
                      {fte >= 0.5 ? fte.toFixed(0) : ""}
                    </Typography>
                  </Box>
                </Tooltip>
              );
            })}
          </Box>
        );
      })}
    </Box>
  );
}

function VariantB({ experts }: { experts: DashExpert[] }) {
  const [loc, setLoc] = useState<string | null>(null);
  const bench = experts.filter(staffable);
  const fte = fteByMonth(experts);
  const cityRows = sortByFreeSoonest(bench.filter((e) => e.location === loc));

  return (
    <Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">
        <b>{bench.length}</b> on the bench across <b>{byLocation(experts).length}</b> locations · <b>{fte[0].toFixed(0)}</b> FTE available now ·{" "}
        <b>{freeingSoon(experts, 30).length}</b> freeing in 30 days · <b>{experts.filter((e) => e.status === "Draft").length}</b> drafts at the gate
      </Typography>
      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", xl: "minmax(0, 1.1fr) minmax(0, 1fr)" }, gap: 2, alignItems: "start" }}>
        <Box sx={{ position: { xl: "sticky" }, top: 16 }}>
          <ExpertMap points={mapPoints(experts, null)} selected={loc} onSelect={setLoc} height={560} />
        </Box>
        <Stack spacing={2}>
          <Panel title="Available FTE by location and month" action={<CapacityLegend />}>
            <LocationHeatmap experts={experts} selected={loc} onSelect={setLoc} />
          </Panel>
          {loc ? (
            <Panel title={`${loc} — ${cityRows.length} on the bench`} action={<Button size="small" onClick={() => setLoc(null)}>Clear</Button>}>
              <Gantt rows={cityRows} labelWidth={160} dense />
            </Panel>
          ) : (
            <Typography variant="body2" color="text.secondary">
              Pick a city on the map or a row above to see its people month by month.
            </Typography>
          )}
        </Stack>
      </Box>
    </Stack>
  );
}

// ---- Variant C: Time first ------------------------------------------------------------------

function VariantC({ experts }: { experts: DashExpert[] }) {
  const [month, setMonth] = useState(0);
  const [loc, setLoc] = useState<string | null>(null);
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const [mode, setMode] = useState<"available" | "all">("available");
  const bench = experts.filter(staffable);
  const inMonth = bench.filter((e) => e.monthly[month] > 0);
  const fte = fteByMonth(experts);
  const groups = useMemo(
    () => byLocation(bench.filter((e) => (mode === "all" || e.monthly[month] > 0) && (!loc || e.location === loc))),
    [bench, mode, month, loc],
  );

  return (
    <Stack spacing={2}>
      <Panel title="Capacity forecast — pick a month">
        <Forecast experts={experts} selected={month} onSelect={setMonth} height={140} />
      </Panel>

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "minmax(0, 2fr) minmax(0, 1fr)" }, gap: 2, alignItems: "start" }}>
        <Panel
          title={`${ADMIN_MONTHS[month].label}: who is free, by location`}
          action={
            <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
              {loc && <Chip size="small" label={loc} onDelete={() => setLoc(null)} />}
              <ToggleButtonGroup size="small" exclusive value={mode} onChange={(_, v) => v && setMode(v)}>
                <ToggleButton value="available">Available</ToggleButton>
                <ToggleButton value="all">Everyone</ToggleButton>
              </ToggleButtonGroup>
            </Stack>
          }
        >
          <CapacityLegend />
          <Stack spacing={0.5} sx={{ mt: 1 }}>
            {groups.map(({ location, list }) => {
              const isOpen = open[location] ?? (groups.length <= 2 || groups[0].location === location || groups[1]?.location === location);
              const gFte = list.reduce((s, e) => s + e.monthly[month] / 100, 0);
              return (
                <Box key={location}>
                  <Stack
                    direction="row"
                    sx={{ alignItems: "center", gap: 1, cursor: "pointer", py: 0.5 }}
                    onClick={() => setOpen({ ...open, [location]: !isOpen })}
                  >
                    {isOpen ? <ExpandMore fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
                    <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>
                      {location}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {list.length} people · {gFte.toFixed(1)} FTE in {ADMIN_MONTHS[month].label}
                    </Typography>
                  </Stack>
                  <Collapse in={isOpen} unmountOnExit>
                    <Box sx={{ pl: 3.5, pb: 1 }}>
                      <Gantt rows={sortByFreeSoonest(list)} highlight={month} labelWidth={180} dense />
                    </Box>
                  </Collapse>
                </Box>
              );
            })}
          </Stack>
        </Panel>

        <Stack spacing={2}>
          <Stack direction="row" sx={{ flexWrap: "wrap", gap: 2 }}>
            <Kpi label={`Free in ${ADMIN_MONTHS[month].label}`} value={inMonth.length} sub={`${fte[month].toFixed(0)} FTE`} />
            <Kpi label="Fully free" value={bench.filter((e) => e.monthly[month] >= 100).length} sub="100% that month" />
            <Kpi label="Utilisation" value={`${bench.length ? (100 - bench.reduce((s, e) => s + e.monthly[month], 0) / bench.length).toFixed(0) : 0}%`} />
            <Kpi label="Δ vs now" value={`${fte[month] - fte[0] >= 0 ? "+" : ""}${(fte[month] - fte[0]).toFixed(0)} FTE`} />
          </Stack>
          <Panel title={`Where the capacity is in ${ADMIN_MONTHS[month].label}`}>
            <ExpertMap points={mapPoints(experts, month)} selected={loc} onSelect={setLoc} height={260} />
          </Panel>
        </Stack>
      </Box>
    </Stack>
  );
}

// ---- route ----------------------------------------------------------------------------------

export default function DashboardPrototype() {
  const variant = useVariant(VARIANTS);
  const { data, isLoading, isError, progress } = useDashboardRoster();

  return (
    <>
      <PageHeader title="Dashboard (prototype)" subtitle={`Variant ${variant} — ${VARIANTS.find((v) => v.key === variant)!.name}`} width="wide">
      <Box sx={{ pb: 12 }}>
        {isLoading && (
          <Box sx={{ maxWidth: 480 }}>
            <Typography variant="body2" color="text.secondary">
              Loading {progress.done}/{progress.total || "…"} expert timelines (prototype fan-out)
            </Typography>
            <LinearProgress variant={progress.total ? "determinate" : "indeterminate"} value={progress.total ? (100 * progress.done) / progress.total : 0} />
          </Box>
        )}
        {isError && <Typography color="error">Roster failed to load.</Typography>}
        {data && variant === "A" && <VariantA experts={data} />}
        {data && variant === "B" && <VariantB experts={data} />}
        {data && variant === "C" && <VariantC experts={data} />}
      </Box>
      </PageHeader>
      <PrototypeSwitcher variants={VARIANTS} />
    </>
  );
}
