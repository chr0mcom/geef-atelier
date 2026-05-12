/* global React, StatusBadge, Severity */

function StyleGuide() {
  const colorGroups = [
    {
      title: "Surfaces",
      items: [
        { name: "--bg-deep", val: "deepest" },
        { name: "--bg", val: "page base" },
        { name: "--surface", val: "card / sheet" },
        { name: "--surface-2", val: "elevated" },
        { name: "--surface-3", val: "highest" },
      ],
    },
    {
      title: "Ink",
      items: [
        { name: "--ink", val: "primary text" },
        { name: "--ink-2", val: "secondary" },
        { name: "--ink-3", val: "muted" },
        { name: "--ink-4", val: "placeholder" },
      ],
    },
    {
      title: "Hero",
      items: [
        { name: "--hero", val: "aged-gold ink" },
        { name: "--hero-deep", val: "deeper variant" },
      ],
    },
    {
      title: "Status",
      items: [
        { name: "--st-pending", val: "Pending — ◯" },
        { name: "--st-running", val: "Running — ● pulse" },
        { name: "--st-completed", val: "Completed — ✓" },
        { name: "--st-failed", val: "Failed — ✕" },
        { name: "--st-aborted", val: "Aborted — ⊘" },
      ],
    },
    {
      title: "Severity",
      items: [
        { name: "--sv-critical", val: "Critical — ▲" },
        { name: "--sv-major", val: "Major — ◆" },
        { name: "--sv-minor", val: "Minor — ●" },
        { name: "--sv-info", val: "Info — ▬" },
      ],
    },
  ];

  return (
    <div className="guide">
      <div style={{ marginBottom: 32 }}>
        <div className="t-eyebrow eyebrow">Design system</div>
        <h1 style={{ fontFamily: "var(--font-display)", fontWeight: 300, fontSize: 46, letterSpacing: "-0.018em", margin: "8px 0 6px" }}>
          The atelier in pieces
        </h1>
        <p style={{ color: "var(--ink-3)", fontStyle: "italic", fontFamily: "var(--font-display)", fontSize: 15, maxWidth: "62ch", margin: 0 }}>
          Tokens, type, and component vocabulary. Swap the palette with the Tweaks panel to see how
          each component adapts.
        </p>
      </div>

      {/* COLOR */}
      <section>
        <h2>Color tokens</h2>
        {colorGroups.map((g) => (
          <div key={g.title} style={{ marginBottom: 24 }}>
            <div className="t-eyebrow eyebrow" style={{ marginBottom: 10 }}>{g.title}</div>
            <div className="swatch-grid">
              {g.items.map(it => (
                <div className="swatch" key={it.name}>
                  <div className="chip" style={{ background: `var(${it.name})` }} />
                  <div className="label">
                    <div className="name">{it.name}</div>
                    <div className="val">{it.val}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </section>

      {/* TYPE */}
      <section>
        <h2>Typography</h2>
        <div className="type-row">
          <div className="ttl">Display · Newsreader</div>
          <div>
            <div style={{ fontFamily: "var(--font-display)", fontWeight: 300, fontSize: 56, lineHeight: 1.05, letterSpacing: "-0.02em" }}>
              Manuscripts, not <em style={{ color: "var(--hero)" }}>outputs</em>.
            </div>
            <div style={{ color: "var(--ink-3)", fontFamily: "var(--font-mono)", fontSize: 11, marginTop: 8, letterSpacing: ".05em" }}>
              56 / 60 — weight 300 — letterspacing -0.02em
            </div>
          </div>
        </div>
        <div className="type-row">
          <div className="ttl">Article body · Newsreader</div>
          <div>
            <p style={{ fontFamily: "var(--font-display)", fontSize: 16, lineHeight: 1.7, margin: 0, maxWidth: "62ch" }}>
              The quantity in question is called the <em>chromatic number of the plane</em>, often denoted
              χ(ℝ²). It is the smallest number of colors with which one can assign a color to every point of the plane
              such that no two points at unit distance share the same color.
            </p>
          </div>
        </div>
        <div className="type-row">
          <div className="ttl">UI body · Geist</div>
          <div style={{ fontFamily: "var(--font-ui)", fontSize: 14, lineHeight: 1.55, maxWidth: "60ch" }}>
            UI chrome, buttons, navigation, form labels. Steady, modern, unfussy. 13–14px is the working range.
          </div>
        </div>
        <div className="type-row">
          <div className="ttl">Mono · JetBrains Mono</div>
          <div>
            <div className="t-mono" style={{ fontSize: 13 }}>R-2026-0184 · 18,724 tok · $0.142</div>
            <div className="t-eyebrow eyebrow" style={{ marginTop: 6 }}>EYEBROW LABELS · 11/16 · ls 0.16em</div>
          </div>
        </div>
      </section>

      {/* COMPONENTS */}
      <section>
        <h2>Status &amp; severity</h2>
        <div style={{ display: "flex", gap: 20, flexWrap: "wrap", alignItems: "center", marginBottom: 24 }}>
          <StatusBadge status="pending" />
          <StatusBadge status="running" />
          <StatusBadge status="completed" />
          <StatusBadge status="failed" />
          <StatusBadge status="aborted" />
        </div>
        <div style={{ display: "flex", gap: 24, flexWrap: "wrap", alignItems: "center" }}>
          <Severity kind="crit" />
          <Severity kind="maj" />
          <Severity kind="min" />
          <Severity kind="inf" />
        </div>
        <div style={{ marginTop: 14, color: "var(--ink-3)", fontFamily: "var(--font-display)", fontStyle: "italic", fontSize: 13, maxWidth: "70ch" }}>
          Every status and severity carries a unique <strong style={{ color: "var(--ink-2)" }}>shape mark</strong> as
          well as color — so the meaning survives even for color-blind readers, or in monochrome print.
        </div>
      </section>

      <section>
        <h2>Buttons</h2>
        <div style={{ display: "flex", gap: 14, flexWrap: "wrap" }}>
          <button className="btn primary">Hand to the crew</button>
          <button className="btn">Secondary action</button>
          <button className="btn ghost">Cancel</button>
          <button className="btn danger">Cancel run</button>
          <button className="btn small">Small action</button>
        </div>
      </section>

      <section>
        <h2>Spacing scale</h2>
        <div style={{ display: "flex", alignItems: "flex-end", gap: 14 }}>
          {[4, 8, 12, 16, 24, 32, 48, 64, 96].map(n => (
            <div key={n} style={{ textAlign: "center" }}>
              <div style={{ width: n, height: n, background: "var(--hero)", marginBottom: 8 }} />
              <div className="t-mono" style={{ fontSize: 10, color: "var(--ink-3)" }}>{n}</div>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

window.StyleGuide = StyleGuide;
