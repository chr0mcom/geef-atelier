/* global React, HeroPress, IterationLoop,
   PhaseGrounding, PhaseExecution, PhaseEvaluation, PhaseFinalize,
   CapSources, CapFilter, CapModels, CapExport, CapLearn, CapTrust */

const { useEffect, useRef, useState } = React;

// ============================================================
// Headline copy options
// ============================================================
const HEADLINES = {
  crew: {
    h1: <>Texts, <em>crafted by a crew</em> —<br />not blurted by a machine.</>,
    sub: <>Geef.Atelier runs your briefing through a pipeline of collaborating models — drafting, critiquing, and refining <em>until the work holds up</em>. The result is a text that's been argued over, not just generated.</>
  },
  manufactory: {
    h1: <>A <em>manufactory</em><br />for the written word.</>,
    sub: <>Not a chatbot. A workshop of AI models that draft, critique, and refine your text in iterations — <em>until the crew is satisfied</em>. Every run is reproducible, every reviewer auditable.</>
  },
  press: {
    h1: <>Press <em>once</em>.<br />Refined <em>many times</em>.</>,
    sub: <>A briefing enters the press. A crew of models drafts, critiques, and rewrites — over and over — until the manuscript <em>converges</em>. Finished. Sealed. Exported.</>
  }
};

// ============================================================
// IntersectionObserver helper — reveal on scroll
// ============================================================
function useInView(once = true, threshold = 0.18) {
  const ref = useRef(null);
  const [inView, setInView] = useState(false);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const obs = new IntersectionObserver(
      (entries) => {
        entries.forEach((e) => {
          if (e.isIntersecting) {
            setInView(true);
            if (once) obs.unobserve(e.target);
          } else if (!once) {
            setInView(false);
          }
        });
      },
      { threshold }
    );
    obs.observe(el);
    return () => obs.disconnect();
  }, [once, threshold]);
  return [ref, inView];
}

// ============================================================
// 1 — Top nav
// ============================================================
function Nav() {
  return (
    <nav className="lp-nav">
      <div className="brand">
        <div className="crest">G</div>
        <div className="wordmark">
          <span>geef</span>
          <span className="dot">.</span>
          <span className="atelier">atelier</span>
        </div>
      </div>
      <div className="midnav">
        <a>The principle</a>
        <a>The crew</a>
        <a>Manuscript</a>
        <a>SDK</a>
      </div>
      <div className="actions">
        <a className="signin">Sign in</a>
        <button className="lp-cta">
          Enter the Atelier
          <span className="arrow">→</span>
        </button>
      </div>
    </nav>);

}

// ============================================================
// 2 — Hero
// ============================================================
function Hero({ headline }) {
  const copy = HEADLINES[headline] || HEADLINES.crew;
  return (
    <section className="lp-section intro lp-hero" data-screen-label="01 Hero" style={{ padding: "0px" }}>
      <div className="lp-container" style={{ display: "contents" }}>
        <div className="lede-col">
          <div className="colophon-row">
            <span className="pip"></span>
            <span>Vol. I · No. 24</span>
            <span style={{ color: "var(--ink-4)" }}>/</span>
            <span>A workshop, in beta</span>
          </div>
          <h1>{copy.h1}</h1>
          <p className="sub">{copy.sub}</p>
          <div className="hero-actions">
            <button className="lp-cta lg">
              Enter the Atelier
              <span className="arrow">→</span>
            </button>
            <button className="lp-cta lg ghost">
              See the principle
              <span className="arrow">↓</span>
            </button>
          </div>
          <div className="hero-meta">
            <div className="item">
              <span>Pipeline</span>
              <span className="v">Grounding · Execution · Evaluation · Finalize</span>
            </div>
            <div className="item">
              <span>Crew</span>
              <span className="v">Bring any model — OpenAI · Anthropic · Local</span>
            </div>
            <div className="item">
              <span>Built on</span>
              <span className="v">the open Geef SDK</span>
            </div>
          </div>
        </div>

        <div className="hero-visual">
          <div className="frame">
            <div className="colophon">
              <span>Folio I</span>
              <span><b>Press at work</b></span>
            </div>
            <div className="colophon-r">Run · R-0c41-2e7</div>
            <HeroPress />
            <div className="stage-floor"></div>
          </div>
        </div>
      </div>
    </section>);

}

