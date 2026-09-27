// PROTOTYPE — throwaway (branch prototype/dashboard). Floating variant switcher: ← / → cycle the
// `?variant=` search param. Renders nothing in a production build.
import { useEffect } from "react";
import { useSearchParams } from "react-router";
import { Box, IconButton, Typography } from "@mui/material";
import ChevronLeft from "@mui/icons-material/ChevronLeft";
import ChevronRight from "@mui/icons-material/ChevronRight";

export interface VariantDef {
  key: string;
  name: string;
}

export function useVariant(variants: VariantDef[]): string {
  const [params] = useSearchParams();
  const v = params.get("variant");
  return variants.some((x) => x.key === v) ? v! : variants[0].key;
}

export default function PrototypeSwitcher({ variants }: { variants: VariantDef[] }) {
  const [params, setParams] = useSearchParams();
  const current = useVariant(variants);
  const idx = variants.findIndex((v) => v.key === current);

  const go = (d: number) => {
    const next = variants[(idx + d + variants.length) % variants.length];
    const p = new URLSearchParams(params);
    p.set("variant", next.key);
    setParams(p, { replace: true });
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = document.activeElement as HTMLElement | null;
      if (el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.isContentEditable)) return;
      if (e.key === "ArrowLeft") go(-1);
      if (e.key === "ArrowRight") go(1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  if (import.meta.env.PROD) return null;

  return (
    <Box
      sx={{
        position: "fixed",
        bottom: 20,
        left: "50%",
        transform: "translateX(-50%)",
        zIndex: 2000,
        display: "flex",
        alignItems: "center",
        gap: 1,
        px: 1,
        py: 0.5,
        borderRadius: 999,
        bgcolor: "#111",
        color: "#fff",
        boxShadow: "0 6px 24px rgba(0,0,0,.35)",
        border: "2px dashed #F59E0B",
      }}
    >
      <IconButton size="small" onClick={() => go(-1)} sx={{ color: "#fff" }} aria-label="Previous variant">
        <ChevronLeft />
      </IconButton>
      <Typography variant="body2" sx={{ fontFamily: "monospace", minWidth: 220, textAlign: "center" }}>
        PROTOTYPE {current} — {variants[idx].name}
      </Typography>
      <IconButton size="small" onClick={() => go(1)} sx={{ color: "#fff" }} aria-label="Next variant">
        <ChevronRight />
      </IconButton>
    </Box>
  );
}
