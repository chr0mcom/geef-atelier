/* global React, IconPen, IconAdd, IconChevron, IconCopy, IconDownload,
          IconClose, IconCheck, IconUser, IconLogout, IconQuill, IconRefresh,
          IconArchive, IconHome, IconBook, IconPalette, IconLayers */
const { useState, useEffect, useRef, useMemo } = React;

/* ============================================================
   SAMPLE DATA — realistic content, no lorem
   ============================================================ */
const NOW = new Date("2026-05-12T14:32:00");

const RUNS = [
  {
    id: "R-2026-0184",
    status: "completed",
    briefing:
      "How many colors are required at minimum to color a plane such that any two points at distance 1 receive different colors? Write an accessible but mathematically precise expository note (~700 words) for a general technical audience, with at least one concrete construction (Moser spindle) and a paragraph on the 2018 de Grey result.",
    title: "Chromatic number of the plane — expository note",
    createdAt: "2026-05-12T13:54:00",
    startedAt: "2026-05-12T13:54:08",
    completedAt: "2026-05-12T13:58:42",
    tokens: 18_724,
    cost: 0.142,
    iterations: 3,
    crew: ["Executor", "BriefingFidelityReviewer", "ClarityReviewer"],
    cancelable: false,
  },
  {
    id: "R-2026-0185",
    status: "running",
    activeStage: 1, // 0 executor, 1 reviewers, 2 executor revising
    iterationInProgress: 2,
    briefing:
      "Draft a legal brief (≤ 1200 words, formal register) responding to the opposing party's motion to compel arbitration in Reinhardt v. Vortek Industries. Argue that the arbitration clause is procedurally unconscionable. Cite Armendariz and Sanchez. Plain headings, no rhetorical flourishes.",
    title: "Reinhardt v. Vortek — opposition to motion to compel arbitration",
    createdAt: "2026-05-12T14:28:11",
    startedAt: "2026-05-12T14:28:18",
    completedAt: null,
    tokens: 9_320,
    cost: 0.071,
    iterations: 2,
    crew: ["Executor", "BriefingFidelityReviewer", "ClarityReviewer"],
    cancelable: true,
  },
  {
    id: "R-2026-0183",
    status: "completed",
    briefing:
      "Write a 250-word biographical headnote on Hannah Arendt for inclusion in an undergraduate philosophy reader. Emphasise the trajectory from Königsberg to The Human Condition; avoid hagiography.",
    title: "Hannah Arendt — biographical headnote",
    createdAt: "2026-05-12T11:02:00",
    startedAt: "2026-05-12T11:02:04",
    completedAt: "2026-05-12T11:03:21",
    tokens: 4_182,
    cost: 0.032,
    iterations: 1,
    crew: ["Executor", "BriefingFidelityReviewer", "ClarityReviewer"],
  },
  {
    id: "R-2026-0182",
    status: "failed",
    briefing:
      "Translate the attached German specification (RFC-style) into English, preserving normative language (MUST/SHOULD/MAY). Source: §§ 3.2–3.7.",
    title: "DE → EN translation of internal RFC §§ 3.2–3.7",
    createdAt: "2026-05-12T09:41:00",
    startedAt: "2026-05-12T09:41:09",
    completedAt: "2026-05-12T09:43:55",
    tokens: 6_010,
    cost: 0.048,
    iterations: 3,
    error: "Source attachment missing § 3.5; convergence not reached.",
    crew: ["Executor", "BriefingFidelityReviewer", "ClarityReviewer"],
  },
  {
    id: "R-2026-0181",
    status: "completed",
    briefing:
      "A long-form essay (~1600 words) on the aesthetics of monospaced typography in scientific publishing. Position: TeX is not the ceiling. Audience: editors and book designers.",
    title: "The aesthetics of monospace — essay for editors",
    createdAt: "2026-05-11T22:18:00",
    startedAt: "2026-05-11T22:18:12",
    completedAt: "2026-05-11T22:25:04",
    tokens: 22_140,
    cost: 0.176,
    iterations: 2,
  },
  {
    id: "R-2026-0180",
    status: "aborted",
    briefing:
      "Marketing copy for the relaunch of the Brombeer typeface family. Three lengths: 60 / 160 / 400 words. Voice: dry, knowing, no superlatives.",
    title: "Brombeer relaunch — marketing copy (three lengths)",
    createdAt: "2026-05-11T17:50:00",
    startedAt: "2026-05-11T17:50:08",
    completedAt: "2026-05-11T17:51:02",
    tokens: 1_820,
    cost: 0.014,
    iterations: 1,
  },
  {
    id: "R-2026-0179",
    status: "pending",
    briefing:
      "Translate the attached colophon into Latin (yes, really). 80–110 words.",
    title: "Colophon — DE → Latin",
    createdAt: "2026-05-12T14:31:50",
    iterations: 0,
  },
];

/* ============================================================
   The 'hero' run — full iteration tree (Chromatic number)
   ============================================================ */
