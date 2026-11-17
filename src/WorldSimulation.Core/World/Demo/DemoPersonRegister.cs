using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 14-1 章第 7 項：一位具名人物（代表人物）的永久紀錄。上任時登記，卸任（老死、餓死、天災、聚落無人）時補上卸任週與原因，
/// 之後不再更動、也不刪除——進入歷史的人物用實體編號永遠查得到（總綱第 4 節法則 1）。
/// <see cref="ExitWeek"/>＝0 表示仍在任。
/// </summary>
public sealed record DemoPersonRecord(long Id, string Name, DemoTempSex Sex, int BirthYear, DemoRepresentativeRole Role,
    int SettlementId, int AppointedWeek, int ExitWeek = 0, string ExitReason = "", long ParentId = 0, DemoTrait Traits = DemoTrait.None)
{
    public bool InOffice => ExitWeek == 0;
}

/// <summary>第 14-1 章第 7 項：人物登記表（只增不減）。純函式，不抽亂數。</summary>
public static class DemoPersonRegister
{
    public static ImmutableSortedDictionary<long, DemoPersonRecord> Empty { get; } = ImmutableSortedDictionary<long, DemoPersonRecord>.Empty;

    /// <summary>依本週週報的上任與卸任更新登記表：先登記上任，再補卸任（同一週上任又卸任也留得下來）。</summary>
    public static ImmutableSortedDictionary<long, DemoPersonRecord> Advance(
        ImmutableSortedDictionary<long, DemoPersonRecord> register, DemoWeekReport report,
        IReadOnlyCollection<DemoRepresentative>? inOffice = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(report);
        var builder = register.ToBuilder();
        foreach (var r in report.Appointments)
        {
            if (builder.ContainsKey(r.Id))
                throw new InvalidOperationException($"人物 #{r.Id} 重複登記（實體編號不重用）。");
            builder[r.Id] = new DemoPersonRecord(r.Id, r.Name, r.Sex, r.BirthYear, r.Role, r.SettlementId, r.AppointedWeek, ParentId: r.ParentId, Traits: r.Traits);
        }
        foreach (var x in report.RepresentativeExits)
        {
            // 加入登記表之前就在任的人物（舊存檔）沒有紀錄：卸任時無從補登，略過。
            if (builder.TryGetValue(x.Id, out var record) && record.InOffice)
                builder[x.Id] = record with { ExitWeek = report.Week, ExitReason = x.Reason };
        }
        // 保證「登記表上在任的人＝目前在任的代表人物」：任何沒有卸任紀錄就離開名單的人，在這一週補上卸任。
        if (inOffice is not null)
        {
            var current = inOffice.Select(r => r.Id).ToHashSet();
            foreach (var record in builder.Values.Where(p => p.InOffice && !current.Contains(p.Id)).ToList())
                builder[record.Id] = record with { ExitWeek = report.Week, ExitReason = "卸任" };
            foreach (var r in inOffice.Where(r => !builder.ContainsKey(r.Id)))
                builder[r.Id] = new DemoPersonRecord(r.Id, r.Name, r.Sex, r.BirthYear, r.Role, r.SettlementId, r.AppointedWeek, ParentId: r.ParentId, Traits: r.Traits);
        }
        return builder.ToImmutable();
    }

    /// <summary>從在任的代表人物建立登記表（新世界開局、或沒有登記表的舊存檔）。</summary>
    public static ImmutableSortedDictionary<long, DemoPersonRecord> FromInOffice(IEnumerable<DemoRepresentative> representatives)
        => representatives.ToImmutableSortedDictionary(r => r.Id,
            r => new DemoPersonRecord(r.Id, r.Name, r.Sex, r.BirthYear, r.Role, r.SettlementId, r.AppointedWeek, ParentId: r.ParentId, Traits: r.Traits));
}
