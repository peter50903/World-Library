using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-17 章第 5 節：人以指示物（棋子）表示——依功能分組、大小表示人數（每十倍一級）。只改畫面。
/// 戰略層：每個聚落一枚居民棋（大小＝規模）；區域層：再加農民、樵夫、礦工、獵人、漁民與遷徙隊伍，工作的棋子在聚落與工作地之間來回；
/// 近景層不畫指示物（使用者 2026-11-05）。人數是由聚落人口推得的示意比例【TEMP】，不是模擬裡的職業帳。
/// </summary>
public partial class DemoHexMap
{
    public enum TokenKind { Residents, Farmers, Woodcutters, Miners, Hunters, Fishers, Migrants, Founders }

    /// <summary>一枚指示物：種類、人數、從哪一格到哪一格（居民棋兩者相同）。</summary>
    public readonly record struct Token(int SettlementId, TokenKind Kind, long People, DemoCellRef Home, DemoCellRef Work);

    /// <summary>第 08-17 章：會狩獵的聚落（由觀察者介面注入）。</summary>
    public IReadOnlySet<int> Hunters { get; set; } = new HashSet<int>();

    /// <summary>【TEMP】各職業佔聚落人口的示意比例（%）；每個聚落最多幾枚；往返一趟幾秒。</summary>
    private const int TempFarmerPercent = 40, TempWoodcutterPercent = 10, TempMinerPercent = 10, TempHunterPercent = 10, TempFisherPercent = 15;
    private const int TempMaxTokens = 6;
    private const float TempTokenTripSeconds = 4f;

    private readonly List<Token> _tokens = new();
    private (object?, object?, object?, object?, object?, object?, int) _tokenKey;
    private bool _tokensMoving;

    /// <summary>人數級距：每十倍一級（1～50、51～500、501～5,000、5,001～50,000、50,000 以上）。</summary>
    public static int TokenLevel(long people) => people <= 50 ? 0 : people <= 500 ? 1 : people <= 5_000 ? 2 : people <= 50_000 ? 3 : 4;

    /// <summary>人數標籤：千以上寫「1.2千」、萬以上寫「3.4萬」。</summary>
    public static string TokenCount(long people) => people < 1_000 ? people.ToString()
        : people < 10_000 ? $"{people / 100 / 10f:0.#}千" : $"{people / 1_000 / 10f:0.#}萬";

    /// <summary>每個聚落的指示物（聚落、規模、領域、土地或伐木採礦改變時重算）。</summary>
    public IReadOnlyList<Token> Tokens()
    {
        var key = ((object?)Sites, (object?)SettlementScale, (object?)Lineage, (object?)Land, (object?)Spells, (object?)Hunters, CurrentWeek);
        if (key.Equals(_tokenKey))
            return _tokens;
        _tokenKey = key;
        _tokens.Clear();
        if (World is null || Sites is null)
            return _tokens;
        foreach (var site in Sites.Settlements)
        {
            if (site.IsRuin || FadedRuins.Contains(site.Id) || !SettlementScale.TryGetValue(site.Id, out var scale) || scale.Item1 <= 0)
                continue;
            var people = scale.Item1;
            var home = new DemoCellRef(site.Column, site.Row);
            var list = new List<Token> { new(site.Id, TokenKind.Residents, people, home, home) };
            long Share(int percent) => Math.Max(1, people * percent / 100);
            var origin = World.GetCell(site.Column, site.Row);
            IEnumerable<DemoHexCell> Near(int range) => World.Within(origin, range).Where(c => !ReferenceEquals(c, origin)); // 第 08-18a 章：方形範圍
            // 農民：最近的一塊自家農田。
            var farm = Near(2).Where(c => Land.StateOf(new DemoCellRef(c.Column, c.Row)) == DemoLandDevelopment.Farmland
                    && (!Lineage.TryGetValue(new DemoCellRef(c.Column, c.Row), out var o) || o.SettlementId == site.Id))
                .OrderBy(c => DemoWorldSites.HexDistance(origin, c)).ThenBy(c => c.Row).ThenBy(c => c.Column).FirstOrDefault();
            if (farm is not null)
                list.Add(new(site.Id, TokenKind.Farmers, Share(TempFarmerPercent), home, new DemoCellRef(farm.Column, farm.Row)));
            // 樵夫、礦工：進行中的伐木、採礦（第 08-16 章第 14 節）。
            foreach (var spell in Spells)
            {
                if (spell.SettlementId != site.Id || CurrentWeek <= spell.FromWeek || CurrentWeek > spell.ToWeek || spell.Cells.Count == 0)
                    continue;
                if (spell.Kind == DemoSettlementEventKind.Logging && list.All(t => t.Kind != TokenKind.Woodcutters))
                    list.Add(new(site.Id, TokenKind.Woodcutters, Share(TempWoodcutterPercent), home, spell.Cells[0]));
                else if (spell.Kind == DemoSettlementEventKind.MiningBegan && list.All(t => t.Kind != TokenKind.Miners))
                    list.Add(new(site.Id, TokenKind.Miners, Share(TempMinerPercent), home, spell.Cells[0]));
            }
            // 獵人：會狩獵、附近有森林。
            if (Hunters.Contains(site.Id) && Near(2).Where(c => c.Terrain == DemoTerrain.Forest)
                    .OrderBy(c => DemoWorldSites.HexDistance(origin, c)).ThenBy(c => (c.Row * 7 + c.Column * 3 + site.Id) % 5).FirstOrDefault() is { } wood)
                list.Add(new(site.Id, TokenKind.Hunters, Share(TempHunterPercent), home, new DemoCellRef(wood.Column, wood.Row)));
            // 漁民：臨河或臨海。
            if (Near(1).Where(c => c.Terrain == DemoTerrain.Water).OrderBy(c => c.Row).ThenBy(c => c.Column).FirstOrDefault() is { } water)
                list.Add(new(site.Id, TokenKind.Fishers, Share(TempFisherPercent), home, new DemoCellRef(water.Column, water.Row)));
            _tokens.AddRange(list.Take(TempMaxTokens));
        }
        return _tokens;
    }

