using System.Collections.Generic;

namespace Wayfinder.Search;

/// <summary>
/// How a query matched, worst to best. The tier dominates the ranking: a
/// substring hit always outranks the best fuzzy hit, so typing more of a name
/// never pushes it down the list.
/// </summary>
public enum MatchKind
{
    /// The query's characters appear in order but not contiguously. "arum" → "the Aurum Vale".
    Fuzzy,
    /// The query appears verbatim somewhere inside. "vale" → "the Aurum Vale".
    Substring,
    /// The query starts a word. "aurum" → "the <b>Aurum</b> Vale".
    WordStart,
    /// The query starts the whole name. "the au" → "the Aurum Vale".
    Prefix,
    /// The query is the whole name.
    Exact,
}

/// <summary>One candidate a provider offers to the matcher.</summary>
/// <param name="Id">Opaque to the search layer; the provider uses it to act on the pick.</param>
/// <param name="Name">The game's own localised display name. Never built by this plugin.</param>
/// <param name="Detail">Right-aligned annotation — level, gil cost. Not searched.</param>
/// <param name="Keywords">
/// Secondary text that also matches, scored at a discount so a name match always
/// wins: a duty's content type, an aetheryte's zone.
/// </param>
/// <param name="Available">
/// False for something the game will not let the player pick yet. Shown dimmed and
/// sorted last rather than hidden — a search box that silently omits things is
/// worse than one that shows a greyed row.
/// </param>
public sealed record SearchCandidate(
    ulong                 Id,
    string                Name,
    string?               Detail    = null,
    IReadOnlyList<string>? Keywords = null,
    bool                  Available = true)
{
    /// <summary>
    /// Lower-cased, diacritic-folded <see cref="Name"/>, plus the offsets at which
    /// words start. Computed once when the index is built, never per keystroke.
    /// </summary>
    internal NormalizedText Normalized { get; } = NormalizedText.Of(Name);

    internal NormalizedText[] NormalizedKeywords { get; } = NormalizedText.Of(Keywords);
}

/// <summary>A candidate that matched, with the score it is ranked by.</summary>
public readonly record struct SearchResult(SearchCandidate Candidate, int Score, MatchKind Kind)
{
    public ulong   Id     => Candidate.Id;
    public string  Name   => Candidate.Name;
    public string? Detail => Candidate.Detail;
}
