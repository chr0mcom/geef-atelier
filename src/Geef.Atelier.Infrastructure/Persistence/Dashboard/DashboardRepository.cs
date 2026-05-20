using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geef.Atelier.Infrastructure.Persistence.Dashboard;

internal sealed class DashboardRepository(AtelierDbContext db) : IDashboardRepository
{
    // ── Welcome strip ────────────────────────────────────────────────────────

    public async Task<WelcomeStrip> GetWelcomeStripAsync(
        string username, bool isAdmin, DashboardScope scope, CancellationToken ct)
    {
        var utcNow   = DateTimeOffset.UtcNow;
        var today    = new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);
        var scopedUser = scope == DashboardScope.My ? username : null;

        var q = db.Runs.AsNoTracking().Where(r => r.Status == RunStatus.Completed);
        if (scopedUser is not null) q = q.Where(r => r.CreatedByUser == scopedUser);

        var todayCount = await q.CountAsync(r => r.CompletedAt >= today && r.CompletedAt < tomorrow, ct);
        var total      = await q.CountAsync(ct);
        var totalAll   = await db.Runs.AsNoTracking().CountAsync(r => r.Status == RunStatus.Completed, ct);

        // streak from completed-at dates for this user (always My scope for streak)
        var runDates = await db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed && r.CreatedByUser == username)
            .Select(r => r.CompletedAt!.Value)
            .ToListAsync(ct);
        var dateParts = runDates.Select(d => DateOnly.FromDateTime(d.UtcDateTime)).Distinct();
        var streak = StreakDaysCalculator.Calculate(dateParts, DateOnly.FromDateTime(DateTime.UtcNow));

        var completedCount = await db.Runs.AsNoTracking()
            .CountAsync(r => r.Status == RunStatus.Completed && r.CreatedByUser == username, ct);
        var allUserRuns = await db.Runs.AsNoTracking()
            .CountAsync(r => r.CreatedByUser == username, ct);
        var craftMark = CraftMarkCalculator.Calculate(completedCount, allUserRuns);

        return new WelcomeStrip(username, todayCount, streak, craftMark, total);
    }

    // ── Press status ─────────────────────────────────────────────────────────

    public async Task<PressStatus> GetPressStatusAsync(string? username, CancellationToken ct)
    {
        var activeRuns = await db.Runs.AsNoTracking()
            .Where(r => (r.Status == RunStatus.Pending || r.Status == RunStatus.Running)
                     && (username == null || r.CreatedByUser == username))
            .OrderBy(r => r.CreatedAt)
            .Take(4)
            .Select(r => new { r.Id, r.CrewTemplateName, r.StartedAt, r.CreatedAt, r.ConfigJson })
            .ToListAsync(ct);

        if (activeRuns.Count == 0)
        {
            var lastCompleted = await db.Runs.AsNoTracking()
                .Where(r => (r.Status == RunStatus.Completed || r.Status == RunStatus.Failed)
                         && (username == null || r.CreatedByUser == username))
                .OrderByDescending(r => r.CompletedAt)
                .Select(r => r.CompletedAt)
                .FirstOrDefaultAsync(ct);
            return new PressStatus(PressState.Idle, [], lastCompleted);
        }

        var pressRuns = new List<PressRun>();
        foreach (var run in activeRuns)
        {
            // Determine MaxIterations from ConfigJson, fallback to 3
            var maxIter = 3;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(run.ConfigJson);
                if (doc.RootElement.TryGetProperty("maxIterations", out var mi))
                    maxIter = mi.GetInt32();
            }
            catch { /* ignore invalid JSON */ }

            // Load events for phase derivation
            var events = await db.Set<EventEntity>().AsNoTracking()
                .Where(e => e.RunId == run.Id)
                .OrderBy(e => e.CreatedAt)
                .Select(e => e.EventType)
                .ToListAsync(ct);

            var (phase, iter) = PressPhaseMapper.Map(events);

            pressRuns.Add(new PressRun(
                run.Id,
                run.CrewTemplateName,
                phase,
                iter,
                maxIter,
                run.StartedAt ?? run.CreatedAt));
        }

        var state = pressRuns.Count == 1 ? PressState.Single : PressState.Multi;
        return new PressStatus(state, pressRuns, null);
    }

    // ── Ledger ───────────────────────────────────────────────────────────────

    public async Task<LedgerStats> GetLedgerStatsAsync(string? username, CancellationToken ct)
    {
        var now      = DateTimeOffset.UtcNow;
        var today    = now.Date;
        var weekAgo  = today.AddDays(-7);
        var prevWeek = today.AddDays(-14);
        var monthAgo = today.AddDays(-30);

        var q = db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed
                     && (username == null || r.CreatedByUser == username));

        var all = await q.Select(r => new
        {
            r.CompletedAt,
            TotalCostEur    = r.TotalCostEur,
            CostTotal       = r.CostTotal,
            FinalizerCostEur = r.FinalizerCostEur
        }).ToListAsync(ct);

        static decimal LedgerCost(decimal? totalCostEur, decimal costTotal, decimal? finalizerCostEur)
        {
            var t = totalCostEur ?? (costTotal > 0m ? costTotal : 0m);
            return t + (finalizerCostEur ?? 0m);
        }

        var todayRuns     = all.Where(r => r.CompletedAt >= today).ToList();
        var yesterdayRuns = all.Where(r => r.CompletedAt >= today.AddDays(-1) && r.CompletedAt < today).ToList();
        var weekRuns      = all.Where(r => r.CompletedAt >= weekAgo).ToList();
        var prevWkRuns    = all.Where(r => r.CompletedAt >= prevWeek && r.CompletedAt < weekAgo).ToList();
        var monthRuns     = all.Where(r => r.CompletedAt >= monthAgo).ToList();

        var todayCost  = todayRuns.Sum(r     => LedgerCost(r.TotalCostEur, r.CostTotal, r.FinalizerCostEur));
        var yesCost    = yesterdayRuns.Sum(r  => LedgerCost(r.TotalCostEur, r.CostTotal, r.FinalizerCostEur));
        var weekCost   = weekRuns.Sum(r       => LedgerCost(r.TotalCostEur, r.CostTotal, r.FinalizerCostEur));
        var prevWkCost = prevWkRuns.Sum(r     => LedgerCost(r.TotalCostEur, r.CostTotal, r.FinalizerCostEur));
        var monthCost  = monthRuns.Sum(r      => LedgerCost(r.TotalCostEur, r.CostTotal, r.FinalizerCostEur));
        var allCost    = all.Sum(r            => LedgerCost(r.TotalCostEur, r.CostTotal, r.FinalizerCostEur));

        var todayPct   = TrendFormatter.ComputePct(todayCost, yesCost);
        var weekPct    = TrendFormatter.ComputePct(weekCost, prevWkCost);

        return new LedgerStats(
            Today:     new LedgerTile(todayRuns.Count,  todayCost,  TrendFormatter.GetDirection(todayPct),  todayPct),
            ThisWeek:  new LedgerTile(weekRuns.Count,   weekCost,   TrendFormatter.GetDirection(weekPct),   weekPct),
            ThisMonth: new LedgerTile(monthRuns.Count,  monthCost,  TrendDirection.Flat,                    0m),
            AllTime:   new LedgerTile(all.Count,        allCost,    TrendDirection.Flat,                    0m));
    }

    // ── Activity heatmap ─────────────────────────────────────────────────────

    public async Task<ActivityHeatmap> GetActivityHeatmapAsync(string? username, CancellationToken ct)
    {
        var sinceUtc = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-364), TimeSpan.Zero);
        var todayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var q = db.Runs.AsNoTracking()
            .Where(r => r.CreatedAt >= sinceUtc
                     && (username == null || r.CreatedByUser == username))
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() });

        var raw = await q.ToListAsync(ct);
        var dict = raw.ToDictionary(x => DateOnly.FromDateTime(x.Date), x => x.Count);

        var maxCount = dict.Values.DefaultIfEmpty(0).Max();
        var cells = new List<HeatmapCell>();
        for (var d = sinceUtc; d <= todayUtc; d = d.AddDays(1))
        {
            var date  = DateOnly.FromDateTime(d.UtcDateTime);
            var count = dict.GetValueOrDefault(date, 0);
            var level = maxCount == 0 ? 0 : (int)Math.Round((double)count / maxCount * 4);
            cells.Add(new HeatmapCell(date, count, level));
        }

        var peak = dict.Count == 0
            ? new PeakAttribution(DateOnly.FromDateTime(DateTime.UtcNow.Date), 0)
            : dict.MaxBy(kv => kv.Value) is { } p
                ? new PeakAttribution(p.Key, p.Value)
                : new PeakAttribution(DateOnly.FromDateTime(DateTime.UtcNow.Date), 0);

        return new ActivityHeatmap(cells, peak);
    }

    // ── Crew DNA ─────────────────────────────────────────────────────────────

    public async Task<CrewDna> GetCrewDnaAsync(string? username, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var q = db.Runs.AsNoTracking()
            .Where(r => r.CreatedAt >= since && (username == null || r.CreatedByUser == username))
            .GroupBy(r => r.CrewTemplateName ?? "klassik")
            .Select(g => new { Template = g.Key, Count = g.Count() });

        var raw  = await q.OrderByDescending(x => x.Count).ToListAsync(ct);
        var total = raw.Sum(x => x.Count);
        if (total == 0) total = 1;

        var entries = raw.Select(x =>
        {
            var display = SystemCrew.CrewTemplates.TryGetValue(x.Template, out var t) ? t.DisplayName : x.Template;
            return new CrewDnaEntry(x.Template, display, x.Count, (double)x.Count / total);
        }).ToList();

        return new CrewDna(entries);
    }

    // ── Cost Forge ───────────────────────────────────────────────────────────

    public async Task<CostForge> GetCostForgeAsync(string? username, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        // Iteration actor costs
        var iterCosts = await db.IterationActorCosts.AsNoTracking()
            .Join(db.Set<IterationEntity>().AsNoTracking(), iac => iac.IterationId, it => it.Id, (iac, it) => new { iac, it })
            .Join(db.Runs.AsNoTracking(), x => x.it.RunId, r => r.Id, (x, r) => new { x.iac, r })
            .Where(x => x.r.CreatedAt >= since && (username == null || x.r.CreatedByUser == username))
            .Select(x => new
            {
                Provider  = x.iac.ProviderName ?? "unknown",
                ActorType = x.iac.ActorType,
                Cost      = x.iac.CostEur ?? 0m
            })
            .ToListAsync(ct);

        // Finalization actor costs
        var finCosts = await db.FinalizationActorCosts.AsNoTracking()
            .Join(db.Runs.AsNoTracking(), f => f.RunId, r => r.Id, (f, r) => new { f, r })
            .Where(x => x.r.CreatedAt >= since && (username == null || x.r.CreatedByUser == username))
            .Select(x => new
            {
                Provider  = x.f.ProviderName ?? "unknown",
                ActorRole = "Finalizer",
                Cost      = x.f.CostEur ?? 0m
            })
            .ToListAsync(ct);

        // Grounding actor costs
        var groundingCosts = await db.GroundingActorCosts.AsNoTracking()
            .Join(db.Runs.AsNoTracking(), g => g.RunId, r => r.Id, (g, r) => new { g, r })
            .Where(x => x.r.CreatedAt >= since && (username == null || x.r.CreatedByUser == username))
            .Select(x => new
            {
                Provider  = x.g.ProviderName ?? "unknown",
                ActorRole = "Refiner",
                Cost      = x.g.CostEur ?? 0m
            })
            .ToListAsync(ct);

        var flows = new List<CostFlow>();
        var allCosts = iterCosts
            .Select(x => (
                Provider: x.Provider,
                Role: x.ActorType switch
                {
                    ActorType.Executor  => "Executor",
                    ActorType.Reviewer  => "Reviewer",
                    ActorType.Advisor   => "Advisor",
                    _                   => "Other"
                },
                Cost: x.Cost))
            .Concat(finCosts.Select(x => (Provider: x.Provider, Role: x.ActorRole, Cost: x.Cost)))
            .Concat(groundingCosts.Select(x => (Provider: x.Provider, Role: x.ActorRole, Cost: x.Cost)))
            .ToList();

        var total = allCosts.Sum(x => x.Cost);
        if (total == 0) total = 1m;

        var grouped = allCosts
            .GroupBy(x => (x.Provider, x.Role))
            .Select(g => new CostFlow(g.Key.Provider, g.Key.Role, g.Sum(x => x.Cost), (double)(g.Sum(x => x.Cost) / total)))
            .OrderByDescending(f => f.CostEur)
            .ToList();

        return new CostForge(grouped, allCosts.Sum(x => x.Cost));
    }

    // ── Sweet Spot Histogram ─────────────────────────────────────────────────

    public async Task<SweetSpotHistogram> GetSweetSpotAsync(string? username, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var q = db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed && r.CreatedAt >= since
                     && (username == null || r.CreatedByUser == username));

        var runIds = await q.Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count == 0)
            return new SweetSpotHistogram([], 0, 0);

        var iterCounts = await db.Set<IterationEntity>().AsNoTracking()
            .Where(i => runIds.Contains(i.RunId))
            .GroupBy(i => i.RunId)
            .Select(g => g.Count())
            .ToListAsync(ct);

        var buckets = iterCounts.GroupBy(c => c)
            .OrderBy(g => g.Key)
            .Select(g => new SweetSpotBucket(g.Key, g.Count(), (double)g.Count() / iterCounts.Count))
            .ToList();

        return new SweetSpotHistogram(
            buckets,
            iterCounts.Count > 0 ? iterCounts.Average() : 0,
            iterCounts.Count);
    }

    // ── Manuscripts Gallery ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<ManuscriptCard>> GetManuscriptsAsync(string? username, CancellationToken ct)
    {
        var q = db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed
                     && r.FinalText != null
                     && (username == null || r.CreatedByUser == username))
            .OrderByDescending(r => r.CompletedAt)
            .Take(12);

        var runs = await q.Select(r => new
        {
            r.Id, r.BriefingText, r.CrewTemplateName,
            r.WordCount, r.TotalCostEur, r.CostTotal, r.FinalizerCostEur,
            r.CompletedAt
        }).ToListAsync(ct);

        // Count iterations per run
        var runIdList = runs.Select(r => r.Id).ToList();
        var iterCounts = await db.Set<IterationEntity>().AsNoTracking()
            .Where(i => runIdList.Contains(i.RunId))
            .GroupBy(i => i.RunId)
            .Select(g => new { RunId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RunId, x => x.Count, ct);

        return runs.Select(r =>
        {
            var snippet = r.BriefingText.Length > 80
                ? r.BriefingText[..77] + "…"
                : r.BriefingText;
            var cost = (r.TotalCostEur ?? (r.CostTotal > 0 ? r.CostTotal : 0m)) + (r.FinalizerCostEur ?? 0m);
            return new ManuscriptCard(
                r.Id,
                snippet,
                r.CrewTemplateName,
                r.WordCount ?? 0,
                cost > 0 ? cost : null,
                r.CompletedAt!.Value,
                iterCounts.GetValueOrDefault(r.Id, 1));
        }).ToList();
    }

    // ── Token Stream ─────────────────────────────────────────────────────────

    public async Task<TokenStream> GetTokenStreamAsync(string? username, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        var iterTokens = await db.IterationActorCosts.AsNoTracking()
            .Join(db.Set<IterationEntity>().AsNoTracking(), iac => iac.IterationId, it => it.Id, (iac, it) => new { iac, it })
            .Join(db.Runs.AsNoTracking(), x => x.it.RunId, r => r.Id, (x, r) => new { x.iac, r })
            .Where(x => x.r.CreatedAt >= since && (username == null || x.r.CreatedByUser == username))
            .Select(x => new
            {
                x.iac.ActorType,
                Tokens = (long)(x.iac.InputTokens + x.iac.OutputTokens),
                Date = x.iac.CreatedAt.Date
            })
            .ToListAsync(ct);

        var finTokenRows = await db.FinalizationActorCosts.AsNoTracking()
            .Join(db.Runs.AsNoTracking(), f => f.RunId, r => r.Id, (f, r) => new { f, r })
            .Where(x => x.r.CreatedAt >= since && (username == null || x.r.CreatedByUser == username))
            .Select(x => new
            {
                Tokens = (long)(x.f.InputTokens + x.f.OutputTokens),
                Date = x.f.CreatedAt.Date
            })
            .ToListAsync(ct);

        long Tok(ActorType t) => iterTokens.Where(x => x.ActorType == t).Sum(x => x.Tokens);
        var finalizerTotal = finTokenRows.Sum(x => x.Tokens);

        var sparkline = iterTokens.GroupBy(x => DateOnly.FromDateTime(x.Date))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Tokens));
        foreach (var f in finTokenRows)
        {
            var d = DateOnly.FromDateTime(f.Date);
            sparkline[d] = sparkline.GetValueOrDefault(d, 0L) + f.Tokens;
        }

        var sparklineList = sparkline.OrderBy(kv => kv.Key)
            .Select(kv => new TokenStreamDay(kv.Key, kv.Value))
            .ToList();

        var iterTotal = iterTokens.Sum(x => x.Tokens);
        return new TokenStream(
            TotalTokens:     iterTotal + finalizerTotal,
            ExecutorTokens:  Tok(ActorType.Executor),
            ReviewerTokens:  Tok(ActorType.Reviewer),
            AdvisorTokens:   Tok(ActorType.Advisor),
            FinalizerTokens: finalizerTotal,
            Sparkline:       sparklineList);
    }

    // ── Critics Bench ────────────────────────────────────────────────────────

    public async Task<CriticsBenchMatrix> GetCriticsBenchAsync(string? username, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        var findings = await db.Set<FindingEntity>().AsNoTracking()
            .Join(db.Set<IterationEntity>().AsNoTracking(), f => f.IterationId, i => i.Id, (f, i) => new { f, i })
            .Join(db.Runs.AsNoTracking(), x => x.i.RunId, r => r.Id, (x, r) => new { x.f, r })
            .Where(x => x.r.CreatedAt >= since && (username == null || x.r.CreatedByUser == username))
            .Select(x => new { x.f.ReviewerName, x.f.Severity })
            .ToListAsync(ct);

        var rows = findings
            .GroupBy(f => f.ReviewerName)
            .Select(g =>
            {
                // Minor/Info = non-blocking feedback (pass); Critical/Major = blocker (fail)
                var passes = g.Count(f => f.Severity == FindingSeverity.Minor || f.Severity == FindingSeverity.Info);
                var fails  = g.Count(f => f.Severity == FindingSeverity.Critical || f.Severity == FindingSeverity.Major);
                var total   = passes + fails;
                var display = SystemCrew.ReviewerProfiles.TryGetValue(g.Key, out var rp)
                    ? rp.DisplayName : g.Key;
                var model   = SystemCrew.ReviewerProfiles.TryGetValue(g.Key, out var rp2)
                    ? rp2.Model : null;
                return new CriticsRow(g.Key, display, passes, fails,
                    total > 0 ? (double)passes / total : 0.0, model);
            })
            .OrderByDescending(r => r.PassRate)
            .ToList();

        return new CriticsBenchMatrix(rows);
    }

    // ── Provider Bench ───────────────────────────────────────────────────────

    public async Task<ProviderBench> GetProviderBenchAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        var iterRows = await db.IterationActorCosts.AsNoTracking()
            .Where(x => x.CreatedAt >= since && x.ProviderName != null)
            .GroupBy(x => x.ProviderName!)
            .Select(g => new
            {
                Provider     = g.Key,
                Requests     = g.Count(),
                Cost         = g.Sum(x => x.CostEur ?? 0m),
                InputTokens  = (long)g.Sum(x => (long)x.InputTokens),
                OutputTokens = (long)g.Sum(x => (long)x.OutputTokens)
            })
            .ToListAsync(ct);

        var finRows = await db.FinalizationActorCosts.AsNoTracking()
            .Where(x => x.CreatedAt >= since && x.ProviderName != null)
            .GroupBy(x => x.ProviderName!)
            .Select(g => new
            {
                Provider     = g.Key,
                Requests     = g.Count(),
                Cost         = g.Sum(x => x.CostEur ?? 0m),
                InputTokens  = (long)g.Sum(x => (long)x.InputTokens),
                OutputTokens = (long)g.Sum(x => (long)x.OutputTokens)
            })
            .ToListAsync(ct);

        var groundingRows = await db.GroundingActorCosts.AsNoTracking()
            .Where(x => x.CreatedAt >= since && x.ProviderName != null)
            .GroupBy(x => x.ProviderName!)
            .Select(g => new
            {
                Provider     = g.Key,
                Requests     = g.Count(),
                Cost         = g.Sum(x => x.CostEur ?? 0m),
                InputTokens  = (long)g.Sum(x => (long)x.InputTokens),
                OutputTokens = (long)g.Sum(x => (long)x.OutputTokens)
            })
            .ToListAsync(ct);

        var combined = iterRows
            .Concat(finRows.Select(x => new { x.Provider, x.Requests, x.Cost, x.InputTokens, x.OutputTokens }))
            .Concat(groundingRows.Select(x => new { x.Provider, x.Requests, x.Cost, x.InputTokens, x.OutputTokens }))
            .GroupBy(x => x.Provider)
            .Select(g =>
            {
                var display = g.Key; // Use provider name as display until SystemCrew lookup
                return new ProviderRow(
                    g.Key, display, ProviderState.Active,
                    g.Sum(x => x.Requests),
                    g.Sum(x => x.Cost),
                    g.Sum(x => x.InputTokens),
                    g.Sum(x => x.OutputTokens));
            })
            .OrderByDescending(r => r.RequestCount)
            .ToList();

        return new ProviderBench(combined);
    }

    // ── Knowledge Base ───────────────────────────────────────────────────────

    public async Task<KnowledgeBaseStats> GetKnowledgeBaseStatsAsync(CancellationToken ct)
    {
        var docs = await db.KnowledgeDocuments.AsNoTracking()
            .Select(d => new { d.Id, d.OriginalFilename, d.ChunkCount, d.FileSizeBytes })
            .ToListAsync(ct);

        var topFiles = docs
            .OrderByDescending(d => d.ChunkCount)
            .Take(5)
            .Select(d => new KbFileRef(d.Id, d.OriginalFilename, d.ChunkCount, null))
            .ToList();

        return new KnowledgeBaseStats(
            docs.Count,
            docs.Sum(d => d.ChunkCount),
            docs.Sum(d => d.FileSizeBytes),
            topFiles);
    }

    // ── Day Book ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DayBookEntry>> GetDayBookAsync(
        string? username, bool isAdmin, DashboardScope scope, CancellationToken ct)
    {
        var scopedUser = scope == DashboardScope.My ? username : null;
        var entries    = new List<DayBookEntry>();

        // Completed runs
        var completedQ = db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Completed
                     && (scopedUser == null || r.CreatedByUser == scopedUser));
        var completed = await completedQ
            .OrderByDescending(r => r.CompletedAt)
            .Take(5)
            .Select(r => new { r.Id, r.BriefingText, r.CompletedAt })
            .ToListAsync(ct);
        entries.AddRange(completed.Select(r => new DayBookEntry(
            DayBookKind.RunCompleted,
            DayBookFormatter.GetVerb(DayBookKind.RunCompleted),
            r.BriefingText.Length > 40 ? r.BriefingText[..37] + "…" : r.BriefingText,
            null,
            r.CompletedAt!.Value)));

        // Failed / aborted runs
        var failedQ = db.Runs.AsNoTracking()
            .Where(r => (r.Status == RunStatus.Failed || r.Status == RunStatus.Aborted)
                     && (scopedUser == null || r.CreatedByUser == scopedUser));
        var failed = await failedQ
            .OrderByDescending(r => r.CompletedAt)
            .Take(3)
            .Select(r => new { r.BriefingText, r.CompletedAt })
            .ToListAsync(ct);
        entries.AddRange(failed.Select(r => new DayBookEntry(
            DayBookKind.RunFailed,
            DayBookFormatter.GetVerb(DayBookKind.RunFailed),
            r.BriefingText.Length > 40 ? r.BriefingText[..37] + "…" : r.BriefingText,
            null,
            r.CompletedAt ?? DateTimeOffset.UtcNow)));

        // Indexed knowledge documents (scope-independent)
        var docs = await db.KnowledgeDocuments.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Take(3)
            .Select(d => new { d.OriginalFilename, d.CreatedAt })
            .ToListAsync(ct);
        entries.AddRange(docs.Select(d => new DayBookEntry(
            DayBookKind.DocumentIndexed,
            DayBookFormatter.GetVerb(DayBookKind.DocumentIndexed),
            d.OriginalFilename,
            null,
            d.CreatedAt)));

        // Materialized templates (studio analyses)
        var templates = await db.TemplateStudioAnalyses.AsNoTracking()
            .Where(a => a.MaterializedTemplateName != null)
            .OrderByDescending(a => a.CreatedAt)
            .Take(2)
            .Select(a => new { a.MaterializedTemplateName, a.CreatedAt })
            .ToListAsync(ct);
        entries.AddRange(templates.Select(a => new DayBookEntry(
            DayBookKind.TemplateCreated,
            DayBookFormatter.GetVerb(DayBookKind.TemplateCreated),
            a.MaterializedTemplateName!,
            null,
            a.CreatedAt)));

        return entries
            .OrderByDescending(e => e.At)
            .Take(12)
            .ToList();
    }
}
