using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18d 章：地表套用第五、六批素材。地表方格（t_*，16×16、可無縫拼接）依地形、生態區、離岸距離、聚落地面與田的階段挑一組，
/// 同一組裡依方格的固定雜湊挑一張；方格內每個圖像素直接取素材的顏色（不再降彩度）。
/// 缺哪一組就退回原本程式的調色盤（<see cref="Ground"/>）。只改畫面。
/// </summary>
public partial class TerrainSurface
{
    /// <summary>一組地表方格（同一種地面的幾張變化）。</summary>
    private sealed class TileSet
    {
        private readonly PixelParts.Sprite[] _tiles;

        public TileSet(string prefix, int count)
        {
            var list = new List<PixelParts.Sprite>();
            if (count == 0)
            {
                if (PixelParts.Get(prefix) is { Width: TempTile, Height: TempTile } single)
                    list.Add(single);
            }
            else
                for (var k = 1; k <= count; k++)
                    if (PixelParts.Get($"{prefix}_{k}") is { Width: TempTile, Height: TempTile } tile)
                        list.Add(tile);
            _tiles = list.ToArray();
        }

        public bool Empty => _tiles.Length == 0;

        /// <summary>方格 (tx, ty) 裡圖像素 (x, y) 的顏色；沒有素材回傳 null。</summary>
        public Color? At(int tx, int ty, int x, int y, int salt)
        {
            if (_tiles.Length == 0)
                return null;
            var tile = _tiles[_tiles.Length == 1 ? 0 : Math.Min(_tiles.Length - 1, (int)(Hash(tx, ty, salt) * _tiles.Length))];
            return tile.At(x & (TempTile - 1), y & (TempTile - 1));
        }
    }

    /// <summary>全部地表方格（第一次用到時讀）。</summary>
    private sealed class GroundArt
    {
        // 第 09-1a 章：第十一批地面方格（b11_g_*）優先，沒有再用第五批（t_*）。
        public readonly TileSet Sea = Prefer("b11_g_water_sea", 2, "t_sea", 2), SeaShallow = Prefer("b11_g_water_shallow", 2, "t_sea_shallow", 2),
            SeaDeep = Prefer("b11_g_water_deep", 2, "t_sea_deep", 2);
        public readonly TileSet River = Prefer("b11_g_water_river", 2, "t_river", 2), RiverShallow = Prefer("b11_g_water_shallow", 2, "t_river_shallow", 2), Ford = new("t_ford_stones", 0);
        public readonly TileSet Sand = Prefer("b11_g_sand", 2, "t_sand", 3), SandWet = Prefer("b11_g_sand_wet", 1, "t_sand_wet", 2), Soil = new("t_soil", 3), Mud = new("t_mud", 2),
            Ash = Prefer("b11_g_ash", 1, "t_ash", 3);
        public readonly TileSet Hill = Prefer("b11_g_hill", 3, "t_ground_hill", 3);
        public readonly TileSet GrassTemperate = Prefer("b11_g_grass_temperate", 4, "t_grass_temperate", 4), GrassMeadow = Prefer("b11_g_grass_meadow", 4, "t_grass_meadow", 3),
            GrassSteppe = Prefer("b11_g_grass_steppe", 4, "t_grass_steppe", 3);
        public readonly TileSet FloorForest = Prefer("b11_g_forest_floor", 3, "t_floor_forest", 3), FloorConifer = new("t_floor_conifer", 3), FloorAutumn = new("t_floor_autumn", 3);
        public readonly TileSet PlazaDirt = Prefer("b11_g_plaza_dirt", 1, "t_plaza_dirt", 0), PlazaStone = Prefer("b11_g_plaza_stone", 2, "t_plaza_stone", 2),
            RoadDirt = Prefer("b11_g_road_dirt", 2, "t_road_dirt", 2), RoadCobble = Prefer("b11_g_cobble", 1, "t_road_cobble", 2), Yard = Prefer("b11_g_yard", 2, "t_yard", 0),
            Garden = new("b11_g_garden", 2);
        // 第 08-20f 章：農田（第十一批 → 第十批 → 第五批）。
        public readonly TileSet Wheat = Prefer3("b11_g_field_wheat", "b9_field_wheat", "t_field_wheat"), Flax = Prefer3("b11_g_field_flax", "b9_field_flax", "t_field_flax"),
            Vegetable = Prefer3("b11_g_field_vegetable", "b9_field_vegetable", "t_field_vegetable"), Green = Prefer3("b11_g_field_green", "b9_field_wheat_green", "t_field_green"),
            Plowed = Prefer3("b11_g_field_plowed", "b9_field_plowed", "t_field_plowed"), Sown = Prefer3("b11_g_field_sown", "b9_field_sown", "t_field_sown"),
            Sprout = Prefer3("b11_g_field_sprout", "b9_field_sprout", "t_field_sprout"), Ridge = new("t_field_ridge", 0);

        private static TileSet Prefer(string fresh, int freshCount, string old, int oldCount)
            => new TileSet(fresh, freshCount) is { Empty: false } set ? set : new TileSet(old, oldCount);

