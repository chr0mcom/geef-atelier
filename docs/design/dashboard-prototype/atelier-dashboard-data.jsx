/* global React */
const { useMemo } = React;

/* ============================================================
   DASHBOARD DATA — two scope modes
   ============================================================ */

const TODAY = new Date("2026-05-19T08:32:00");

// ---- Welcome strip
const WELCOME = {
  my:  { greeting: "Good morning, Stefan.", streakDays: 7,  streakLabel: "days at the bench" },
  all: { greeting: "Good morning, Stefan.", streakDays: 14, streakLabel: "days workshop-wide", suffix: "(Viewing all workshops)" },
};

// ---- Press (live runs)
const PRESS = {
  my: {
    state: "single", // 'idle' | 'single' | 'multi'
    runs: [{
      id: "b324c0fa",
      user: "stefan",
      template: "klassik",
      iteration: 2, of: 3,
      activePhase: 1, // 0..3
      startedSecondsAgo: 84,
    }],
  },
  all: {
    state: "multi",
    runs: [
      { id: "b324c0fa", user: "stefan", template: "klassik",  iteration: 2, of: 3, activePhase: 1, startedSecondsAgo: 84  },
      { id: "9a2e771c", user: "alice",  template: "academic", iteration: 1, of: 3, activePhase: 0, startedSecondsAgo: 24  },
      { id: "e041cc28", user: "bob",    template: "marketing-launch", iteration: 3, of: 3, activePhase: 2, startedSecondsAgo: 612 },
    ],
  },
};

// ---- Ledger (4 hero tiles)
const LEDGER = {
  my: [
    { label: "Commissions this month", value: "47",      delta: "▲ 12 vs last month",   trend: "up" },
    { label: "Cost to date",           value: "€8.42",   delta: "▲ €1.12 vs last month", trend: "up" },
    { label: "Words set",              value: "24,580",  delta: "→ similar to last month", trend: "flat" },
    { label: "Craft mark",             value: "92 / 100",delta: "▲ 3 points",            trend: "up" },
  ],
  all: [
    { label: "Commissions this month", value: "312",     delta: "▲ 84 vs last month",   trend: "up" },
    { label: "Cost to date",           value: "€58.91",  delta: "▲ €12.40 vs last month", trend: "up" },
    { label: "Words set",              value: "312,400", delta: "▲ 14% vs last month",   trend: "up" },
    { label: "Craft mark",             value: "89 / 100",delta: "→ similar to last month", trend: "flat" },
  ],
};

/* ---- Activity calendar: 52w × 7d, seeded realistic */
function makeCalendar(seed, weekdayPeaks = [1, 4]) {
  const cells = [];
  // 364 days back from today, walk forward
  const start = new Date(TODAY);
  start.setDate(start.getDate() - 51 * 7 - start.getDay() + 1);
  const rng = mulberry32(seed);
  let peakDate = null, peakCount = 0;
  for (let w = 0; w < 52; w++) {
    const col = [];
    for (let d = 0; d < 7; d++) {
      const date = new Date(start);
      date.setDate(start.getDate() + w * 7 + d);
      if (date > TODAY) { col.push(null); continue; }
      const weekdayBoost = weekdayPeaks.includes(d) ? 2.4 : 1;
      const weekendDamp = (d === 0 || d === 6) ? 0.35 : 1;
      const recencyBoost = 1 + (w / 52) * 0.6; // more recent = more
      const r = rng();
      const raw = r * 12 * weekdayBoost * weekendDamp * recencyBoost;
      const count = Math.max(0, Math.round(raw - 1));
      if (count > peakCount) { peakCount = count; peakDate = new Date(date); }
      col.push({ date, count });
    }
    cells.push(col);
  }
  return { cells, peakDate, peakCount };
}
function mulberry32(seed) {
  let t = seed >>> 0;
  return () => {
    t |= 0; t = (t + 0x6D2B79F5) | 0;
    let r = Math.imul(t ^ (t >>> 15), 1 | t);
    r = (r + Math.imul(r ^ (r >>> 7), 61 | r)) ^ r;
    return ((r ^ (r >>> 14)) >>> 0) / 4294967296;
  };
}
const CALENDAR = {
  my:  makeCalendar(20260519),
  all: makeCalendar(987654321),
};

// ---- Crew DNA
const CREW_DNA = {
  my: {
    executor: [{ name: "default-executor", n: 42 }, { name: "custom-tech-exec", n: 8 }],
    reviewers: [
      { name: "briefing-fidelity", n: 38 },
      { name: "clarity", n: 34 },
      { name: "academic-argumentation-rigor", n: 12 },
    ],
    advisors: [],
  },
  all: {
    executor: [{ name: "default-executor", n: 287 }, { name: "custom-tech-exec", n: 8 }],
    reviewers: [
      { name: "briefing-fidelity", n: 245 },
      { name: "clarity", n: 220 },
      { name: "academic-argumentation-rigor", n: 78 },
    ],
    advisors: [{ name: "legal-domain-expert", n: 24 }],
  },
};

