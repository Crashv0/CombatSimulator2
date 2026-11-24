using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

// Painting the strip: each area's name and fold mark, then its portraits; an ignored one dimmed under a red cross,
// the one being fought marked with the game's combat icon.
internal sealed partial class TargetStrip
{
    private void Paint()
    {
        var font = Look.Font(400);
        var size = 13;
        var text = Look.Text(_canvas);
        var muted = Look.Muted(_canvas);
        var ignored = PanelState.Ignored;
        if (_areas.Count == 0)
        {
            _canvas.DrawString(
                font,
                new Vector2(0, size + 4),
                _selection == null
                    ? Tr.Get("Checking targets for this setup…")
                    : Tr.Get("No target matches the filters."),
                HorizontalAlignment.Left,
                -1,
                size,
                muted
            );
            return;
        }
        foreach (var area in _areas)
        {
            var allIgnored = area.Targets.TrueForAll(t => ignored.Contains(t.Key));
            var baseline = area.NameRect.Position.Y + size + 2;
            _canvas.DrawString(
                font,
                new Vector2(area.NameRect.Position.X, baseline),
                area.Name,
                HorizontalAlignment.Left,
                area.NameRect.Size.X,
                size,
                allIgnored ? muted : text,
                TextServer.JustificationFlag.None,
                TextServer.Direction.Auto,
                TextServer.Orientation.Horizontal
            );
            if (allIgnored)
                _canvas.PxLine(
                    new Vector2(area.NameRect.Position.X, baseline - size / 3f),
                    new Vector2(area.NameRect.End.X, baseline - size / 3f),
                    muted,
                    1
                );
            var folded = _folded.Contains(area.Id);
            _canvas.DrawString(
                font,
                new Vector2(area.FoldRect.Position.X, baseline),
                folded ? " ▸ " + area.Targets.Count.ToString(Tr.Culture) : " ▾",
                HorizontalAlignment.Left,
                -1,
                size,
                muted
            );
            for (var i = 0; i < area.Portraits.Count; i++)
                PaintPortrait(area.Targets[i], area.Portraits[i], ignored.Contains(area.Targets[i].Key));
        }
    }

    private void PaintPortrait(Model.TargetFact target, Rect2 rect, bool ignored)
    {
        _canvas.PxRect(rect, Colors.Black with { A = 0.3f });
        // A target the run would refuse (locked in the build, or not yet fightable) is faint, as it can't count.
        var faint = !target.CanFight;
        var tint =
            ignored ? new Color(1, 0.55f, 0.55f, 0.45f)
            : faint ? Colors.White with { A = 0.45f }
            : Colors.White;
        if (ArtOf(target.Key) is { } art)
            _canvas.DrawTextureRect(art, rect.Grow(-2), false, tint);
        if (ignored)
        {
            var cross = Look.Deadly with { A = 0.9f };
            var inner = rect.Grow(-7);
            _canvas.PxLine(inner.Position, inner.End, cross, 2);
            _canvas.PxLine(
                new Vector2(inner.End.X, inner.Position.Y),
                new Vector2(inner.Position.X, inner.End.Y),
                cross,
                2
            );
        }
        if (target.Key == Fighting && Art.File(Art.CombatPath) is { } combat)
            _canvas.DrawTextureRect(combat, new Rect2(rect.End.X - 15, rect.Position.Y + 1, 14, 14), false);
        var selected = PanelState.Selected == target.Key;
        _canvas.PxRect(
            rect,
            selected ? Colors.White with { A = 0.85f }
                : ignored ? Look.Deadly with { A = 0.6f }
                : Colors.White with { A = 0.12f },
            false,
            selected ? 2 : 1
        );
    }
}
