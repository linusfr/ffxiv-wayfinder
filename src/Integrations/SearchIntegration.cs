using System;

using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.NativeWrapper;

using Wayfinder.Search;
using Wayfinder.UI;

namespace Wayfinder.Integrations;

/// <summary>
/// Attaches a <see cref="ISearchProvider"/> to its native window: tracks the window
/// through the addon lifecycle, rebuilds the candidate list when the game refreshes
/// it, draws the overlay while it is on screen, and hands picks back to the provider.
///
/// <para>Adding another searchable window means writing a provider and registering
/// it. Nothing below is duty- or teleport-specific.</para>
///
/// <para><b>Lifecycle rather than polling.</b> The addon pointer is captured at
/// PostSetup and dropped at PreFinalize, so the draw path never scans for the window
/// by name, and the candidate list is rebuilt once per opening rather than per frame. <see cref="Dalamud.Plugin.Services.IGameGui.GetAddonByName"/> is used
/// exactly once, at construction, to cover the case where the plugin loads while the
/// window is already open.</para>
///
/// <para><b>Failure is silence.</b> Any exception out of provider code disables this
/// integration for the session and logs once. A search box that vanishes after a
/// game update is a nuisance; one that throws every frame is a crash report.</para>
/// </summary>
internal abstract class SearchIntegration : IDisposable
{
    /// <summary>
    /// Give up after this many consecutive failures. One is too eager — a single
    /// frame caught mid-teardown is normal — and unbounded means an endless log.
    /// </summary>
    private const int FailureBudget = 5;

    private readonly SearchOverlay _overlay;
    private readonly SearchService _search = new();

    private AtkUnitBasePtr _addon;
    private bool           _candidatesStale = true;
    private int            _failures;
    private bool           _disabledByError;

    protected SearchIntegration(ISearchProvider provider)
    {
        Provider = provider;
        _overlay = new SearchOverlay($"Wayfinder{provider.AddonName}");

        // PostSetup and PreFinalize only. PostRefresh is deliberately not listened for:
        // the game refreshes these windows on every tab change and on its own timers, and
        // rebuilding several hundred candidates each time would put real work on frames
        // where nothing searchable actually changed. What these lists contain changes on
        // a zone, an attunement, an unlock or a purchase — all of which are in hand again
        // the next time the window is opened, which is the only moment the search box
        // exists to be used.
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   provider.AddonName, OnAddonSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, provider.AddonName, OnAddonFinalize);

        // The window may already be open — a /xlplugins reload mid-session, or a dev
        // install. Lifecycle events for an addon that was set up before this listener
        // existed never arrive.
        _addon = Plugin.GameGui.GetAddonByName(provider.AddonName, 1);
    }

    public ISearchProvider Provider { get; }

    /// <summary>The config flag that turns this integration on. Checked every frame, so toggling needs no reload.</summary>
    protected abstract bool IsEnabled(Configuration config);

    /// <summary>Forget the cached candidates; they are rebuilt on the next frame that needs them.</summary>
    public void Invalidate() => _candidatesStale = true;

    public void Draw(Configuration config)
    {
        if (_disabledByError || !IsEnabled(config))
            return;

        if (_addon.IsNull)
            return;

        var geometry = AddonAnchor.Measure(_addon);
        if (geometry == null)
            return;

        try
        {
            if (_candidatesStale)
            {
                _search.SetCandidates(Provider.BuildCandidates());
                _candidatesStale = false;
            }

            _search.SetSensitivity(config.Sensitivity);

            var picked = _overlay.Draw(geometry.Value, _search, Provider, config, Plugin.GameFont);
            if (picked == null)
                return;

            // Deferred by a tick rather than called here. Activating re-enters game UI
            // code — the Duty Finder switches category and repopulates, the Teleport
            // window starts a cast and closes itself — and doing that from inside the
            // draw callback means the addon this overlay is anchored to can be torn
            // down and rebuilt part-way through the frame drawing it.
            var provider = Provider;
            Plugin.Framework.RunOnTick(() => Activate(provider, picked));

            // The pick is spent whether or not the game accepts it: the native window
            // has taken over, and leaving the query behind would have the overlay
            // listing results for something already chosen.
            _overlay.Reset();
            _search.Clear();

            _failures = 0;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
        }
    }

    private static void Activate(ISearchProvider provider, SearchCandidate candidate)
    {
        try
        {
            provider.Activate(candidate);
        }
        catch (Exception ex)
        {
            // Off the draw path by now, so this cannot be counted against the budget
            // without reaching back into the integration. Logged and dropped: the worst
            // case is one pick that did nothing.
            Plugin.Log.Error(ex, $"Wayfinder: activating \"{candidate.Name}\" failed.");
        }
    }

    private void OnFailure(Exception ex)
    {
        _failures++;
        Plugin.Log.Error(ex, $"Wayfinder: {Provider.AddonName} search failed ({_failures}/{FailureBudget}).");

        if (_failures < FailureBudget)
            return;

        _disabledByError = true;
        Plugin.Log.Warning(
            $"Wayfinder: disabling {Provider.AddonName} search for this session. " +
            "A game update has probably moved something; check for a plugin update.");
    }

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        _addon           = args.Addon;
        _candidatesStale = true;

        _overlay.Reset();
        _search.Clear();

        if (Plugin.Instance.Config.AutoFocus)
            _overlay.RequestFocus();
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args)
    {
        _addon = default;
        _overlay.Reset();
        _search.Clear();
    }

    public virtual void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup,   Provider.AddonName, OnAddonSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, Provider.AddonName, OnAddonFinalize);
    }
}