// ============================================================
// 3 — The turn
// ============================================================
function Turn() {
  const [ref, inView] = useInView(true, 0.25);
  return (
    <section
      className={"lp-section lp-turn lp-reveal " + (inView ? "in" : "")}
      ref={ref}
      data-screen-label="02 The Turn">
      
      <div className="lp-container">
        <p className="lp-eyebrow centered">The Turn</p>
        <h2 className="quote">
          Most AI tools answer in <em>one breath</em>.
          A good text is never written that way — it's drafted,
          challenged, and <em>rewritten</em>.
        </h2>

        <div className="drafts">
          <div className="draft-card bad">
            <div className="tag"><span>Draft 01</span><span>One pass</span></div>
            <div className="lines">
              <i style={{ width: "92%" }}></i>
              <i style={{ width: "78%" }}></i>
              <i style={{ width: "85%" }}></i>
              <i style={{ width: "62%" }}></i>
              <i style={{ width: "88%" }}></i>
              <i style={{ width: "70%" }}></i>
              <i style={{ width: "54%" }}></i>
            </div>
            <div className="seal">— rejected</div>
          </div>

          <div className="draft-arrow">
            <div>
              <div style={{ fontSize: 28, lineHeight: 1, color: "var(--hero)" }}>→</div>
              <div style={{ marginTop: 6 }}>refined</div>
            </div>
          </div>

          <div className="draft-card good">
            <div className="tag"><span>Iteration 04</span><span style={{ color: "var(--hero)" }}>converged</span></div>
            <div className="lines">
              <i style={{ width: "84%" }}></i>
              <i style={{ width: "94%" }}></i>
              <i style={{ width: "76%" }}></i>
              <i style={{ width: "88%" }}></i>
              <i style={{ width: "92%" }}></i>
              <i style={{ width: "70%" }}></i>
              <i style={{ width: "80%" }}></i>
            </div>
            <div className="seal">— sealed</div>
          </div>
        </div>
      </div>
    </section>);

}

