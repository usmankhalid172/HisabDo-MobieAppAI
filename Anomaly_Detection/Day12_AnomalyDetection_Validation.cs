using System;
using System.Collections.Generic;
using System.Linq;

namespace HisabDo.AI.Day12.Validation;

/// <summary>
/// Simple deterministic validation harness for Day 12 anomaly rules.
/// This is a test/reference file, not an AI/LLM implementation.
/// </summary>
public sealed record SampleExpense(
    string UserId,
    string TransactionId,
    decimal Amount,
    string Category,
    DateTime Date,
    string Description);

public sealed record ValidationCase(
    string Id,
    string Description,
    bool ExpectedAnomaly);

public static class Day12AnomalyValidation
{
    public static IReadOnlyList<(ValidationCase Case, bool Actual, bool Passed)>
        Run(IReadOnlyList<SampleExpense> transactions)
    {
        var cases = new List<ValidationCase>
        {
            new("DS-01", "Normal food spending", false),
            new("DS-02", "Single expense at least 2x category baseline", true),
            new("DS-03", "Category spike between 20% and 49.99%", true),
            new("DS-04", "Category spike at least 50%", true),
            new("DS-05", "Category at least 2x historical baseline", true),
            new("DS-06", "Possible duplicate transaction", true)
        };

        // This harness intentionally keeps the checks simple.
        // Production implementation should use the project's real anomaly service.
        var results = new List<(ValidationCase, bool, bool)>();

        foreach (var testCase in cases)
        {
            bool actual = testCase.Id switch
            {
                "DS-01" => HasNoLargeOutlier(transactions),
                "DS-02" => HasAmountAtLeastTwoTimesCategoryAverage(transactions),
                "DS-03" => HasCategoryGrowth(transactions, 20m, 49.99m),
                "DS-04" => HasCategoryGrowth(transactions, 50m, decimal.MaxValue),
                "DS-05" => HasCategoryAtLeastTwoTimesBaseline(transactions),
                "DS-06" => HasPossibleDuplicate(transactions),
                _ => false
            };

            results.Add((testCase, actual, actual == testCase.ExpectedAnomaly));
        }

        return results;
    }

    private static bool HasNoLargeOutlier(IReadOnlyList<SampleExpense> tx)
        => !tx.GroupBy(x => x.Category)
              .Any(g =>
              {
                  var avg = g.Average(x => x.Amount);
                  return avg > 0 && g.Any(x => x.Amount >= avg * 2m);
              });

    private static bool HasAmountAtLeastTwoTimesCategoryAverage(
        IReadOnlyList<SampleExpense> tx)
        => tx.GroupBy(x => x.Category)
              .Any(g =>
              {
                  var avg = g.Average(x => x.Amount);
                  return avg > 0 && g.Any(x => x.Amount >= avg * 2m);
              });

    private static bool HasCategoryGrowth(
        IReadOnlyList<SampleExpense> tx,
        decimal minimumPercent,
        decimal maximumPercent)
    {
        // Assumes Date periods are already represented by earlier/later dates.
        var latestDate = tx.Max(x => x.Date).Date;
        var current = tx.Where(x => x.Date.Date == latestDate)
                        .GroupBy(x => x.Category)
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var history = tx.Where(x => x.Date.Date < latestDate)
                        .GroupBy(x => x.Category)
                        .ToDictionary(g => g.Key, g => g.Average(x => x.Amount));

        return current.Any(c =>
            history.TryGetValue(c.Key, out var baseline) &&
            baseline > 0 &&
            (c.Value - baseline) / baseline * 100m >= minimumPercent &&
            (c.Value - baseline) / baseline * 100m <= maximumPercent);
    }

    private static bool HasCategoryAtLeastTwoTimesBaseline(
        IReadOnlyList<SampleExpense> tx)
        => tx.GroupBy(x => x.Category)
              .Any(g =>
              {
                  var ordered = g.OrderBy(x => x.Date).ToList();
                  if (ordered.Count < 2) return false;

                  var latest = ordered[^1].Amount;
                  var baseline = ordered.Take(ordered.Count - 1).Average(x => x.Amount);

                  return baseline > 0 && latest >= baseline * 2m;
              });

    private static bool HasPossibleDuplicate(
        IReadOnlyList<SampleExpense> tx)
    {
        return tx.GroupBy(x => new
                {
                    x.UserId,
                    x.Amount,
                    x.Category,
                    x.Date.Date,
                    Description = Normalize(x.Description)
                })
            .Any(g => g.Count() > 1);
    }

    private static string Normalize(string value)
        => string.Join(" ",
            value.Trim()
                 .ToLowerInvariant()
                 .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/*
Important:
1. The production HisabDo service should remain the source of truth.
2. These sample checks are for methodology validation.
3. Replace sample objects with real test fixtures from the .NET test project.
4. Do not send raw database records to an LLM to decide whether they are anomalies.
5. Validate expected results first; then pass verified anomaly DTOs to the AI explanation layer.
*/
