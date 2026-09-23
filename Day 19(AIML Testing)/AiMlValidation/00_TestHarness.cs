using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Validation;

public static class Report
{
    public static int Passed = 0;
    public static int Failed = 0;
    public static int Warnings = 0;
    public static readonly List<string> Failures = new();
    public static readonly List<string> WarningNotes = new();
    public static string CurrentSuite = "";

    public static void Suite(string name)
    {
        CurrentSuite = name;
        Console.WriteLine();
        Console.WriteLine("=".PadRight(78, '='));
        Console.WriteLine(name);
        Console.WriteLine("=".PadRight(78, '='));
    }

    public static void Check(string testName, bool condition, string detail = "")
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine($"  [PASS] {testName}");
        }
        else
        {
            Failed++;
            var msg = $"{CurrentSuite} :: {testName} — {detail}";
            Failures.Add(msg);
            Console.WriteLine($"  [FAIL] {testName} — {detail}");
        }
    }

    public static void Warn(string testName, string detail)
    {
        Warnings++;
        var msg = $"{CurrentSuite} :: {testName} — {detail}";
        WarningNotes.Add(msg);
        Console.WriteLine($"  [WARN] {testName} — {detail}");
    }

    public static void Info(string message) => Console.WriteLine($"  {message}");

    public static void Metric(string label, double value, string unit = "")
    {
        Console.WriteLine($"  METRIC {label}: {value:0.0}{unit}");
    }

    public static void PrintFinalSummary()
    {
        Console.WriteLine();
        Console.WriteLine("#".PadRight(78, '#'));
        Console.WriteLine($"TOTAL: {Passed} passed, {Failed} failed, {Warnings} warnings");
        Console.WriteLine("#".PadRight(78, '#'));
        if (Failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("FAILURES:");
            foreach (var f in Failures) Console.WriteLine($"  - {f}");
        }
        if (WarningNotes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("WARNINGS (methodology notes, not hard failures):");
            foreach (var w in WarningNotes) Console.WriteLine($"  - {w}");
        }
    }
}

/// <summary>Simple confusion-matrix accumulator for accuracy metrics.</summary>
public sealed class ConfusionMatrix
{
    public int TruePositive, FalsePositive, TrueNegative, FalseNegative;

    public void Record(bool actualPositive, bool predictedPositive)
    {
        if (actualPositive && predictedPositive) TruePositive++;
        else if (!actualPositive && predictedPositive) FalsePositive++;
        else if (!actualPositive && !predictedPositive) TrueNegative++;
        else FalseNegative++;
    }

    public double Precision => TruePositive + FalsePositive == 0 ? 0 : (double)TruePositive / (TruePositive + FalsePositive);
    public double Recall => TruePositive + FalseNegative == 0 ? 0 : (double)TruePositive / (TruePositive + FalseNegative);
    public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
    public double Accuracy
    {
        get
        {
            var total = TruePositive + FalsePositive + TrueNegative + FalseNegative;
            return total == 0 ? 0 : (double)(TruePositive + TrueNegative) / total;
        }
    }

    public void Print(string label)
    {
        Report.Info($"{label}: TP={TruePositive} FP={FalsePositive} TN={TrueNegative} FN={FalseNegative}");
        Report.Metric($"{label} Precision", Precision * 100, "%");
        Report.Metric($"{label} Recall", Recall * 100, "%");
        Report.Metric($"{label} F1", F1 * 100, "%");
        Report.Metric($"{label} Accuracy", Accuracy * 100, "%");
    }
}
