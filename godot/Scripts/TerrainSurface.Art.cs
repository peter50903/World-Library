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
        public readonly TileSet Sea = new("t_sea", 2), SeaShallow = new("t_sea_shallow", 2), SeaDeep = new("t_sea_deep", 2);
        public readonly TileSet River = new("t_river", 2), RiverShallow = new("t_river_shallow", 2), Ford = new("t_ford_stones", 0);
        public readonly TileSet Sand = new("t_sand", 3), SandWet = new("t_sand_wet", 2), Soil = new("t_soil", 3), Mud = new("t_mud", 2), Ash = new("t_ash", 3);
        public readonly TileSet Hill = new("t_ground_hill", 3);
        public readonly TileSet GrassTemperate = new("t_grass_temperate", 4), GrassMeadow = new("t_grass_meadow", 3), GrassSteppe = new("t_grass_steppe", 3);
        public readonly TileSet FloorForest = new("t_floor_forest", 3), FloorConifer = new("t_floor_conifer", 3), FloorAutumn = new("t_floor_autumn", 3);
        public readonly TileSet PlazaDirt = new("t_plaza_dirt", 0), PlazaStone = new("t_plaza_stone", 2), RoadDirt = new("t_road_dirt", 2),
            RoadCobble = new("t_road_cobble", 2), Yard = new("t_yard", 0);
        public readonly TileSet Wheat = new("t_field_wheat", 0), Flax = new("t_field_flax", 0), Vegetable = new("t_field_vegetable", 0),
            Green = new("t_field_green", 0), Plowed = new("t_field_plowed", 0), Sown = new("t_field_sown", 0), Sprout = new("t_field_sprout", 0),
            Ridge = new("t_field_ridge", 0);

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
    private bool _groundRaw;

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

    /// <summary>陸地的素材底色（田、聚落空地交給 <see cref="Plot"/>）；這種地面沒有素材回傳 null。</summary>
    private Color? LandArt(Code code, byte fourth, Biome biome, int tx, int ty, int x, int y)
    {
        var art = Art;
        return code switch
        {
            Code.Forest or Code.Logged => Tex(art.Floor(biome), tx, ty, x, y),
            // 試玩回饋 2026-11-08 之 5：山脈核心與礦床是岩石地面，外圍丘陵是稍暗的草地（看得出高起來）。
            Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit when RockyTile(tx, ty)
                || code is not Code.Hills && OreSpot(tx, ty) => Tex(art.Hill, tx, ty, x, y),
            Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit => Tex(art.Grass(biome), tx, ty, x, y) is { } grass ? grass.Darkened(0.12f) : null,
            Code.Farmland => Plot(x, y) ?? Tex(art.Grass(biome), tx, ty, x, y),
            Code.Clearing when fourth == SettlementGround => Tex(art.Grass(biome), tx, ty, x, y),
            Code.Clearing => fourth > 0 ? Plot(x, y) ?? Tex(art.Grass(biome), tx, ty, x, y) : Tex(art.Soil, tx, ty, x, y),
            Code.DryGrass => Tex(art.GrassSteppe, tx, ty, x, y),
            Code.Burning or Code.Scorched => Tex(art.Ash, tx, ty, x, y),
            _ => Tex(art.Grass(biome), tx, ty, x, y)
        };
    }

    /// <summary>一塊田的素材色：熟田依雜湊種麥、菜、亞麻或綠作物；開墾中依進度翻土→播種→綠苗。
    /// 還沒開墾到這塊回傳透明色（畫草地）；沒有田的素材回傳 null。</summary>
    private Color? FieldArt(float dev, float p, int px, int py, int x, int y)
    {
        var art = Art;
        if (art.Wheat.Empty)
            return null;
        TileSet set;
        if (dev >= 1f)
            set = p < 0.55f ? art.Wheat : p < 0.75f ? art.Vegetable : p < 0.88f ? art.Flax : art.Green;
        else if (p > dev * 1.6f)
            return new Color(0f, 0f, 0f, 0f);
        else
            set = dev < 0.5f ? art.Plowed : dev < 0.8f ? art.Sown : art.Sprout;
        return Tex(set, px, py, x, y) ?? Tex(art.Wheat, px, py, x, y);
    }
}
