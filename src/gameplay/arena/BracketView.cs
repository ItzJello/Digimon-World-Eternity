using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// Single-elimination tree. Double elimination draws a winners tree, a losers
/// tree, and the grand final. Empty pills stay up until that match is played.
/// </summary>
public partial class BracketView : Control
{
    private static readonly Color Pill = new(0.22f, 0.27f, 0.32f);
    private static readonly Color PillLive = new(0.30f, 0.34f, 0.28f);
    private static readonly Color PillPlayer = new(0.20f, 0.48f, 0.78f);
    private static readonly Color PillEmpty = new(0.14f, 0.16f, 0.2f);
    private static readonly Color LineColor = new(0.72f, 0.76f, 0.82f);
    private static readonly Color Gold = new(1f, 0.84f, 0.4f);

    private TournamentBoard? _board;
    private TournamentMatch? _next;

    public void ShowBoard(TournamentBoard board, TournamentMatch? next)
    {
        _board = board;
        _next = next;
        QueueRedraw();
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        if (_board == null || Size.X < 80f || Size.Y < 80f)
            return;
        _board.FillKnown();
        if (_board.Format == ArenaFormat.DoubleEliminationPvp && _board.Matches.Count >= 13)
        {
            float split = Size.Y * 0.52f;
            DrawLabel(new Vector2(0, 2), "Winners");
            DrawWinners(new Rect2(0, 20, Size.X - 210f, split - 24f), false);
            DrawLabel(new Vector2(0, split), "Losers");
            DrawLosers(new Rect2(0, split + 20f, Size.X - 210f, Size.Y - split - 20f));
            DrawGrand(new Rect2(Size.X - 196f, 24f, 188f, Size.Y - 32f));
            return;
        }

        DrawWinners(new Rect2(0, 0, Size.X, Size.Y), true);
    }

    private void DrawWinners(Rect2 area, bool trophy)
    {
        if (_board == null || _board.Matches.Count < 7)
            return;

        float reserve = trophy ? 64f : 0f;
        float usable = area.Size.X - reserve;
        float pillW = Mathf.Min(168f, usable / 5.1f);
        float stride = (usable - pillW) / 3f;
        float span = area.Size.Y / 4f;
        float pillH = Mathf.Clamp(span * 0.36f, 40f, 54f);
        float x0 = area.Position.X;

        var seeds = new Rect2[8];
        var quarters = new Rect2[4];
        for (int match = 0; match < 4; match++)
        {
            float top = area.Position.Y + span * match;
            seeds[match * 2] = new Rect2(x0, top + span * 0.08f, pillW, pillH);
            seeds[match * 2 + 1] = new Rect2(x0, top + span * 0.54f, pillW, pillH);
            quarters[match] = Between(x0 + stride, seeds[match * 2], seeds[match * 2 + 1], pillW, pillH);
        }

        Rect2 semi0 = Between(x0 + stride * 2f, quarters[0], quarters[1], pillW, pillH);
        Rect2 semi1 = Between(x0 + stride * 2f, quarters[2], quarters[3], pillW, pillH);
        Rect2 final = Between(x0 + stride * 3f, semi0, semi1, pillW, pillH);

        for (int match = 0; match < 4; match++)
            Join(seeds[match * 2], seeds[match * 2 + 1], quarters[match]);
        Join(quarters[0], quarters[1], semi0);
        Join(quarters[2], quarters[3], semi1);
        Join(semi0, semi1, final);

        for (int match = 0; match < 4; match++)
        {
            DrawEntrant(seeds[match * 2], SlotOf(_board.Matches[match].Left), match);
            DrawEntrant(seeds[match * 2 + 1], SlotOf(_board.Matches[match].Right), match);
            DrawAdvance(quarters[match], match, false);
        }

        DrawAdvance(semi0, 4, false);
        DrawAdvance(semi1, 5, false);
        DrawAdvance(final, 6, true);

        if (!trophy)
            return;
        Vector2 from = RightMid(final);
        Vector2 cup = new(area.End.X - 22f, from.Y - 8f);
        DrawLine(from, new Vector2(cup.X - 16f, from.Y), LineColor, 2f);
        DrawTrophy(cup, _board.Matches[6].Played);
    }

