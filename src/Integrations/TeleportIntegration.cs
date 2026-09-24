using System.Collections.Generic;
using System.Globalization;

using Dalamud.Game.ClientState.Aetherytes;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

using Wayfinder.Search;

namespace Wayfinder.Integrations;

/// <summary>
/// Search for the native Teleport window (<c>Teleport</c>).
///
/// <para><b>The destinations are the game's, not a list here.</b>
/// <see cref="Dalamud.Plugin.Services.IAetheryteList"/> is Dalamud's wrapper over
/// <c>Telepo.TeleportList</c> — the same vector the native window draws from. It
/// already contains only what this character has attuned, with that character's gil
/// costs and favourites applied. Nothing is filtered, unlocked or priced here,
/// because there is nothing left to filter: an aetheryte the player cannot use is
/// not in the list to begin with.</para>
///
/// <para><b>Names</b> come from the aetheryte's <c>PlaceName</c> row, in the client's
/// language.</para>
///
/// <para><b>Picking teleports</b>, because that is what clicking a row in the native
/// window does — it has no intermediate selection step and no confirmation dialog.
/// <c>Telepo.Teleport</c> is the client function that row-click reaches, and it is
/// called with an id taken straight from the game's own list. The gil check, the
/// movement and combat restrictions, the cast and the server's own validation all
/// happen exactly as they would have. This is not a teleport implementation; it is
/// one call into the game's.</para>
/// </summary>
internal sealed unsafe class TeleportIntegration : SearchIntegration
{
    public TeleportIntegration() : base(new TeleportProvider()) { }

    protected override bool IsEnabled(Configuration config) => config.TeleportSearch;

    private sealed class TeleportProvider : ISearchProvider
    {
        public string AddonName   => "Teleport";
        public string Placeholder => "Search destinations...";

        public IReadOnlyList<SearchCandidate> BuildCandidates()
        {
            var list       = Plugin.AetheryteList;
            var candidates = new List<SearchCandidate>(list.Length);

            for (var i = 0; i < list.Length; i++)
            {
                var entry = list[i];
                if (entry == null)
                    continue;

                var aetheryte = entry.AetheryteData.ValueNullable;
                if (aetheryte == null)
                    continue;

                var place = aetheryte.Value.PlaceName.ValueNullable?.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(place))
                    continue;

                candidates.Add(new SearchCandidate(
                    Pack(entry),
                    Describe(place, entry),
                    Cost(entry),
                    Keywords(entry)));
            }

            return candidates;
        }

        public void Activate(SearchCandidate candidate)
        {
            var telepo = Telepo.Instance();
            if (telepo == null)
                return;

            var aetheryteId = (uint)(candidate.Id & 0xFFFFFFFF);
            var subIndex    = (byte)(candidate.Id >> 32);

            telepo->Teleport(aetheryteId, subIndex);
        }

        /// <summary>
        /// Aetheryte id plus sub-index, which is what <c>Telepo.Teleport</c> takes.
        /// Housing destinations share their district's aetheryte id and are told apart
        /// only by the sub-index, so the id alone would not round-trip.
        /// </summary>
        private static ulong Pack(IAetheryteEntry entry)
            => ((ulong)entry.SubIndex << 32) | entry.AetheryteId;

        /// <summary>
        /// The district name, with ward and plot appended for the housing entries that
        /// would otherwise be several identical rows.
        ///
        /// <para>The native window labels these with estate wording this plugin has no
        /// localised source for, so it stays with the place name the game does give it
        /// and adds only numbers. Searching the district still finds them, which is the
        /// part that matters.</para>
        /// </summary>
        private static string Describe(string place, IAetheryteEntry entry)
        {
            if (entry.Plot == 0 && entry.Ward == 0)
                return place;

            var culture = CultureInfo.InvariantCulture;

            return entry.Plot > 0
                ? $"{place} ({entry.Ward.ToString(culture)}-{entry.Plot.ToString(culture)})"
                : $"{place} ({entry.Ward.ToString(culture)})";
        }

        private static string? Cost(IAetheryteEntry entry)
        {
            if (entry.GilCost == 0)
                return entry.IsFavourite ? "★" : null;

            var gil = entry.GilCost.ToString("N0", CultureInfo.CurrentCulture);
            return entry.IsFavourite ? $"★  {gil}" : gil;
        }

        /// <summary>
        /// The zone the aetheryte stands in, so typing a region finds the shards in it —
        /// "thanalan" reaching Ul'dah's aetheryte the way a player would expect.
        /// </summary>
        private static IReadOnlyList<string>? Keywords(IAetheryteEntry entry)
        {
            var territory = entry.AetheryteData.ValueNullable?.Territory.ValueNullable?.PlaceName
                                 .ValueNullable?.Name.ExtractText();

            return string.IsNullOrWhiteSpace(territory) ? null : new[] { territory };
        }
    }
}
