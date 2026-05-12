/* global React, ReactDOM, RUNS,
          LoginScreen, WelcomeScreen, NewRunScreen, RunsListScreen, RunDetailScreen, StyleGuide,
          TweaksPanel, TweakSection, TweakColor, TweakRadio, useTweaks,
          IconAdd, IconUser, IconLogout, IconLayers */

const { useState, useEffect, useRef } = React;

/* ============================================================
   TWEAKS — palette switcher + density
   ============================================================ */
const TWEAK_DEFAULTS = /*EDITMODE-BEGIN*/{
  "palette": "noir",
  "density": "comfortable"
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
  const [auth, setAuth] = useState(false);
  const [route, setRoute] = useState({ name: "welcome", params: {} });
  const [userMenu, setUserMenu] = useState(false);

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
      <nav className="nav">
        <div className="nav-brand" onClick={() => go("welcome")}>
          <div className="crest">G</div>
          <div className="wordmark">
            geef<span className="dot">.</span><span className="atelier">atelier</span>
          </div>
        </div>
        <div className="nav-links">
          <div className={"nav-link" + (route.name === "welcome" ? " active" : "")} onClick={() => go("welcome")}>Atelier</div>
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
            <div className="av">i</div>
            <span>isolde.geef</span>
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
        {route.name === "welcome" && <WelcomeScreen go={go} />}
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

        <TweakSection label="Jump to screen">
          <div style={{ display: "grid", gap: 6, padding: "4px 14px 8px" }}>
            <button className="btn small" onClick={() => go("welcome")}>Atelier (welcome)</button>
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