    private void DrawTrophy(Vector2 center, bool earned)
    {
        Color color = earned ? new Color(0.45f, 0.78f, 0.95f) : new Color(0.45f, 0.5f, 0.56f);
        Vector2[] cup =
        {
            center + new Vector2(-14, -16),
            center + new Vector2(14, -16),
            center + new Vector2(8, 4),
            center + new Vector2(-8, 4),
        };
        DrawColoredPolygon(cup, color);
        DrawLine(center + new Vector2(0, 4), center + new Vector2(0, 14), color, 3f);
        DrawLine(center + new Vector2(-12, 16), center + new Vector2(12, 16), color, 3f);
    }

    private void DrawLosers(Rect2 area)
    {
        if (_board == null)
            return;

        float pillH = Mathf.Clamp(area.Size.Y / 8.5f, 32f, 44f);
        float pillW = Mathf.Min(148f, area.Size.X / 5.4f);
        float stride = (area.Size.X - pillW) / 4f;
        float half = area.Size.Y * 0.5f;
        float x = area.Position.X;

        Band(area.Position.Y, half, x, stride, pillW, pillH, 0, 1, 7, 5, out Rect2 win9);
        Band(area.Position.Y + half, half, x, stride, pillW, pillH, 2, 3, 8, 4, out Rect2 win10);

        Rect2 finals = Between(x + stride * 3f, win9, win10, pillW, pillH);
        Join(win9, win10, finals);
        DrawAdvance(finals, 11, false);

        int dropped = LoserOf(6);
        Rect2 drop = new(x + stride * 3f, area.End.Y - pillH - 2f, pillW, pillH);
        if (drop.Position.Y < finals.End.Y + 4f)
            drop.Position = new Vector2(drop.Position.X, finals.End.Y + 6f);
        Rect2 champ = new(x + stride * 4f, (finals.Position.Y + drop.Position.Y) * 0.5f, pillW, pillH);
        if (champ.End.X > area.End.X)
            champ.Position = new Vector2(area.End.X - pillW, champ.Position.Y);
        DrawPill(drop, dropped, Live(dropped, 12), false);
        Join(finals, drop, champ);
        DrawAdvance(champ, 12, true);
    }

    private void Band(
        float top, float height, float x, float stride, float pillW, float pillH,
        int quarterA, int quarterB, int losersMatch, int semiMatch, out Rect2 advanced)
    {
        float gap = Mathf.Max(4f, (height * 0.46f - pillH));
        Rect2 first = new(x, top + 2f, pillW, pillH);
        Rect2 second = new(x, first.End.Y + gap, pillW, pillH);
        Rect2 won = Between(x + stride, first, second, pillW, pillH);
        Join(first, second, won);
        DrawEntrant(first, LoserOf(quarterA), losersMatch);
        DrawEntrant(second, LoserOf(quarterB), losersMatch);
        DrawAdvance(won, losersMatch, false);

        int dropSlot = LoserOf(semiMatch);
        Rect2 drop = new(x + stride, won.End.Y + 6f, pillW, pillH);
        if (drop.End.Y > top + height - 2f)
            drop.Position = new Vector2(drop.Position.X, top + height - pillH - 2f);
        advanced = Between(x + stride * 2f, won, drop, pillW, pillH);
        DrawPill(drop, dropSlot, Live(dropSlot, losersMatch + 2), false);
        Join(won, drop, advanced);
        DrawAdvance(advanced, losersMatch + 2, false);
    }

    private void DrawGrand(Rect2 area)
    {
        if (_board == null)
            return;
        DrawLabel(new Vector2(area.Position.X, area.Position.Y), "Grand Final");
        float pillW = area.Size.X - 4f;
        float pillH = 46f;
        float y = area.Position.Y + 28f;
        Rect2 winners = new(area.Position.X, y, pillW, pillH);
        Rect2 losers = new(area.Position.X, y + 64f, pillW, pillH);
        Rect2 cup = new(area.Position.X, y + 148f, pillW, pillH);
        DrawAdvance(winners, 6, false);
        DrawAdvance(losers, 12, false);
        Join(winners, losers, cup);
        DrawAdvance(cup, 13, true);

        if (_board.Matches.Count < 15 || _board.Matches[14].Left < 0)
            return;
        TournamentMatch resetMatch = _board.Matches[14];
        Rect2 reset = new(area.Position.X, cup.End.Y + 28f, pillW, pillH);
        DrawLabel(new Vector2(area.Position.X, reset.Position.Y - 18f), "Reset");
        int shown = resetMatch.Played ? resetMatch.Winner : resetMatch.Left;
        DrawEntrant(reset, shown, 14);
    }

