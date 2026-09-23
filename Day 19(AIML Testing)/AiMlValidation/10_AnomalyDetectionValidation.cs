using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HisabDo.AI.Day12.AnomalyDetection;

namespace HisabDo.AI.Validation;

internal sealed class InMemoryExpenseRepo : IExpenseTransactionRepository
{
    private readonly List<ExpenseTransaction> _all;
    public InMemoryExpenseRepo(List<ExpenseTransaction> all) => _all = all;

    public Task<IReadOnlyList<ExpenseTransaction>> GetExpensesAsync(
        string userId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var result = _all.Where(t => t.UserId == userId && t.Date >= startUtc && t.Date < endUtc).ToList();
        return Task.FromResult<IReadOnlyList<ExpenseTransaction>>(result);
    }
}

public static class AnomalyDetectionValidation
{
    private const string User = "user-anomaly-test";
    private static readonly DateOnly Start = new(2026, 9, 1);
    private static readonly DateOnly End = new(2026, 9, 30);

    private static ExpenseTransaction Tx(string id, decimal amount, string? category, DateTime date, string? desc = null) =>
        new(User, id, TransactionType.Expense, amount, category, date, desc);

    private static async Task<SpendingAnomalyResult> Run(List<ExpenseTransaction> txns)
    {
        var service = new SpendingAnomalyDetectionService(new InMemoryExpenseRepo(txns));
        return await service.DetectAsync(User, Start, End);
    }

    public static async Task RunAll()
    {
        Report.Suite("ANOMALY DETECTION — SpendingAnomalyDetectionService (Day 12)");
        await UnusualAmountBoundaryTests();
        await DuplicateDetectionTests();
        await CategorySpikeTests_IncludingKnownBug();
        await InsufficientDataAndZeroValueTests();
        await LabeledAccuracyRun();
    }

    private static async Task UnusualAmountBoundaryTests()
    {
        Report.Info("--- Unusual Amount boundary tests (threshold = max(avg*2, avg+2*sd)) ---");

        List<ExpenseTransaction> Baseline() => new()
        {
            Tx("h1", 800, "Food", new DateTime(2026, 6, 15)),
            Tx("h2", 800, "Food", new DateTime(2026, 7, 15)),
            Tx("h3", 800, "Food", new DateTime(2026, 8, 15)),
        };

        var far = Baseline(); far.Add(Tx("c1", 9000, "Food", new DateTime(2026, 9, 10)));
        var r1 = await Run(far);
        Report.Check("A1: far outlier (9000 vs baseline 800) is flagged UnusualAmount",
            r1.Anomalies.Any(a => a.Type == AnomalyType.UnusualAmount && a.TransactionId == "c1"),
            $"anomalies={string.Join(",", r1.Anomalies.Select(a => a.Type))}");

        var normal = Baseline(); normal.Add(Tx("c2", 820, "Food", new DateTime(2026, 9, 10)));
        var r2 = await Run(normal);
        Report.Check("A2: normal amount (820 close to baseline 800) is NOT flagged",
            !r2.Anomalies.Any(a => a.TransactionId == "c2"));

        var justAbove = Baseline(); justAbove.Add(Tx("c3", 1601, "Food", new DateTime(2026, 9, 10)));
        var r3 = await Run(justAbove);
        Report.Check("A3: exactly-above-threshold amount (1601 > 1600) IS flagged",
            r3.Anomalies.Any(a => a.TransactionId == "c3"));

        var justBelow = Baseline(); justBelow.Add(Tx("c4", 1599, "Food", new DateTime(2026, 9, 10)));
        var r4 = await Run(justBelow);
        Report.Check("A4: just-below-threshold amount (1599 < 1600) is NOT flagged",
            !r4.Anomalies.Any(a => a.TransactionId == "c4"));

        var overallFallback = new List<ExpenseTransaction>
        {
            Tx("h4", 500, "Transport", new DateTime(2026, 6, 5)),
            Tx("h5", 520, "Transport", new DateTime(2026, 7, 5)),
            Tx("h6", 300, "Bills", new DateTime(2026, 8, 5)),
            Tx("h7", 310, "Bills", new DateTime(2026, 8, 20)),
            Tx("c5", 5000, "Food", new DateTime(2026, 9, 10)),
        };
        var r5 = await Run(overallFallback);
        Report.Check("A5: brand-new category with no baseline of its own uses the overall historical baseline, and a large amount is still flagged",
            r5.Anomalies.Any(a => a.TransactionId == "c5"),
            $"anomalies={string.Join(",", r5.Anomalies.Select(a => a.TransactionId))}");

        var thinBaseline = new List<ExpenseTransaction>
        {
            Tx("h8", 800, "Food", new DateTime(2026, 8, 15)),
            Tx("c6", 9000, "Food", new DateTime(2026, 9, 10)),
        };
        var r6 = await Run(thinBaseline);
        Report.Check("A6: insufficient category history (<2 data points) correctly skips the check rather than guessing",
            !r6.Anomalies.Any(a => a.TransactionId == "c6"),
            "with only 1 historical point the engine has no statistical basis to flag — verified it does not invent one");
    }