// ============================================================
// 4 — GEEF principle (the centrepiece)
// ============================================================
function GeefFlow() {
  const [ref, inView] = useInView(true, 0.32);
  const [phase, setPhase] = useState(-1); // -1 idle, 0..3 active, 4 done

  // animation choreography: the pulse runs G → E → E (loops 3 times) → F
  useEffect(() => {
    if (!inView) return;
    if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setPhase(4);
      return;
    }
    let cancelled = false;
    const seq = async () => {
      const wait = (ms) => new Promise((r) => setTimeout(r, ms));
      await wait(280);
      if (cancelled) return;setPhase(0);
      await wait(900);
      if (cancelled) return;setPhase(1);
      await wait(900);
      if (cancelled) return;setPhase(2);
      // loop 3 times at evaluation
      await wait(2400);
      if (cancelled) return;
      setPhase(3);
      await wait(800);
      if (cancelled) return;
      setPhase(4);
    };
    seq();
    return () => {cancelled = true;};
  }, [inView]);

  const phases = [
  {
    ord: "Phase G",
    glyph: "G",
    name: "Grounding",
    copy: <>Gathers the facts — <em>web research, your knowledge base, documents, papers</em> — and filters out the noise before the draft begins.</>,
    Ico: PhaseGrounding
  },
  {
    ord: "Phase E",
    glyph: "E",
    name: "Execution",
    copy: <>An <em>executor</em> model writes the first full draft from the briefing and the gathered context.</>,
    Ico: PhaseExecution
  },
  {
    ord: "Phase E",
    glyph: "E",
    name: "Evaluation",
    copy: <>A <em>crew of reviewers</em> critiques the draft — flagging errors, weak arguments, unclear passages, each with severity.</>,
    Ico: PhaseEvaluation
  },
  {
    ord: "Phase F",
    glyph: "F",
    name: "Finalize",
    copy: <>The converged text is polished and exported — <em>PDF, Word, Markdown</em>, or sent onward to your workflow.</>,
    Ico: PhaseFinalize
  }];


  return (
    <section
      className="lp-section lp-geef lp-reveal in"
      ref={ref}
      data-screen-label="03 GEEF Principle" style={{ padding: "120px 0px" }}>
      
      <div className="lp-container">
        <div className="lp-geef-head">
          <div>
            <p className="lp-eyebrow">The Principle</p>
            <h2 className="lp-h2">
              Four phases. <em>One loop</em>. <br />
              The work, refined until it holds.
            </h2>
          </div>
          <div>
            <p className="lp-lede">
              GEEF is the pipeline that powers every run in the Atelier.
              A briefing is grounded in real sources, executed into a draft,
              evaluated by a crew of reviewers — and rewritten,
              again and again, <em style={{ color: "var(--hero)", fontStyle: "italic" }}>until it converges</em>.
            </p>
          </div>
        </div>

        <div className={"geef-flow " + (phase >= 0 ? "run" : "")}>
          <div className="ribbon"><span className="ink"></span></div>
          <div className="loop">
            <IterationLoop />
            <div className="loop-label">
              ↻ &nbsp;until convergence
            </div>
          </div>

          {phases.map((p, i) => {
            const passed = phase > i;
            const active = phase === i || i === 2 && phase >= 2 && phase < 3;
            return (
              <div
                key={i}
                className={
                "geef-phase " + (
                passed ? "has-passed " : "") + (
                active ? "is-active " : "")
                }>
                
                <div className="num">
                  <span className="ord">{p.ord}</span>
                  {i === 2 ?
                  <span className="iter-badge">Iter <b>×3</b></span> :
                  null}
                </div>
                <div className="ico"><p.Ico /></div>
                <div style={{ borderStyle: "solid", borderColor: "rgba(0, 0, 0, 0)", borderWidth: "0px", padding: "0px", margin: "0px 0px 10px 1px" }}>
                  <h3 className="name" style={{ margin: "0px" }}>{p.name}</h3>
                  <p className="copy" style={{ padding: "0px", margin: "14px 0px" }}>{p.copy}</p>
                </div>
                <div className="pip-row">
                  <span className="pip"></span>
                  <span>
                    {i === 0 ? "Sources gathered" :
                    i === 1 ? "First draft written" :
                    i === 2 ? "Findings · severity · revision" :
                    "Sealed · exported"}
                  </span>
                </div>
              </div>);

          })}
        </div>

        <div style={{ marginTop: 28, display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 16 }}>
          <span className="lp-mono-caption">
            Every transition is logged. Every iteration auditable.
          </span>
          <span className="lp-mono-caption" style={{ color: "var(--ink-2)" }}>
            G &nbsp;→&nbsp; E &nbsp;↻&nbsp; E &nbsp;→&nbsp; F
          </span>
        </div>
      </div>
    </section>);

}

