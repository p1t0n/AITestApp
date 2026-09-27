// PROTOTYPE — throwaway (branch prototype/dashboard). Not production code.
//
// Question: what should a User see of their own availability, this calendar year by month?
// Plan: three variants on `/me/prototype/availability`, `?variant=A|B|C`. Real data — the signed-in
// User's own record via /me/visibility → /experts/:id. A record with no availability entries gets a
// clearly labelled sample schedule so the variants have something to draw.
//
//   A — Year strip: one Gantt row, Jan–Dec, a today marker, and the change list under it.
//   B — Month cards: a 4×3 calendar of months, one ring gauge each.
//   C — Step line: capacity as a step line across the year, change points annotated, the headline
//       being "what changes next".
import { Alert, Box, Card, CardContent, CircularProgress, Stack, Tooltip, Typography, useTheme } from "@mui/material";
import { Navigate } from "react-router";
import PageHeader from "../../components/PageHeader";
import PrototypeSwitcher, { useVariant, type VariantDef } from "../../components/PrototypeSwitcher";
import { useExpert, useMyVisibility } from "../../api";
import type { AvailabilityEntry } from "../../types";
import { capacityOn, monthAverage, monthsFrom, TODAY } from "./dashboardData";
import { CapacityLegend, useCapacityFill } from "./DashboardPrototype";

const VARIANTS: VariantDef[] = [
  { key: "A", name: "Year strip" },
  { key: "B", name: "Month cards" },
  { key: "C", name: "Step line" },
];

const YEAR = TODAY.getUTCFullYear();
// One calendar year, so the "Jan 27"-style year suffix the rolling admin horizon needs is noise here.
const MONTHS = monthsFrom(new Date(Date.UTC(YEAR, 0, 1)), 12).map((m) => ({ ...m, label: m.label.split(" ")[0] }));
const THIS_MONTH = TODAY.getUTCMonth();
const DAY_OF_YEAR = (TODAY.getTime() - Date.UTC(YEAR, 0, 1)) / 86400000;
const DAYS_IN_YEAR = (Date.UTC(YEAR + 1, 0, 1) - Date.UTC(YEAR, 0, 1)) / 86400000;

const SAMPLE: AvailabilityEntry[] = [
  { id: "s1", effectiveFrom: `${YEAR}-01-01`, capacityPercent: 0 },
  { id: "s2", effectiveFrom: `${YEAR}-04-14`, capacityPercent: 50 },
  { id: "s3", effectiveFrom: `${YEAR}-07-01`, capacityPercent: 100 },
  { id: "s4", effectiveFrom: `${YEAR}-09-20`, capacityPercent: 20 },
  { id: "s5", effectiveFrom: `${YEAR}-11-10`, capacityPercent: 80 },
];

function changesThisYear(entries: AvailabilityEntry[]) {
  return [...entries]
    .filter((e) => e.effectiveFrom.startsWith(String(YEAR)))
    .sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom));
}

const fmt = (iso: string) => new Date(iso + "T00:00:00Z").toLocaleDateString("en", { day: "numeric", month: "short", timeZone: "UTC" });

// ---- A: Year strip --------------------------------------------------------------------------