    private void DrawEntrant(Rect2 rect, int slot, int matchId)
    {
        DrawPill(rect, slot, Live(slot, matchId), false);
    }

    private void DrawAdvance(Rect2 rect, int matchId, bool championColumn)
    {
        if (_board == null || matchId < 0 || matchId >= _board.Matches.Count)
        {
            DrawPill(rect, -1, false, false);
            return;
        }

        TournamentMatch match = _board.Matches[matchId];
        int slot = match.Played ? match.Winner : -1;
        bool champion = championColumn && match.Played;
        int feeds = Feeds(matchId);
        DrawPill(rect, slot, !champion && Live(slot, feeds), champion);
    }

    private int Feeds(int matchId)
    {
        if (_board == null)
            return -1;
        for (int i = 0; i < _board.Matches.Count; i++)
        {
            TournamentMatch match = _board.Matches[i];
            if (match.LeftSource == matchId && !match.LeftIsLoser)
                return i;
            if (match.RightSource == matchId && !match.RightIsLoser)
                return i;
        }

        return -1;
    }

    private bool Live(int slot, int matchId)
    {
        return slot >= 0 && _next != null && _next.Id == matchId
            && (_next.Left == slot || _next.Right == slot);
    }

    private void DrawPill(Rect2 rect, int slot, bool live, bool champion)
    {
        if (_board == null)
            return;
        bool player = slot >= 0 && slot < _board.Slots.Count && _board.Slot(slot).Player;
        Color fill = slot < 0 ? PillEmpty : champion || player ? PillPlayer : live ? PillLive : Pill;
        var box = new StyleBoxFlat { BgColor = fill };
        box.SetCornerRadiusAll(4);
        DrawStyleBox(box, rect);
        if (live && !player)
            DrawRect(rect.Grow(1.5f), Gold, false, 2f);
        if (slot < 0)
            return;

        TournamentSlot side = _board.Slot(slot);
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.46f), side.Digimon,
            HorizontalAlignment.Center, rect.Size.X, 15, Colors.White);
        DrawString(font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.84f), side.Trainer,
            HorizontalAlignment.Center, rect.Size.X, 12, new Color(1f, 0.93f, 0.72f));
    }

    private void DrawLabel(Vector2 at, string text)
    {
        DrawString(ThemeDB.FallbackFont, at + new Vector2(0, 14), text, HorizontalAlignment.Left, -1, 14, Gold);
    }

    private static Rect2 Between(float x, Rect2 top, Rect2 bottom, float width, float height)
    {
        float mid = (TopMid(top).Y + TopMid(bottom).Y) * 0.5f;
        return new Rect2(x, mid - height * 0.5f, width, height);
    }

    private void Join(Rect2 top, Rect2 bottom, Rect2 dest)
    {
        Vector2 a = RightMid(top);
        Vector2 b = RightMid(bottom);
        Vector2 d = LeftMid(dest);
        float spine = Mathf.Clamp((Mathf.Max(a.X, b.X) + d.X) * 0.5f, Mathf.Max(a.X, b.X) + 8f, d.X - 6f);
        DrawLine(a, new Vector2(spine, a.Y), LineColor, 2f);
        DrawLine(b, new Vector2(spine, b.Y), LineColor, 2f);
        DrawLine(new Vector2(spine, a.Y), new Vector2(spine, b.Y), LineColor, 2f);
        DrawLine(new Vector2(spine, d.Y), d, LineColor, 2f);
    }

    private int LoserOf(int matchId)
    {
        if (_board == null || matchId < 0 || matchId >= _board.Matches.Count)
            return -1;
        TournamentMatch match = _board.Matches[matchId];
        if (!match.Played || match.Winner < 0)
            return -1;
        return match.Winner == match.Left ? match.Right : match.Left;
    }

    private static int SlotOf(int slot) => slot;

    private static Vector2 RightMid(Rect2 rect) => new(rect.End.X, rect.Position.Y + rect.Size.Y * 0.5f);

    private static Vector2 LeftMid(Rect2 rect) => new(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f);

    private static Vector2 TopMid(Rect2 rect) => new(rect.Position.X + rect.Size.X * 0.5f, rect.Position.Y + rect.Size.Y * 0.5f);
}
