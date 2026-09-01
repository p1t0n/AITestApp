/**
 * PROTOTYPE — throwaway. P1T-175's remaining question, on `/prototype/privacy`.
 *
 * "Three variants of the Expert's Privacy & data page, switchable via `?variant=`, on the throwaway
 * `/prototype/privacy` route — mounted inside the real signed-in shell so they are judged against
 * the real rail, theme and density."
 *
 * The two questions this exists to answer, from P1T-175:
 *   4. Do the five profile states read at a glance — including the pairs that coincide?
 *   5. Are pause and delete visibly different kinds of thing?
 *
 * Nothing here talks to the backend. Actions mutate local state and append to a log, because the
 * question is what the surface should look like, not whether the API works.
 */
import { useState } from "react";
import { Button, Divider, Stack, Typography } from "@mui/material";
import { useSearchParams } from "react-router-dom";
import PrototypeSwitcher from "../../components/PrototypeSwitcher";
import VariantA from "./VariantA";
import VariantB from "./VariantB";
import VariantC from "./VariantC";
import { INITIAL, derive, type Actions, type PrivacyState } from "./privacyState";

const VARIANTS = ["A", "B", "C"];
const NAMES: Record<string, string> = {
  A: "The record",
  B: "Status card + accordions",
  C: "Intents",
};

export default function PrivacyDataPrototype() {
  const [params, setParams] = useSearchParams();
  const variant = params.get("variant") ?? "A";

  const [s, setS] = useState<PrivacyState>(INITIAL);
  const [log, setLog] = useState<string[]>([]);
  const say = (m: string) => setLog((l) => [m, ...l].slice(0, 3));

  const on: Actions = {
    pause: () => {
      setS((p) => ({ ...p, hiddenAt: new Date().toISOString() }));
      say("paused");
    },
    unpause: () => {
      setS((p) => ({ ...p, hiddenAt: null }));
      say("resumed");
    },
    exportData: () => say(`export requested (${derive(s).canExport})`),
    object: () => say("OBJECTED → would erase"),
    deleteAll: (w) => say(`DELETE with control word "${w}" → would erase`),
    contest: (job) => say(`contested: ${job}`),
  };

  const setVariant = (v: string) => {
    const next = new URLSearchParams(params);
    next.set("variant", v);
    setParams(next, { replace: true });
  };

  const d = derive(s);

  const controls = (
    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
      <Typography variant="caption" sx={{ color: "#fff", opacity: 0.7 }}>
        drive state:
      </Typography>
      <Button
        size="small"
        variant="text"
        sx={{ color: "#9fe", minWidth: 0 }}
        onClick={() => setS((p) => ({ ...p, ownsRow: !p.ownsRow, claimPending: p.ownsRow }))}
      >
        {s.ownsRow ? "owns row" : s.claimPending ? "claim pending" : "no profile"}
      </Button>
      <Divider orientation="vertical" flexItem sx={{ borderColor: "#444" }} />
      <Button
        size="small"
        variant="text"
        sx={{ color: "#9fe", minWidth: 0 }}
        onClick={() => (d.paused ? on.unpause() : on.pause())}
      >
        {d.paused ? "paused" : "active"}
      </Button>
      <Divider orientation="vertical" flexItem sx={{ borderColor: "#444" }} />
      <Button
        size="small"
        variant="text"
        sx={{ color: "#9fe", minWidth: 0 }}
        onClick={() =>
          setS((p) => ({ ...p, daysToExpiry: p.daysToExpiry <= 30 ? 730 : 12 }))
        }
      >
        {d.expiring ? `expiring (${s.daysToExpiry}d)` : "not expiring"}
      </Button>
      <Divider orientation="vertical" flexItem sx={{ borderColor: "#444" }} />
      <Button
        size="small"
        variant="text"
        sx={{ color: "#9fe", minWidth: 0 }}
        onClick={() =>
          setS((p) => ({
            ...p,
            basis: p.basis === "contract" ? "legitimate-interest" : "contract",
          }))
        }
      >
        {s.basis === "contract" ? "6(1)(b) contract" : "6(1)(f) legit interest"}
      </Button>
    </Stack>
  );

  const readout = (
    <span>
      owns={String(s.ownsRow)} pending={String(s.claimPending)} paused={String(d.paused)} expiring=
      {String(d.expiring)} basis={s.basis} scannable={String(d.scannable)} export={d.canExport}{" "}
      object={String(d.canObject)}
      {log.length > 0 && <> · last: {log[0]}</>}
    </span>
  );

  return (
    <>
      {variant === "A" && <VariantA s={s} on={on} />}
      {variant === "B" && <VariantB s={s} on={on} />}
      {variant === "C" && <VariantC s={s} on={on} />}
      <PrototypeSwitcher
        variants={VARIANTS}
        names={NAMES}
        current={variant}
        onChange={setVariant}
        readout={readout}
        controls={controls}
      />
    </>
  );
}
