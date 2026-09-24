using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

using Wayfinder.Search;

namespace Wayfinder.UI;

/// <summary>
/// The search field and its results, drawn flush under a native game window.
///
/// <para><b>Why under, rather than inside.</b> Both windows this attaches to fill
/// their top edge with controls — the Duty Finder's category radio buttons, the
/// Teleport window's region tabs. Anything drawn inside the frame would cover one of
/// them, and covering category navigation to add a search box defeats the point of
/// keeping the native UI in charge. Docked underneath, the overlay covers nothing,
/// the results grow downwards away from the window, and dragging the game window
/// takes the search box with it.</para>
///
/// <para><b>What makes it read as native.</b> The game's own Axis font, the game's
/// window background colour, and a frame that lines up with the native window's
/// left and right edges. It is still an ImGui window — it will not tint with a
/// custom game UI theme, and that is the honest cost of not injecting nodes.</para>
/// </summary>
internal sealed class SearchOverlay
{
    /// <summary>Hairline gap so the two frames read as attached rather than as one smeared box.</summary>
    private const float GapToAddon = 2f;

    private const int MaxVisibleRows = 10;
    private const int QueryMaxLength = 128;

    /// <summary>Roughly the game's window fill: dark desaturated blue, mostly opaque.</summary>
    private static readonly Vector4 WindowBackground = new(0.055f, 0.075f, 0.11f, 0.94f);
    private static readonly Vector4 FieldBackground  = new(0.02f, 0.03f, 0.05f, 0.90f);
    private static readonly Vector4 BorderColour     = new(0.42f, 0.38f, 0.26f, 0.55f);
    private static readonly Vector4 DetailColour     = new(0.62f, 0.62f, 0.58f, 1f);
    private static readonly Vector4 UnavailableColour = new(0.52f, 0.45f, 0.40f, 1f);

    private readonly string _id;

    private string _query        = string.Empty;
    private int    _selectedRow;
    private bool   _focusRequested;
    private bool   _inputActive;
    private bool   _scrollToSelection;

    public SearchOverlay(string id) => _id = id;

    /// <summary>Ask for keyboard focus on the next frame drawn.</summary>
    public void RequestFocus() => _focusRequested = true;

    /// <summary>
    /// Wipe the field. Called when the window closes, so reopening it does not
    /// resume somebody's half-typed query from an hour ago.
    /// </summary>
    public void Reset()
    {
        _query             = string.Empty;
        _selectedRow       = 0;
        _inputActive       = false;
        _focusRequested    = false;
        _scrollToSelection = false;
    }

