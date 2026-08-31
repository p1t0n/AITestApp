import { useState } from "react";
import type { ElementType, MouseEvent, ReactNode } from "react";
import {
  AppBar,
  Box,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Toolbar,
  Tooltip,
  Typography,
} from "@mui/material";
import AccountTreeOutlinedIcon from "@mui/icons-material/AccountTreeOutlined";
import ArticleOutlinedIcon from "@mui/icons-material/ArticleOutlined";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import DarkModeOutlinedIcon from "@mui/icons-material/DarkModeOutlined";
import GroupOutlinedIcon from "@mui/icons-material/GroupOutlined";
import LightModeOutlinedIcon from "@mui/icons-material/LightModeOutlined";
import LogoutIcon from "@mui/icons-material/Logout";
import MenuIcon from "@mui/icons-material/Menu";
import SettingsBrightnessOutlinedIcon from "@mui/icons-material/SettingsBrightnessOutlined";
import { visuallyHidden } from "@mui/utils";
import { Link as RouterLink, useLocation, useNavigate } from "react-router-dom";
import { signOut } from "../api";
import { useSessionEmail } from "../auth/useAuth";
import {
  followSystemMode,
  setMode,
  useThemeModeChoice,
  type ThemeModeChoice,
} from "../theme/mode";
import { RAIL_COLLAPSED_WIDTH, RAIL_WIDTH, useRailPush, type AppRail } from "./useAppRail";

/** The product name. Frozen: `App.errors.test.tsx` reads it to prove the shell survived a page throw. */
export const BRAND = "CV Manager";

/**
 * The three places. Accessible names are frozen (`manuals/spa-design-system.md` §9) — the e2e and
 * unit suites both assert `link` by name, and a collapsed rail shows no text at all, which is why
 * every item carries an explicit `aria-label` in both states rather than relying on its label node.
 */
const NAV: { label: string; to: string; icon: ReactNode }[] = [
  { label: "CVs", to: "/", icon: <ArticleOutlinedIcon /> },
  { label: "Skill Catalog", to: "/catalog", icon: <AccountTreeOutlinedIcon /> },
  { label: "Users", to: "/users", icon: <GroupOutlinedIcon /> },
];

const THEME_CHOICES: { value: ThemeModeChoice; label: string; icon: ReactNode }[] = [
  { value: "light", label: "Light", icon: <LightModeOutlinedIcon /> },
  { value: "dark", label: "Dark", icon: <DarkModeOutlinedIcon /> },
  { value: "system", label: "System", icon: <SettingsBrightnessOutlinedIcon /> },
];

/** Which route the rail should mark as current. `/` only matches exactly; the rest by prefix. */
function isCurrent(pathname: string, to: string): boolean {
  return to === "/" ? pathname === "/" : pathname.startsWith(to);
}

/**
 * One row of the rail, in either state. Collapsed rows keep the accessible name and gain a tooltip;
 * expanded rows keep the tooltip too, which costs nothing and is where a truncated email is read.
 */
interface RailRowProps {
  label: string;
  collapsed: boolean;
  icon: ReactNode;
  selected?: boolean;
  component?: ElementType;
  to?: string;
  "aria-haspopup"?: "menu";
  onClick?: (event: MouseEvent<HTMLElement>) => void;
}

function RailRow({ label, collapsed, icon, ...rest }: RailRowProps) {
  return (
    <Tooltip
      title={label}
      placement="right"
      disableHoverListener={!collapsed}
      disableFocusListener={!collapsed}
      disableTouchListener={!collapsed}
    >
      <ListItemButton
        aria-label={label}
        sx={{
          borderRadius: 1,
          mx: 1,
          my: 0.25,
          minHeight: 40,
          justifyContent: collapsed ? "center" : "flex-start",
          px: collapsed ? 0 : 1.5,
        }}
        {...rest}
      >
        <ListItemIcon
          sx={{ minWidth: 0, mr: collapsed ? 0 : 2, color: "inherit", justifyContent: "center" }}
        >
          {icon}
        </ListItemIcon>
        {!collapsed && (
          <ListItemText
            primary={label}
            primaryTypographyProps={{ variant: "body2", noWrap: true, fontWeight: 500 }}
          />
        )}
      </ListItemButton>
    </Tooltip>
  );
}

/**
 * The theme control. Three choices rather than a two-state flip, because the mechanism underneath
 * has three states and a toggle that pins an override on first click could never hand the default
 * back (`src/theme/mode.ts`).
 */