const RUN_DETAIL_184 = {
  ...RUNS[0],
  iterations: [
    {
      n: 1,
      summary: "First draft. Solid skeleton — two factual imprecisions flagged.",
      findings: [
        {
          severity: "crit",
          reviewer: "ClarityReviewer",
          msg: "The description of the Moser spindle is factually imprecise: the Moser spindle consists of 7 vertices and 11 edges, not 'seven points' in general. The construction depends on two rhombi sharing an edge — that detail is missing and the proof of χ ≥ 4 collapses without it.",
          resolved: true,
        },
        {
          severity: "maj",
          reviewer: "BriefingFidelityReviewer",
          msg: "Briefing requests a paragraph on the 2018 de Grey result. The draft mentions it in a single sentence; the briefing asks for an explanation of *what* he showed (χ ≥ 5) and *how* (a unit-distance graph with 1581 vertices, since reduced).",
          resolved: true,
        },
        {
          severity: "min",
          reviewer: "ClarityReviewer",
          msg: "Sentence 4, paragraph 2: nested clause makes the parse hard. Consider splitting at 'such that'.",
          resolved: true,
        },
      ],
      artifact: (
        <>
          <h3>The number we seek</h3>
          <p>
            The chromatic number of the plane, written χ(ℝ²), is the smallest number of colors with
            which one can paint every point of the plane such that no two points at unit distance share a color.
          </p>
          <h3>Known bounds</h3>
          <p>For a long time, only the following bounds were known:</p>
          <span className="math">4 ≤ χ(ℝ²) ≤ 7.</span>
          <p>
            The lower bound is attained by exhibiting a finite configuration of points
            (seven points) that requires four colors — the so-called Moser spindle. The upper
            bound is obtained by a hexagonal tiling, where seven colors suffice.
          </p>
          <h3>A 2018 breakthrough</h3>
          <p>
            In 2018 Aubrey de Grey raised the lower bound to 5 by means of a large unit-distance graph.
            This is the first improvement since 1950.
          </p>
        </>
      ),
    },
    {
      n: 2,
      summary: "Findings addressed. Both factual issues fixed. One minor stylistic note remains.",
      findings: [
        {
          severity: "min",
          reviewer: "ClarityReviewer",
          msg: "Section transition between 'Known bounds' and 'A 2018 breakthrough' is abrupt. A bridging sentence about *why* the lower bound resisted improvement for so long would land better with a general technical audience.",
          resolved: true,
        },
        {
          severity: "inf",
          reviewer: "BriefingFidelityReviewer",
          msg: "Word count currently 612. Briefing asked for ~700; consider expanding the construction paragraph by one example.",
          resolved: true,
        },
      ],
      artifact: (
        <>
          <h3>The number we seek</h3>
          <p>
            The chromatic number of the plane, written χ(ℝ²), is the smallest number of colors with
            which one can paint every point of the plane such that no two points at unit distance share a color.
          </p>
          <h3>Known bounds</h3>
          <p>For roughly seven decades, only the following bounds were established:</p>
          <span className="math">4 ≤ χ(ℝ²) ≤ 7.</span>
          <p>
            The lower bound is attained by the <em>Moser spindle</em>, a unit-distance graph on{" "}
            <span className="change-mark">7 vertices and 11 edges</span> built from two rhombi sharing
            a common edge, in which every triangle forces a fresh color. The upper bound is obtained
            by a periodic tiling by hexagons of diameter slightly less than 1, where seven colors suffice.
          </p>
          <h3>A 2018 breakthrough</h3>
          <p>
            In April 2018 Aubrey de Grey <span className="change-mark">raised the lower bound to 5</span> by
            exhibiting a unit-distance graph on 1581 vertices that requires five colors. The construction has
            since been reduced — by collaborative computer search — to a few hundred vertices.
          </p>
        </>
      ),
    },
    {
      n: 3,
      summary: "Final. Convergence reached — all reviewers report no findings of severity ≥ Minor.",
      findings: [
        {
          severity: "inf",
          reviewer: "ClarityReviewer",
          msg: "No further findings. The piece reads well aloud; mathematical typography is consistent. Approved.",
          resolved: false,
        },
      ],
      artifact: null, // final lives in the manuscript section
    },
  ],
};

/* ============================================================
   Static helpers
   ============================================================ */
const STATUS_LABEL = {
  pending: "Pending",
  running: "Running",
  completed: "Completed",
  failed: "Failed",
  aborted: "Aborted",
};

function StatusBadge({ status, large }) {
  return (
    <span className={`status ${status} ${large ? "status-lg" : ""}`}>
      <span className="glyph" />
      <span>{STATUS_LABEL[status]}</span>
    </span>
  );
}

function Severity({ kind }) {
  const map = { crit: "critical", maj: "major", min: "minor", inf: "info" };
  const label = { crit: "Critical", maj: "Major", min: "Minor", inf: "Info" };
  return (
    <span className={`severity ${map[kind]}`}>
      <span className="mark" />
      <span>{label[kind]}</span>
    </span>
  );
}

function timeAgo(iso) {
  if (!iso) return "—";
  const t = new Date(iso);
  const diff = (NOW - t) / 1000;
  if (diff < 60) return `${Math.floor(diff)}s ago`;
  if (diff < 3600) return `${Math.floor(diff / 60)}min ago`;
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
  return `${Math.floor(diff / 86400)}d ago`;
}
function timeShort(iso) {
  if (!iso) return "—";
  const t = new Date(iso);
  return t.toLocaleString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false });
}
function dateShort(iso) {
  if (!iso) return "—";
  const t = new Date(iso);
  return t.toLocaleDateString("en-US", { month: "short", day: "2-digit" });
}

Object.assign(window, {
  RUNS, RUN_DETAIL_184, STATUS_LABEL, StatusBadge, Severity,
  timeAgo, timeShort, dateShort, NOW,
});