    /// <summary>
    /// Draws one frame. Returns the candidate the player picked, or null.
    ///
    /// <para>The only work proportional to the data is <see cref="SearchService.SetQuery"/>,
    /// and that returns immediately unless the text actually changed.</para>
    /// </summary>
    public SearchCandidate? Draw(
        AddonGeometry   geometry,
        SearchService   search,
        ISearchProvider provider,
        Configuration   config,
        IFontHandle?    gameFont)
    {
        using var font = gameFont is { Available: true } ? gameFont.Push() : null;

        var width = MathF.Max(geometry.Size.X, 220f);

        ImGui.SetNextWindowPos(new Vector2(geometry.Position.X, geometry.Bottom + GapToAddon));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));
        ImGui.SetNextWindowBgAlpha(WindowBackground.W);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBackground);
        ImGui.PushStyleColor(ImGuiCol.Border,   BorderColour);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,  FieldBackground);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar        |
            ImGuiWindowFlags.NoResize          |
            ImGuiWindowFlags.NoMove            |
            ImGuiWindowFlags.NoCollapse        |
            ImGuiWindowFlags.NoScrollbar       |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoSavedSettings   |
            ImGuiWindowFlags.AlwaysAutoResize;

        SearchCandidate? picked = null;

        if (ImGui.Begin($"###{_id}", flags))
            picked = DrawContents(width, search, provider, config);

        ImGui.End();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(3);

        // font is popped by the using declaration. Popping it here as well would
        // unbalance ImGui's font stack.
        return picked;
    }

    private SearchCandidate? DrawContents(float width, SearchService search, ISearchProvider provider, Configuration config)
    {
        var results = search.Results;

        // Escape is read before the field is drawn, because ImGui's own handling of it
        // inside InputText reverts the text rather than clearing it. Only the first
        // press is intercepted, and only with the field focused and non-empty: on an
        // empty field nothing is done, so ImGui's own Escape deactivates the input, and
        // the press after that reaches the game window. Escape with the search box
        // unfocused is never looked at.
        if (config.ClearOnEscape && _inputActive && _query.Length > 0 && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _query          = string.Empty;
            _selectedRow    = 0;
            _focusRequested = true;
        }


        if (_inputActive && results.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            {
                _selectedRow      = (_selectedRow + 1) % results.Count;
                _scrollToSelection = true;
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            {
                _selectedRow      = (_selectedRow - 1 + results.Count) % results.Count;
                _scrollToSelection = true;
            }
        }

        if (_focusRequested)
        {
            ImGui.SetKeyboardFocusHere();
            _focusRequested = false;
        }

        ImGui.SetNextItemWidth(width - (ImGui.GetStyle().WindowPadding.X * 2f));
        var submitted = ImGui.InputTextWithHint(
            $"##{_id}Input",
            provider.Placeholder,
            ref _query,
            QueryMaxLength,
            ImGuiInputTextFlags.EnterReturnsTrue);

        _inputActive = ImGui.IsItemActive();

        if (search.SetQuery(_query))
        {
            _selectedRow       = 0;
            _scrollToSelection = true;
        }

        results = search.Results;
        if (_selectedRow >= results.Count)
            _selectedRow = 0;

        if (results.Count == 0)
        {
            if (!search.IsEmpty)
            {
                ImGui.Separator();
                ImGui.TextDisabled("No matches.");
            }

            return null;
        }

        if (submitted)
            return results[_selectedRow].Candidate;

        return DrawResults(results);
    }

    private SearchCandidate? DrawResults(IReadOnlyList<SearchResult> results)
    {
        ImGui.Separator();

        var rowHeight   = ImGui.GetTextLineHeightWithSpacing();
        var visibleRows = Math.Min(results.Count, MaxVisibleRows);
        var listHeight  = (rowHeight * visibleRows) + 2f;

        SearchCandidate? picked = null;

        if (ImGui.BeginChild($"##{_id}Results", new Vector2(0f, listHeight), false,
                             ImGuiWindowFlags.NoSavedSettings))
        {
            // Measured inside the child: the scrollbar, once one appears, takes width
            // the outer window does not know about, and the detail column has to stop
            // short of it rather than slide underneath.
            var innerWidth = ImGui.GetContentRegionAvail().X;

            for (var i = 0; i < results.Count; i++)
            {
                var result    = results[i];
                var available = result.Candidate.Available;

                if (!available)
                    ImGui.PushStyleColor(ImGuiCol.Text, UnavailableColour);

                // The label carries the row index so two duties sharing a name — the
                // game has several — stay distinct ImGui items.
                if (ImGui.Selectable($"{result.Name}##{_id}{i}", i == _selectedRow,
                                     ImGuiSelectableFlags.AllowDoubleClick))
                {
                    _selectedRow = i;
                    picked       = result.Candidate;
                }

                if (!available)
                {
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Not unlocked yet — the game will not let this be selected.");
                }

                if (string.IsNullOrEmpty(result.Detail))
                    continue;

                var detailWidth = ImGui.CalcTextSize(result.Detail).X;
                var offset      = innerWidth - detailWidth;
                if (offset <= ImGui.CalcTextSize(result.Name).X + 12f)
                    continue;

                ImGui.SameLine(offset);
                ImGui.TextColored(DetailColour, result.Detail);
            }

            // Only when the arrow keys just moved the selection. Forcing the scroll
            // every frame the field is focused would make the list impossible to
            // scroll with the mouse while typing.
            if (_scrollToSelection)
            {
                ImGui.SetScrollY(Math.Max(0f, ((_selectedRow + 1) * rowHeight) - listHeight));
                _scrollToSelection = false;
            }
        }

        ImGui.EndChild();
        return picked;
    }
}
