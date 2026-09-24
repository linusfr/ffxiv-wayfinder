using System;
using System.Collections.Generic;

using Dalamud.Game.Command;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using Wayfinder.Integrations;
using Wayfinder.UI;

namespace Wayfinder;

public sealed class Plugin : IDalamudPlugin
{
    // ── Injected services ─────────────────────────────────────────────────────
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager     { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle         AddonLifecycle  { get; private set; } = null!;
    [PluginService] internal static IGameGui                GameGui         { get; private set; } = null!;
    [PluginService] internal static IAetheryteList          AetheryteList   { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;
    [PluginService] internal static IUnlockState            UnlockState     { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    /// <summary>
    /// The game's own Axis face, so the search box reads as part of the window it is
    /// attached to rather than as a plugin bolted underneath it. Null until the atlas
    /// has built; the overlay falls back to Dalamud's default font until then.
    /// </summary>
    internal static IFontHandle? GameFont { get; private set; }

    internal Configuration Config { get; }

    private readonly List<SearchIntegration> _integrations = new();
    private readonly ConfigurationWindow     _configWindow;

    private const string CmdConfig = "/wayfindercfg";

    public Plugin()
    {
        Instance = this;
        Config   = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        GameFont = PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(
            new GameFontStyle(GameFontFamilyAndSize.Axis12));

        _configWindow = new ConfigurationWindow(this);

        _integrations.Add(new DutyFinderIntegration());
        _integrations.Add(new TeleportIntegration());

        CommandManager.AddHandler(CmdConfig, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open Wayfinder settings.",
        });

        // Attunements and teleport costs change with the zone, and a duty unlocks
        // mid-session. Both are rebuilt when the relevant window next opens, but a
        // window left open across a zone change would otherwise keep a stale list.
        ClientState.TerritoryChanged += OnTerritoryChanged;

        PluginInterface.UiBuilder.Draw         += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;
        PluginInterface.UiBuilder.OpenMainUi   += OnOpenConfig;

        Log.Info("Wayfinder: Plugin loaded.");
    }

    private void OnDraw()
    {
        foreach (var integration in _integrations)
            integration.Draw(Config);

        _configWindow.Draw();
    }

    /// <summary>Drop every cached candidate list. Cheap: they rebuild lazily, on the next frame that draws.</summary>
    internal void InvalidateCandidates()
    {
        foreach (var integration in _integrations)
            integration.Invalidate();
    }

    internal void SaveConfig()
    {
        PluginInterface.SavePluginConfig(Config);
        InvalidateCandidates();
    }

    private void OnTerritoryChanged(uint territory) => InvalidateCandidates();

    private void OnConfigCommand(string cmd, string args) => _configWindow.IsVisible = !_configWindow.IsVisible;

    private void OnOpenConfig() => _configWindow.IsVisible = true;

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw         -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfig;
        PluginInterface.UiBuilder.OpenMainUi   -= OnOpenConfig;

        ClientState.TerritoryChanged -= OnTerritoryChanged;

        CommandManager.RemoveHandler(CmdConfig);

        foreach (var integration in _integrations)
            integration.Dispose();
        _integrations.Clear();

        GameFont?.Dispose();
        GameFont = null;

        Log.Info("Wayfinder: Plugin unloaded.");
    }
}
