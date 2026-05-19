/* global React,
          WELCOME, PRESS, LEDGER, CALENDAR, CREW_DNA, COST_FORGE, HIST, TODAY,
          IconAdd, IconBook, IconRefresh, IconChevron, IconCheck, IconLayers */
const { useState, useMemo, useEffect } = React;

/* ============================================================
   A. WELCOME STRIP
   ============================================================ */
function WelcomeStrip({ scope, isAdmin, onScope }) {
  const w = WELCOME[scope];
  const dateStr = TODAY.toLocaleDateString("en-US", { weekday: "long", year: "numeric", month: "long", day: "numeric" });
  return (
    <div className="dash-welcome">
      <div className="left">
        <h1 className="greet">
          {w.greeting}
          {w.suffix && <span className="suffix">{w.suffix}</span>}
        </h1>
      </div>
      <div className="right">
        <div className="date">{dateStr}</div>
        <span className="streak">
          <span className="quill">✦</span>
          {w.streakDays} {w.streakLabel}
        </span>
        {isAdmin && (
          <div className="scope-toggle" role="tablist" aria-label="Workshop scope">
            <div className={"opt" + (scope === "my" ? " active" : "")} onClick={() => onScope("my")}>My Workshop</div>
            <div className={"opt" + (scope === "all" ? " active" : "")} onClick={() => onScope("all")}>All Workshops</div>
          </div>
        )}
      </div>
    </div>
  );
}

/* ============================================================
   B. THE PRESS
   ============================================================ */
const PHASES = ["Grounding", "Execution", "Evaluation", "Finalize"];

