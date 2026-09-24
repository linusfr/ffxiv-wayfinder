using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using Wayfinder.Search;

namespace Wayfinder.UI;

public sealed class ConfigurationWindow : IDisposable
{
    private static readonly Vector4 Heading = new(1f, 0.8f, 0.3f, 1f);

    private readonly Plugin _plugin;
    private Configuration Config => _plugin.Config;

    private bool _isVisible;
    public bool IsVisible { get => _isVisible; set => _isVisible = value; }

    public ConfigurationWindow(Plugin plugin) => _plugin = plugin;

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(440, 330), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Wayfinder###WayfinderSettings", ref _isVisible))
        {
            ImGui.End();
            return;
        }

        Section("Where to search");
        Toggle("Duty Finder", Config.DutyFinderSearch, v => Config.DutyFinderSearch = v);
        Toggle("Teleport",    Config.TeleportSearch,   v => Config.TeleportSearch = v);
        ImGui.TextDisabled("  The search box appears under the window while it is open.");

        Section("Matching");
        DrawSensitivity();

        Section("Keyboard");
        Toggle("Focus the search box when the window opens", Config.AutoFocus, v => Config.AutoFocus = v);
        ImGui.TextDisabled("  Off: click the box first, as with the game's own fields.");
        Toggle("Escape clears the search", Config.ClearOnEscape, v => Config.ClearOnEscape = v);
        ImGui.TextDisabled("  A second Escape leaves the box, a third closes the game's window.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled("Search only. Selecting, queueing and teleporting stay with the game.");

        ImGui.End();
    }

    private void DrawSensitivity()
    {
        var current = Config.Sensitivity;

        if (ImGui.BeginCombo("##sensitivity", Label(current)))
        {
            foreach (var option in Enum.GetValues<Sensitivity>())
            {
                if (ImGui.Selectable(Label(option), option == current) && option != current)
                {
                    Config.Sensitivity = option;
                    _plugin.SaveConfig();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled($"  {Explain(current)}");
    }

    private static string Label(Sensitivity sensitivity) => sensitivity switch
    {
        Sensitivity.Strict   => "Strict",
        Sensitivity.Balanced => "Balanced",
        Sensitivity.Loose    => "Loose",
        _                           => sensitivity.ToString(),
    };

    private static string Explain(Sensitivity sensitivity) => sensitivity switch
    {
        Sensitivity.Strict   => "Only what the name actually contains. \"arum\" finds nothing.",
        Sensitivity.Balanced => "\"arum\" finds the Aurum Vale; scattered letters do not match everything.",
        Sensitivity.Loose    => "Any letters in order. More results, more noise.",
        _                           => string.Empty,
    };

    private void Toggle(string label, bool value, Action<bool> set)
    {
        if (!ImGui.Checkbox(label, ref value))
            return;

        set(value);
        _plugin.SaveConfig();
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Heading, title);
        ImGui.Separator();
    }

    public void Dispose() { }
}
