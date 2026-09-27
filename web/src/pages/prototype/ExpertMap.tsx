// PROTOTYPE — throwaway (branch prototype/dashboard).
//
// Experts by location as a bubble map: Europe as the main frame (18 of 20 roster locations sit in
// it), North America as an inset for Toronto and Austin, and the two "Remote" buckets as chips —
// a remote person has no point on a map, and pretending otherwise would put them in the Atlantic.
// Bubble area = headcount; fill depth = share of that city available today.
import { alpha, Box, Chip, Stack, Tooltip, Typography, useTheme } from "@mui/material";
import { geoMercator, geoPath, type GeoProjection } from "d3-geo";
import { feature } from "topojson-client";
import type { FeatureCollection } from "geojson";
import type { Topology } from "topojson-specification";
import world from "world-atlas/countries-50m.json";
import { useMemo } from "react";

/** Hand-geocoded: the roster's locations are free text, and there are twenty of them. */
export const GEO: Record<string, [number, number]> = {
  "Warsaw, Poland": [21.01, 52.23],
  "Amsterdam, Netherlands": [4.9, 52.37],
  "London, United Kingdom": [-0.13, 51.51],
  "Wrocław, Poland": [17.04, 51.11],
  "Zagreb, Croatia": [15.98, 45.81],
  "Tallinn, Estonia": [24.75, 59.44],
  "Prague, Czech Republic": [14.42, 50.08],
  "Kyiv, Ukraine": [30.52, 50.45],
  "Dublin, Ireland": [-6.26, 53.35],
  "Lisbon, Portugal": [-9.14, 38.72],
  "Toronto, Canada": [-79.38, 43.65],
  "Austin, TX, USA": [-97.74, 30.27],
  "Sofia, Bulgaria": [23.32, 42.7],
  "Madrid, Spain": [-3.7, 40.42],
  "Vienna, Austria": [16.37, 48.21],
  "Porto, Portugal": [-8.61, 41.15],
  "Stockholm, Sweden": [18.07, 59.33],
  "Berlin, Germany": [13.4, 52.52],
};

export interface MapPoint {
  location: string;
  count: number;
  /** 0-1: share of the location's headcount with capacity > 0 today. */
  availableShare: number;
}

const countries = feature(
  world as unknown as Topology,
  (world as unknown as Topology).objects.countries,
) as unknown as FeatureCollection;

function Frame({
  projection,
  width,
  height,
  points,
  maxCount,
  selected,
  onSelect,
  scale,
}: {
  projection: GeoProjection;
  width: number;
  height: number;
  points: MapPoint[];
  maxCount: number;
  selected: string | null;
  onSelect: (l: string | null) => void;
  scale: number;
}) {
  const t = useTheme();
  const path = geoPath(projection);
  return (
    <svg viewBox={`0 0 ${width} ${height}`} width="100%" style={{ display: "block" }} role="img" aria-label="Experts by location">
      <rect width={width} height={height} fill={t.palette.background.default} />
      {countries.features.map((f, i) => (
        <path key={i} d={path(f) ?? ""} fill={t.palette.action.hover} stroke={t.palette.divider} strokeWidth={0.5} />
      ))}
      {points
        .filter((p) => GEO[p.location])
        .sort((a, b) => b.count - a.count)
        .map((p) => {
          const [x, y] = projection(GEO[p.location])!;
          const r = Math.max(4, Math.sqrt(p.count / maxCount) * 22 * scale);
          const on = selected === p.location;
          return (
            <Tooltip
              key={p.location}
              title={`${p.location} — ${p.count} experts, ${Math.round(p.availableShare * 100)}% available today`}
              arrow
            >
              <circle
                cx={x}
                cy={y}
                r={r}
                fill={alpha(t.palette.info.main, 0.25 + 0.7 * p.availableShare)}
                stroke={on ? t.palette.primary.main : t.palette.background.paper}
                strokeWidth={on ? 3 : 2}
                style={{ cursor: "pointer" }}
                onClick={() => onSelect(on ? null : p.location)}
              />
            </Tooltip>
          );
        })}
    </svg>
  );
}

export default function ExpertMap({
  points,
  selected,
  onSelect,
  height = 420,
}: {
  points: MapPoint[];
  selected: string | null;
  onSelect: (l: string | null) => void;
  height?: number;
}) {
  const width = Math.round(height * 1.25);
  const maxCount = Math.max(1, ...points.map((p) => p.count));
  const europe = useMemo(
    () =>
      geoMercator().fitExtent(
        [
          [10, 10],
          [width - 10, height - 10],
        ],
        // Wide to the west on purpose: the Atlantic is where the North America inset sits, so it
        // covers ocean rather than Iberia.
        { type: "MultiPoint", coordinates: [[-30, 35], [33, 35], [33, 62], [-30, 62]] },
      ),
    [width, height],
  );
  const insetW = Math.round(width * 0.3);
  const insetH = Math.round(insetW * 0.7);
  const na = useMemo(
    () =>
      geoMercator().fitExtent(
        [
          [6, 6],
          [insetW - 6, insetH - 6],
        ],
        { type: "MultiPoint", coordinates: [[-106, 26], [-70, 26], [-70, 48], [-106, 48]] },
      ),
    [insetW, insetH],
  );
  const remote = points.filter((p) => !GEO[p.location]);

  return (
    <Box>
      <Box sx={{ position: "relative", borderRadius: 1, overflow: "hidden", border: 1, borderColor: "divider" }}>
        <Frame projection={europe} width={width} height={height} points={points} maxCount={maxCount} selected={selected} onSelect={onSelect} scale={height / 420} />
        <Box
          sx={{ position: "absolute", left: 8, top: 8, width: "30%", border: 1, borderColor: "divider", borderRadius: 1, overflow: "hidden", bgcolor: "background.paper" }}
        >
          <Typography variant="caption" color="text.secondary" sx={{ position: "absolute", left: 6, top: 2 }}>
            North America
          </Typography>
          <Frame projection={na} width={insetW} height={insetH} points={points} maxCount={maxCount} selected={selected} onSelect={onSelect} scale={0.6 * (height / 420)} />
        </Box>
      </Box>
      <Stack direction="row" spacing={1} sx={{ mt: 1, flexWrap: "wrap", alignItems: "center" }}>
        {remote.map((p) => (
          <Chip
            key={p.location}
            size="small"
            variant={selected === p.location ? "filled" : "outlined"}
            label={`${p.location} · ${p.count}`}
            onClick={() => onSelect(selected === p.location ? null : p.location)}
          />
        ))}
        <Typography variant="caption" color="text.secondary" sx={{ ml: "auto !important" }}>
          Bubble area = experts · depth = share available today · click to filter
        </Typography>
      </Stack>
    </Box>
  );
}