// ============================================================
// 5 — The Crew
// ============================================================
function Crew() {
  const [ref, inView] = useInView(true, 0.18);
  return (
    <section
      className={"lp-section lp-crew lp-reveal " + (inView ? "in" : "")}
      ref={ref}
      data-screen-label="04 Crew">
      
      <div className="lp-container" style={{ display: "contents" }}>
        <div className="crew-sheet">
          <div className="top">
            <div className="label">— The crew sheet —</div>
            <div className="title">A treatise on patient craft</div>
            <div className="ord">Run · R-0c41-2e7 · Briefing · 18 May 2026</div>
          </div>
          <div>
            <div className="crew-row">
              <div className="role">Briefing</div>
              <div className="who">Argue, with mathematical care, that…</div>
              <div className="tag">Inbound</div>
            </div>
            <div className="crew-row">
              <div className="role">Grounding</div>
              <div className="who">Web research <span className="amp">·</span> arXiv <span className="amp">·</span> Private vault</div>
              <div className="tag">42 sources</div>
            </div>
            <div className="crew-row">
              <div className="role">Executor</div>
              <div className="who">Claude Sonnet 4.5</div>
              <div className="tag">Master</div>
            </div>
            <div className="crew-row">
              <div className="role">Reviewers</div>
              <div className="who">GPT-5 <span className="amp">·</span> Gemini 2.5 <span className="amp">·</span> Llama 4 Maverick</div>
              <div className="tag">Strict</div>
            </div>
            <div className="crew-row">
              <div className="role">Advisors</div>
              <div className="who">Style critic <span className="amp">·</span> Fact-checker</div>
              <div className="tag">Optional</div>
            </div>
            <div className="crew-row">
              <div className="role">Finalizers</div>
              <div className="who">Polish <span className="amp">·</span> Strip AI-tells <span className="amp">·</span> Export PDF</div>
              <div className="tag">Outbound</div>
            </div>
          </div>
          <div className="signature">
            <span>Composed by — <b style={{ color: "var(--hero-deep)" }}>the operator</b></span>
            <span>Convergence at iter. 04</span>
          </div>
        </div>

        <div className="crew-copy">
          <p className="lp-eyebrow">The Crew</p>
          <h2 className="lp-h2">
            <em>You</em> direct the workshop.
          </h2>
          <p className="lp-lede">
            Pick the executor who writes. The reviewers who critique.
            The sources they may draw on. The format the manuscript leaves in.
            The Atelier is a workshop with the operator at its centre —
            not a black box that guesses.
          </p>

          <div className="points">
            <div className="point">
              <div className="n">01</div>
              <div className="t">Compose a crew per run.
                <span>One model writes; several review. Each plays a role you define.</span>
              </div>
            </div>
            <div className="point">
              <div className="n">02</div>
              <div className="t">Set the strictness.
                <span>Loose for sketches; ruthless for filings. The crew converges at the bar you set.</span>
              </div>
            </div>
            <div className="point">
              <div className="n">03</div>
              <div className="t">Choose what they may read.
                <span>Web, private vault, uploads, arXiv, Semantic Scholar — or nothing at all.</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>);

}

