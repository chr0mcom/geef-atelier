/* global React, MANUSCRIPTS, TOKEN, CRITICS, PROVIDERS, KB, DAYBOOK,
          IconAdd, IconCheck, IconBook, IconDownload, IconClose, IconCopy, IconRefresh */
const { useState, useMemo } = React;

/* ============================================================
   F1. RECENT MANUSCRIPTS — Gallery
   ============================================================ */
const FMT_LABEL = { pdf: "PDF", docx: "DOCX", md: "MD", html: "HTML", json: "JSON", txt: "TXT" };

function ManuscriptsGallery({ scope, go }) {
  const list = MANUSCRIPTS[scope];
  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Recent manuscripts</h3>
          <div className="card-sub">{scope === "all" ? "The latest works across all workshops" : "The latest works"}</div>
        </div>
        <div style={{ fontFamily: "var(--font-mono)", fontSize: 11, color: "var(--ink-3)", letterSpacing: ".06em", cursor: "pointer" }} onClick={() => go("runs")}>
          ALL ⟶
        </div>
      </div>
      <div className="gallery">
        {list.map((m, i) => (
          <div className="ms-card" key={i} onClick={() => go("detail", "R-2026-0184")}>
            <div className="fmt-icon">{FMT_LABEL[m.fmt]}</div>
            <div className="body">
              <div className="name">{m.name}</div>
              <div className="meta">
                {m.user && <><span className="by">by {m.user}</span> · </>}
                {m.when}
              </div>
            </div>
            <div className="right">
              <span>{m.size}</span>
              <span className="dl"><IconDownload size={13} /></span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ============================================================
   F2. TOKEN STREAM — Sparkline
   ============================================================ */
function TokenStream({ scope }) {
  const t = TOKEN[scope];
  const W = 280, H = 100, P = 4;
  const series = t.series;
  const max = Math.max(...series);
  const min = Math.min(...series);
  const range = Math.max(1, max - min);

  const pts = series.map((v, i) => {
    const x = P + (i / (series.length - 1)) * (W - P * 2);
    const y = H - P - ((v - min) / range) * (H - P * 2);
    return [x, y];
  });
  const line = pts.map(([x, y], i) => `${i ? "L" : "M"} ${x.toFixed(1)} ${y.toFixed(1)}`).join(" ");
  const area = line + ` L ${W - P} ${H - P} L ${P} ${H - P} Z`;

  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Token stream</h3>
          <div className="card-sub">Last 30 days</div>
        </div>
      </div>
      <div className="spark">
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none">
          <path d={area} className="area" />
          <path d={line} className="line" />
          {pts.map(([x, y], i) => i === pts.length - 1 && (
            <circle key={i} cx={x} cy={y} r="2.5" fill="var(--hero)" />
          ))}
        </svg>
      </div>
      <div className="spark-stat" style={{ marginTop: 12 }}>
        <div className="v">{t.total}</div>
        <div className="l">tokens</div>
        <div className="d">{t.delta}</div>
      </div>
    </div>
  );
}

/* ============================================================
   G1. THE CRITICS' BENCH — Reviewer × Severity matrix
   ============================================================ */
function CriticsBench({ scope }) {
  const rows = CRITICS[scope];
  const strict = CRITICS.strict[scope];

  // Normalize per-column for cell intensity
  const cols = [0, 0, 0, 0];
  rows.forEach(r => r.row.forEach((v, i) => { if (v > cols[i]) cols[i] = v; }));
  const cellClass = ["crit", "maj", "min", "inf"];
  const cellHead = ["Critical", "Major", "Minor", "Info"];

  const opacity = (v, max) => {
    if (v === 0) return 0.08;
    const r = v / max;
    return 0.18 + r * 0.82;
  };

  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">The Critics' Bench</h3>
          <div className="card-sub">Who flags what, last 30 days</div>
        </div>
      </div>
      <div className="crit-matrix">
        <table>
          <thead>
            <tr>
              <th />
              {cellHead.map((h) => <th key={h} style={{ textAlign: "center" }}>{h}</th>)}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.name}>
                <td className="reviewer">{r.name}</td>
                {r.row.map((v, i) => (
                  <td key={i} className={`cell ${cellClass[i]}`}>
                    <div className="block" style={{ opacity: opacity(v, cols[i]) }} title={`${r.name}: ${v} ${cellHead[i].toLowerCase()} findings`}>
                      <span className="count" style={{ color: v / cols[i] > 0.45 ? "var(--on-hero)" : "currentColor", opacity: v > 0 ? 1 : 0 }}>
                        {v > 0 ? v : ""}
                      </span>
                    </div>
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        <div className="strictest">
          Strictest reviewer: <span className="name">{strict}</span>
        </div>
      </div>
    </div>
  );
}

/* ============================================================
   G2. PROVIDER BENCH
   ============================================================ */
function ProviderBench({ scope }) {
  const list = PROVIDERS[scope];
  const counts = PROVIDERS.counts[scope];
  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Provider bench</h3>
          <div className="card-sub">
            {counts.configured} configured · {counts.active} {scope === "all" ? "used across all workshops" : "actively used"}
          </div>
        </div>
      </div>
      <div className="providers">
        {list.map((p) => (
          <div className="provider-row" key={p.name}>
            <span className={`provider-dot ${p.state}`} />
            <span className="provider-name">{p.name}</span>
            <span className="provider-type">{p.type}</span>
            <span className="provider-when">{p.when}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ============================================================
   G3. KNOWLEDGE BASE
   ============================================================ */
function KnowledgeBase() {
  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">Knowledge base</h3>
          <div className="card-sub">Shared across all workshops</div>
        </div>
      </div>
      <div className="kb-stat">
        {KB.documents}<span className="small">documents</span>
      </div>
      <div className="kb-side">{KB.chunks} chunks · {KB.embeddings} embeddings</div>

      <div className="kb-block">
        <div className="label">Most recently added</div>
        <div className="file">{KB.mostRecent.name}<span className="when">· {KB.mostRecent.when}</span></div>
      </div>
      <div className="kb-block">
        <div className="label">Most referenced</div>
        <div className="file">{KB.mostCited.name}<span className="when">· {KB.mostCited.count} citations</span></div>
      </div>

      <button className="btn small" style={{ marginTop: 22, width: "100%", justifyContent: "center" }}>
        <IconBook size={13} /> Open knowledge base
      </button>
    </div>
  );
}

/* ============================================================
   H. THE DAY BOOK
   ============================================================ */
const DAY_ICON = {
  check: <IconCheck size={14} />,
  doc:   <IconBook size={14} />,
  plus:  <IconAdd size={14} />,
  star:  <span style={{ fontFamily: "var(--font-display)", fontSize: 13, lineHeight: 1, color: "var(--hero)" }}>✦</span>,
  x:     <IconClose size={14} />,
  link:  <IconRefresh size={14} />,
};

function DayBook({ scope }) {
  const list = DAYBOOK[scope];
  const hasUsers = list.some(r => r.who);
  return (
    <div className="dash-card">
      <div className="dash-card-head">
        <div>
          <h3 className="card-title">The Day Book</h3>
          <div className="card-sub">{scope === "all" ? "What stirred today across all workshops" : "What stirred today"}</div>
        </div>
      </div>
      <div className="daybook">
        {list.map((row, i) => (
          <div className={"day-row " + (hasUsers ? "" : "no-user")} key={i}>
            <div className="when">{row.when}</div>
            <div className="icon-cell">{DAY_ICON[row.icon] || <IconCheck size={14} />}</div>
            {hasUsers && <div className="who">{row.who || "—"}</div>}
            <div className="body">
              <span className="verb">{row.verb}</span>
              <span className="rest">— {row.rest}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

Object.assign(window, {
  ManuscriptsGallery, TokenStream, CriticsBench, ProviderBench, KnowledgeBase, DayBook,
});
