using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Wayfinder.Search;

/// <summary>
/// A name reduced to the form the matcher compares against, together with the
/// offsets at which its words start.
///
/// <para>Folding is lower-casing plus stripping combining marks, so a German or
/// French client's "Höhle" is found by typing "hohle". It is deliberately not a
/// transliteration: nothing here translates or romanises, because the plugin must
/// search whatever language the game is drawing.</para>
///
/// <para>Word starts are what make "vale" rank above a mid-word hit and what lets
/// "alex" find every Alexander wing. Japanese and Chinese text has no spaces, so
/// there the list is just {0} and matching falls through to substring and fuzzy —
/// which still work, because those are character-level.</para>
/// </summary>
internal readonly struct NormalizedText
{
    private static readonly NormalizedText[] NoKeywords = Array.Empty<NormalizedText>();

    public string Text      { get; }
    public int[]  WordStarts { get; }

    private NormalizedText(string text, int[] wordStarts)
    {
        Text       = text;
        WordStarts = wordStarts;
    }

    public static NormalizedText Of(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return new NormalizedText(string.Empty, Array.Empty<int>());

        var text = Fold(value);

        var starts = new List<int>(8);
        var atWordStart = true;
        for (var i = 0; i < text.Length; i++)
        {
            if (IsSeparator(text[i]))
            {
                atWordStart = true;
                continue;
            }

            if (atWordStart)
            {
                starts.Add(i);
                atWordStart = false;
            }
        }

        return new NormalizedText(text, starts.ToArray());
    }

    public static NormalizedText[] Of(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
            return NoKeywords;

        var result = new NormalizedText[values.Count];
        for (var i = 0; i < values.Count; i++)
            result[i] = Of(values[i]);

        return result;
    }

    /// <summary>
    /// Hyphens and apostrophes separate words as far as searching goes, so "toto"
    /// finds "the Thousand Maws of Toto-Rak" and "longstop" finds "Brayflox's
    /// Longstop".
    /// </summary>
    private static bool IsSeparator(char c)
        => char.IsWhiteSpace(c) || c is '-' or '\'' or '’' or '(' or ')' or ':' or ',' or '.' or '/' or '・';

    private static string Fold(string value)
    {
        var lowered = value.ToLowerInvariant();

        // FormD splits "ö" into "o" + combining diaeresis; dropping the marks leaves
        // the base letter. Skipped entirely when there is nothing to strip, which is
        // every English name.
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        if (decomposed.Length == lowered.Length)
            return lowered;

        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Scores a query against a name. Small and allocation-free on the hot path: the
/// candidate side is folded once when the index is built, the query once per
/// keystroke, and scoring itself only walks spans.
///
/// <para>Ranking is tier-first, score-second. The tiers are the ones the game's own
/// players expect — exact, prefix, word start, substring, fuzzy — and because
/// <see cref="MatchKind"/> is compared before the score, no amount of fuzzy
/// cleverness can put a loose match above a literal one.</para>
/// </summary>
internal static class FuzzyMatcher
{
    /// <summary>Base scores, one tier apart, so within-tier tuning can never cross tiers.</summary>
    private const int ExactBase     = 10_000;
    private const int PrefixBase    = 8_000;
    private const int WordStartBase = 6_000;
    private const int SubstringBase = 4_000;
    private const int FuzzyBase     = 0;

    /// <summary>Ceiling on what the fuzzy tier can earn, leaving it below SubstringBase.</summary>
    private const int FuzzyRange = 3_000;

    /// <summary>
    /// A keyword hit scores as if it were one tier weaker than the same hit on the
    /// name, so "Dungeon" (a content type) never outranks a duty actually called
    /// that.
    /// </summary>
    private const int KeywordPenalty = 2_500;

    /// <summary>Folds a query the same way candidates were folded. Call once per keystroke.</summary>
    public static NormalizedText Prepare(string query) => NormalizedText.Of(query);

    /// <summary>
    /// The best match between the query and the candidate's name or keywords, or
    /// null if nothing matched at the requested sensitivity.
    /// </summary>
    public static SearchResult? Match(in NormalizedText query, SearchCandidate candidate, Sensitivity sensitivity)
    {
        if (query.Text.Length == 0)
            return null;

        var best = Score(query, candidate.Normalized, sensitivity);

        foreach (var keyword in candidate.NormalizedKeywords)
        {
            var hit = Score(query, keyword, sensitivity);
            if (hit == null)
                continue;

            var penalised = (hit.Value.score - KeywordPenalty, hit.Value.kind);
            if (best == null || penalised.Item1 > best.Value.score)
                best = penalised;
        }

        return best == null
            ? null
            : new SearchResult(candidate, best.Value.score, best.Value.kind);
    }

    private static (int score, MatchKind kind)? Score(in NormalizedText query, in NormalizedText target, Sensitivity sensitivity)
    {
        var q = query.Text.AsSpan();
        var t = target.Text.AsSpan();

        if (t.Length == 0 || q.Length > t.Length)
            return null;

        // Shorter names win ties: with "vale" typed, "the Aurum Vale" should beat a
        // hypothetical "the Aurum Vale (Hard) Extreme Savage". Capped so it can never
        // eat a whole tier.
        var brevity = Math.Max(0, 200 - t.Length);

        if (t.SequenceEqual(q))
            return (ExactBase + brevity, MatchKind.Exact);

        if (t.StartsWith(q, StringComparison.Ordinal))
            return (PrefixBase + brevity, MatchKind.Prefix);

        // Earlier words rank higher — a hit on the second word beats one on the sixth.
        foreach (var start in target.WordStarts)
        {
            if (t[start..].StartsWith(q, StringComparison.Ordinal))
                return (WordStartBase + brevity - start, MatchKind.WordStart);
        }

        var index = t.IndexOf(q, StringComparison.Ordinal);
        if (index >= 0)
            return (SubstringBase + brevity - index, MatchKind.Substring);

        if (sensitivity == Sensitivity.Strict)
            return null;

        var fuzzy = ScoreSubsequence(q, t, target.WordStarts, sensitivity);
        return fuzzy == null ? null : (FuzzyBase + fuzzy.Value, MatchKind.Fuzzy);
    }

    /// <summary>
    /// Greedy in-order character match — "arum" against "the aurum vale" — scored on
    /// how tightly the characters cluster and how many of them land on word starts.
    ///
    /// <para>Greedy rather than optimal on purpose. An optimal alignment would cost a
    /// full DP table per candidate per keystroke for a ranking difference nobody can
    /// see on lists of a few hundred rows.</para>
    /// </summary>
    private static int? ScoreSubsequence(ReadOnlySpan<char> q, ReadOnlySpan<char> t, int[] wordStarts, Sensitivity sensitivity)
    {
        var  score      = 0;
        var  ti         = 0;
        var  firstHit   = -1;
        var  lastHit    = -1;
        var  streak     = 0;
        var  wordHits   = 0;

        foreach (var qc in q)
        {
            var found = -1;
            for (var i = ti; i < t.Length; i++)
            {
                if (t[i] != qc)
                    continue;

                found = i;
                break;
            }

            if (found < 0)
                return null;

            if (firstHit < 0)
                firstHit = found;

            streak = found == lastHit + 1 ? streak + 1 : 0;

            // Contiguity is the strongest signal that this is the word the player
            // meant: "arum" matching a-u-r-u-m contiguously beats the same letters
            // scattered across three words.
            score += 20 + (streak * 15);

            if (Array.IndexOf(wordStarts, found) >= 0)
            {
                score += 25;
                wordHits++;
            }

            lastHit = found;
            ti      = found + 1;
        }

        // Everything the match had to skip over, inside the matched span and before it.
        var spread = (lastHit - firstHit + 1) - q.Length;
        score -= spread * 4;
        score -= Math.Min(firstHit, 20) * 2;

        if (score <= 0)
            return null;

        // Balanced wants most of the query to have landed somewhere meaningful.
        // Without this, a three-letter query matches almost every long name.
        if (sensitivity == Sensitivity.Balanced)
        {
            var density = (double)q.Length / Math.Max(1, lastHit - firstHit + 1);
            if (density < 0.34 && wordHits == 0)
                return null;
        }

        return Math.Min(score, FuzzyRange);
    }
}
