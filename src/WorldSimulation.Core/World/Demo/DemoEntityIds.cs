using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// C04（P1.0.03-a）：示範世界記錄欄位仍為 int（聚落／資源點／人口群體／隊伍）與 long（個人），
/// 但數值一律只能由世界唯一的 <see cref="EntityIdAllocator"/> 發出的 <see cref="EntityId"/> 經這裡的 checked 轉換產生。
/// </summary>
internal static class DemoEntityIds
{
    /// <summary>新的分配器快照（下一號為 1）。</summary>
    public static EntityIdAllocatorSnapshot Fresh => new EntityIdAllocator().Capture();

    public static int NextInt(EntityIdAllocator ids) => ToInt(ids.Allocate());

    public static long NextLong(EntityIdAllocator ids) => ToLong(ids.Allocate());

    public static int ToInt(EntityId id) => checked((int)id.Value);

    public static long ToLong(EntityId id) => checked((long)id.Value);

    /// <summary>只供無地圖的相容結算：下一號接在這份示範地點既有全部 ID 之後。</summary>
    public static EntityIdAllocatorSnapshot After(DemoWorldSites sites)
        => new(EntityIdAllocatorSnapshot.CurrentVersion,
            checked((ulong)sites.EntityIds().DefaultIfEmpty(0).Max() + 1UL), false);

    /// <summary>快照的下一號（long，供週報與存檔比對）。</summary>
    public static long NextValue(EntityIdAllocatorSnapshot snapshot) => checked((long)snapshot.NextValue);
}