// ---- Cost forge (Sankey)
const COST_FORGE = {
  my: [
    { template: "klassik",          model: "gpt-5.5",          cost: 3.12, runs: 14 },
    { template: "juristisch",       model: "claude-opus-4.7",  cost: 2.01, runs: 6  },
    { template: "akademisch",       model: "gemini-2.5-pro",   cost: 1.24, runs: 5  },
    { template: "custom-marketing", model: "gpt-4o-mini",      cost: 0.87, runs: 9  },
  ],
  all: [
    { template: "klassik",          model: "gpt-5.5",          cost: 22.40, runs: 96  },
    { template: "akademisch",       model: "claude-opus-4.7",  cost: 14.10, runs: 41  },
    { template: "juristisch",       model: "claude-opus-4.7",  cost: 11.30, runs: 34  },
    { template: "custom-marketing", model: "gemini-2.5-pro",   cost:  6.80, runs: 52  },
    { template: "tech-review",      model: "gpt-4o-mini",      cost:  4.31, runs: 89  },
  ],
};

// ---- Iteration histogram
const HIST = {
  my:  { bars: [12, 23, 18, 4],   verdict: "Pipeline running well-calibrated." },
  all: { bars: [84, 156, 58, 14], verdict: "Pipeline running well-calibrated across workshops." },
};

// ---- Recent manuscripts
const MANUSCRIPTS = {
  my: [
    { name: "Marketing-Brief-Q2.pdf",     fmt: "pdf",  when: "32 min ago", size: "1.2 MB", user: null },
    { name: "Legal-Opinion-Final.docx",   fmt: "docx", when: "1 h ago",    size: "18 KB",  user: null },
    { name: "Tech-Documentation.md",      fmt: "md",   when: "2 h ago",    size: "4 KB",   user: null },
    { name: "Academic-Argument.pdf",      fmt: "pdf",  when: "5 h ago",    size: "220 KB", user: null },
    { name: "marketing-export.html",      fmt: "html", when: "yesterday",  size: "12 KB",  user: null },
    { name: "summary.json",               fmt: "json", when: "2 d ago",    size: "2 KB",   user: null },
  ],
  all: [
    { name: "Marketing-Brief-Q2.pdf",     fmt: "pdf",  when: "32 min ago", size: "1.2 MB", user: "stefan" },
    { name: "Academic-Paper-Final.pdf",   fmt: "pdf",  when: "1 h ago",    size: "480 KB", user: "alice"  },
    { name: "Legal-Opinion-Final.docx",   fmt: "docx", when: "1 h ago",    size: "18 KB",  user: "stefan" },
    { name: "tech-spec-v2.md",            fmt: "md",   when: "2 h ago",    size: "6 KB",   user: "bob"    },
    { name: "Tech-Documentation.md",      fmt: "md",   when: "2 h ago",    size: "4 KB",   user: "stefan" },
    { name: "Marketing-Launch.html",      fmt: "html", when: "4 h ago",    size: "22 KB",  user: "alice"  },
  ],
};

// ---- Token stream (30 daily values)
function makeSparkline(seed, scale) {
  const rng = mulberry32(seed);
  const arr = [];
  for (let i = 0; i < 30; i++) {
    const weekday = (i + 5) % 7;
    const weekend = (weekday === 0 || weekday === 6) ? 0.35 : 1;
    arr.push(Math.round(scale * weekend * (0.55 + rng() * 0.55)));
  }
  return arr;
}
const TOKEN = {
  my:  { total: "342,180",   series: makeSparkline(42, 16000), delta: "▲ 14% vs last month", trend: "up" },
  all: { total: "1,847,200", series: makeSparkline(7,  85000), delta: "▲ 18% vs last month", trend: "up" },
};

// ---- Critics' bench matrix
const CRITICS = {
  my: [
    { name: "briefing-fidelity",     row: [2, 8, 24, 4] },
    { name: "clarity",               row: [1, 3, 28, 12] },
    { name: "academic-rigor",        row: [6, 18, 14, 2] },
    { name: "legal-jargon-precision",row: [0, 2, 14, 1] },
    { name: "legal-clause-risk",     row: [4, 12, 6, 1] },
  ],
  all: [
    { name: "briefing-fidelity",     row: [14, 62, 188, 32] },
    { name: "clarity",               row: [6, 22, 214, 96] },
    { name: "academic-rigor",        row: [38, 142, 110, 18] },
    { name: "legal-jargon-precision",row: [0, 14, 102, 7] },
    { name: "legal-clause-risk",     row: [22, 78, 38, 9] },
  ],
  strict: { my: "academic-rigor", all: "academic-rigor" },
};

