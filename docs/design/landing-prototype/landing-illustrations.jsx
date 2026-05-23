/* global React */

// ============================================================
// GEEF.ATELIER — Landing illustrations (line-art, hairline)
// ============================================================

const Hair = ({ children, viewBox = "0 0 24 24", w = 24, h = 24, strokeWidth = 1.2, ...rest }) => (
  <svg
    width={w}
    height={h}
    viewBox={viewBox}
    fill="none"
    stroke="currentColor"
    strokeWidth={strokeWidth}
    strokeLinecap="round"
    strokeLinejoin="round"
    {...rest}
  >
    {children}
  </svg>
);

// ---- Phase icons (hairline, 96x72) ----
const PhaseGrounding = () => (
  <Hair viewBox="0 0 96 72" w="100%" h="100%" strokeWidth="1.1">
    {/* Stack of three offset pages — sources gathered */}
    <g>
      <rect x="14" y="20" width="36" height="44" rx="1" transform="rotate(-6 32 42)" />
      <rect x="20" y="16" width="36" height="44" rx="1" transform="rotate(3 38 38)" />
      <rect x="26" y="14" width="36" height="44" rx="1" />
      {/* sourced lines on top page */}
      <path d="M30 22h28 M30 28h24 M30 34h26 M30 40h18" opacity="0.55" />
    </g>
    {/* Magnifying glass */}
    <g transform="translate(56 6)">
      <circle cx="14" cy="14" r="10" />
      <path d="M22 22l8 8" />
      <path d="M14 9v10 M9 14h10" opacity="0.55" />
    </g>
  </Hair>
);

const PhaseExecution = () => (
  <Hair viewBox="0 0 96 72" w="100%" h="100%" strokeWidth="1.1">
    {/* Quill writing on a page */}
    <rect x="14" y="14" width="50" height="50" rx="1" />
    {/* written lines */}
    <path d="M22 24h34 M22 32h28 M22 40h32 M22 48l14 0" opacity="0.55" />
    {/* fresh, in-progress stroke at the bottom */}
    <path d="M22 56h16" stroke="var(--hero)" />
    {/* quill */}
    <g transform="translate(46 -6) rotate(28 36 40)">
      <path d="M30 38l30 -24" />
      <path d="M30 38l5 -2 l-2 5 z" />
      <path d="M58 16l5 -3" />
    </g>
  </Hair>
);

const PhaseEvaluation = () => (
  <Hair viewBox="0 0 96 72" w="100%" h="100%" strokeWidth="1.1">
    {/* Page with margin notes */}
    <rect x="14" y="12" width="42" height="50" rx="1" />
    <path d="M22 22h26 M22 28h22 M22 34h24 M22 40h18 M22 46h22" opacity="0.55" />
    {/* Three critic figures stacked along the right margin */}
    <g transform="translate(62 14)">
      <circle cx="10" cy="6" r="4" />
      <path d="M3 18c1.5 -4 4.5 -6 7 -6s5.5 2 7 6" />
    </g>
    <g transform="translate(62 32)">
      <circle cx="10" cy="6" r="4" />
      <path d="M3 18c1.5 -4 4.5 -6 7 -6s5.5 2 7 6" />
    </g>
    <g transform="translate(62 50)" opacity="0.6">
      <circle cx="10" cy="6" r="4" />
      <path d="M3 18c1.5 -4 4.5 -6 7 -6s5.5 2 7 6" />
    </g>
    {/* severity marks in the margin */}
    <g stroke="none" fill="currentColor">
      <polygon points="9,22 14,30 4,30" />
      <polygon points="9,38 14,42 9,46 4,42" />
      <circle cx="9" cy="52" r="2.5" />
    </g>
  </Hair>
);

const PhaseFinalize = () => (
  <Hair viewBox="0 0 96 72" w="100%" h="100%" strokeWidth="1.1">
    {/* Sealed manuscript */}
    <rect x="18" y="10" width="44" height="54" rx="1" />
    <path d="M26 18h28 M26 24h22 M26 30h26 M26 36h20 M26 42h24" opacity="0.55" />
    {/* wax seal */}
    <circle cx="68" cy="44" r="11" stroke="var(--hero)" />
    <circle cx="68" cy="44" r="7" stroke="var(--hero)" strokeDasharray="2 2" />
    <path d="M65 41l3 6 4 -8" stroke="var(--hero)" />
    {/* corner fold */}
    <path d="M52 10l10 10h-10z" opacity="0.4" />
  </Hair>
);

// ---- Capability icons (36x36) ----
const CapSources = () => (
  <Hair viewBox="0 0 36 36" strokeWidth="1.1" w="100%" h="100%">
    <circle cx="18" cy="18" r="11" />
    <path d="M7 18h22 M18 7c4 4 4 18 0 22 M18 7c-4 4 -4 18 0 22" opacity="0.7" />
  </Hair>
);