function Press({ scope, go }) {
  const p = PRESS[scope];

  if (p.state === "idle") {
    return (
      <div className="press-card idle">
        <div className="quiet">
          <div>
            <h2>The workshop is quiet.</h2>
            <div className="since">Last commission completed 2 h 14 min ago.</div>
          </div>
          <div style={{ display: "flex", gap: 10 }}>
            <button className="btn primary" onClick={() => go("new")}>
              <IconAdd size={14} /> New commission
            </button>
            <button className="btn" onClick={() => go("guide")}>
              <IconLayers size={13} /> Open Studio
            </button>
          </div>
        </div>
      </div>
    );
  }

  const single = p.state === "single";
  return (
    <div className="press-card">
      <div className="head">
        <span className="pulse-tag">
          <span className="dot" />
          {single ? "Pulse" : `${p.runs.length} active runs`}
        </span>
        <span className="live-updates">
          <IconRefresh size={11} /> Live updates active
        </span>
      </div>

      {single ? (
        <>
          <div className="phase-rail">
            {PHASES.map((name, i) => (
              <div
                key={name}
                className={"phase " + (i < p.runs[0].activePhase ? "done" : i === p.runs[0].activePhase ? "active" : "")}
              >
                <div className="seal">{["G", "E", "Ev", "F"][i]}</div>
                <div className="name">{name}</div>
              </div>
            ))}
          </div>
          <div className="press-meta-line">
            <span><span className="key">Template:</span><strong style={{ color: "var(--ink)" }}>{p.runs[0].template}</strong></span>
            <span><span className="key">Iteration:</span>{p.runs[0].iteration} of {p.runs[0].of}</span>
            <span><span className="key">Started:</span>{formatAgo(p.runs[0].startedSecondsAgo)}</span>
            <span><span className="key">Run:</span>{p.runs[0].id}</span>
          </div>
        </>
      ) : (
        <div className="press-runs">
          {p.runs.map((r) => (
            <div className="press-run" key={r.id}>
              <div className="who">
                <span className="user">{r.user}</span> · {r.template}
              </div>
              <div className="compact-rail">
                {PHASES.map((name, i) => (
                  <div key={name} className={"phase " + (i < r.activePhase ? "done" : i === r.activePhase ? "active" : "")}>
                    <span className="pip" />
                    <span className="name">{name[0]}</span>
                  </div>
                ))}
              </div>
              <div className="iter-readout">Iteration {r.iteration}/{r.of} · {formatAgo(r.startedSecondsAgo)}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
function formatAgo(seconds) {
  if (seconds < 90) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`;
  return `${Math.floor(seconds / 3600)} h ${Math.round((seconds % 3600) / 60)} min ago`;
}

/* ============================================================
   C. THE LEDGER
   ============================================================ */
function Ledger({ scope }) {
  const tiles = LEDGER[scope];
  return (
    <div className="ledger">
      {tiles.map((t) => (
        <div className="ledger-tile" key={t.label}>
          <div className="label">{t.label}</div>
          <div className="value">{t.value}</div>
          <div className={`delta ${t.trend}`}>{t.delta}</div>
        </div>
      ))}
    </div>
  );
}

/* ============================================================
   D. ACTIVITY CALENDAR
   ============================================================ */
function ActivityCalendar({ scope }) {
  const data = CALENDAR[scope];
  const [hover, setHover] = useState(null);

  // bucketize counts into 4 levels (excluding 0)
  const max = data.cells.flat().filter(Boolean).reduce((m, c) => Math.max(m, c.count), 1);
  const level = (n) => {
    if (n <= 0) return 0;
    const ratio = n / max;
    if (ratio < 0.25) return 1;
    if (ratio < 0.5) return 2;
    if (ratio < 0.78) return 3;
    return 4;
  };

  // monthly headers — show month label at columns where the month transitions,
  // but skip a label if too close to the previous one (avoid overlap at the edge).
  const monthLabels = useMemo(() => {
    const out = [];
    let lastMonth = -1;
    let lastWeek = -99;
    data.cells.forEach((col, w) => {
      const first = col.find(Boolean);
      if (!first) return;
      const m = first.date.getMonth();
      if (m !== lastMonth) {
        if (w - lastWeek >= 3) {
          out.push({ week: w, label: first.date.toLocaleDateString("en-US", { month: "short" }) });
          lastWeek = w;
        }
        lastMonth = m;
      }
    });
    return out;
  }, [scope]);

  return (
    <div className="dash-card flush" style={{ padding: 22 }}>
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Activity calendar</h3>
          <div className="card-sub">
            Last 52 weeks {scope === "all" && "· all workshops"}
          </div>
        </div>
      </div>

      <div className="calendar-wrap">
        <div className="weekdays">
          <span></span>
          <span>M</span>
          <span></span>
          <span>W</span>
          <span></span>
          <span>F</span>
          <span></span>
        </div>
        <div>
          <div className="months" style={{ position: "relative", height: 12 }}>
            {monthLabels.map((m) => (
              <span key={m.week} style={{ position: "absolute", left: m.week * 13 }}>{m.label}</span>
            ))}
          </div>
          <div className="heatmap">
            {data.cells.map((col, w) => (
              <div className="heatmap-col" key={w}>
                {col.map((cell, d) => {
                  if (!cell) return <div className="heatmap-cell empty" key={d} />;
                  const lvl = level(cell.count);
                  return (
                    <div
                      key={d}
                      className={`heatmap-cell lvl-${lvl}`}
                      onMouseEnter={() => setHover(cell)}
                      onMouseLeave={() => setHover(null)}
                      title={`${cell.date.toLocaleDateString("en-US", { weekday: "long", month: "short", day: "numeric" })} — ${cell.count} commission${cell.count === 1 ? "" : "s"}`}
                    />
                  );
                })}
              </div>
            ))}
          </div>
        </div>
        <div className="calendar-legend">
          <div className="scale">
            <span style={{ background: "color-mix(in oklch, var(--ink) 8%, var(--surface))" }}></span>
            <span style={{ background: "color-mix(in oklch, var(--hero) 28%, var(--surface))" }}></span>
            <span style={{ background: "color-mix(in oklch, var(--hero) 55%, var(--surface))" }}></span>
            <span style={{ background: "color-mix(in oklch, var(--hero) 80%, var(--surface))" }}></span>
            <span style={{ background: "var(--hero)" }}></span>
          </div>
          <div className="peak">
            {hover ? (
              <>
                {hover.date.toLocaleDateString("en-US", { weekday: "short", month: "short", day: "numeric" })}<br />
                <em>{hover.count}</em> commission{hover.count === 1 ? "" : "s"}
              </>
            ) : (
              <>
                Peak: <em>{data.peakCount}</em> commissions on<br />
                {data.peakDate?.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" })}
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

/* ============================================================
   E1. CREW DNA — The Workbench
   ============================================================ */
function CrewDNA({ scope }) {
  const data = CREW_DNA[scope];
  const groups = [
    { title: "Executor", items: data.executor },
    { title: "Reviewers", items: data.reviewers },
    { title: "Advisors", items: data.advisors },
  ];
  const max = Math.max(1, ...groups.flatMap(g => g.items.map(i => i.n)));
  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">The Workbench</h3>
          <div className="card-sub">{scope === "all" ? "Most popular tools across workshops" : "Your preferred tools, last 30 days"}</div>
        </div>
      </div>
      <div className="crew-cols">
        {groups.map((g) => (
          <div key={g.title}>
            <div className="crew-col-title">{g.title}</div>
            {g.items.length === 0 ? (
              <div className="crew-empty">— none —</div>
            ) : (
              g.items.map((it) => (
                <div className="crew-entry" key={it.name}>
                  <div className="name">{it.name}</div>
                  <div className="bar-row">
                    <div className="bar">
                      <span className="bar-fill" style={{ width: `${(it.n / max) * 100}%` }} />
                    </div>
                    <div className="count">{it.n}</div>
                  </div>
                </div>
              ))
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

/* ============================================================
   E2. COST FORGE — Sankey
   ============================================================ */
function CostForge({ scope }) {
  const flows = COST_FORGE[scope];
  const total = flows.reduce((s, f) => s + f.cost, 0);
  const W = 720;
  const H = 240;
  const padTop = 32;
  const padBottom = 8;
  const usable = H - padTop - padBottom;
  const gap = 6;
  const nodeW = 14;

  // Left col: templates (sum heights). Each flow's height proportional to cost.
  const totalGap = gap * (flows.length - 1);
  const scale = (usable - totalGap) / total;

  let y = padTop;
  const left = flows.map((f) => {
    const h = f.cost * scale;
    const node = { x: 40, y, h, label: f.template, cost: f.cost };
    y += h + gap;
    return node;
  });

  // Models: dedupe & order by appearance, stack accordingly
  const modelOrder = [];
  flows.forEach((f) => {
    if (!modelOrder.find(m => m.name === f.model)) {
      modelOrder.push({ name: f.model, cost: 0 });
    }
    modelOrder.find(m => m.name === f.model).cost += f.cost;
  });
  let my = padTop;
  const mid = modelOrder.map((m) => {
    const h = m.cost * scale;
    const node = { x: W / 2 - nodeW / 2, y: my, h, label: m.name, cost: m.cost };
    my += h + gap;
    return node;
  });

  // Right col: cost share bars per flow, restacked by template order (same as left)
  let ry = padTop;
  const right = flows.map((f) => {
    const h = f.cost * scale;
    const node = { x: W - 200, y: ry, h, label: f.template, cost: f.cost };
    ry += h + gap;
    return node;
  });

  // For ribbons, model nodes can host multiple flows — track per-model y offsets.
  const modelOffset = {};
  modelOrder.forEach(m => modelOffset[m.name] = 0);

  const ribbons = flows.map((f, i) => {
    const L = left[i];
    const M = mid.find(m => m.label === f.model);
    const R = right[i];
    const h = f.cost * scale;
    const mOff = modelOffset[f.model];
    modelOffset[f.model] += h;

    const x1 = L.x + nodeW;
    const x2 = M.x;
    const x3 = M.x + nodeW;
    const x4 = R.x;

    const y1 = L.y;
    const y2 = M.y + mOff;
    const y3 = M.y + mOff;
    const y4 = R.y;

    // Bezier path from L (rect on right) to M (rect on left) to R
    const c1 = (x1 + x2) / 2;
    const c2 = (x3 + x4) / 2;

    const d = [
      `M ${x1} ${y1}`,
      `C ${c1} ${y1}, ${c1} ${y2}, ${x2} ${y2}`,
      `L ${x2} ${y2 + h}`,
      `C ${c1} ${y2 + h}, ${c1} ${y1 + h}, ${x1} ${y1 + h}`,
      "Z",
      `M ${x3} ${y3}`,
      `C ${c2} ${y3}, ${c2} ${y4}, ${x4} ${y4}`,
      `L ${x4} ${y4 + h}`,
      `C ${c2} ${y4 + h}, ${c2} ${y3 + h}, ${x3} ${y3 + h}`,
      "Z",
    ].join(" ");

    return { d, f };
  });

  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Cost forge</h3>
          <div className="card-sub">{scope === "all" ? "Where the budget flows across all workshops" : "Where the budget flows"}</div>
        </div>
        <div className="t-mono" style={{ fontSize: 11, color: "var(--ink-3)", letterSpacing: ".06em" }}>
          € {total.toFixed(2)} this month
        </div>
      </div>

      <svg viewBox={`0 0 ${W} ${H}`} className="sankey" preserveAspectRatio="xMidYMid meet">
        {/* Column headers */}
        <text x="40" y="16" className="col-title">TEMPLATE</text>
        <text x={W / 2 - nodeW / 2} y="16" className="col-title">MODEL</text>
        <text x={W - 200} y="16" className="col-title">COST</text>

        {/* Ribbons */}
        {ribbons.map((r, i) => (
          <path key={i} d={r.d} className="ribbon">
            <title>{`${r.f.template} → ${r.f.model}: ${r.f.runs} runs, €${r.f.cost.toFixed(2)}`}</title>
          </path>
        ))}

        {/* Left nodes */}
        {left.map((n) => (
          <g key={n.label}>
            <rect x={n.x} y={n.y} width={nodeW} height={n.h} className="node" />
            <text x={n.x - 8} y={n.y + n.h / 2 + 4} textAnchor="end" className="node-label">{n.label}</text>
          </g>
        ))}
        {/* Mid nodes */}
        {mid.map((n) => (
          <g key={n.label}>
            <rect x={n.x} y={n.y} width={nodeW} height={n.h} className="node" />
            <text x={n.x + nodeW / 2} y={n.y - 6} textAnchor="middle" className="node-label">{n.label}</text>
          </g>
        ))}
        {/* Right nodes + cost labels */}
        {right.map((n) => (
          <g key={n.label}>
            <rect x={n.x} y={n.y} width={nodeW} height={n.h} className="node" style={{ fill: "var(--hero)" }} />
            <text x={n.x + nodeW + 10} y={n.y + n.h / 2 + 4} className="node-label" style={{ fill: "var(--ink)" }}>€{n.cost.toFixed(2)}</text>
          </g>
        ))}
      </svg>
    </div>
  );
}

/* ============================================================
   E3. ITERATION SWEET-SPOT
   ============================================================ */
function SweetSpot({ scope }) {
  const data = HIST[scope];
  const max = Math.max(...data.bars);
  const labels = ["1 iter.", "2 iter.", "3 iter.", "4+ iter."];
  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Iteration sweet-spot</h3>
          <div className="card-sub">{scope === "all" ? "How pipelines iterate across all workshops" : "How often your pipeline iterates"}</div>
        </div>
      </div>
      <div className="hist">
        <div className="hist-sweet-strip" />
        {data.bars.map((n, i) => (
          <div className={"hist-bar " + (i === 1 || i === 2 ? "sweet" : "")} key={i}>
            <div className="fill" style={{ height: `${(n / max) * 100}%` }}>
              <span className="value">{n}</span>
            </div>
            <div className="x-label">{labels[i]}</div>
          </div>
        ))}
      </div>
      <div className="hist-verdict">
        <span className="quill">✦</span>
        <span>{data.verdict}</span>
      </div>
    </div>
  );
}

Object.assign(window, {
  WelcomeStrip, Press, Ledger, ActivityCalendar, CrewDNA, CostForge, SweetSpot,
});