    private static string TokenGlyph(TokenKind kind, DemoSettlementTier tier) => kind switch
    {
        TokenKind.Residents => tier switch
        {
            DemoSettlementTier.Camp => "營", DemoSettlementTier.Village => "村", DemoSettlementTier.Town => "鎮",
            DemoSettlementTier.City => "城", _ => "都"
        },
        TokenKind.Farmers => "農",
        TokenKind.Woodcutters => "樵",
        TokenKind.Miners => "礦",
        TokenKind.Hunters => "獵",
        TokenKind.Fishers => "漁",
        TokenKind.Founders => "拓",
        _ => "遷"
    };

    /// <summary>指示物在畫面上的半徑（像素）：隨縮放略變、隨人數每級加大兩成。</summary>
    private static float TokenRadius(float radius, int level) => Math.Clamp(radius * 0.42f, 3.5f, 8.5f) * (1f + 0.22f * level);

    private Vector2 CellScreen(Vector2 start, float radius, DemoCellRef c)
    {
        var cell = World!.GetCell(c.Column, c.Row);
        return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
    }

    /// <summary>戰略層、區域層的指示物（近景層不畫）。</summary>
    private void DrawTokens(Vector2 start, float radius, ViewLayer layer)
    {
        if (World is null || Sites is null || layer == ViewLayer.Local)
            return;
        var seconds = Time.GetTicksMsec() / 1000f;
        var regional = layer == ViewLayer.Regional;
        var work = regional && _mode == Mode.Terrain;
        var moving = false;
        foreach (var token in Tokens())
        {
            if (token.Kind != TokenKind.Residents && !work)
                continue;
            var home = CellScreen(start, radius, token.Home);
            if (!OnScreen(home, radius * 4f))
                continue;
            var at = home;
            if (token.Kind != TokenKind.Residents)
            {
                // 在聚落與工作地之間來回（每枚錯開一點時間）；停在工作地的時間比較長。
                var target = CellScreen(start, radius, token.Work);
                var phase = (token.SettlementId * 0.37f + (int)token.Kind * 0.19f) % 1f;
                var t = (1f - Mathf.Cos(Mathf.Tau * (seconds / TempTokenTripSeconds + phase))) / 2f;
                at = home.Lerp(target, 0.25f + 0.75f * Mathf.Clamp(t * 1.3f, 0f, 1f));
                moving = true;
            }
            var tier = SettlementScale.TryGetValue(token.SettlementId, out var scale) ? scale.Item2 : DemoSettlementTier.Camp;
            var showCount = regional || TokenLevel(token.People) >= 2;
            DrawToken(at, radius, token.Kind, token.People, tier, CultureOf.TryGetValue(token.SettlementId, out var culture) ? CultureColor(culture) : (Color?)null, showCount);
        }
        if (work)
            foreach (var batch in Migrants)
            {
                var at = MigrantPosition(start, radius, batch);
                if (!OnScreen(at, radius * 3f))
                    continue;
                DrawToken(at, radius, batch.Founding ? TokenKind.Founders : TokenKind.Migrants, batch.People, DemoSettlementTier.Camp, null, true);
                moving = true;
            }
        _tokensMoving = moving;
    }