function ThemeControl({ collapsed }: { collapsed: boolean }) {
  const choice = useThemeModeChoice();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const current = THEME_CHOICES.find((c) => c.value === choice) ?? THEME_CHOICES[2];

  return (
    <>
      <RailRow
        label="Theme"
        collapsed={collapsed}
        icon={current.icon}
        aria-haspopup="menu"
        onClick={(e) => setAnchor(e.currentTarget)}
      />
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {THEME_CHOICES.map((c) => (
          <MenuItem
            key={c.value}
            selected={c.value === choice}
            onClick={() => {
              if (c.value === "system") followSystemMode();
              else setMode(c.value);
              setAnchor(null);
            }}
          >
            <ListItemIcon sx={{ color: "inherit" }}>{c.icon}</ListItemIcon>
            <ListItemText primary={c.label} />
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}

/** Brand block. Collapsed it keeps the mark alone — the name is still the tooltip and the label. */
function Brand({ collapsed }: { collapsed: boolean }) {
  return (
    <Box
      sx={{
        display: "flex",
        alignItems: "center",
        gap: 1.5,
        minHeight: 56,
        px: collapsed ? 0 : 2,
        justifyContent: collapsed ? "center" : "flex-start",
      }}
    >
      <Box
        aria-hidden
        sx={{
          width: 32,
          height: 32,
          flexShrink: 0,
          borderRadius: 1,
          bgcolor: "primary.main",
          color: "primary.contrastText",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontSize: "0.75rem",
          fontWeight: 700,
          letterSpacing: "0.02em",
        }}
      >
        CV
      </Box>
      {/* Rendered even when collapsed — `visibility: hidden` would drop it from the a11y tree and
          from `getByText`, and the whole shell's claim is that it survives whatever the page does. */}
      <Typography variant="subtitle1" noWrap sx={collapsed ? visuallyHidden : undefined}>
        {BRAND}
      </Typography>
    </Box>
  );
}

/** Everything inside the rail, in either the permanent or the temporary drawer. */
function RailContents({ rail, onNavigate }: { rail: AppRail; onNavigate?: () => void }) {
  const { collapsed } = rail;
  const location = useLocation();
  const navigate = useNavigate();
  const email = useSessionEmail();

  return (
    <Box sx={{ display: "flex", flexDirection: "column", height: "100%", overflowX: "hidden" }}>
      <Brand collapsed={collapsed} />
      <Divider />

      <List component="nav" aria-label="Main" sx={{ py: 1 }}>
        {NAV.map((item) => (
          <RailRow
            key={item.to}
            label={item.label}
            collapsed={collapsed}
            icon={item.icon}
            component={RouterLink}
            to={item.to}
            selected={isCurrent(location.pathname, item.to)}
            onClick={onNavigate}
          />
        ))}
      </List>

      {/* The bottom block is pinned to the bottom by this, not by a magic height. */}
      <Box sx={{ flexGrow: 1 }} />

      <Divider />
      <List sx={{ py: 1 }}>
        <ThemeControl collapsed={collapsed} />
        <RailRow
          label="Sign out"
          collapsed={collapsed}
          icon={<LogoutIcon />}
          onClick={() => {
            onNavigate?.();
            signOut();
            navigate("/signin");
          }}
        />
      </List>

      {email && !collapsed && (
        <Tooltip title={email} placement="right">
          <Typography
            variant="caption"
            noWrap
            data-testid="rail-user"
            sx={{ px: 2.5, pb: 1.5, color: "text.secondary", display: "block" }}
          >
            {email}
          </Typography>
        </Tooltip>
      )}

      {!rail.isNarrow && (
        <>
          <Divider />
          <Box sx={{ display: "flex", justifyContent: collapsed ? "center" : "flex-end", p: 0.5 }}>
            <Tooltip
              title={
                rail.squeezed
                  ? "Not enough room to expand the rail"
                  : collapsed
                    ? "Expand the navigation rail"
                    : "Collapse the navigation rail"
              }
              placement="right"
            >
              {/* Disabled rather than a silent no-op while squeezed: a control that looks like it
                  worked and changed nothing is worse than one that plainly cannot. A disabled
                  button takes no focus, so the tooltip needs a wrapper to hang its events on. */}
              <Box component="span" sx={{ display: "inline-flex" }}>
                <IconButton
                  size="small"
                  disabled={rail.squeezed}
                  aria-label={collapsed ? "Expand the navigation rail" : "Collapse the navigation rail"}
                  onClick={rail.toggleCollapsed}
                >
                  {collapsed ? <ChevronRightIcon /> : <ChevronLeftIcon />}
                </IconButton>
              </Box>
            </Tooltip>
          </Box>
        </>
      )}
    </Box>
  );
}

/**
 * The app's left edge. Above `md` a permanent, fixed drawer that publishes what it covers as
 * {@link RAIL_PUSH_VAR}; below `md` a temporary drawer behind a slim top bar, covering nothing.
 *
 * Rendered only for a signed-in user, which is what makes the push property exist exactly as long
 * as there is a rail to make room for — the same reasoning as the dock's own push (P1T-154).
 */
export default function AppRailNav({ rail }: { rail: AppRail }) {
  useRailPush(rail);
  const width = rail.collapsed ? RAIL_COLLAPSED_WIDTH : RAIL_WIDTH;

  // The app's own chrome is not part of anything worth printing, exactly as the old AppBar was not.
  const hideInPrint = { "@media print": { display: "none" } } as const;

  if (rail.isNarrow) {
    return (
      <>
        <AppBar position="sticky" elevation={0} color="default" sx={hideInPrint}>
          <Toolbar variant="dense">
            <IconButton
              edge="start"
              aria-label="Open the navigation"
              onClick={rail.openDrawer}
              sx={{ mr: 1 }}
            >
              <MenuIcon />
            </IconButton>
            <Typography variant="subtitle1" noWrap>
              {BRAND}
            </Typography>
          </Toolbar>
        </AppBar>
        <Drawer
          open={rail.drawerOpen}
          onClose={rail.closeDrawer}
          sx={hideInPrint}
          PaperProps={{ sx: { width: RAIL_WIDTH } }}
        >
          <RailContents rail={rail} onNavigate={rail.closeDrawer} />
        </Drawer>
      </>
    );
  }

  return (
    <Drawer
      variant="permanent"
      sx={{ width, flexShrink: 0, ...hideInPrint }}
      PaperProps={{
        sx: {
          width,
          borderRight: 1,
          borderColor: "divider",
          overflowX: "hidden",
          transition: "width 150ms ease",
        },
      }}
    >
      <RailContents rail={rail} />
    </Drawer>
  );
}
