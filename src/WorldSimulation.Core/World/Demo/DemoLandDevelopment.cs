using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08 章 a 節：土地開發狀態（與地形分離）。地形說「這格是草原」，開發狀態說「這格有沒有被人開墾」。
/// 農田不是草原自動生成的：只有真實的開墾（勞力、材料、時間）能把格子推進到開墾中、農田。
/// 本章只建模型與畫面辨識，不產糧；產量上限由持有的農業知識決定，第 11 章接上。
/// </summary>
public enum DemoLandDevelopment
{
    Undeveloped = 0,
    /// <summary>開墾中：已有人投入勞力，尚未成為農田。</summary>
    Clearing = 1,
    /// <summary>農田：不再提供野採（草原野採來源移除），只產農糧（第 11 章才啟用）。</summary>
    Farmland = 2
}

/// <summary>一格的開發狀態與開發程度（0～100%）。未開發的格不列出。</summary>
public sealed record DemoLandParcel(int Column, int Row, DemoLandDevelopment State, int DevelopmentPercent)
{
    public DemoCellRef Cell => new(Column, Row);
}

/// <summary>
/// 第 08 章 a 節：全世界的土地開發狀態（不可變）。只有草原、非聚落格可以開發；
/// 開墾中 1～99%，農田 1～100%（開發程度＝農田完成度，供日後依知識上限算產量）。
/// 目前沒有任何合法的開墾行動，所以世界裡永遠是空的；讀檔時非空即拒讀（時間或人口不會自動變農田）。
/// </summary>
public sealed class DemoLandDevelopmentMap
{
    public static DemoLandDevelopmentMap Empty { get; } = new(ImmutableSortedDictionary<(int Row, int Column), DemoLandParcel>.Empty);

    private readonly ImmutableSortedDictionary<(int Row, int Column), DemoLandParcel> _parcels;

    private DemoLandDevelopmentMap(ImmutableSortedDictionary<(int Row, int Column), DemoLandParcel> parcels)
        => _parcels = parcels;

    /// <summary>依列、欄排序的已開發格。</summary>
    public IReadOnlyList<DemoLandParcel> Parcels => _parcels.Values.ToArray();

    public DemoLandDevelopment StateOf(DemoCellRef cell)
        => _parcels.TryGetValue((cell.Row, cell.Column), out var p) ? p.State : DemoLandDevelopment.Undeveloped;

    public DemoLandParcel? ParcelAt(DemoCellRef cell)
        => _parcels.TryGetValue((cell.Row, cell.Column), out var p) ? p : null;

    /// <summary>農田格（不再提供野採）。</summary>
    public IReadOnlyList<DemoCellRef> FarmlandCells()
        => _parcels.Values.Where(p => p.State == DemoLandDevelopment.Farmland).Select(p => p.Cell).ToArray();

    /// <summary>設定一格的開發狀態（純函式，回傳新地圖）；不合法即丟例外。</summary>
    public DemoLandDevelopmentMap With(DemoHexWorld map, DemoWorldSites? sites, DemoLandParcel parcel)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(parcel);
        var cell = map.GetCell(parcel.Column, parcel.Row);
        if (cell.Terrain != DemoTerrain.Grassland)
            throw new InvalidOperationException("只有草原格可以開墾成農田。");
        if (sites?.SettlementAt(parcel.Column, parcel.Row) is { IsRuin: false })
            throw new InvalidOperationException("聚落所在格不能開墾成農田。");
        var valid = parcel.State switch
        {
            DemoLandDevelopment.Undeveloped => parcel.DevelopmentPercent == 0,
            DemoLandDevelopment.Clearing => parcel.DevelopmentPercent is >= 1 and <= 99,
            DemoLandDevelopment.Farmland => parcel.DevelopmentPercent is >= 1 and <= 100,
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException("土地開發狀態與開發程度不相符。");
        var key = (parcel.Row, parcel.Column);
        return new DemoLandDevelopmentMap(parcel.State == DemoLandDevelopment.Undeveloped
            ? _parcels.Remove(key)
            : _parcels.SetItem(key, parcel));
    }

    /// <summary>決定性文字指紋。</summary>
    internal string Fingerprint() => string.Join(";", _parcels.Values.Select(p => $"{p.Column},{p.Row}:{p.State}:{p.DevelopmentPercent}"));
}