    private static async Task DuplicateDetectionTests()
    {
        Report.Info("--- Duplicate Transaction tests ---");

        var trueDupe = new List<ExpenseTransaction>
        {
            Tx("d1", 1200, "Shopping", new DateTime(2026, 9, 20), "Shoes - City Mall"),
            Tx("d2", 1200, "Shopping", new DateTime(2026, 9, 20), "Shoes - City Mall"),
        };
        var r1 = await Run(trueDupe);
        Report.Check("D1: identical amount+category+description, same day -> flagged as duplicate candidate",
            r1.Anomalies.Any(a => a.Type == AnomalyType.DuplicateTransaction));

        var differentAmount = new List<ExpenseTransaction>
        {
            Tx("d3", 1200, "Shopping", new DateTime(2026, 9, 20), "Shoes"),
            Tx("d4", 1500, "Shopping", new DateTime(2026, 9, 20), "Jacket"),
        };
        var r2 = await Run(differentAmount);
        Report.Check("D2: same day, different amounts -> NOT flagged as duplicate",
            !r2.Anomalies.Any(a => a.Type == AnomalyType.DuplicateTransaction));

        var noDescription = new List<ExpenseTransaction>
        {
            Tx("d5", 500, "Food", new DateTime(2026, 9, 12)),
            Tx("d6", 500, "Food", new DateTime(2026, 9, 12)),
        };
        var r3 = await Run(noDescription);
        var flaggedWithNoDescription = r3.Anomalies.Any(a => a.Type == AnomalyType.DuplicateTransaction);
        if (flaggedWithNoDescription)
            Report.Warn("D3: two same-day, same-amount, same-category transactions with NO description",
                "both got flagged as a duplicate candidate. CategoriesMatch/DescriptionsMatch both treat a blank field as \"matches anything\", " +
                "so two coincidentally-identical but genuinely separate purchases (e.g. two Rs 500 lunches) will always be flagged when no description is recorded. " +
                "Not necessarily wrong for an MVP (better to over-flag and let the user dismiss it), but worth confirming this is the intended behavior.");
        else
            Report.Check("D3: same day/amount/category with no description is NOT flagged", true);
    }

    private static async Task CategorySpikeTests_IncludingKnownBug()
    {
        Report.Info("--- Category Spike tests (period total vs. historical DAILY average) ---");

        var realSpike = new List<ExpenseTransaction>
        {
            Tx("h1", 300, "Transport", new DateTime(2026, 6, 10)),
            Tx("h2", 300, "Transport", new DateTime(2026, 7, 10)),
            Tx("h3", 300, "Transport", new DateTime(2026, 8, 10)),
            Tx("c1", 2500, "Transport", new DateTime(2026, 9, 10)),
        };
        var r1 = await Run(realSpike);
        Report.Check("S1: genuine spike (300 -> 2500, one purchase/month both periods) IS flagged High",
            r1.PeriodSpikes.Any(s => s.Category == "Transport" && s.Severity == AnomalySeverity.High),
            $"spikes={string.Join(",", r1.PeriodSpikes.Select(s => $"{s.Category}:{s.GrowthPercent}%"))}");

        var stable = new List<ExpenseTransaction>
        {
            Tx("h4", 4500, "Bills", new DateTime(2026, 6, 5)),
            Tx("h5", 4500, "Bills", new DateTime(2026, 7, 5)),
            Tx("h6", 4500, "Bills", new DateTime(2026, 8, 5)),
            Tx("c2", 4600, "Bills", new DateTime(2026, 9, 5)),
        };
        var r2 = await Run(stable);
        Report.Check("S2: stable spending (+2.2%) is correctly NOT flagged as a spike",
            !r2.PeriodSpikes.Any(s => s.Category == "Bills"));

        var multiTxnPerMonth = new List<ExpenseTransaction>
        {
            Tx("h7", 800, "Food", new DateTime(2026, 6, 15)),
            Tx("h8", 850, "Food", new DateTime(2026, 7, 15)),
            Tx("h9", 900, "Food", new DateTime(2026, 8, 15)),
            Tx("c3", 850, "Food", new DateTime(2026, 9, 5)),
            Tx("c4", 850, "Food", new DateTime(2026, 9, 20)),
        };
        var r3 = await Run(multiTxnPerMonth);
        var foodSpike = r3.PeriodSpikes.FirstOrDefault(s => s.Category == "Food");
        if (foodSpike != null)
        {
            Report.Warn("S3: KNOWN ISSUE re-confirmed — normal per-purchase spending (2 x Rs 850, same as historical per-purchase amounts) is WRONGLY flagged as a spike",
                $"growth reported as {foodSpike.GrowthPercent:0.0}% and severity {foodSpike.Severity}; historical average used was Rs {foodSpike.HistoricalAverage:0} " +
                "(a per-DAY average) compared against this month's Rs 1,700 TOTAL — see validation report Finding #1.");
        }
        else
        {
            Report.Check("S3: multi-transaction-per-month normal spending is correctly NOT flagged", true);
        }
    }