const CapFilter = () => (
  <Hair viewBox="0 0 36 36" strokeWidth="1.1" w="100%" h="100%">
    <path d="M6 8h24l-9 12v8l-6 -2v-6z" />
    <circle cx="28" cy="11" r="2" fill="currentColor" stroke="none" />
  </Hair>
);

const CapModels = () => (
  <Hair viewBox="0 0 36 36" strokeWidth="1.1" w="100%" h="100%">
    {/* three concentric circles, off-centre */}
    <circle cx="13" cy="14" r="6" />
    <circle cx="22" cy="19" r="6" />
    <circle cx="16" cy="24" r="6" />
  </Hair>
);

const CapExport = () => (
  <Hair viewBox="0 0 36 36" strokeWidth="1.1" w="100%" h="100%">
    <rect x="9" y="6" width="18" height="24" rx="1" />
    <path d="M14 14h8 M14 19h8 M14 24h6" opacity="0.7" />
    <path d="M22 4l4 4 M26 4l-4 4" />
  </Hair>
);

const CapLearn = () => (
  <Hair viewBox="0 0 36 36" strokeWidth="1.1" w="100%" h="100%">
    {/* spiral */}
    <path d="M18 6a12 12 0 1 1 -12 12 a9 9 0 0 1 18 0 a6 6 0 0 1 -12 0 a3 3 0 0 1 6 0" />
  </Hair>
);

const CapTrust = () => (
  <Hair viewBox="0 0 36 36" strokeWidth="1.1" w="100%" h="100%">
    {/* shield with checkmark */}
    <path d="M18 4l12 4v8c0 8 -5 14 -12 16c-7 -2 -12 -8 -12 -16V8z" />
    <path d="M13 18l4 4l7 -8" />
  </Hair>
);

// ---- Hero printing press ----
const HeroPress = () => (
  <svg
    className="press-anim"
    viewBox="0 0 560 620"
    width="100%"
    height="100%"
    fill="none"
    stroke="var(--ink-2)"
    strokeWidth="1.2"
    strokeLinecap="round"
    strokeLinejoin="round"
  >
    {/* baseplate */}
    <line x1="40" y1="540" x2="520" y2="540" stroke="var(--hairline-strong)" />
    {/* sepia gold ambient halo behind the press */}
    <ellipse cx="280" cy="300" rx="220" ry="200" fill="var(--hero-soft)" stroke="none" />

    {/* incoming briefing (paper feeding into the press from above) */}
    <g className="paper-in" transform="translate(218 70)">
      <rect x="0" y="0" width="124" height="76" rx="1" fill="var(--paper)" stroke="var(--paper-rule)" />
      <line x1="14" y1="18" x2="100" y2="18" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <line x1="14" y1="28" x2="86"  y2="28" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <line x1="14" y1="38" x2="92"  y2="38" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <line x1="14" y1="48" x2="72"  y2="48" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <text x="62" y="68" fontFamily="JetBrains Mono" fontSize="7" letterSpacing="0.16em" textAnchor="middle" fill="var(--paper-ink-2)" stroke="none">BRIEFING</text>
    </g>

    {/* uprights */}
    <line x1="100" y1="170" x2="100" y2="540" />
    <line x1="460" y1="170" x2="460" y2="540" />

    {/* top crossbar with wordmark */}
    <line x1="80" y1="170" x2="480" y2="170" />
    <line x1="80" y1="180" x2="480" y2="180" stroke="var(--hairline)" />
    <text x="280" y="158" fontFamily="Newsreader" fontStyle="italic" fontSize="18" textAnchor="middle" fill="var(--ink-2)" stroke="none" letterSpacing="0.05em">geef · atelier</text>

    {/* the platen — moves up and down */}
    <g className="platen" transform="translate(0 0)">
      <rect x="120" y="210" width="320" height="78" rx="2" stroke="var(--ink-2)" fill="var(--surface-2)" />
      <line x1="140" y1="226" x2="420" y2="226" stroke="var(--hairline)" />
      <line x1="140" y1="240" x2="420" y2="240" stroke="var(--hairline)" />
      <line x1="140" y1="254" x2="420" y2="254" stroke="var(--hairline)" />
      <line x1="140" y1="268" x2="420" y2="268" stroke="var(--hairline)" />
      {/* press lever */}
      <line x1="440" y1="246" x2="510" y2="200" />
      <circle cx="510" cy="200" r="6" fill="var(--surface)" />
      <circle cx="510" cy="200" r="2" fill="var(--hero)" stroke="none" />
      {/* glow underneath when pressing */}
      <rect className="stamp-glow" x="160" y="288" width="240" height="10" fill="var(--hero)" stroke="none" opacity="0" />
    </g>

    {/* the bed (paper being pressed) */}
    <rect x="140" y="300" width="280" height="160" rx="1" fill="var(--paper)" stroke="var(--paper-rule)" />
    {/* type laid out on the bed */}
    <g stroke="none" fill="var(--paper-ink-2)" opacity="0.7" fontFamily="Newsreader" fontSize="9">
      <text x="160" y="324" fontFamily="JetBrains Mono" fontSize="7" letterSpacing="0.22em" fill="var(--paper-ink-2)">RUN · ITERATION 04</text>
      <text x="160" y="346" fontStyle="italic" fontSize="14" fill="var(--paper-ink)">A treatise on patient craft</text>
    </g>
    <g stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.5">
      <line x1="160" y1="360" x2="400" y2="360" />
      <line x1="160" y1="372" x2="376" y2="372" />
      <line x1="160" y1="384" x2="396" y2="384" />
      <line x1="160" y1="396" x2="352" y2="396" />
      <line x1="160" y1="408" x2="388" y2="408" />
      <line x1="160" y1="420" x2="328" y2="420" />
      <line x1="160" y1="432" x2="384" y2="432" />
      <line x1="160" y1="444" x2="304" y2="444" />
    </g>

    {/* ink rollers (twin cylinders to the side) */}
    <g transform="translate(60 280)">
      <line x1="0" y1="0" x2="0" y2="160" />
      <line x1="20" y1="0" x2="20" y2="160" />
      <ellipse cx="10" cy="20" rx="14" ry="6" stroke="var(--ink-2)" fill="var(--surface-2)" />
      <ellipse cx="10" cy="80" rx="14" ry="6" stroke="var(--ink-2)" fill="var(--surface-2)" />
      <ellipse cx="10" cy="140" rx="14" ry="6" stroke="var(--ink-2)" fill="var(--surface-2)" />
    </g>

    {/* base bench */}
    <line x1="60" y1="480" x2="500" y2="480" />
    <line x1="60" y1="520" x2="500" y2="520" />
    <line x1="80" y1="480" x2="80" y2="520" />
    <line x1="280" y1="480" x2="280" y2="520" />
    <line x1="480" y1="480" x2="480" y2="520" />

    {/* output sheet ribbon (the manuscript exiting) */}
    <g className="paper-out" transform="translate(370 470)">
      <rect x="0" y="0" width="100" height="64" rx="1" fill="var(--paper)" stroke="var(--paper-rule)" />
      <line x1="10" y1="12" x2="86" y2="12" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <line x1="10" y1="20" x2="72" y2="20" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <line x1="10" y1="28" x2="80" y2="28" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      <line x1="10" y1="36" x2="60" y2="36" stroke="var(--paper-ink-2)" strokeWidth="0.8" opacity="0.7" />
      {/* wax seal on output */}
      <circle cx="78" cy="48" r="8" fill="var(--hero)" stroke="none" />
      <circle cx="78" cy="48" r="5" fill="none" stroke="var(--on-hero)" strokeDasharray="1 2" />
    </g>

    {/* phase labels under the press, hairline */}
    <g fontFamily="JetBrains Mono" fontSize="8" letterSpacing="0.22em" textAnchor="middle" fill="var(--ink-3)" stroke="none">
      <text x="100" y="556">G</text>
      <text x="220" y="556">E</text>
      <text x="340" y="556">E</text>
      <text x="460" y="556">F</text>
      <line x1="120" y1="552" x2="200" y2="552" stroke="var(--hairline)" />
      <line x1="240" y1="552" x2="320" y2="552" stroke="var(--hairline)" />
      <line x1="360" y1="552" x2="440" y2="552" stroke="var(--hairline)" />
    </g>
  </svg>
);

