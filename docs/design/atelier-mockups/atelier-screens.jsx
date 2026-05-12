/* global React, StatusBadge, Severity, RUNS, RUN_DETAIL_184,
          timeAgo, timeShort, dateShort, NOW,
          IconPen, IconAdd, IconChevron, IconCopy, IconDownload,
          IconClose, IconCheck, IconUser, IconLogout, IconQuill,
          IconRefresh, IconArchive, IconHome, IconBook, IconPalette, IconLayers */
const { useState, useEffect, useRef } = React;

/* ============================================================
   LOGIN
   ============================================================ */
function LoginScreen({ onAuth }) {
  const [user, setUser] = useState("isolde.geef");
  const [pw, setPw] = useState("");
  const [err, setErr] = useState(false);
  const submit = (e) => {
    e.preventDefault();
    if (pw.length < 1) { setErr(true); return; }
    onAuth();
  };
  return (
    <div className="login-stage">
      <div className="login-left">
        <div className="ink-pool">
          <span></span><span></span><span></span>
        </div>
        <div className="brand-row">
          <div className="nav-brand">
            <div className="crest">G</div>
            <div className="wordmark">
              geef<span className="dot">.</span><span className="atelier">atelier</span>
            </div>
          </div>
        </div>
        <div className="quote">
          A text manufactory. <em>Briefings handed to a crew of writers and editors</em>, returned as
          manuscripts.
        </div>
        <div className="colophon">
          <span>EST. 2026 · ZÜRICH</span>
          <span>VOL. III</span>
          <span>NO. 047</span>
        </div>
      </div>
      <div className="login-right">
        <form className="login-card" onSubmit={submit}>
          <h1>Enter the atelier</h1>
          <p className="sub">Single workshop, single hand.</p>

          {err && (
            <div className="error-banner">
              <IconClose size={14} />
              Invalid credentials. Try again.
            </div>
          )}

          <div className="field">
            <label>Handle</label>
            <input value={user} onChange={(e) => setUser(e.target.value)} autoFocus />
          </div>
          <div className="field">
            <label>Passphrase</label>
            <input type="password" value={pw} onChange={(e) => setPw(e.target.value)} placeholder="•••••••••" />
          </div>

          <button type="submit" className="btn primary" style={{ width: "100%", justifyContent: "center", padding: "12px 16px", marginTop: 6 }}>
            Open the door
            <IconChevron size={14} />
          </button>

          <div style={{ marginTop: 22, fontSize: 11, color: "var(--ink-3)", fontFamily: "var(--font-mono)", letterSpacing: ".08em", textAlign: "center" }}>
            (Try anything as a passphrase — this is a prototype.)
          </div>
        </form>
      </div>
    </div>
  );
}

/* ============================================================
   WELCOME / LANDING
   ============================================================ */