function VariantA({ entries }: { entries: AvailabilityEntry[] }) {
  const fill = useCapacityFill();
  const monthly = MONTHS.map((m) => monthAverage(entries, m.year, m.month));
  return (
    <Stack spacing={3}>
      <Card variant="outlined">
        <CardContent sx={{ pb: "36px !important" }}>
          <Stack direction="row" sx={{ mb: 2, alignItems: "center" }}>
            <Typography variant="subtitle1" sx={{ flexGrow: 1 }}>
              My availability in {YEAR}
            </Typography>
            <CapacityLegend />
          </Stack>
          <Box sx={{ position: "relative" }}>
            <Box sx={{ display: "grid", gridTemplateColumns: "repeat(12, 1fr)", gap: "2px" }}>
              {monthly.map((c, i) => (
                <Tooltip key={i} title={`${MONTHS[i].label}: ${c}% available on average`}>
                  <Box>
                    <Box sx={{ height: 44, borderRadius: 0.5, bgcolor: fill(c), border: 1, borderColor: c <= 0 ? "divider" : "transparent", display: "grid", placeItems: "center" }}>
                      <Typography variant="caption" sx={{ color: c > 55 ? "info.contrastText" : "text.secondary", fontVariantNumeric: "tabular-nums" }}>
                        {c}%
                      </Typography>
                    </Box>
                    <Typography variant="caption" color={i === THIS_MONTH ? "text.primary" : "text.secondary"} sx={{ display: "block", textAlign: "center", fontWeight: i === THIS_MONTH ? 700 : 400 }}>
                      {MONTHS[i].label}
                    </Typography>
                  </Box>
                </Tooltip>
              ))}
            </Box>
            <Box sx={{ position: "absolute", top: -6, height: 56, left: `${(DAY_OF_YEAR / DAYS_IN_YEAR) * 100}%`, borderLeft: 2, borderColor: "primary.main" }}>
              <Typography variant="caption" sx={{ position: "absolute", bottom: -34, left: -14, color: "primary.dark", fontWeight: 700 }}>
                today
              </Typography>
            </Box>
          </Box>
        </CardContent>
      </Card>
      <Card variant="outlined">
        <CardContent>
          <Typography variant="subtitle1" sx={{ mb: 1 }}>
            Changes this year
          </Typography>
          {changesThisYear(entries).map((e) => (
            <Stack key={e.id} direction="row" spacing={2} sx={{ py: 0.5, opacity: e.effectiveFrom < TODAY.toISOString().slice(0, 10) ? 0.6 : 1 }}>
              <Typography variant="body2" sx={{ width: 70, fontVariantNumeric: "tabular-nums" }}>
                {fmt(e.effectiveFrom)}
              </Typography>
              <Typography variant="body2">→ {e.capacityPercent}% available</Typography>
            </Stack>
          ))}
        </CardContent>
      </Card>
    </Stack>
  );
}

// ---- B: Month cards -------------------------------------------------------------------------

function VariantB({ entries }: { entries: AvailabilityEntry[] }) {
  const monthly = MONTHS.map((m) => monthAverage(entries, m.year, m.month));
  return (
    <Box sx={{ display: "grid", gridTemplateColumns: { xs: "repeat(2, 1fr)", sm: "repeat(3, 1fr)", md: "repeat(4, 1fr)" }, gap: 2 }}>
      {monthly.map((c, i) => {
        const past = i < THIS_MONTH;
        const changes = changesThisYear(entries).filter((e) => Number(e.effectiveFrom.slice(5, 7)) === i + 1);
        return (
          <Card key={i} variant="outlined" sx={{ opacity: past ? 0.55 : 1, borderColor: i === THIS_MONTH ? "primary.main" : "divider", borderWidth: i === THIS_MONTH ? 2 : 1 }}>
            <CardContent sx={{ textAlign: "center" }}>
              <Typography variant="subtitle2">
                {MONTHS[i].label} {i === THIS_MONTH && "· now"}
              </Typography>
              <Box sx={{ position: "relative", display: "inline-flex", my: 1 }}>
                <CircularProgress variant="determinate" value={100} size={84} thickness={5} sx={{ color: "divider", position: "absolute" }} />
                <CircularProgress variant="determinate" value={c} size={84} thickness={5} color="info" />
                <Box sx={{ position: "absolute", inset: 0, display: "grid", placeItems: "center" }}>
                  <Typography variant="h6" sx={{ fontVariantNumeric: "tabular-nums" }}>
                    {c}%
                  </Typography>
                </Box>
              </Box>
              <Typography variant="caption" color="text.secondary" sx={{ display: "block", minHeight: 20 }}>
                {changes.length ? changes.map((e) => `${fmt(e.effectiveFrom)} → ${e.capacityPercent}%`).join(", ") : c >= 100 ? "fully available" : c <= 0 ? "booked" : "partly available"}
              </Typography>
            </CardContent>
          </Card>
        );
      })}
    </Box>
  );
}

// ---- C: Step line ---------------------------------------------------------------------------

