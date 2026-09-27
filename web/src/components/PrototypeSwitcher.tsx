// PROTOTYPE — throwaway. Floating variant switcher for `?variant=` UI prototypes. Never ships:
// renders nothing in a production build.
import { useEffect } from "react";
import { useSearchParams } from "react-router";

export interface PrototypeVariant {
  key: string;
  name: string;
}

export default function PrototypeSwitcher({ variants }: { variants: PrototypeVariant[] }) {
  const [params, setParams] = useSearchParams();
  const current = params.get("variant") ?? "";
  const index = Math.max(0, variants.findIndex((v) => v.key === current));

  const go = (delta: number) => {
    const next = variants[(index + delta + variants.length) % variants.length];
    const p = new URLSearchParams(params);
    if (next.key) p.set("variant", next.key);
    else p.delete("variant");
    setParams(p, { replace: true });
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      if (el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.isContentEditable)) return;
      if (e.key === "ArrowLeft") go(-1);
      if (e.key === "ArrowRight") go(1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  if (import.meta.env.PROD) return null;

  const v = variants[index];
  const btn: React.CSSProperties = {
    background: "transparent",
    color: "#fff",
    border: "none",
    fontSize: 18,
    cursor: "pointer",
    padding: "0 10px",
  };
  return (
    <div
      style={{
        position: "fixed",
        bottom: 20,
        left: "50%",
        transform: "translateX(-50%)",
        zIndex: 2000,
        display: "flex",
        alignItems: "center",
        gap: 4,
        padding: "8px 12px",
        borderRadius: 999,
        background: "#111",
        color: "#fff",
        font: "600 13px system-ui, sans-serif",
        boxShadow: "0 6px 24px rgba(0,0,0,.35)",
        outline: "2px dashed #ff4fd8",
      }}
    >
      <button style={btn} onClick={() => go(-1)} aria-label="Previous variant">
        ←
      </button>
      <span style={{ minWidth: 260, textAlign: "center" }}>
        PROTOTYPE · {v.key || "—"} — {v.name}
      </span>
      <button style={btn} onClick={() => go(1)} aria-label="Next variant">
        →
      </button>
    </div>
  );
}
