using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-51 章（使用者 2026-11-18：遊戲的兩個重點是文明發展與人類和怪物的領地爭奪）：人類與怪物爭地看得見。
/// 遠征軍的棋子沿虛線一週一週走向地下城，到了守在地下城旁，上方寫「遠征軍攻到第 2/3 層」與層數格；
/// 獸潮的怪物棋子從地下城走向被盯上的聚落（紅色虛線、「獸潮→某村・還要 2 週」）；
/// 怪物地盤變大（新佔的格閃紅）或縮小（收復的格閃金）時閃一下。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】遠征軍與獸潮的顏色、地盤變化閃多久（毫秒）、一次最多閃幾格。</summary>
    private static readonly Color TempExpeditionColor = new(0.42f, 0.64f, 0.95f), TempSwarmColor = new(0.88f, 0.22f, 0.30f);
    private const float TempFlashMs = 2600f;
    private const int TempMaxFlashes = 600;

    private readonly Dictionary<(long Cell, bool Swarm), (float From, float To, ulong Ms)> _tripMoves = new();
    private readonly List<(DemoCellRef Cell, bool Taken, ulong Ms)> _flashes = new();
    private HashSet<DemoCellRef> _monsterCells = new();
    private object? _flashWorld;

    private DemoSettlement? SiteOf(int id) => Sites?.Settlements.FirstOrDefault(s => s.Id == id);

    /// <summary>行動走到幾成（上一週的位置慢慢走到這一週，同 08-46 的軍隊棋子）。</summary>
    private float TripProgress((long, bool) key, float progress)
    {
        var now = Time.GetTicksMsec();
        if (!_tripMoves.TryGetValue(key, out var move) || Math.Abs(move.To - progress) > 0.0001f)
        {
            var from = _tripMoves.TryGetValue(key, out var old) && old.To <= progress ? old.To : progress; // 新的一趟從起點開始
            move = (from, progress, now);
            _tripMoves[key] = move;
        }
        var t = Math.Min(1f, (now - move.Ms) / TempArmyMoveMs);
        if (t < 1f)
            _animatedOnScreen = true;
        return Mathf.Lerp(move.From, move.To, Ease(t));
    }

    /// <summary>每次換上新的怪物資料時，比對怪物地盤：新佔的格與收復的格記下來閃一下（換世界、第一次給時不閃）。</summary>
    private void NoteTerritoryChanges()
    {
        var now = MonsterNests.Where(n => !n.Dormant).SelectMany(n => n.Cells).ToHashSet();
        if (!ReferenceEquals(_flashWorld, World))
        {
            _flashWorld = World;
            _flashes.Clear();
            _tripMoves.Clear();
            _monsterCells = now;
            return;
        }
        var ms = Time.GetTicksMsec();
        foreach (var cell in now.Where(c => !_monsterCells.Contains(c)))
            _flashes.Add((cell, true, ms));
        foreach (var cell in _monsterCells.Where(c => !now.Contains(c)))
            _flashes.Add((cell, false, ms));
        if (_flashes.Count > TempMaxFlashes)
            _flashes.RemoveRange(0, _flashes.Count - TempMaxFlashes);
        _monsterCells = now;
    }

    /// <summary>層數格：攻下（打過）的層填滿遠征軍的顏色。</summary>
    private void DrawLayerPips(Vector2 center, int layers, int done)
    {
        const float size = 8f, gap = 3f;
        var width = layers * size + (layers - 1) * gap;
        var left = center.X - width / 2f;
        for (var i = 0; i < layers; i++)
        {
            var rect = new Rect2(new Vector2(left + i * (size + gap), center.Y - size / 2f), new Vector2(size, size));
            DrawRect(rect.Grow(1.5f), new Color(0.06f, 0.04f, 0.03f, 0.9f));
            DrawRect(rect, i < done ? TempExpeditionColor.Lightened(0.2f) : new Color(0.28f, 0.22f, 0.18f, 0.9f));
        }
    }

    /// <summary>第 08-51 章：遠征軍、獸潮與地盤的變化（畫在戰線之後、戰鬥標記之前）。</summary>
    private void DrawMonsterFronts(Vector2 start, float radius)
    {
        if (World is null)
            return;
        DrawTerritoryFlash(start, radius);
        if (MonsterNests.Count == 0)
            return;
        var font = ThemeDB.FallbackFont;
        var seconds = Time.GetTicksMsec() / 1000f;
        var view = new Rect2(Vector2.Zero, Size).Grow(radius * 4f + 80f);
        foreach (var nest in MonsterNests)
        {
            if (!World.Contains(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row))
                continue;
            var lair = ScreenOf(start, radius, nest.Dungeon.Cell);
            if (nest.Expedition is { } exp && SiteOf(exp.SettlementId) is { } home && World.Contains(home.Column, home.Row))
            {
                var from = ScreenOf(start, radius, new DemoCellRef(home.Column, home.Row));
                if (view.HasPoint(from) || view.HasPoint(lair))
                {
                    _animatedOnScreen = true;
                    if (CurrentWeek < exp.ArriveWeek)
                    {
                        var at = from.Lerp(lair, TripProgress((nest.CellId, false), exp.Progress(CurrentWeek)));
                        var stop = at.DistanceTo(lair) > radius ? lair + (at - lair).Normalized() * radius * 0.7f : lair;
                        DrawMarchPath(new[] { at, stop }, TempExpeditionColor, seconds, 1f, true);
                        DrawArmyToken(font, at, TempExpeditionColor, 0, TokenKind.Army);
                        DrawTag(font, at + new Vector2(0f, -TempTokenHeight - 8f), $"遠征{nest.Dungeon.Name}・還要 {exp.ArriveWeek - CurrentWeek} 週", TempExpeditionColor);
                    }
                    else
                    {
                        var layers = nest.Dungeon.Layers;
                        var done = Math.Clamp(CurrentWeek - exp.ArriveWeek + 1, 1, layers);
                        DrawMarchPath(new[] { from, lair }, TempExpeditionColor, seconds, 0.35f, false); // 補給線（淡）
                        DrawArmyToken(font, lair + new Vector2(-Math.Max(16f, radius * 0.9f), 8f), TempExpeditionColor, 0, TokenKind.Army);
                        var top = lair - new Vector2(0f, Math.Max(26f, radius * 1.3f));
                        DrawLayerPips(top, layers, done);
                        DrawTag(font, top - new Vector2(0f, 14f), $"遠征軍攻到第 {done}/{layers} 層", TempExpeditionColor);
                    }
                }
            }
            if (nest.Swarm is { } wave && SiteOf(wave.SettlementId) is { } prey && World.Contains(prey.Column, prey.Row))
            {
                var to = ScreenOf(start, radius, new DemoCellRef(prey.Column, prey.Row));
                if (!view.HasPoint(to) && !view.HasPoint(lair))
                    continue;
                _animatedOnScreen = true;
                var at = lair.Lerp(to, TripProgress((nest.CellId, true), wave.Progress(CurrentWeek)));
                var stop = at.DistanceTo(to) > radius ? to + (at - to).Normalized() * radius * 0.7f : to;
                DrawMarchPath(new[] { at, stop }, TempSwarmColor, seconds, 1f, true);
                DrawArmyToken(font, at, TempSwarmColor, nest.Monsters, TokenKind.Swarm, 1.2f);
                DrawTag(font, at + new Vector2(0f, -TempTokenHeight * 1.2f - 8f), $"{(nest.AtSea ? "海怪潮" : "獸潮")}→{prey.Name}・還要 {Math.Max(1, wave.ArriveWeek - CurrentWeek)} 週", TempSwarmColor);
            }
        }
    }

    /// <summary>怪物地盤的變化：新佔的格閃紅、收復的格閃金，慢慢淡去。</summary>
    private void DrawTerritoryFlash(Vector2 start, float radius)
    {
        if (_flashes.Count == 0)
            return;
        var now = Time.GetTicksMsec();
        _flashes.RemoveAll(f => now - f.Ms > TempFlashMs);
        if (_flashes.Count == 0)
            return;
        _animatedOnScreen = true;
        var size = Math.Max(3f, radius * 0.85f);
        foreach (var (cell, taken, ms) in _flashes)
        {
            if (!World!.Contains(cell.Column, cell.Row))
                continue;
            var at = ScreenOf(start, radius, cell);
            if (!OnScreen(at, radius))
                continue;
            var fade = 1f - (now - ms) / TempFlashMs;
            var pulse = 0.6f + 0.4f * Mathf.Sin((now - ms) / 130f);
            var color = taken ? new Color(0.95f, 0.18f, 0.22f) : new Color(1f, 0.85f, 0.35f);
            DrawCircle(at, size, new Color(color, 0.45f * fade * pulse));
        }
    }

    /// <summary>第 08-51 章：滑鼠在地下城或被獸潮盯上的聚落時的說明（沒有＝空字串）。</summary>
    public string MonsterFrontTip(int column, int row)
    {
        var at = new DemoCellRef(column, row);
        var lines = new List<string>();
        foreach (var nest in MonsterNests)
        {
            if (nest.Expedition is { } exp && nest.Dungeon.Cell == at)
                lines.Add(CurrentWeek < exp.ArriveWeek
                    ? $"⚔ {SiteOf(exp.SettlementId)?.Name}的遠征軍正在路上，還要 {exp.ArriveWeek - CurrentWeek} 週"
                    : $"⚔ {SiteOf(exp.SettlementId)?.Name}的遠征軍攻到第 {Math.Clamp(CurrentWeek - exp.ArriveWeek + 1, 1, nest.Dungeon.Layers)}/{nest.Dungeon.Layers} 層（怪物剩 {nest.Monsters} 隻）");
            if (nest.Swarm is { } wave && (nest.Dungeon.Cell == at || SiteOf(wave.SettlementId) is { } prey && prey.Column == column && prey.Row == row))
                lines.Add($"☠ {nest.Dungeon.Name}的{(nest.AtSea ? "海怪潮" : "獸潮")}（{nest.Monsters} 隻）正朝{SiteOf(wave.SettlementId)?.Name}湧來，還要 {Math.Max(1, wave.ArriveWeek - CurrentWeek)} 週");
        }
        return string.Join("\n", lines);
    }
}