// ============================================================
// 6 — Proof of craft (run mockup)
// ============================================================
function Proof() {
  const [ref, inView] = useInView(true, 0.18);
  return (
    <section
      className={"lp-section lp-proof lp-reveal " + (inView ? "in" : "")}
      ref={ref}
      data-screen-label="05 Proof">
      
      <div className="lp-container">
        <div className="lp-proof-head">
          <div>
            <p className="lp-eyebrow">Proof of Craft</p>
            <h2 className="lp-h2">
              Every run, <em>auditable</em>.
            </h2>
          </div>
          <p className="lp-lede">
            Nothing is hidden. See what every reviewer found,
            how the text evolved across iterations, and what each run cost —
            laid out like a fine document, not a dashboard.
          </p>
        </div>

        <div className="run-mock">
          <div className="mock-head">
            <div>
              <div className="crumb">
                <span>Runs</span>
                <span style={{ color: "var(--ink-4)" }}>/</span>
                <span className="id">R-0c41-2e7</span>
                <span style={{ color: "var(--ink-4)" }}>·</span>
                <span>4 iterations</span>
              </div>
              <div className="title">A treatise on patient craft</div>
            </div>
            <div className="pill-row">
              <span className="stamp">Converged</span>
            </div>
          </div>

          <div className="ms">
            <div className="ms-colophon">
              <span>Final manuscript</span>
              <span>Sealed · 18 May 2026</span>
            </div>
            <h3>A treatise on patient craft</h3>
            <p>
              <span className="drop">A</span> good text is not born in one
              breath. It is <em>drafted, challenged, and refined</em> — a slow,
              <span className="change">deliberate</span> exchange between a hand that writes and a crew
              that reads with intent. Where a single pass produces fluency,
              an iteration produces conviction.
            </p>
            <p>
              The press in this workshop does not <span className="change">stamp once</span> and
              call it done. It impresses, lifts, and impresses again, until the
              page yields a mark that needs no defending.
            </p>
            <div className="ms-sign">
              <span>— composed by the crew</span>
              <span>Folio I · No. 24</span>
            </div>
          </div>

          <div className="side">
            <div className="lbl">Reviewer findings · by iteration</div>
            <div className="iter-line">
              <span className="it">Iter 01</span>
              <span className="bar">
                <span className="b crit"></span>
                <span className="b crit"></span>
                <span className="b maj"></span>
                <span className="b maj"></span>
                <span className="b maj"></span>
                <span className="b min"></span>
                <span className="b min"></span>
              </span>
              <span className="count">7</span>
            </div>
            <div className="iter-line">
              <span className="it">Iter 02</span>
              <span className="bar">
                <span className="b crit"></span>
                <span className="b maj"></span>
                <span className="b maj"></span>
                <span className="b min"></span>
                <span className="b min"></span>
              </span>
              <span className="count">5</span>
            </div>
            <div className="iter-line">
              <span className="it">Iter 03</span>
              <span className="bar">
                <span className="b maj"></span>
                <span className="b min"></span>
                <span className="b info"></span>
              </span>
              <span className="count">3</span>
            </div>
            <div className="iter-line">
              <span className="it">Iter 04</span>
              <span className="bar">
                <span className="b info"></span>
              </span>
              <span className="count" style={{ color: "var(--st-completed)" }}>✓ 0 critical</span>
            </div>

            <div className="totals">
              <div className="t">Tokens<b>148.2k</b></div>
              <div className="t">Cost<b>$0.42</b></div>
              <div className="t">Crew<b>1 executor · 3 reviewers</b></div>
              <div className="t">Convergence<b>Iter 04</b></div>
            </div>
          </div>
        </div>
      </div>
    </section>);

}

// ============================================================
// 7 — Capabilities
// ============================================================
function Capabilities() {
  const [ref, inView] = useInView(true, 0.14);
  const caps = [
  { Ico: CapSources, meta: "Grounding", title: <>Grounded in <em>real sources</em></>, copy: "Web, private knowledge base, uploaded documents, scientific papers from arXiv and Semantic Scholar." },
  { Ico: CapFilter, meta: "Filtering", title: <>A second AI <em>filters the noise</em></>, copy: "Gathered sources are refined by a model before they ever reach the executor — only the relevant survives." },
  { Ico: CapModels, meta: "Models", title: <>Your models, <em>your choice</em></>, copy: "OpenAI, Anthropic, Google, local models, or your own subscriptions via CLI. The Atelier is provider-agnostic." },
  { Ico: CapExport, meta: "Finishing", title: <>Finished, <em>not just written</em></>, copy: "Export to PDF, Word, Markdown. Transform tone. Strip AI-tells. Add summaries. Ready for the world." },
  { Ico: CapLearn, meta: "Learnings", title: <>It <em>learns</em></>, copy: "Validated learnings from past runs feed forward into new ones. Opt-in, gated, never assumed." },
  { Ico: CapTrust, meta: "Trust", title: <>Built for <em>trust</em></>, copy: "Multi-user, self-hosted, every secret kept out of the database. Auditable by design." }];

  return (
    <section
      className={"lp-section lp-caps lp-reveal " + (inView ? "in" : "")}
      ref={ref}
      data-screen-label="06 Capabilities">
      
      <div className="lp-container">
        <div className="lp-caps-head">
          <div>
            <p className="lp-eyebrow">The Workshop</p>
            <h2 className="lp-h2">
              What the press <em>can draw on</em>.
            </h2>
          </div>
          <p className="lp-lede" style={{ paddingBottom: 10 }}>
            Six capabilities that distinguish a manufactory from a chat tool.
            Each one earns its place; none is decorative.
          </p>
        </div>

        <div className="caps-grid">
          {caps.map((c, i) =>
          <div className="cap" key={i}>
              <div className="ico"><c.Ico /></div>
              <div className="meta">— {c.meta}</div>
              <h3>{c.title}</h3>
              <p>{c.copy}</p>
            </div>
          )}
        </div>
      </div>
    </section>);

}