    private static async Task InsufficientDataAndZeroValueTests()
    {
        Report.Info("--- Insufficient data / zero & negative value tests ---");

        var noHistoryAtAll = new List<ExpenseTransaction> { Tx("c1", 9000, "Food", new DateTime(2026, 9, 10)) };
        var r1 = await Run(noHistoryAtAll);
        Report.Check("Z1: zero historical data at all -> no crash, no invented anomaly",
            r1.Anomalies.Count == 0 && r1.PeriodSpikes.Count == 0,
            $"got {r1.Anomalies.Count} anomalies, {r1.PeriodSpikes.Count} spikes — engine must not guess with zero baseline");

        var noCurrentData = new List<ExpenseTransaction>
        {
            Tx("h1", 800, "Food", new DateTime(2026, 6, 15)),
            Tx("h2", 800, "Food", new DateTime(2026, 7, 15)),
            Tx("h3", 800, "Food", new DateTime(2026, 8, 15)),
        };
        var r2 = await Run(noCurrentData);
        Report.Check("Z2: zero current-period transactions -> empty, clean result",
            r2.Anomalies.Count == 0 && r2.PeriodSpikes.Count == 0);

        var zeroAndNegative = new List<ExpenseTransaction>
        {
            Tx("h1", 800, "Food", new DateTime(2026, 6, 15)),
            Tx("h2", 800, "Food", new DateTime(2026, 7, 15)),
            Tx("z1", 0, "Food", new DateTime(2026, 9, 5)),
            Tx("z2", -500, "Food", new DateTime(2026, 9, 6)),
            Tx("c1", 820, "Food", new DateTime(2026, 9, 10)),
        };
        var r3 = await Run(zeroAndNegative);
        Report.Check("Z3: zero and negative-amount transactions are silently excluded, not treated as anomalies or baseline data",
            !r3.Anomalies.Any(a => a.TransactionId is "z1" or "z2"));
    }

    private static async Task LabeledAccuracyRun()
    {
        Report.Info("--- Labeled accuracy run (ground truth vs. detected, at the case level) ---");
        var matrix = new ConfusionMatrix();

        async Task Case(string name, bool expectAnomaly, List<ExpenseTransaction> txns)
        {
            var result = await Run(txns);
            var detected = result.Anomalies.Count > 0;
            matrix.Record(expectAnomaly, detected);
            Report.Info($"  {(detected == expectAnomaly ? "OK" : "MISMATCH")} [{name}] expected={expectAnomaly} detected={detected}");
        }

        List<ExpenseTransaction> StableHistory(string cat, decimal amt) => new()
        {
            Tx("h1", amt, cat, new DateTime(2026, 6, 15)),
            Tx("h2", amt, cat, new DateTime(2026, 7, 15)),
            Tx("h3", amt, cat, new DateTime(2026, 8, 15)),
        };

        var p1 = StableHistory("Food", 800); p1.Add(Tx("c", 9000, "Food", new DateTime(2026, 9, 10)));
        await Case("P1 far outlier", true, p1);

        var p2 = new List<ExpenseTransaction> {
            Tx("d1", 1000, "Entertainment", new DateTime(2026, 9, 8)),
            Tx("d2", 1000, "Entertainment", new DateTime(2026, 9, 8)),
        };
        await Case("P2 duplicate", true, p2);

        var p3 = StableHistory("Transport", 300); p3.Add(Tx("c", 2500, "Transport", new DateTime(2026, 9, 10)));
        await Case("P3 genuine spike", true, p3);

        var n1 = StableHistory("Food", 800); n1.Add(Tx("c", 810, "Food", new DateTime(2026, 9, 10)));
        await Case("N1 normal amount", false, n1);

        var n2 = StableHistory("Bills", 4500); n2.Add(Tx("c", 4550, "Bills", new DateTime(2026, 9, 5)));
        await Case("N2 stable bills", false, n2);

        await Case("N3 no data at all", false, new List<ExpenseTransaction>());

        matrix.Print("Anomaly Detection (case-level)");
        if (matrix.FalseNegative > 0 || matrix.FalsePositive > 0)
            Report.Warn("Anomaly Detection accuracy", $"{matrix.FalsePositive} false positive(s), {matrix.FalseNegative} false negative(s) on this labeled set — see report for which cases and why");
    }
}
