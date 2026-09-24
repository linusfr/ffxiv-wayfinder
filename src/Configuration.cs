using Dalamud.Configuration;

using Wayfinder.Search;

namespace Wayfinder;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// Search box under the Duty Finder. Off draws nothing; the window is untouched
    /// either way, so toggling needs no reload.
    public bool DutyFinderSearch { get; set; } = true;

    /// Search box under the Teleport window.
    public bool TeleportSearch { get; set; } = true;

    /// How much slack the fuzzy tier gets. Balanced: "arum" still finds the Aurum
    /// Vale, but three letters do not match half the game.
    public Sensitivity Sensitivity { get; set; } = Sensitivity.Balanced;

    /// Put the cursor in the search box when the window opens. Off by default —
    /// the game's own windows do not take the keyboard on open, and one that does
    /// swallows the first keystroke of whatever you were about to do instead.
    public bool AutoFocus { get; set; } = false;

    /// Escape empties the search box; a second Escape lets go of it so the next one
    /// closes the game's window as usual. Off leaves Escape to the game entirely.
    public bool ClearOnEscape { get; set; } = true;
}
