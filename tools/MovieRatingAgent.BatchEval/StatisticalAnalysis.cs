namespace MovieRatingAgent.BatchEval;

/// <summary>
/// Rigorous statistical analysis for comparing LLM output distributions.
/// </summary>
public static class StatisticalAnalysis
{
    public record DescriptiveStats(
        int N,
        double Mean,
        double Median,
        double StdDev,
        double Variance,
        double Min,
        double Max,
        double Range,
        double Q1,
        double Q3,
        double IQR,
        double CoefficientOfVariation,
        double Skewness,
        double Kurtosis,
        double CI95Lower,
        double CI95Upper,
        double StandardError);

    public record ComparisonResult(
        string GroupA,
        string GroupB,
        double MeanDifference,
        double CohensD,
        string EffectSizeLabel,
        double MannWhitneyU,
        double MannWhitneyZ,
        double MannWhitneyP,
        bool IsSignificant);

    public static DescriptiveStats ComputeDescriptive(IReadOnlyList<double> values)
    {
        var n = values.Count;
        if (n == 0) return EmptyStats();

        var sorted = values.OrderBy(v => v).ToArray();
        var mean = sorted.Average();
        var median = Percentile(sorted, 0.50);
        var q1 = Percentile(sorted, 0.25);
        var q3 = Percentile(sorted, 0.75);

        var variance = n > 1
            ? sorted.Sum(v => Math.Pow(v - mean, 2)) / (n - 1)
            : 0.0;
        var stdDev = Math.Sqrt(variance);
        var se = stdDev / Math.Sqrt(n);

        // 95% CI using z=1.96 (adequate for n>=30; for n<30 this is approximate)
        var z95 = 1.96;
        var ci95Lower = mean - z95 * se;
        var ci95Upper = mean + z95 * se;

        var cv = mean != 0 ? (stdDev / Math.Abs(mean)) * 100.0 : 0.0;

        // Skewness (Fisher's) — guard against zero stdDev
        var skewness = n > 2 && stdDev > 0
            ? (n / ((double)(n - 1) * (n - 2))) * sorted.Sum(v => Math.Pow((v - mean) / stdDev, 3))
            : 0.0;

        // Excess kurtosis (Fisher's) — guard against zero stdDev
        var kurtosis = n > 3 && stdDev > 0
            ? ((n * (n + 1.0)) / ((n - 1.0) * (n - 2.0) * (n - 3.0)))
              * sorted.Sum(v => Math.Pow((v - mean) / stdDev, 4))
              - (3.0 * Math.Pow(n - 1.0, 2)) / ((n - 2.0) * (n - 3.0))
            : 0.0;

        return new DescriptiveStats(
            N: n,
            Mean: Math.Round(mean, 4),
            Median: Math.Round(median, 4),
            StdDev: Math.Round(stdDev, 4),
            Variance: Math.Round(variance, 4),
            Min: sorted[0],
            Max: sorted[^1],
            Range: sorted[^1] - sorted[0],
            Q1: Math.Round(q1, 4),
            Q3: Math.Round(q3, 4),
            IQR: Math.Round(q3 - q1, 4),
            CoefficientOfVariation: Math.Round(cv, 4),
            Skewness: Math.Round(skewness, 4),
            Kurtosis: Math.Round(kurtosis, 4),
            CI95Lower: Math.Round(ci95Lower, 4),
            CI95Upper: Math.Round(ci95Upper, 4),
            StandardError: Math.Round(se, 4));
    }

    /// <summary>
    /// Compare two distributions using Cohen's d (effect size) and Mann-Whitney U test.
    /// </summary>
    public static ComparisonResult Compare(
        string nameA, IReadOnlyList<double> a,
        string nameB, IReadOnlyList<double> b)
    {
        var meanA = a.Average();
        var meanB = b.Average();
        var meanDiff = meanA - meanB;

        // Cohen's d (pooled standard deviation)
        var varA = a.Count > 1 ? a.Sum(v => Math.Pow(v - meanA, 2)) / (a.Count - 1) : 0;
        var varB = b.Count > 1 ? b.Sum(v => Math.Pow(v - meanB, 2)) / (b.Count - 1) : 0;
        var pooledSd = Math.Sqrt(((a.Count - 1) * varA + (b.Count - 1) * varB) / (a.Count + b.Count - 2));
        var cohensD = pooledSd > 0 ? meanDiff / pooledSd : 0;
        var effectLabel = Math.Abs(cohensD) switch
        {
            < 0.2 => "negligible",
            < 0.5 => "small",
            < 0.8 => "medium",
            _ => "large"
        };

        // Mann-Whitney U test
        var (u, z, p) = MannWhitneyU(a, b);

        return new ComparisonResult(
            GroupA: nameA,
            GroupB: nameB,
            MeanDifference: Math.Round(meanDiff, 4),
            CohensD: Math.Round(cohensD, 4),
            EffectSizeLabel: effectLabel,
            MannWhitneyU: Math.Round(u, 2),
            MannWhitneyZ: Math.Round(z, 4),
            MannWhitneyP: Math.Round(p, 6),
            IsSignificant: p < 0.05);
    }

    private static (double U, double Z, double P) MannWhitneyU(
        IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var n1 = a.Count;
        var n2 = b.Count;

        // Combine and rank
        var combined = a.Select(v => (Value: v, Group: 0))
            .Concat(b.Select(v => (Value: v, Group: 1)))
            .OrderBy(x => x.Value)
            .ToList();

        // Assign ranks (handle ties with average rank)
        var ranks = new double[combined.Count];
        var i = 0;
        while (i < combined.Count)
        {
            var j = i;
            while (j < combined.Count && combined[j].Value == combined[i].Value)
                j++;
            var avgRank = (i + j + 1.0) / 2.0; // 1-based average
            for (var k = i; k < j; k++)
                ranks[k] = avgRank;
            i = j;
        }

        // Sum ranks for group A
        var r1 = 0.0;
        for (var idx = 0; idx < combined.Count; idx++)
        {
            if (combined[idx].Group == 0)
                r1 += ranks[idx];
        }

        var u1 = r1 - (n1 * (n1 + 1.0) / 2.0);
        var u2 = (double)n1 * n2 - u1;
        var u = Math.Min(u1, u2);

        // Normal approximation for large samples
        var mu = (double)n1 * n2 / 2.0;
        var sigma = Math.Sqrt((double)n1 * n2 * (n1 + n2 + 1) / 12.0);
        var z = sigma > 0 ? (u1 - mu) / sigma : 0;

        // Two-tailed p-value (normal approximation)
        var p = 2.0 * NormalCdf(-Math.Abs(z));

        return (u, z, p);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var index = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var fraction = index - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    /// <summary>
    /// Standard normal CDF approximation (Abramowitz and Stegun 26.2.17).
    /// </summary>
    private static double NormalCdf(double z)
    {
        if (z < -8.0) return 0.0;
        if (z > 8.0) return 1.0;

        var sum = 0.0;
        var term = z;
        for (var i = 3; i <= 99; i += 2)
        {
            sum += term;
            term *= z * z / i;
        }
        return 0.5 + sum * Math.Exp(-0.5 * z * z - 0.91893853320467274178); // ln(sqrt(2*pi))
    }

    private static DescriptiveStats EmptyStats() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
