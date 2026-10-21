using System.Text.Json;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 06 章 d 節：TEMP 生存參數（config/demo/survival-temp.json）＋29 數值參數的缺水倍率與週腐敗率。
/// 只用於森林／河流自然採集世界（共享採集情境）；舊示範情境不變（第 08 章退場）。
/// </summary>
public sealed record DemoSurvivalTempSettings(
    int SchemaVersion,
    string Status,
    int ChildFoodPercent,
    int PeoplePerWaterCarrier,
    int ElderAgeYears,
    int ChildStarvationWeight,
    int AdultStarvationWeight,
    int ElderStarvationWeight)
{
    public static DemoSurvivalTempSettings Current { get; } = Load();

    /// <summary>29 `P_WATER_DEHYDRATION_RATE`【TEMP】：缺水惡化速度相對飢餓的倍率。</summary>
    public static decimal DehydrationRate => OfficialConfig.Current.Tunable.GetNumber("P_WATER_DEHYDRATION_RATE");

    /// <summary>29 `P_FOOD_LOSS_WEEK`（未平衡）：聚落存糧每週腐敗比例。</summary>
    public static decimal FoodLossPerWeek => OfficialConfig.Current.Tunable.GetNumber("P_FOOD_LOSS_WEEK");

    private static DemoSurvivalTempSettings Load()
    {
        var settings = JsonSerializer.Deserialize<DemoSurvivalTempSettings>(OfficialConfig.ReadDemoText("survival-temp.json"))
            ?? throw new InvalidOperationException("config/demo/survival-temp.json 讀不到。");
        if (settings.SchemaVersion != 1 || settings.Status != "TEMP"
            || settings.ChildFoodPercent is < 0 or > 100 || settings.PeoplePerWaterCarrier < 1 || settings.ElderAgeYears < 1
            || settings.ChildStarvationWeight < 1 || settings.AdultStarvationWeight < 1 || settings.ElderStarvationWeight < 1)
            throw new InvalidOperationException("config/demo/survival-temp.json 內容不合法。");
        return settings;
    }

    /// <summary>一週食物需求：成年人每人全份，未成年每人 ChildFoodPercent%（向下取整到整數單位）。</summary>
    public long Demand(long adults, long children, long foodPerPerson)
        => checked(adults * foodPerPerson + children * foodPerPerson * ChildFoodPercent / 100);

    /// <summary>需要的取水者人數（每 PeoplePerWaterCarrier 人 1 名，無條件進位；沒有人＝0）。</summary>
    public long CarriersNeeded(long people) => people <= 0 ? 0 : (people + PeoplePerWaterCarrier - 1) / PeoplePerWaterCarrier;

    /// <summary>腐敗量＝floor(期初存糧 × 週腐敗率)。</summary>
    public static long Spoilage(long foodOpening) => (long)Math.Floor(foodOpening * FoodLossPerWeek);

    /// <summary>缺水造成的生存債＝未取到水的人數 × 每人週食量 × 缺水倍率（向下取整）。</summary>
    public static long DehydrationDebt(long peopleWithoutWater, long foodPerPerson)
        => (long)Math.Floor(peopleWithoutWater * foodPerPerson * DehydrationRate);
}