        private static TileSet Prefer3(string a, string b, string c)
            => new TileSet(a, 0) is { Empty: false } x ? x : new TileSet(b, 0) is { Empty: false } y ? y : new TileSet(c, 0);

        public readonly TileSet FrontDirt = Prefer("b11_g_road_dirt", 2, "b9_road_dirt", 2), FrontStone = Prefer("b11_g_road_stone", 2, "b9_road_stone", 2),
            FrontBrick = Prefer("b11_g_road_brick", 2, "b9_road_brick", 2); // 第 08-20e 章；第 09-1a 章第十一批優先

        public TileSet Grass(Biome biome) => biome switch
        {
            Biome.Meadow => GrassMeadow,
            Biome.Steppe => GrassSteppe,
            _ => GrassTemperate
        };

        public TileSet Floor(Biome biome) => biome switch
        {
            Biome.Conifer => FloorConifer,
            Biome.Autumn => FloorAutumn,
            _ => FloorForest
        };
    }

    private GroundArt? _art;
    private GroundArt Art => _art ??= new GroundArt();

    /// <summary>剛才 <see cref="Ground"/> 回傳的顏色是不是直接取自素材（是就不再降彩度）。</summary>
    /// 第 09-3 章：地面平行計算，每個執行緒各記各的。
    [ThreadStatic]
    private static bool _groundRaw;

    /// <summary>取素材顏色；有素材時記下「這是素材色」。</summary>
    private Color? Tex(TileSet set, int tx, int ty, int x, int y, int salt = 3)
    {
        if (set.At(tx, ty, x, y, salt) is not { } c)
            return null;
        _groundRaw = true;
        return c;
    }

    /// <summary>第 08-18d 章：樹的素材名（依生態區；缺少時回傳 null，改用舊素材或程式畫的）。</summary>
    private static PixelParts.Sprite? TreeArt(Tree tree, Biome biome, int tx, int ty)
    {
        var h = Hash(tx, ty, 28);
        if (tree.Small)
            return PixelParts.Variant("px_bush", 4, h);
        if (tree.Pine)
            return PixelParts.Variant("px_tree_conifer", 4, h);
        return biome switch
        {
            Biome.Autumn => h < 0.5f ? PixelParts.Variant("px_tree_autumn_red", 3, h * 2f) : PixelParts.Variant("px_tree_autumn_gold", 3, h * 2f - 1f),
            Biome.Meadow when Hash(tx, ty, 29) < 0.35f => PixelParts.Variant("px_tree_birch", 2, h),
            _ => PixelParts.Variant("px_tree_broad", 4, h)
        };
    }

    /// <summary>樹在縮圖上的色點：素材樹冠中段的顏色（沒有素材用程式調色盤）。</summary>
    private static readonly Dictionary<PixelParts.Sprite, Color> CanopyColors = new(ReferenceEqualityComparer.Instance);

    private static Color? Canopy(PixelParts.Sprite? sprite)
    {
        if (sprite is null)
            return null;
        if (CanopyColors.TryGetValue(sprite, out var known))
            return known;
        float r = 0, g = 0, b = 0;
        var n = 0;
        for (var y = sprite.Height / 4; y < sprite.Height / 2; y++)
            for (var x = sprite.Width / 4; x < sprite.Width * 3 / 4; x++)
            {
                var c = sprite.At(x, y);
                if (c.A < 0.5f)
                    continue;
                r += c.R; g += c.G; b += c.B;
                n++;
            }
        var color = n == 0 ? new Color(0.2f, 0.4f, 0.2f) : new Color(r / n, g / n, b / n);
        CanopyColors[sprite] = color;
        return color;
    }

    /// <summary>陸地的素材底色；這種地面沒有素材回傳 null。</summary>
    private Color? LandArt(Code code, byte fourth, Biome biome, int tx, int ty, int x, int y)
    {
        var art = Art;
        return code switch
        {
            Code.Forest or Code.Logged => Tex(art.Floor(biome), tx, ty, x, y),
            // 試玩回饋 2026-11-08 之 5：山脈核心與礦床是岩石地面，外圍丘陵是稍暗的草地（看得出高起來）。
            Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit when RockyTile(tx, ty)
                || code is not Code.Hills && OreClear(tx, ty) => Tex(art.Hill, tx, ty, x, y), // 礦床的岩石地鋪滿中央 4×4 方格，整堆礦石與礦坑都站在岩石上
            Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit => Tex(art.Grass(biome), tx, ty, x, y) is { } grass ? grass.Darkened(0.12f) : null,
            // 第 08-6b 章：田改由聚落版面畫在城牆外一圈（ground 的田地代碼）；地表上的田格本身畫成草地。
            Code.Farmland => Tex(art.Grass(biome), tx, ty, x, y),
            Code.Clearing when fourth == SettlementGround => Tex(art.Grass(biome), tx, ty, x, y),
            Code.Clearing => fourth > 0 ? Tex(art.Grass(biome), tx, ty, x, y) : Tex(art.Soil, tx, ty, x, y),
            Code.DryGrass => Tex(art.GrassSteppe, tx, ty, x, y),
            Code.Burning or Code.Scorched => Tex(art.Ash, tx, ty, x, y),
            _ => Tex(art.Grass(biome), tx, ty, x, y)
        };
    }

}
