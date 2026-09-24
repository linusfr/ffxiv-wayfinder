using System.Collections.Generic;

namespace Wayfinder.Search;

/// <summary>
/// Everything a searchable game window has to supply. Implementing this plus a
/// handful of lines in <see cref="Integrations.SearchIntegration"/> is the whole
/// cost of making another native window searchable — the matcher, the overlay, the
/// anchoring and the keyboard handling are already shared.
/// </summary>
public interface ISearchProvider
{
    /// <summary>The native addon this attaches to, e.g. "ContentsFinder".</summary>
    string AddonName { get; }

    /// <summary>Placeholder inside the empty search box. Short.</summary>
    string Placeholder { get; }

    /// <summary>
    /// Everything searchable, taken from the game's current data. Called when the
    /// window opens, when the game refreshes it, and on config changes — never per
    /// frame and never per keystroke.
    /// </summary>
    IReadOnlyList<SearchCandidate> BuildCandidates();

    /// <summary>
    /// Hand the pick to the game. Implementations call the native function the
    /// window's own row-click reaches and do nothing else; every unlock, level,
    /// role, gil and confirmation check stays where the game put it.
    /// </summary>
    void Activate(SearchCandidate candidate);
}
