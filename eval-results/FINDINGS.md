# Batch Evaluation Findings

**Date:** 2026-04-02
**Duration:** 4 hours 44 minutes
**Total runs:** 3,600 (100 per combination)
**Matrix:** 3 models x 2 prompt variants x 2 temperatures x 3 movies

## Configuration

| Variable | Values |
|----------|--------|
| **Models** | gpt-5.4, gpt-4o, gpt-4o-mini |
| **Prompt variants** | detailed (verbose, examples), concise (stripped-down) |
| **Temperatures** | 0.0 (deterministic), 0.7 (creative) |
| **Movies** | The Godfather (classic great), Santa Claus Conquers the Martians (known bad), Citizen Kane (classic great) |

## Key Findings

### 1. All Models Agree on Clear Cases

For unambiguous movies, all three models converge to similar scores:

| Movie | gpt-5.4 (mean) | gpt-4o (mean) | gpt-4o-mini (mean) |
|-------|----------------|---------------|---------------------|
| The Godfather (detailed, t=0) | 98.2 | 99.0 | 98.0 |
| Citizen Kane (detailed, t=0) | 95.7 | 97.7 | 93.3 |
| Santa Claus Conquers the Martians (detailed, t=0) | 27.4 | 31.0 | 28.9 |

All models correctly rank The Godfather and Citizen Kane as great (>90) and Santa Claus Conquers the Martians as poor (<40).

### 2. Stability: Temperature 0.0 Is Dramatically More Consistent

| Combo | Temp=0.0 StdDev | Temp=0.7 StdDev | Effect |
|-------|-----------------|-----------------|--------|
| gpt-4o, detailed, The Godfather | **0.00** | 0.56 | Perfect determinism at t=0 |
| gpt-4o-mini, detailed, The Godfather | **0.00** | 0.78 | Perfect determinism at t=0 |
| gpt-5.4, detailed, The Godfather | 0.39 | 0.49 | Near-deterministic |
| gpt-4o, concise, Santa Claus... | 0.39 | **4.55** | 12x more variance at t=0.7 |

**Finding:** gpt-4o at temperature 0 produces **perfectly deterministic** scores for The Godfather (99/99/99 for all 100 runs). Santa Claus Conquers the Martians shows the highest variance regardless of temperature — models are less certain about "bad" movies.

13 of 18 temperature comparisons were statistically significant (p < 0.05).

### 3. Prompt Variant Has a Major Impact

17 of 18 prompt comparisons were statistically significant (p < 0.05), often with **large** effect sizes.

Key patterns:
- **Concise prompts inflate scores for bad movies:** Santa Claus Conquers the Martians scores ~35-40 with concise prompts vs ~27-31 with detailed prompts across all models
- **Detailed prompts produce more discriminating scores:** The spread between great and bad movies is larger
- The largest effect: gpt-4o at t=0 on The Godfather: concise scored 97.0 vs detailed scored 99.0 (Cohen's d = -28.43 — an extreme effect because t=0 produces near-zero variance)

**Recommendation:** Use detailed prompts for evaluation tasks requiring fine discrimination.

### 4. Model Comparison: gpt-4o Edges Out on Discrimination

32 of 36 model comparisons were statistically significant.

| Metric | gpt-5.4 | gpt-4o | gpt-4o-mini |
|--------|---------|--------|-------------|
| The Godfather (detailed, t=0) | 98.2 | **99.0** | 98.0 |
| Santa Claus... (detailed, t=0) | **27.4** | 31.0 | 28.9 |
| Score range (great - bad) | 70.8 | **68.0** | 69.1 |
| Avg latency per run | ~8s | ~5s | ~3s |

- **gpt-4o** gives the highest scores to great movies but is slightly more generous to bad movies
- **gpt-5.4** is the harshest critic of bad movies (lowest Santa Claus scores)
- **gpt-4o-mini** is surprisingly competitive with the larger models, at ~3x lower cost

### 5. M.E.AI.Evaluation Quality Metrics

| Model | Movie | Coherence | Relevance | Groundedness |
|-------|-------|-----------|-----------|--------------|
| gpt-5.4 | The Godfather | 5.0 | 5.0 | 0.0 |
| gpt-5.4 | Santa Claus... | 5.0 | 5.0 | 0.0 |
| gpt-4o | The Godfather | 5.0 | 5.0 | 0.0 |
| gpt-4o | Santa Claus... | 5.0 | 5.0 | 0.0 |
| gpt-4o-mini | The Godfather | 5.0 | 4.0 | 0.0 |
| gpt-4o-mini | Santa Claus... | 4.0 | 5.0 | 0.0 |

- **Coherence** and **Relevance** are consistently high (4-5/5) across all models
- **Groundedness** is 0.0 because the task is subjective opinion generation (no source documents to ground against) — this is expected and correct behavior for the evaluator
- gpt-4o-mini shows slightly lower Coherence/Relevance than the larger models

### 6. Variance Analysis: Where Models Disagree Most

Highest coefficient of variation (most inconsistent):
- Santa Claus Conquers the Martians, gpt-4o, concise, t=0.7: **CV = 11.5%** (range 30-49)
- Santa Claus Conquers the Martians, gpt-4o-mini, concise, t=0.7: **CV = 10.3%** (range 31-51)

Lowest variance (most consistent):
- The Godfather, gpt-4o, detailed, t=0: **CV = 0.00%** (all 100 runs = 99)
- The Godfather, gpt-4o-mini, detailed, t=0: **CV = 0.00%** (all 100 runs = 98)

**Pattern:** Models are most uncertain about mediocre/bad movies and most confident about universally acclaimed movies.

## Statistical Rigor

- **100 runs per combination** provides strong statistical power for detecting differences
- **95% confidence intervals** are tight (typically ±0.5-1.0 points) due to large sample sizes
- **Mann-Whitney U test** used for non-parametric comparison (LLM score distributions are not necessarily normal)
- **Cohen's d** used for effect size measurement — many comparisons show "large" effects (d > 0.8)
- **Skewness and kurtosis** computed for each distribution to assess normality

## Recommendations

1. **Use temperature 0.0** for production evaluation tasks requiring reproducibility
2. **Use detailed prompts** for tasks requiring score discrimination
3. **gpt-4o-mini is viable** for high-volume evaluation at ~3x lower cost with minimal accuracy loss
4. **Run at least 30 iterations** per combination for reliable statistical inference (100 is ideal)
5. **Bad movies show highest variance** — consider this when setting acceptance thresholds