    /// <summary>一枚棋子：落地陰影、圓形底座（外圈文化色；最高一級加金邊）、職業字、下方人數。</summary>
    private void DrawToken(Vector2 at, float radius, TokenKind kind, long people, DemoSettlementTier tier, Color? rim, bool showCount)
    {
        var level = TokenLevel(people);
        var r = TokenRadius(radius, level);
        var residents = kind == TokenKind.Residents;
        var fill = residents ? new Color(0.93f, 0.78f, 0.42f) : kind is TokenKind.Migrants or TokenKind.Founders ? new Color(0.86f, 0.38f, 0.26f) : new Color(0.22f, 0.18f, 0.14f);
        var ink = residents ? new Color(0.20f, 0.12f, 0.05f) : Colors.White;
        DrawCircle(at + new Vector2(r * 0.18f, r * 0.22f), r * 1.05f, new Color(0f, 0f, 0f, 0.35f)); // 陰影
        var font = ThemeDB.FallbackFont;
        var tokenBase = PixelParts.IsBatch5("tok_base") ? PixelParts.Tinted("tok_base", rim ?? new Color(0.55f, 0.50f, 0.42f)) : null;
        if (tokenBase is not null && PixelParts.Texture(TokenArt(kind, tier)) is { } icon)
        {
            // 第 08-18d 章：第五批棋子——木質底座（外圈染文化色）、職業或聚落層級圖示、最高一級加金邊；直徑照指示物的級距。
            var d = 2f * (r + 1.2f);
            if (level >= 4 && PixelParts.Texture("tok_rim_gold") is { } gold)
                DrawTextureRect(gold, new Rect2(at - Vector2.One * d * 52f / 96f, Vector2.One * d * 52f / 48f), false);
            DrawTextureRect(tokenBase, new Rect2(at - Vector2.One * d / 2f, Vector2.One * d), false);
            DrawTextureRect(icon, new Rect2(at - Vector2.One * d / 3f, Vector2.One * d * 2f / 3f), false);
        }
        else
        {
            if (level >= 4)
                DrawCircle(at, r + 2.5f, new Color(0.98f, 0.82f, 0.30f)); // 金邊
            DrawCircle(at, r + 1.2f, rim ?? new Color(0.10f, 0.08f, 0.06f));
            DrawCircle(at, r, fill);
        }
        if (tokenBase is null && r >= 5.5f)
        {
            var size = Math.Max(8, (int)(r * 1.25f));
            var glyph = TokenGlyph(kind, tier);
            var width = font.GetStringSize(glyph, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, at + new Vector2(-width / 2f, size * 0.36f), glyph, HorizontalAlignment.Left, -1, size, ink);
        }
        if (showCount && r >= 4.5f)
        {
            var size = Math.Max(8, (int)(r * 0.95f));
            var text = TokenCount(people);
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            var pos = at + new Vector2(-width / 2f, r + size + 1f);
            DrawRect(new Rect2(pos + new Vector2(-2f, -size), new Vector2(width + 4f, size + 3f)), new Color(0.08f, 0.06f, 0.05f, 0.75f));
            DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }

    /// <summary>第 08-18d 章：棋子中央的圖示（居民棋依聚落層級，其他依職業）。</summary>
    private static string TokenArt(TokenKind kind, DemoSettlementTier tier) => kind switch
    {
        TokenKind.Farmers => "tok_farmer",
        TokenKind.Woodcutters => "tok_woodcutter",
        TokenKind.Miners => "tok_miner",
        TokenKind.Hunters => "tok_hunter",
        TokenKind.Fishers => "tok_fisher",
        TokenKind.Migrants => "tok_migrants",
        TokenKind.Founders => "tok_settlers",
        _ => tier switch
        {
            DemoSettlementTier.Camp => "tok_camp",
            DemoSettlementTier.Village => "tok_village",
            DemoSettlementTier.Town => "tok_town",
            DemoSettlementTier.City => "tok_city",
            _ => "tok_capital"
        }
    };
}
