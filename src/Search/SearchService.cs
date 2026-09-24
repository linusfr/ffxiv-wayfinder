using System;
using System.Collections.Generic;

namespace Wayfinder.Search;

/// <summary>How much slack the fuzzy tier is allowed.</summary>
public enum Sensitivity
{
    /// No fuzzy tier at all: exact, prefix, word start and substring only.
    Strict,
    /// Fuzzy, but a scattered match has to land on a word start to count.
    Balanced,
    /// Any in-order character match counts.
    Loose,
}

/// <summary>
/// Holds one window's candidate list and the results for the current query.
///
/// <para>The cost model is the point of this class. Candidates are folded once when
/// <see cref="SetCandidates"/> is called. Results are recomputed only in
/// <see cref="SetQuery"/>, which no-ops when the text has not actually changed.
/// Drawing a frame reads <see cref="Results"/> and allocates nothing.</para>
/// </summary>
public sealed class SearchService
{
    /// <summary>
    /// Enough to fill the overlay twice over. Ranking past this is work nobody
    /// scrolls to, and it bounds the sort on a one-character query that matches
    /// everything.
    /// </summary>
    private const int MaxResults = 50;

    private readonly List<SearchResult> _results = new(MaxResults);

    private IReadOnlyList<SearchCandidate> _candidates = Array.Empty<SearchCandidate>();
    private string                         _query      = string.Empty;
    private Sensitivity                    _sensitivity = Sensitivity.Balanced;

    public IReadOnlyList<SearchResult> Results => _results;
    public string                      Query   => _query;
    public bool                        IsEmpty => _query.Length == 0;
    public int                         CandidateCount => _candidates.Count;

    public void SetCandidates(IReadOnlyList<SearchCandidate> candidates)
    {
        _candidates = candidates;
        Run();
    }

    public void SetSensitivity(Sensitivity sensitivity)
    {
        if (_sensitivity == sensitivity)
            return;

        _sensitivity = sensitivity;
        Run();
    }

    /// <summary>Returns true when the query actually changed and results were rebuilt.</summary>
    public bool SetQuery(string query)
    {
        query ??= string.Empty;
        if (string.Equals(_query, query, StringComparison.Ordinal))
            return false;

        _query = query;
        Run();
        return true;
    }

    public void Clear() => SetQuery(string.Empty);

    private void Run()
    {
        _results.Clear();

        var trimmed = _query.Trim();
        if (trimmed.Length == 0)
            return;

        var query = FuzzyMatcher.Prepare(trimmed);
        if (query.Text.Length == 0)
            return;

        foreach (var candidate in _candidates)
        {
            var result = FuzzyMatcher.Match(query, candidate, _sensitivity);
            if (result != null)
                _results.Add(result.Value);
        }

        // Anything the game will not let the player pick sinks below everything it
        // will, whatever it scored.
        _results.Sort(static (a, b) =>
        {
            if (a.Candidate.Available != b.Candidate.Available)
                return a.Candidate.Available ? -1 : 1;
            if (a.Kind != b.Kind)
                return b.Kind.CompareTo(a.Kind);
            if (a.Score != b.Score)
                return b.Score.CompareTo(a.Score);

            return string.CompareOrdinal(a.Name, b.Name);
        });

        if (_results.Count > MaxResults)
            _results.RemoveRange(MaxResults, _results.Count - MaxResults);
    }
}
