using System.Collections.Generic;

using FFXIVClientStructs.FFXIV.Client.UI.Agent;

using Lumina.Excel;
using Lumina.Excel.Sheets;

using Wayfinder.Search;

// Both FFXIVClientStructs and Lumina define a ContentRoulette. The sheet row is the
// one meant everywhere below; the client struct is not used here at all.
using ContentRoulette = Lumina.Excel.Sheets.ContentRoulette;

namespace Wayfinder.Integrations;

/// <summary>
/// Search for the native Duty Finder (<c>ContentsFinder</c>).
///
/// <para><b>Selection is the game's own.</b> Picking a result calls
/// <c>AgentContentsFinder.OpenRegularDuty</c> or <c>OpenRouletteDuty</c> — the
/// functions behind a duty link in chat and the Duty Finder button in the Timers
/// window. They switch the native window to the right category and tick the entry,
/// which is why this plugin needs no category-navigation code, no list filtering and
/// no hooks. Every unlock, level, item level, role and party check stays in the game,
/// on the far side of one call.</para>
///
/// <para><b>Names come from the sheets, not from a list here.</b>
/// <c>ContentFinderCondition.Name</c> is what the client is already drawing, in the
/// client's language. The sheet stores them mid-sentence — "the Aurum Vale" — and
/// the game capitalises when a name leads a line, which is what
/// <see cref="CapitaliseFirst"/> reproduces. It is a no-op in languages that do not
/// lead with a lowercase article.</para>
/// </summary>
internal sealed unsafe class DutyFinderIntegration : SearchIntegration
{
    public DutyFinderIntegration() : base(new DutyFinderProvider()) { }

    protected override bool IsEnabled(Configuration config) => config.DutyFinderSearch;

    private sealed class DutyFinderProvider : ISearchProvider
    {
        /// <summary>Packed into the high bits of the candidate id to tell the two sheets apart.</summary>
        private const ulong RouletteTag = 1UL << 32;

        public string AddonName   => "ContentsFinder";
        public string Placeholder => "Search duties...";

        public IReadOnlyList<SearchCandidate> BuildCandidates()
        {
            var candidates = new List<SearchCandidate>(512);

            AddRoulettes(candidates);
            AddDuties(candidates);

            return candidates;
        }

        /// <summary>
        /// Roulettes first: they are what the Duty Finder opens on, and there are a
        /// dozen of them against hundreds of duties, so a tie never buries one.
        /// </summary>
        private static void AddRoulettes(List<SearchCandidate> candidates)
        {
            var sheet = Plugin.DataManager.GetExcelSheet<ContentRoulette>();
            if (sheet == null)
                return;

            foreach (var roulette in sheet)
            {
                // Roulette ids are handed to OpenRouletteDuty as a byte.
                if (!roulette.IsInDutyFinder || roulette.RowId is 0 or > byte.MaxValue)
                    continue;

                var name = roulette.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                candidates.Add(new SearchCandidate(
                    RouletteTag | roulette.RowId,
                    CapitaliseFirst(name),
                    Requirement(roulette.RequiredLevel, roulette.ItemLevelRequired),
                    Keywords(roulette.Category.ExtractText(), roulette.DutyType.ExtractText())));
            }
        }

        private static void AddDuties(List<SearchCandidate> candidates)
        {
            var sheet = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>();
            if (sheet == null)
                return;

            // Resolved once for the whole pass. The unlock check needs it per row, and
            // this list is several hundred rows long.
            var instanceContent = Plugin.DataManager.GetExcelSheet<InstanceContent>();
            var loggedIn        = Plugin.ClientState.IsLoggedIn;

            foreach (var duty in sheet)
            {
                // The sheet carries rows the Duty Finder never lists — cutscene-only
                // instances, deep dungeon floors, dev leftovers. This is the game's own
                // flag for "appears in the Duty Finder", so the search offers what the
                // window offers.
                if (!duty.IsInDutyFinder || duty.RowId == 0)
                    continue;

                var name = duty.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                candidates.Add(new SearchCandidate(
                    duty.RowId,
                    CapitaliseFirst(name),
                    Requirement(duty.ClassJobLevelRequired, duty.ItemLevelRequired),
                    Keywords(CategoryName(duty), TypeName(duty)),
                    IsUnlocked(duty, instanceContent, loggedIn)));
            }
        }