// ============================================================
// 8 — Closing
// ============================================================
function Closing() {
  const [ref, inView] = useInView(true, 0.22);
  return (
    <section
      className={"lp-section lp-close lp-reveal " + (inView ? "in" : "")}
      ref={ref}
      data-screen-label="07 Closing">
      
      <div className="lp-container">
        <div className="stamp">G</div>
        <h2>Your words deserve a <em>workshop</em>.</h2>
        <div className="actions">
          <button className="lp-cta lg">
            Enter the Atelier
            <span className="arrow">→</span>
          </button>
          <button className="lp-cta lg ghost">
            Read the SDK
            <span className="arrow">↗</span>
          </button>
        </div>
      </div>
    </section>);

}

// ============================================================
// 9 — Footer
// ============================================================
function Footer() {
  return (
    <footer className="lp-footer">
      <div className="lp-container">
        <div className="row">
          <div>
            <div className="brand">
              <div className="lp-nav" style={{ all: "unset" }}></div>
              <div style={{ width: 30, height: 30, border: "1px solid var(--hairline-strong)", borderRadius: "50%", display: "grid", placeItems: "center", color: "var(--hero)", fontFamily: "var(--font-display)", fontSize: 15, background: "var(--surface)" }}>G</div>
              <div style={{ fontFamily: "var(--font-display)", fontSize: 19, display: "flex", alignItems: "baseline", gap: 2 }}>
                <span>geef</span>
                <span style={{ color: "var(--hero)", fontFamily: "var(--font-mono)" }}>.</span>
                <span style={{ fontStyle: "italic", color: "var(--ink-2)" }}>atelier</span>
              </div>
            </div>
            <p style={{ fontFamily: "var(--font-display)", fontStyle: "italic", color: "var(--ink-3)", fontSize: 14, maxWidth: "32ch", marginTop: 18, lineHeight: 1.55 }}>
              A text manufactory. Built on the open Geef SDK.
            </p>
          </div>
          <div className="col">
            <h4>Product</h4>
            <a>The principle</a>
            <a>The crew</a>
            <a>Manuscript</a>
            <a>Pricing</a>
          </div>
          <div className="col">
            <h4>For Operators</h4>
            <a>Sign in</a>
            <a>Documentation</a>
            <a>Self-host</a>
            <a>Status</a>
          </div>
          <div className="col">
            <h4>Open</h4>
            <a>github.com/chr0mcom/geef</a>
            <a>Geef SDK</a>
            <a>Changelog</a>
            <a>Contact</a>
          </div>
          <div className="col" style={{ minWidth: 120 }}>
            <h4>Legal</h4>
            <a>Imprint</a>
            <a>Privacy</a>
            <a>Terms</a>
          </div>
        </div>
        <div className="colophon">
          <span>geef.atelier · A workshop, in beta · MMXXVI</span>
          <span>Built on the <b>open Geef SDK</b></span>
        </div>
      </div>
    </footer>);

}

Object.assign(window, { Nav, Hero, Turn, GeefFlow, Crew, Proof, Capabilities, Closing, Footer, useInView });