// ---- The iteration loop between Execution & Evaluation ----
// Single SVG sized close to the loop container's aspect so the
// preserveAspectRatio="none" stretch is near-uniform — keeps the
// arrow tips clean. Arrows are compact and pushed to the top and
// bottom edges so the centred label has clear room between them.
const IterationLoop = () => (
  <svg
    viewBox="0 0 400 200"
    preserveAspectRatio="none"
    overflow="visible"
  >
    <defs>
      <marker
        id="arrow-r"
        viewBox="0 0 10 10"
        refX="8" refY="5"
        markerWidth="7" markerHeight="7"
        orient="auto"
      >
        <path d="M0,0 L10,5 L0,10 z" fill="var(--hero)" />
      </marker>
      <marker
        id="arrow-l"
        viewBox="0 0 10 10"
        refX="8" refY="5"
        markerWidth="7" markerHeight="7"
        orient="auto"
      >
        <path d="M0,0 L10,5 L0,10 z" fill="var(--hero)" />
      </marker>
    </defs>
    {/* TOP — Execution → Evaluation (draft to crew, left → right) */}
    <path
      className="loop-path"
      d="M 6 65 C 80 8, 320 8, 394 65"
      markerEnd="url(#arrow-r)"
    />
    {/* BOTTOM — Evaluation → Execution (findings back, right → left) */}
    <path
      className="loop-path"
      d="M 394 135 C 320 192, 80 192, 6 135"
      markerEnd="url(#arrow-l)"
    />
  </svg>
);

// expose
Object.assign(window, {
  Hair,
  PhaseGrounding, PhaseExecution, PhaseEvaluation, PhaseFinalize,
  CapSources, CapFilter, CapModels, CapExport, CapLearn, CapTrust,
  HeroPress, IterationLoop,
});