function WelcomeScreen({ go }) {
  const recent = RUNS.slice(0, 5);
  return (
    <div className="welcome">
      <div className="welcome-hero">
        <h1>
          A briefing well-given<br />
          is half the <em>manuscript</em>.
        </h1>
        <div className="meta">
          <div className="greeting">Tuesday · 14:32 · Atelier open</div>
          <div className="today">Three runs underway since this morning.</div>
        </div>
      </div>

      <div className="welcome-cta">
        <div className="cta-card" onClick={() => go("new")}>
          <div className="glyph-stamp">¶</div>
          <div className="t-eyebrow eyebrow">Begin</div>
          <h2>Hand the crew a new briefing</h2>
          <p>
            Describe what should be written and to whom. The crew will draft, review,
            revise — and return when it converges or after three iterations.
          </p>
          <div className="arrow">⟶ Open the form</div>
        </div>
        <div className="welcome-side">
          <div className="stat-tile">
            <div className="label">This month</div>
            <div className="value">47</div>
            <div className="delta">runs · 22 % over April</div>
          </div>
          <div className="stat-tile">
            <div className="label">Convergence rate</div>
            <div className="value">81 %</div>
            <div className="delta">within ≤ 2 iterations</div>
          </div>
        </div>
      </div>

      <div>
        <div className="t-eyebrow eyebrow" style={{ marginBottom: 6 }}>The workbench</div>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginBottom: 10 }}>
          <h2 style={{ fontFamily: "var(--font-display)", fontWeight: 300, fontSize: 28, letterSpacing: "-0.012em", margin: 0 }}>
            Recent work
          </h2>
          <span style={{ fontFamily: "var(--font-mono)", fontSize: 11, color: "var(--ink-3)", cursor: "pointer", letterSpacing: ".06em" }} onClick={() => go("runs")}>
            ALL RUNS ⟶
          </span>
        </div>
        <div className="recent-list">
          {recent.map((r) => (
            <div className="recent-row" key={r.id} onClick={() => go("detail", r.id)}>
              <div className="when">{timeAgo(r.createdAt)}</div>
              <div className="briefing-snip">{r.title}</div>
              <div className="meta t-mono">{r.tokens ? r.tokens.toLocaleString() : "—"} tok</div>
              <StatusBadge status={r.status} />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ============================================================
   NEW RUN
   ============================================================ */
function NewRunScreen({ go }) {
  const [briefing, setBriefing] = useState("");
  const [openCfg, setOpenCfg] = useState(false);
  const [cfg, setCfg] = useState(
`{
  "max_iterations": 3,
  "executor": "model:atelier-prose-v2",
  "reviewers": [
    "BriefingFidelityReviewer",
    "ClarityReviewer"
  ],
  "convergence_threshold": "no_major"
}`);
  const [submitting, setSubmitting] = useState(false);

  const canSubmit = briefing.trim().length > 0 && !submitting;
  const submit = () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setTimeout(() => go("detail", "R-2026-0185"), 700);
  };

  const charCount = briefing.length;
  const wordCount = briefing.trim() ? briefing.trim().split(/\s+/).length : 0;

  return (
    <div className="newrun">
      <div className="head">
        <div className="t-eyebrow eyebrow">New briefing</div>
        <h1>What should we make?</h1>
        <div className="helper">
          A briefing is a commission. Describe the addressee, the tone, the length, the sources.
          The more you say, the less the crew must guess.
        </div>
      </div>

      <div className={"briefing-wrap" + (briefing.length ? " has-content" : "")}>
        <textarea
          value={briefing}
          onChange={(e) => setBriefing(e.target.value)}
          placeholder={`Describe what should be written.

— Who is the addressee?
— What register or tone?
— What length, in words or paragraphs?
— What sources, citations, or constraints?

Be specific. Generic briefings produce generic texts.`}
          autoFocus
        />
        <div className="briefing-corner">
          <span className="ready-pip"></span>
          <span>{charCount} chars · {wordCount} words</span>
          <span>{briefing.length ? "ready to hand" : "awaiting your hand"}</span>
        </div>
      </div>

      <div className={"config-toggle" + (openCfg ? " open" : "")} onClick={() => setOpenCfg(!openCfg)}>
        <span className="caret">▸</span>
        <span>Advanced — pipeline configuration (JSON)</span>
      </div>
      {openCfg && (
        <div className="config-area">
          <textarea value={cfg} onChange={(e) => setCfg(e.target.value)} spellCheck={false} />
        </div>
      )}

      <div className="newrun-actions">
        <div className="left-hint">
          ⌘ + Enter to hand to the crew · esc to discard
        </div>
        <div style={{ display: "flex", gap: 10 }}>
          <button className="btn ghost" onClick={() => go("runs")}>Cancel</button>
          <button
            className="btn primary"
            disabled={!canSubmit}
            onClick={submit}
          >
            {submitting ? "Handing over…" : "Hand to the crew"}
            <IconChevron size={14} />
          </button>
        </div>
      </div>

      <div className="press-row">
        <span>The crew on duty —</span>
        <div className="crew"><span className="mark">E</span> Executor</div>
        <div className="crew"><span className="mark">B</span> Briefing-fidelity reviewer</div>
        <div className="crew"><span className="mark">C</span> Clarity reviewer</div>
      </div>
    </div>
  );
}

/* ============================================================
   RUNS LIST
   ============================================================ */
function RunsListScreen({ go }) {
  const [filter, setFilter] = useState("all");
  const counts = {
    all: RUNS.length,
    pending: RUNS.filter(r => r.status === "pending").length,
    running: RUNS.filter(r => r.status === "running").length,
    completed: RUNS.filter(r => r.status === "completed").length,
    failed: RUNS.filter(r => r.status === "failed").length,
    aborted: RUNS.filter(r => r.status === "aborted").length,
  };
  const list = filter === "all" ? RUNS : RUNS.filter(r => r.status === filter);

  const filters = [
    { k: "all", label: "All" },
    { k: "pending", label: "Pending" },
    { k: "running", label: "Running" },
    { k: "completed", label: "Completed" },
    { k: "failed", label: "Failed" },
    { k: "aborted", label: "Aborted" },
  ];

  return (
    <div className="runs">
      <div className="runs-head">
        <div>
          <div className="t-eyebrow eyebrow">The workbench</div>
          <h1>All runs</h1>
          <div className="sub">Every commission, every revision — newest first.</div>
        </div>
        <div className="filters">
          {filters.map(f => (
            <div
              key={f.k}
              className={"filter-pill" + (filter === f.k ? " active" : "")}
              onClick={() => setFilter(f.k)}
            >
              {f.label} <span className="count">{counts[f.k]}</span>
            </div>
          ))}
        </div>
      </div>

      {list.length === 0 ? (
        <div className="empty-state">
          <h3>An empty workbench</h3>
          <p>No runs match this filter — but the door is open. Begin your next briefing.</p>
          <button className="btn primary" onClick={() => go("new")}>
            <IconAdd size={14} /> New briefing
          </button>
        </div>
      ) : (
        <div className="runs-grid">
          {list.map((r) => <RunRow key={r.id} r={r} onOpen={() => go("detail", r.id)} />)}
        </div>
      )}

      <div className="floating-new">
        <button className="btn primary" onClick={() => go("new")}>
          <IconAdd size={14} /> New briefing
        </button>
      </div>
    </div>
  );
}

function RunRow({ r, onOpen }) {
  const totalIters = 3;
  const done = r.status === "completed" ? r.iterations : (r.status === "running" ? r.iterations - 1 : 0);
  const activeIdx = r.status === "running" ? r.iterations - 1 : -1;
  return (
    <div className="run-row" onClick={onOpen}>
      <div className={`run-stamp ${r.status}`}><span className="inner" /></div>
      <div className="when">
        {timeAgo(r.createdAt)}
        <span className="full">{dateShort(r.createdAt)} · {timeShort(r.createdAt)}</span>
      </div>
      <div className="body">
        <div className="title">{r.title}</div>
        <div className="tags">
          <span>{r.id}</span>
          <span>{r.tokens ? r.tokens.toLocaleString() : "—"} tok</span>
          {r.iterations > 0 && <span>{r.iterations} of {totalIters} iter</span>}
          {r.error && <span style={{ color: "var(--sv-critical)" }}>· {r.error}</span>}
        </div>
      </div>
      <div className="progress">
        {Array.from({ length: totalIters }).map((_, i) => (
          <span
            key={i}
            className={"pip " + (i < done ? "done" : i === activeIdx ? "active" : "")}
          />
        ))}
      </div>
      <div className="open-link">OPEN ⟶</div>
    </div>
  );
}

/* ============================================================
   RUN DETAIL — the heart
   ============================================================ */
function RunDetailScreen({ runId, go }) {
  // Use the rich detail for the hero run, synthesize a minimal one otherwise
  const isHero = runId === "R-2026-0184";
  const run = isHero ? RUN_DETAIL_184 : RUNS.find(r => r.id === runId) || RUNS[0];
  const [openIter, setOpenIter] = useState(isHero ? 2 : null); // open final by default
  const [reconnect, setReconnect] = useState(false);

  // Live-stage simulation for the running run (R-0185)
  const [stage, setStage] = useState(run.status === "running" ? (run.activeStage ?? 1) : -1);
  const [tokens, setTokens] = useState(run.tokens || 0);

  useEffect(() => {
    if (run.status !== "running") return;
    const t = setInterval(() => {
      setTokens((v) => v + Math.floor(40 + Math.random() * 120));
      setStage((s) => (s + 1) % 3);
    }, 3200);
    return () => clearInterval(t);
  }, [run.status]);

  // Fake reconnect blip every ~12s when running, just for demo
  useEffect(() => {
    if (run.status !== "running") return;
    const t = setInterval(() => {
      setReconnect(true);
      setTimeout(() => setReconnect(false), 2400);
    }, 18000);
    return () => clearInterval(t);
  }, [run.status]);

  const iters = isHero ? RUN_DETAIL_184.iterations : [];

  // Press: compute ink-fill state
  const totalStages = 3;
  const ink1 = run.status === "completed" ? 100 : (stage >= 1 ? 100 : 0);
  const ink2 = run.status === "completed" ? 100 : (stage >= 2 ? 100 : 0);

  return (
    <div className="detail">
      <div className="crumb">
        <a onClick={() => go("runs")}>All runs</a>
        <span className="sep">/</span>
        <span className="id t-mono">{run.id}</span>
      </div>

      {/* HEADER */}
      <div className="run-header">
        <div>
          <div className="status-line">
            <StatusBadge status={run.status} large />
            <span className="t-eyebrow eyebrow">Iteration {run.status === "running" ? (run.iterationInProgress || 2) : (Array.isArray(run.iterations) ? run.iterations.length : run.iterations)} of 3</span>
          </div>
          <h1>{run.title}</h1>
          <div className="briefing-block">
            {run.briefing}
          </div>
          {run.error && (
            <div className="error-banner" style={{ marginTop: 18, maxWidth: 600 }}>
              <IconClose size={14} />
              {run.error}
            </div>
          )}
        </div>
        <div className="run-meta">
          <div className="row"><span className="label">Submitted</span><span className="val">{timeShort(run.createdAt)}</span></div>
          <div className="row"><span className="label">Started</span><span className="val">{timeShort(run.startedAt)}</span></div>
          <div className="row"><span className="label">Completed</span><span className="val">{run.completedAt ? timeShort(run.completedAt) : "—"}</span></div>
          <div className="row"><span className="label">Tokens</span><span className="val">{tokens.toLocaleString()}</span></div>
          <div className="row"><span className="label">Cost</span><span className="val">${(run.cost || 0).toFixed(3)}</span></div>
          {run.cancelable && (
            <div className="actions">
              <button className="btn danger small">Cancel run</button>
            </div>
          )}
        </div>
      </div>

      {/* PRESS visualization */}
      <Press
        status={run.status}
        activeStage={run.status === "running" ? stage : -1}
        ink1={ink1} ink2={ink2}
      />

      {/* Iterations */}
      {isHero ? (
        <div className="iters">
          {iters.map((it, idx) => (
            <IterationPanel
              key={it.n}
              iter={it}
              isLast={idx === iters.length - 1}
              open={openIter === idx}
              onToggle={() => setOpenIter(openIter === idx ? null : idx)}
            />
          ))}
        </div>
      ) : run.status === "running" ? (
        <RunningPlaceholder stage={stage} />
      ) : (
        <div className="iter">
          <div className="iter-head" style={{ cursor: "default" }}>
            <div className="num">i.<span className="label">Iteration</span> 1</div>
            <div className="summary">No detailed iteration data for this prototype run — open the hero example.</div>
            <div />
          </div>
        </div>
      )}

      {/* Final manuscript */}
      {run.status === "completed" && isHero && <Manuscript />}

      {reconnect && (
        <div className="reconnect">
          <span className="spinner" />
          Connection to the crew interrupted — retrying.
        </div>
      )}
    </div>
  );
}

/* ---- Press ---- */
function Press({ status, activeStage, ink1, ink2 }) {
  const stages = [
    { role: "Executor", desc: "First draft", n: 0 },
    { role: "Reviewers", desc: "Briefing fidelity + clarity", n: 1 },
    { role: "Executor", desc: "Revision", n: 2 },
  ];
  const cls = (i) => {
    if (status === "completed" || status === "failed" || status === "aborted") return "done";
    if (activeStage === i) return "active";
    if (activeStage > i) return "done";
    return "";
  };
  return (
    <div className="press">
      <div className="ribbon r1" style={{ "--ink": (ink1 || 0) + "%" }} />
      <div className="ribbon r2" style={{ "--ink": (ink2 || 0) + "%" }} />
      {stages.map((s, i) => (
        <div className={`stage ${cls(i)}`} key={i}>
          <div className="seal">{["I", "II", "III"][i]}</div>
          <div className="role">{s.role}</div>
          <div className="name">{
            status === "running" && activeStage === i ?
              (i === 0 ? "Drafting…" : i === 1 ? "Reviewing…" : "Revising…") :
              s.role
          }</div>
          <div className="desc">{s.desc}</div>
        </div>
      ))}
    </div>
  );
}

function RunningPlaceholder({ stage }) {
  const labels = [
    "The Executor is drafting the first version.",
    "The Reviewers are reading the draft and noting findings.",
    "The Executor is revising based on the findings.",
  ];
  return (
    <div className="iter active" style={{ padding: "28px 24px", display: "grid", placeItems: "center", textAlign: "center" }}>
      <div className="inkwell" style={{ marginBottom: 18 }}></div>
      <div className="t-eyebrow eyebrow" style={{ marginBottom: 4 }}>In progress</div>
      <div className="t-display" style={{ fontSize: 22 }}>{labels[stage] || labels[1]}</div>
      <div style={{ marginTop: 8, fontStyle: "italic", color: "var(--ink-3)", fontFamily: "var(--font-display)" }}>
        Iterations will appear here as they are produced.
      </div>
    </div>
  );
}

/* ---- Iteration panel ---- */
function IterationPanel({ iter, isLast, open, onToggle }) {
  const counts = iter.findings.reduce((acc, f) => (acc[f.severity] = (acc[f.severity] || 0) + 1, acc), {});
  return (
    <div className={"iter " + (open ? "open " : "") + (isLast ? "active" : "")}>
      <div className="iter-head" onClick={onToggle}>
        <div className="num">i.<span className="label">Iteration</span> {iter.n}</div>
        <div className="summary">{iter.summary}</div>
        <div className="findings-count">
          {counts.crit && <span className="pill crit">{counts.crit} crit</span>}
          {counts.maj && <span className="pill maj">{counts.maj} maj</span>}
          {counts.min && <span className="pill min">{counts.min} min</span>}
          {counts.inf && <span className="pill inf">{counts.inf} info</span>}
        </div>
        <span className="caret"><IconChevron size={14} /></span>
      </div>
      <div className="iter-body">
        <div className="artifact">
          {iter.artifact || (
            <div style={{ color: "var(--ink-3)", fontStyle: "italic", padding: "20px 0" }}>
              The final draft is rendered below as a manuscript page — see “Final text”.
            </div>
          )}
        </div>
        <div className="findings">
          {iter.findings.map((f, i) => (
            <div className={`finding ${f.severity}`} key={i}>
              <div className="top">
                <Severity kind={f.severity} />
                <span className="reviewer">{f.reviewer}</span>
              </div>
              <div className="msg">{f.msg}</div>
              {f.resolved && (
                <div className="resolved"><IconCheck size={11} /> Resolved in next iteration</div>
              )}
            </div>
          ))}
        </div>
      </div>
      {!isLast && (
        <div className="evolution">
          ↓ <strong>Carried into iteration {iter.n + 1}:</strong> {iter.findings.filter(f => f.severity !== "inf").length} finding{iter.findings.filter(f => f.severity !== "inf").length !== 1 && "s"} requiring revision.
        </div>
      )}
    </div>
  );
}

/* ---- Final manuscript page ---- */
function Manuscript() {
  const [copied, setCopied] = useState(false);
  const copy = () => { setCopied(true); setTimeout(() => setCopied(false), 1400); };
  return (
    <div className="manuscript-wrap">
      <div className="head">
        <div>
          <div className="t-eyebrow eyebrow" style={{ color: "var(--hero)" }}>Final manuscript</div>
          <h2>The chromatic number of the plane</h2>
          <div className="sub">After three iterations · 642 words · serif body, Newsreader</div>
        </div>
        <div className="actions">
          <button className="btn small" onClick={copy}>
            <IconCopy size={13} /> {copied ? "Copied" : "Copy text"}
          </button>
          <button className="btn small ghost">
            <IconDownload size={13} /> Export
          </button>
        </div>
      </div>
      <div className="manuscript">
        <div className="colophon">
          <span>Geef.Atelier — Manuscript R-2026-0184</span>
          <span>Iteration III · Approved</span>
        </div>
        <h1 className="title">The chromatic number of the plane</h1>
        <h2>The number we seek</h2>
        <p>
          <span className="drop">T</span>he quantity in question is called the <em>chromatic number of the plane</em>, often
          denoted χ(ℝ²). It is the smallest number of colors with which one can assign a color to every point of the
          plane such that no two points at unit distance share the same color. The question reads like a riddle and the
          answer has resisted mathematicians for three quarters of a century.
        </p>
        <h2>Known bounds</h2>
        <p>For roughly seven decades, only the following bounds were established:</p>
        <span className="math">4 ≤ χ(ℝ²) ≤ 7.</span>
        <p>
          The lower bound is attained by the <em>Moser spindle</em>, a unit-distance graph on seven vertices and eleven
          edges built from two rhombi sharing a common edge. Every triangle in the spindle forces a fresh color, and the
          combinatorics of the shared edge force a fourth. The upper bound is obtained by a periodic tiling of the plane by
          hexagons of diameter slightly less than 1, on which a seven-color scheme can be exhibited by hand.
        </p>
        <h2>A 2018 breakthrough</h2>
        <p>
          In April 2018 Aubrey de Grey raised the lower bound to 5 by exhibiting a unit-distance graph on 1,581 vertices
          that requires five colors. The result was the first improvement in the lower bound since 1950 — a remarkable
          fact when one considers how many strong combinatorialists had tried in the interim. de Grey's graph has since
          been reduced by collaborative computer search (Project Polymath 16) to a few hundred vertices.
        </p>
        <h2>Where the answer lies</h2>
        <p>
          Today, the best known bounds are:
        </p>
        <span className="math">5 ≤ χ(ℝ²) ≤ 7.</span>
        <p>
          Whether χ(ℝ²) is 5, 6, or 7 remains one of the most accessible open problems in combinatorial geometry —
          accessible in statement, not in proof. A definitive answer would require either a new lower-bound construction
          (a unit-distance graph requiring six colors) or a new upper-bound coloring scheme (six colors arranged so that
          no unit-distance pair clashes). Neither has been found in seventy-six years.
        </p>
        <div className="signature">
          <span>Executor · Atelier Prose v2</span>
          <span>Reviewed · BriefingFidelity, Clarity</span>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, {
  LoginScreen, WelcomeScreen, NewRunScreen, RunsListScreen, RunDetailScreen,
});