// ---- Providers
const PROVIDERS = {
  my: [
    { name: "openrouter",    type: "HTTP", when: "12 min ago", state: "recent" },
    { name: "claude-cli",    type: "CLI",  when: "1 h ago",    state: "recent" },
    { name: "codex-cli",     type: "CLI",  when: "2 h ago",    state: "recent" },
    { name: "gemini-cli",    type: "CLI",  when: "yesterday",  state: "warm"   },
    { name: "openai-direct", type: "HTTP", when: "3 d ago",    state: "cold"   },
    { name: "groq",          type: "HTTP", when: "—",          state: "cold"   },
    { name: "deepseek",      type: "HTTP", when: "—",          state: "cold"   },
    { name: "ollama-local",  type: "HTTP", when: "—",          state: "cold"   },
  ],
  all: [
    { name: "openrouter",    type: "HTTP", when: "12 min ago", state: "recent" },
    { name: "claude-cli",    type: "CLI",  when: "1 h ago",    state: "recent" },
    { name: "codex-cli",     type: "CLI",  when: "2 h ago",    state: "recent" },
    { name: "gemini-cli",    type: "CLI",  when: "4 h ago",    state: "recent" },
    { name: "openai-direct", type: "HTTP", when: "1 h ago",    state: "recent" },
    { name: "groq",          type: "HTTP", when: "8 h ago",    state: "warm"   },
    { name: "deepseek",      type: "HTTP", when: "yesterday",  state: "warm"   },
    { name: "ollama-local",  type: "HTTP", when: "—",          state: "cold"   },
  ],
  counts: { my: { configured: 11, active: 5 }, all: { configured: 11, active: 8 } },
};

// ---- Knowledge base (shared)
const KB = {
  documents: 12,
  chunks: 847,
  embeddings: "1,302,000",
  mostRecent: { name: "research-paper.pdf", when: "3 d ago" },
  mostCited:  { name: "style-guide.md", count: 28 },
};

// ---- Day book activity stream
const DAYBOOK = {
  my: [
    { when: "12 min ago", icon: "check",   verb: "Run completed",                  rest: "with klassik — 2 iterations, €0.04" },
    { when: "32 min ago", icon: "doc",     verb: "Knowledge document indexed",     rest: "research-paper.pdf" },
    { when: "1 h ago",    icon: "check",   verb: "Run completed",                  rest: "with juristisch — 3 iterations, €0.12" },
    { when: "2 h ago",    icon: "plus",    verb: "New custom provider",            rest: "my-openai-direct" },
    { when: "3 h ago",    icon: "star",    verb: "Studio analysis materialized",   rest: "template custom-tech-review" },
    { when: "yesterday",  icon: "x",       verb: "Run failed",                     rest: "marketing-test — StopMaxAttemptsReached" },
    { when: "yesterday",  icon: "link",    verb: "OAuth client connected",         rest: "Claude Desktop" },
    { when: "2 d ago",    icon: "doc",     verb: "Knowledge document indexed",     rest: "notes.md" },
  ],
  all: [
    { who: "stefan", when: "12 min ago", icon: "check", verb: "Run completed",              rest: "klassik · 2 iterations · €0.04" },
    { who: "alice",  when: "28 min ago", icon: "check", verb: "Run completed",              rest: "academic · 3 iterations · €0.18" },
    { who: "alice",  when: "32 min ago", icon: "doc",   verb: "Knowledge document indexed", rest: "research-paper.pdf" },
    { who: "stefan", when: "1 h ago",    icon: "check", verb: "Run completed",              rest: "juristisch · 3 iterations · €0.12" },
    { who: "stefan", when: "2 h ago",    icon: "plus",  verb: "New custom provider",        rest: "my-openai-direct" },
    { who: "bob",    when: "3 h ago",    icon: "link",  verb: "User created",               rest: "(admin action)" },
    { who: "stefan", when: "4 h ago",    icon: "star",  verb: "Studio analysis materialized", rest: "template custom-tech-review" },
    { who: "alice",  when: "yesterday",  icon: "x",     verb: "Run failed",                 rest: "marketing-test" },
  ],
};

Object.assign(window, {
  TODAY, WELCOME, PRESS, LEDGER, CALENDAR, CREW_DNA, COST_FORGE,
  HIST, MANUSCRIPTS, TOKEN, CRITICS, PROVIDERS, KB, DAYBOOK,
});