function VariantC({ entries }: { entries: AvailabilityEntry[] }) {
  const t = useTheme();
  const W = 900;
  const H = 260;
  const pad = { l: 40, r: 16, t: 24, b: 28 };
  const x = (day: number) => pad.l + (day / DAYS_IN_YEAR) * (W - pad.l - pad.r);
  const y = (c: number) => pad.t + (1 - c / 100) * (H - pad.t - pad.b);
  const pts: string[] = [];
  let prev = -1;
  for (let d = 0; d <= DAYS_IN_YEAR; d++) {
    const c = capacityOn(entries, new Date(Date.UTC(YEAR, 0, 1) + d * 86400000));
    if (c !== prev) {
      if (prev >= 0) pts.push(`${x(d)},${y(prev)}`);
      pts.push(`${x(d)},${y(c)}`);
      prev = c;
    }
  }
  pts.push(`${x(DAYS_IN_YEAR)},${y(prev)}`);
  const todayIso = TODAY.toISOString().slice(0, 10);
  const next = changesThisYear(entries).find((e) => e.effectiveFrom > todayIso) ?? [...entries].sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom)).find((e) => e.effectiveFrom > todayIso);
  const now = capacityOn(entries, TODAY);
  const dayOf = (iso: string) => (Date.parse(iso + "T00:00:00Z") - Date.UTC(YEAR, 0, 1)) / 86400000;

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={4} sx={{ alignItems: "baseline", flexWrap: "wrap" }}>
        <Box>
          <Typography variant="caption" color="text.secondary">
            Available now
          </Typography>
          <Typography variant="h3" sx={{ fontVariantNumeric: "tabular-nums" }}>
            {now}%
          </Typography>
        </Box>
        <Box>
          <Typography variant="caption" color="text.secondary">
            Next change
          </Typography>
          <Typography variant="h5">{next ? `${fmt(next.effectiveFrom)} → ${next.capacityPercent}%` : "none scheduled"}</Typography>
        </Box>
      </Stack>
      <Card variant="outlined">
        <CardContent>
          <svg viewBox={`0 0 ${W} ${H}`} width="100%" role="img" aria-label={`Availability across ${YEAR}`}>
            {[0, 50, 100].map((c) => (
              <g key={c}>
                <line x1={pad.l} x2={W - pad.r} y1={y(c)} y2={y(c)} stroke={t.palette.divider} />
                <text x={pad.l - 6} y={y(c) + 4} textAnchor="end" fontSize={11} fill={t.palette.text.secondary}>
                  {c}%
                </text>
              </g>
            ))}
            {MONTHS.map((m, i) => (
              <text key={i} x={x(dayOf(`${YEAR}-${String(i + 1).padStart(2, "0")}-15`))} y={H - 8} textAnchor="middle" fontSize={11} fill={i === THIS_MONTH ? t.palette.text.primary : t.palette.text.secondary}>
                {m.label}
              </text>
            ))}
            <rect x={x(DAY_OF_YEAR)} y={pad.t} width={x(DAYS_IN_YEAR) - x(DAY_OF_YEAR)} height={H - pad.t - pad.b} fill={t.palette.action.hover} />
            <polyline points={pts.join(" ")} fill="none" stroke={t.palette.info.main} strokeWidth={2} />
            <line x1={x(DAY_OF_YEAR)} x2={x(DAY_OF_YEAR)} y1={pad.t - 10} y2={H - pad.b} stroke={t.palette.primary.main} strokeWidth={2} />
            <text x={x(DAY_OF_YEAR) + 4} y={pad.t - 12} fontSize={11} fill={t.palette.text.primary}>
              today
            </text>
            {changesThisYear(entries).map((e) => (
              <Tooltip key={e.id} title={`${fmt(e.effectiveFrom)} → ${e.capacityPercent}%`}>
                <circle cx={x(dayOf(e.effectiveFrom))} cy={y(e.capacityPercent)} r={5} fill={t.palette.info.main} stroke={t.palette.background.paper} strokeWidth={2} />
              </Tooltip>
            ))}
          </svg>
          <Typography variant="caption" color="text.secondary">
            Shaded = ahead of today. Dots are the changes on your record.
          </Typography>
        </CardContent>
      </Card>
    </Stack>
  );
}

// ---- route ----------------------------------------------------------------------------------

export default function MyAvailabilityPrototype() {
  const variant = useVariant(VARIANTS);
  const mine = useMyVisibility();
  const expert = useExpert(mine.data?.expertId ?? "");

  if (mine.isError) return <Navigate to="/me/claim" replace />;

  const real = expert.data?.availabilityEntries ?? [];
  const entries = real.length ? real : SAMPLE;

  return (
    <>
      <PageHeader title="My availability (prototype)" subtitle={`Variant ${variant} — ${VARIANTS.find((v) => v.key === variant)!.name}`} width="content">
        <Box sx={{ pb: 12 }}>
          {!expert.data ? (
            <CircularProgress />
          ) : (
            <>
              {!real.length && (
                <Alert severity="info" sx={{ mb: 2 }}>
                  Your record has no availability entries — showing a sample schedule.
                </Alert>
              )}
              {variant === "A" && <VariantA entries={entries} />}
              {variant === "B" && <VariantB entries={entries} />}
              {variant === "C" && <VariantC entries={entries} />}
            </>
          )}
        </Box>
      </PageHeader>
      <PrototypeSwitcher variants={VARIANTS} />
    </>
  );
}
