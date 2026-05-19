/* global React,
          WelcomeStrip, Press, Ledger, ActivityCalendar, CrewDNA, CostForge, SweetSpot,
          ManuscriptsGallery, TokenStream, CriticsBench, ProviderBench, KnowledgeBase, DayBook */

function DashboardScreen({ go, scope, isAdmin, onScope }) {
  return (
    <div className="dashboard">
      {/* A. Welcome strip */}
      <WelcomeStrip scope={scope} isAdmin={isAdmin} onScope={onScope} />

      {/* B. The Press */}
      <section className="dash-section">
        <Press scope={scope} go={go} />
      </section>

      {/* C. The Ledger */}
      <section className="dash-section">
        <Ledger scope={scope} />
      </section>

      {/* D. Activity calendar */}
      <section className="dash-section">
        <ActivityCalendar scope={scope} />
      </section>

      {/* E. Workbench / Cost forge / Sweet-spot */}
      <section className="dash-section dash-3col">
        <CrewDNA scope={scope} />
        <CostForge scope={scope} />
        <SweetSpot scope={scope} />
      </section>

      {/* F. Manuscripts gallery + token stream */}
      <section className="dash-section dash-2col-gallery">
        <ManuscriptsGallery scope={scope} go={go} />
        <TokenStream scope={scope} />
      </section>

      {/* G. Critics / Providers / KB */}
      <section className="dash-section dash-3col-bottom">
        <CriticsBench scope={scope} />
        <ProviderBench scope={scope} />
        <KnowledgeBase />
      </section>

      {/* H. The Day Book */}
      <section className="dash-section">
        <DayBook scope={scope} />
      </section>
    </div>
  );
}

window.DashboardScreen = DashboardScreen;
