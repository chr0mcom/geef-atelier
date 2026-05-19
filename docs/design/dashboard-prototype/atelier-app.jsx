/* global React, ReactDOM, RUNS,
          LoginScreen, WelcomeScreen, NewRunScreen, RunsListScreen, RunDetailScreen, StyleGuide,
          TweaksPanel, TweakSection, TweakColor, TweakRadio, useTweaks,
          IconAdd, IconUser, IconLogout, IconLayers */

const { useState, useEffect, useRef } = React;

/* ============================================================
   TWEAKS — palette switcher + density
   ============================================================ */
const TWEAK_DEFAULTS = /*EDITMODE-BEGIN*/{
  "palette": "vellum",
  "density": "comfortable",
  "role": "admin"
}/*EDITMODE-END*/;

function applyPalette(palette) {
  const html = document.documentElement;
  html.classList.remove("palette-noir", "palette-vellum", "palette-petrol");
  html.classList.add(`palette-${palette}`);
}
function applyDensity(d) {
  document.documentElement.style.setProperty(
    "--density-pad",
    d === "compact" ? "12px" : d === "comfortable" ? "20px" : "28px"
  );
}

/* ============================================================
   APP SHELL
   ============================================================ */
function App() {
  const [t, setTweak] = useTweaks(TWEAK_DEFAULTS);
  const [auth, setAuth] = useState(() => {
    try { return localStorage.getItem("atelier_auth") === "1"; } catch (e) { return false; }
  });
  const [route, setRoute] = useState({ name: "welcome", params: {} });
  const [userMenu, setUserMenu] = useState(false);
  const [scope, setScope] = useState("my"); // 'my' | 'all'
  const isAdmin = t.role === "admin";

  useEffect(() => {
    try { localStorage.setItem("atelier_auth", auth ? "1" : "0"); } catch (e) {}
  }, [auth]);

  // apply palette + density side-effects
  useEffect(() => { applyPalette(t.palette); }, [t.palette]);
  useEffect(() => { applyDensity(t.density); }, [t.density]);

  const go = (name, id) => {
    setRoute({ name, params: { id } });
    window.scrollTo({ top: 0, behavior: "instant" });
  };

  // Global "running" indicator
  const anyRunning = RUNS.some(r => r.status === "running");

  if (!auth) {
    return <LoginScreen onAuth={() => { setAuth(true); setRoute({ name: "welcome" }); }} />;
  }

  return (
    <div className="app">
      {isAdmin && scope === "all" && <div className="admin-line" aria-hidden="true" />}
      <nav className="nav">
        <div className="nav-brand" onClick={() => go("welcome")}>
          <div className="crest">G</div>
          <div className="wordmark">
            geef<span className="dot">.</span><span className="atelier">atelier</span>
          </div>
        </div>
        <div className="nav-links">
          <div className={"nav-link" + (route.name === "welcome" ? " active" : "")} onClick={() => go("welcome")}>Dashboard</div>
          <div className={"nav-link" + (route.name === "runs" || route.name === "detail" ? " active" : "")} onClick={() => go("runs")}>Runs</div>
          <div className={"nav-link" + (route.name === "new" ? " active" : "")} onClick={() => go("new")}>New briefing</div>
          <div className={"nav-link" + (route.name === "guide" ? " active" : "")} onClick={() => go("guide")}>Style guide</div>
        </div>
        <div className="user-menu" style={{ position: "relative" }}>
          {anyRunning && (
            <div className="global-pulse" onClick={() => go("detail", "R-2026-0185")}>
              <span className="dot"></span>
              1 run underway
            </div>
          )}
          <div className="user-chip" onClick={() => setUserMenu(!userMenu)}>
            <div className="av">s</div>
            <span>stefan{isAdmin && <span style={{ color: "var(--hero)", marginLeft: 4 }}> · admin</span>}</span>
          </div>
          {userMenu && (
            <div className="user-menu-dd">
              <div className="item"><IconUser size={14} /> Profile</div>
              <div className="item" onClick={() => { setUserMenu(false); go("guide"); }}>
                <IconLayers size={14} /> Style guide
              </div>
              <div className="sep" />
              <div className="item" onClick={() => { setUserMenu(false); setAuth(false); }}>
                <IconLogout size={14} /> Sign out
              </div>
            </div>
          )}
        </div>
      </nav>

      <main>
        {route.name === "welcome" && <DashboardScreen go={go} scope={isAdmin ? scope : "my"} isAdmin={isAdmin} onScope={setScope} />}
        {route.name === "runs" && <RunsListScreen go={go} />}
        {route.name === "new" && <NewRunScreen go={go} />}
        {route.name === "detail" && <RunDetailScreen runId={route.params.id} go={go} />}
        {route.name === "guide" && <StyleGuide />}
      </main>

      <TweaksPanel title="Tweaks">
        <TweakSection label="Aesthetic">
          <TweakRadio
            label="Palette"
            value={t.palette}
            onChange={(v) => setTweak("palette", v)}
            options={[
              { value: "noir", label: "Noir" },
              { value: "vellum", label: "Vellum" },
              { value: "petrol", label: "Petrol" },
            ]}
          />
          <TweakRadio
            label="Density"
            value={t.density}
            onChange={(v) => setTweak("density", v)}
            options={[
              { value: "compact", label: "Compact" },
              { value: "comfortable", label: "Comfy" },
              { value: "generous", label: "Generous" },
            ]}
          />
        </TweakSection>

        <TweakSection label="Role">
          <TweakRadio
            label="User role"
            value={t.role}
            onChange={(v) => setTweak("role", v)}
            options={[
              { value: "regular", label: "Regular" },
              { value: "admin", label: "Admin" },
            ]}
          />
          {isAdmin && (
            <TweakRadio
              label="Scope"
              value={scope}
              onChange={setScope}
              options={[
                { value: "my", label: "My" },
                { value: "all", label: "All" },
              ]}
            />
          )}
        </TweakSection>

        <TweakSection label="Jump to screen">
          <div style={{ display: "grid", gap: 6, padding: "4px 14px 8px" }}>
            <button className="btn small" onClick={() => go("welcome")}>Dashboard</button>
            <button className="btn small" onClick={() => go("new")}>New briefing</button>
            <button className="btn small" onClick={() => go("runs")}>Runs list</button>
            <button className="btn small" onClick={() => go("detail", "R-2026-0184")}>Run detail — completed</button>
            <button className="btn small" onClick={() => go("detail", "R-2026-0185")}>Run detail — running</button>
            <button className="btn small" onClick={() => go("guide")}>Style guide</button>
            <button className="btn small ghost" onClick={() => setAuth(false)}>Show login</button>
          </div>
        </TweakSection>
      </TweaksPanel>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App />);