        public void Activate(SearchCandidate candidate)
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
                return;

            var rowId = (uint)(candidate.Id & 0xFFFFFFFF);

            // hideIfShown: false. The window is open — that is the only way the player
            // reached this search box — and the parameter's purpose is to let a second
            // invocation toggle it shut, which is not what picking a search result means.
            if ((candidate.Id & RouletteTag) != 0)
                agent->OpenRouletteDuty((byte)rowId, false);
            else
                agent->OpenRegularDuty(rowId, false);
        }

        /// <summary>
        /// Whether the player has unlocked the duty, for the majority of content where
        /// the game states it plainly.
        ///
        /// <para><c>ContentFinderCondition.Content</c> is an untyped row reference whose
        /// target depends on <c>ContentLinkType</c>. Type 1 is <c>InstanceContent</c> —
        /// dungeons, trials, raids, almost everything anyone searches for — which is the
        /// one case <see cref="Dalamud.Plugin.Services.IUnlockState"/> answers directly.
        /// The other link types point at sheets with no equivalent check, so they are
        /// reported as available rather than guessed at.</para>
        ///
        /// <para>Locked duties are dimmed and sorted last, never hidden. Mis-reading this
        /// then costs a greyed row; hiding on it would cost a duty that cannot be found
        /// at all.</para>
        /// </summary>
        private static bool IsUnlocked(ContentFinderCondition duty, ExcelSheet<InstanceContent>? instanceContent, bool loggedIn)
        {
            // Unlock state lives on the logged-in character. Asking at the title screen
            // would mark the whole list locked, which is worse than not filtering at all.
            if (!loggedIn || instanceContent == null || duty.ContentLinkType != 1)
                return true;

            var contentId = duty.Content.RowId;
            if (contentId == 0 || !instanceContent.TryGetRow(contentId, out var row))
                return true;

            return Plugin.UnlockState.IsInstanceContentUnlocked(row);
        }

        /// <summary>"Lv. 47", "Lv. 59  i120", or nothing where the game states no requirement.</summary>
        private static string? Requirement(int level, int itemLevel)
        {
            if (level <= 0)
                return null;

            // Item level is 0 for most content below 50: the game states no requirement
            // there, so neither does this.
            return itemLevel > 0 ? $"Lv. {level}   i{itemLevel}" : $"Lv. {level}";
        }

        private static string CategoryName(ContentFinderCondition duty)
            => duty.ContentUICategory.ValueNullable?.Name.ExtractText() ?? string.Empty;

        private static string TypeName(ContentFinderCondition duty)
            => duty.ContentType.ValueNullable?.Name.ExtractText() ?? string.Empty;

        /// <summary>
        /// Secondary search text, so "raid" or "alliance" finds the duties the game
        /// files under them. Scored below name matches, so a duty actually called
        /// something never loses to one merely categorised as it.
        /// </summary>
        private static IReadOnlyList<string>? Keywords(string? a, string? b)
        {
            var list = new List<string>(2);

            if (!string.IsNullOrWhiteSpace(a))
                list.Add(a);
            if (!string.IsNullOrWhiteSpace(b) && !string.Equals(a, b, System.StringComparison.Ordinal))
                list.Add(b);

            return list.Count == 0 ? null : list;
        }

        private static string CapitaliseFirst(string value)
            => value.Length > 0 && char.IsLower(value[0])
                ? char.ToUpperInvariant(value[0]) + value[1..]
                : value;
    }
}
