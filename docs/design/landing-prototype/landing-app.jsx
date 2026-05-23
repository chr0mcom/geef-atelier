/* global React, ReactDOM,
   Nav, Hero, Turn, GeefFlow, Crew, Proof, Capabilities, Closing, Footer,
   TweaksPanel, useTweaks, TweakSection, TweakRadio, TweakToggle, TweakSelect */

const LANDING_DEFAULTS = /*EDITMODE-BEGIN*/{
  "palette": "vellum",
  "headline": "crew",
  "motion": true
}/*EDITMODE-END*/;

function App() {
  const [tweaks, setTweak] = useTweaks(LANDING_DEFAULTS);

  // Apply palette to <html>
  React.useEffect(() => {
    const root = document.documentElement;
    root.classList.remove("palette-noir", "palette-vellum", "palette-petrol");
    root.classList.add("palette-" + tweaks.palette);
  }, [tweaks.palette]);

  // Motion preference
  React.useEffect(() => {
    if (!tweaks.motion) {
      document.body.setAttribute("data-static", "1");
    } else {
      document.body.removeAttribute("data-static");
    }
  }, [tweaks.motion]);

  return (
    <div className="landing">
      <Nav />
      <Hero headline={tweaks.headline} />
      <Turn />
      <GeefFlow key={"geef-" + tweaks.headline} />
      <Crew />
      <Proof />
      <Capabilities />
      <Closing />
      <Footer />

      <TweaksPanel title="Tweaks">
        <TweakSection label="Theme">
          <TweakRadio
            label="Palette"
            value={tweaks.palette}
            options={[
              { label: "Vellum", value: "vellum" },
              { label: "Noir", value: "noir" },
              { label: "Petrol", value: "petrol" },
            ]}
            onChange={(v) => setTweak("palette", v)}
          />
        </TweakSection>
        <TweakSection label="Headline">
          <TweakSelect
            label="Variant"
            value={tweaks.headline}
            options={[
              { label: "Crew (default)", value: "crew" },
              { label: "Manufactory", value: "manufactory" },
              { label: "Press once / Refined many", value: "press" },
            ]}
            onChange={(v) => setTweak("headline", v)}
          />
        </TweakSection>
        <TweakSection label="Motion">
          <TweakToggle
            label="Ambient + GEEF flow animation"
            value={tweaks.motion}
            onChange={(v) => setTweak("motion", v)}
          />
        </TweakSection>
      </TweaksPanel>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App />);
