# TEMP 數值待使用者裁決清單（第一批）

> 議題 #75（V0-08B）。盤點基準：`main` `0f0e76f`（V0-01～V0-08A 已合併）。
> **本清單只整理、不改任何值。** 推薦只是建議，不改動世界；沒有採納前一律保留 `TEMP`。
> 使用者決定後，再依 Notion 07 §6 以獨立議題／合併請求同步 Notion 02、`config/` 正式設定檔與程式。
> 涵蓋檢查：`python3 tools/temp_inventory.py --check` 會確認 `src/`、`godot/` 每個含 `TEMP` 的檔案都列在附錄 A。

分類說明（依議題 #75 GPT 補充第 2 點）：

- **a｜設定檔已有正式值**（Enabled＝TRUE、非【TEMP】）：不列待裁決，只追蹤「程式尚未接入」（第 2 節）。
- **b｜待裁決**：值為 TEMP，Notion 02／設定檔沒有正式值（第 1 節）。
- **c｜配置／程式不一致風險**：程式與正式設定語義不同或互相衝突（第 2 節）；不替使用者決定啟用。
- **不需裁決**：只供測試或 CI 重現用的 fixture（種子、情境），不是玩家規則（第 3 節）。

---

## 1｜待裁決（b）

### 1.1 近期必決（影響 P1-M1 驗收判讀）

#### D1｜M1_STABILITY「穩定聚落可長期存在」的通過門檻

| 項目 | 內容 |
|---|---|
| 程式位置 | `src/WorldSimulation.ObserverCli/M1Acceptance.cs:908`（只列「期末有人聚落 x／y」，不判定） |
| 目前值 | 無門檻（TEMP：只列數據） |
| 設定檔現況 | `45G_M1驗收.csv` 第 9 列：條件「不是所有種子都因數值失控在 100 年內全滅或無限爆人口」，Warning；沒有量化門檻 |
| 影響 | `--m1-check` 報告中這一項永遠是「警告」，無法說 M1 穩定性通過 |
| 選項 | **A** 維持只列數據，等出生規則實作後再定門檻。<br>**B** 只檢查「全滅」：任一種子在一般情境 100 年內所有聚落全滅＝警告；不檢查爆量。<br>**C** 全滅與爆量都檢查；爆量倍數（期末／期初人口）需使用者給數字。 |
| 推薦 | **A**。目前示範規則出生固定為 0（`M1Acceptance.cs:565` 已標【覆蓋限制 TEMP】），人口只會減少，「無限爆人口」根本測不到；現在定門檻只會量到衰減速度。 |
| 定案後同步 | 45G 第 9 列「主要診斷」補門檻；`M1Acceptance.cs` 判定與測試 |

#### D2｜M1_VARIETY「不同環境有不同結果」的判定方式

| 項目 | 內容 |
|---|---|
| 程式位置 | `src/WorldSimulation.ObserverCli/M1Acceptance.cs:919`（只列數據，不以種子不同代替環境差異） |
| 目前值 | 無判定方式（TEMP） |
| 設定檔現況 | `45G_M1驗收.csv` 第 10 列：「資源／氣候／路線不同的地區應產生可觀察人口／產業差異」，Warning |
| 影響 | 報告中永遠是「警告」 |
| 選項 | **A** 質性列出：依資源點種類（農地／林地／漁場）分組列每組期末人口，不判定。<br>**B** 定量：分組人口差異超過某門檻才通過；門檻需使用者給數字。<br>**C** 維持現狀，等氣候與正式地區（54G 地形）實作後再定。 |
| 推薦 | **C**。示範地圖只有三種 TEMP 地形、沒有氣候與正式地區，任何差異指標都只反映 TEMP 再生量（D3）的差別，不是「環境」差異。 |
| 定案後同步 | 45G 第 10 列；`M1Acceptance.cs` 判定與測試 |

#### D3｜每週生存結算數值組（`DemoWeeklyTempConfig.Sample`）

| 參數 | 目前 TEMP 值 | 單位 | 程式位置 |
|---|---|---|---|
| 資源點初始存量 `InitialReservePerPoint` | 60 | 糧食單位 | `src/WorldSimulation.Core/World/Demo/DemoWeeklySettlement.cs:33` |
| 資源點存量上限 `ReserveCapPerPoint` | 120 | 糧食單位 | 同上 `:34` |
| 農地／林地／漁場每週再生 | 30／20／25 | 糧食單位／週 | 同上 `:35-37` |
| 每人每週產量 `YieldPerWorkerPerWeek` | 5 | 糧食單位／人／週 | 同上 `:38` |
| 每人每週消耗 `FoodPerPersonPerWeek` | 4 | 糧食單位／人／週 | 同上 `:39` |
| 聚落初始糧食 `InitialFoodPerPerson` | 7 | 糧食單位／人 | 同上 `:40` |

| 項目 | 內容 |
|---|---|
| 設定檔現況 | 01／29 沒有對應的聚落層產量、消耗、再生數值。29 只有 `P_FOOD_LOSS_YEAR`（0.5，未平衡）、`P_FOOD_LOSS_WEEK`（0.01324，未平衡）與觀察者倍率 `P_RENEWABLE_RESOURCE_REGEN_MULTIPLIER`（預設 1），程式都**尚未接入**；示範結算沒有腐敗 |
| 影響 | 決定一般情境下聚落是成長、持平還是慢性缺糧；M1 報告所有生存項目的實際數字 |
| 選項 | **A** 保留現值為 TEMP，等 M2 食物與市場模型時一併定案。<br>**B** 現在就把這組值當第一版正式值寫入 29（需使用者逐項確認單位「糧食單位」的意義）。<br>**C** 先接入既有的週腐敗 `P_FOOD_LOSS_WEEK`（另開任務），其餘保留 TEMP。 |
| 推薦 | **A**。「糧食單位」尚未對應正式食物定義（每種食物各有腐敗率，見 29 第 2 列說明），現在定案容易在 M2 推翻。C 可在 D1 解除後再考慮。 |
| 定案後同步 | Notion 02 生存／資源節；`29_數值參數.csv` 新列；`DemoWeeklySettlement.cs` 改讀設定；`tests/` 中依賴這些值的期望數字 |

#### D4｜飢餓債與餓死門檻

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 每多少飢餓債造成 1 人餓死 `HungerDebtPerStarvationDeath` | 12（糧食單位，聚落合計） | `DemoWeeklySettlement.cs:19`、使用於 `:504-523`、途中隊伍 `:402` |
| 每人每週飢餓債恢復 `HungerRecoveryPerPersonPerWeek` | 2 | `DemoWeeklySettlement.cs:20`、使用於 `:496` |

| 項目 | 內容 |
|---|---|
| 設定檔現況 | 29 第 38–39 列是**個人層、以日計**：`P_HUNGER_HP_SEVERE`（債務 3～7 時每日失去 1% 最大生命值）、`P_HUNGER_HP_CRITICAL`（債務 ≥7 時每日 4%），都「未平衡」。單位與層級和示範的聚落合計債務不同（見第 2 節 c2） |
| 影響 | 缺糧多久開始死人、死多快；遷徙與開拓的觸發時機 |
| 選項 | **A** 保留聚落層 TEMP 換算（12／2），直到個人層實作。<br>**B** 改成以 29 的個人債務階段做聚落層近似：以「每人平均債務天數」判斷，≥7 開始死亡（換算方式需另訂並經審查）。<br>**C** 需使用者裁決新的聚落層門檻數字。 |
| 推薦 | **A**。B 方向較貼近正式規則，但 29 兩列尚未平衡，且「債務單位」在個人層是天數、示範是糧食量，硬換算會把未定案的語義當正式；建議個人層（M2 家庭）時再接。 |
| 定案後同步 | Notion 02 生存節；29 第 38–39 列「是否已平衡」；`DemoWeeklySettlement.cs`；飢荒相關測試 |

### 1.2 可延後（不影響目前驗收判讀）

#### D5｜遷徙與消息參數

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 最少幾人才會外移 `MinPopulationToEmigrate` | 2 | `DemoWeeklySettlement.cs:22`、使用於 `:538` |
| 外移比例除數 `EmigrationShareDivisor`（倖存者的 1／N 出發） | 2（一半） | 同上 `:23`、`:553`、`:583` |
| 已知路線最遠格數 `KnownRouteMaxSteps` | 6 格 | 同上 `:24` |
| 隊伍移動速度 | 每週 1 格 | 同上 `:390`、`:571`、`:600` |
| 消息延遲 `NewsDelayWeeksPerCell` | 每格 1 週 | 同上 `:27`；模型見 `DemoMigration.cs:107-109` |

- 設定檔現況：01／29 沒有對應值。
- 選項：**A** 保留 TEMP；**B** 與正式移動、地圖尺度規則（六角格實際距離）一起定案；**C** 需使用者裁決個別數字。
- 推薦：**B**。速度與消息延遲取決於一格代表多遠，正式六角格尺度未定。

#### D6｜開拓新聚落參數

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 開拓選址最遠格數 `FoundingSearchMaxSteps` | 3 格 | `DemoWeeklySettlement.cs:29`、`:570` |
| 選址條件 | 陸地、無聚落、至少一個鄰格是水域 | `DemoMigration.cs:177-189` |
| 每個聚落同時在路上的開拓隊 | 最多 1 支 | `DemoWeeklySettlement.cs:548` |
| 新聚落資源點存量 | 從 0 開始，只靠再生 | `DemoWeeklySettlement.cs:428` |

- 設定檔現況：45F 只有規則條目，沒有數值。
- 選項：**A** 保留 TEMP；**B** 等正式地形（54G）與水源（河流）規則後再定。
- 推薦：**B**。

#### D7｜示範世界生成

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 地圖尺寸 | 12×8 格 | `DemoHexWorld.cs:34-35` |
| 地形 | 草原／森林／水域各 1／3 機率 | `DemoHexWorld.cs:74` |
| 偏移座標→軸向座標 | odd-r | `DemoHexWorld.cs:71-73`、`DemoMigration.cs:163`、`godot/Scripts/HexCoordinates.cs:14` |
| 聚落數量 | 2～4 | `DemoWorldSites.cs:55-56` |
| 人口群體人數 | 3～8 | `DemoWorldSites.cs:57-58` |
| 聚落名稱 | 示範聚落甲～丁 | `DemoWorldSites.cs:59` |

- 設定檔現況：01 已定案「六角格、有限平面、無環繞」（`WORLD_MAP_GRID_SHAPE` 等），示範地圖符合；**六角朝向與方向編號**在 01 `WORLD_HEX_EDGE_PORT_DIRECTIONS` 說明中寫「未定」。聚落與人口範圍 03 §3 V0-03 寫明 2～4 個聚落。
- 選項：**A** 保留到正式世界生成里程碑；**B** 現在先決定六角朝向（odd-r 尖頂／平頂）。
- 推薦：**A**。

#### D8｜存檔限制（純技術／介面 TEMP）

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 存檔槽數 | 3 | `DemoSaveSlots.cs:42` |
| 可讀世界日數上限 | 36500 日（#90：100 個正式 365 日年，365×100；第 100 年末第 36500 日可存讀，第 36501 日起明確拒絕；是否保留上限待裁決） | `DemoObserverWorld.Save.cs:24` |
| 存檔大小上限 | 64 MiB | `DemoObserverWorld.Save.cs:25` |
| 存檔格式 | JSON 第 2 版（直接快照），第 1 版重播仍可讀 | `DemoObserverWorld.Save.cs:23`、`DemoObserverWorld.Snapshot.cs:24` |

- 選項：**A** 保留 3 槽；**B** 改為不限數量的命名存檔；**C** 與正式存檔規格一起決定。
- 推薦：**C**。日數上限源自 V0-07A 重播讀檔，第 2 版直接快照後是否仍需要，可在正式存檔規格時一併檢討。

#### D9｜Godot 地圖縮放（純介面 TEMP）

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 縮放範圍 | 0.6～3 倍 | `godot/Scripts/DemoHexMap.cs:29-30` |
| 每格滾輪倍率 | 1.2 | `godot/Scripts/DemoHexMap.cs:28` |

- 設定檔現況：01 `WORLD_MAP_PRESENTATION_MODE` 寫「依玩家選擇的世界尺寸，畫面可縮放」，沒有數值。
- 選項：**A** 保留；**B** 等世界尺寸可選後，改為依地圖大小自動計算範圍。
- 推薦：**A**（只影響畫面）。

#### D10｜V0-01 每日示範帳（待核實：沒有逐值標 TEMP）

| 參數 | 目前值 | 程式位置 |
|---|---|---|
| 人口、初始糧食、每日最多採收、每人每日消耗 | 5、8、0～8、1 | `src/WorldSimulation.Core/World/DemoObserverWorld.cs:24-25`（`DemoScenario.Sample`） |

- 現況：這是 V0-01 的舊帳，畫面與文字介面已標「舊版獨立 TEMP 帳，與上方聚落人口無關」（`godot/Scripts/ObserverRoot.cs:1025`），但 `DemoScenario.Sample` 本身沒有逐值標 `TEMP`。
- 選項：**A** 另開任務移除舊帳（週結算已取代它）；**B** 保留並在 `DemoScenario` 補標 TEMP。
- 推薦：**A**，可減少畫面上兩套人口數字造成的混淆；移除會改變存檔內容，需另開議題並經審查。

---

## 2｜設定檔已有正式值或語義衝突（a／c，不列待裁決）

| 編號 | 類別 | 內容 | 建議處理 |
|---|---|---|---|
| c1 | 配置／程式不一致 | 程式示範曆法「一年＝52 週＝364 日」（`DemoCenturyRun.cs:100-104`、文字介面說明 `Program.cs:191`），而 `01_世界參數.csv` 第 2 列 `WORLD_DAYS_PER_YEAR`＝365、Enabled＝TRUE（正式），另有 12 個月、二月 28 天、不設閏年（第 3、5 列）。 | 數值已定案，不需裁決。**#82 已接入（待審）**：`--demo --years` 與 `--m1-check` 從 01 表讀 `WORLD_DAYS_PER_YEAR`（365）傳入 Core，週結算每 7 日、跨年不重置，年報依結算日所在年份歸屬（每年 52 或 53 次）。月份／二月天數尚未用到。 |
| c2 | 配置／程式不一致 | 飢餓債：29 第 38–39 列是個人層、以日計的債務階段；示範是聚落合計、以糧食量計（D4）。 | 列入 D4 選項說明；接入前不得把 12／2 寫成 29 的值。 |
| a1 | 正式值尚未接入 | 29 `P_FOOD_LOSS_WEEK`（0.01324）、`P_FOOD_LOSS_YEAR`（0.5）：示範結算沒有腐敗。兩列「是否已平衡」＝FALSE。 | 見 D3 選項 C。 |
| a2 | 正式值尚未接入 | 29 `P_RENEWABLE_RESOURCE_REGEN_MULTIPLIER`（預設 1，0.25～4）：觀察者倍率，示範再生沒有乘上。 | 預設 1 時結果不變；觀察者調整功能實作時接入。 |
| a3 | 正式值已符合 | 01 地圖格形 HEX、有限平面、無環繞：示範地圖已符合。 | 無。 |

---

## 3｜不需裁決：測試／CI fixture

以下只供重現情境，**不是玩家規則**，不要寫進設定檔：

| 內容 | 位置 |
|---|---|
| M1 驗收預設種子 42、7、1，預設 100 年 | `M1Acceptance.cs:965-968` |
| 饑荒、局部饑荒、開拓情境（`FamineSample`／`LocalFamineSample`／`FrontierSample`） | `DemoWeeklySettlement.cs:42-71`；M1 情境 `M1Acceptance.cs:227` |
| Godot 預設種子 42、局部饑荒／開拓情境與 CI 冒煙測試挑種子（42，再 1～30）、冒煙天數（#81 起玩家也可在畫面選這些 TEMP 情境） | `godot/Scripts/ObserverRoot.cs:14-113` |
| #87 觀察者播放速度：每秒 1／5／30 世界日、每幀最多推進 30 日（純畫面顯示，不是世界規則） | `godot/Scripts/PlaybackClock.cs:12-19` |
| 存檔只接受已知示範情境 | `DemoObserverWorld.Save.cs:45-46` |
| `tests/` 內的期望數字 | 全部由上表 fixture 與 D3～D7 的 TEMP 值推導，D3～D7 定案時一併更新 |

---

## 附錄 A｜`TEMP` 標記檔案清單（盤點基準 `0f0e76f`）

每個檔案的標記已歸入上面的項目；其餘標記是說明文字、錯誤訊息或畫面標籤（說明該資料是 TEMP），不另含數值。

| 檔案 | 標記數 | 歸入項目 |
|---|---|---|
| `src/WorldSimulation.Core/World/Demo/DemoWeeklySettlement.cs` | 32 | D3、D4、D5、D6、第 3 節 |
| `src/WorldSimulation.Core/World/DemoObserverWorld.Save.cs` | 24 | D8、第 3 節 |
| `src/WorldSimulation.Core/World/Demo/DemoWorldSites.cs` | 15 | D7 |
| `src/WorldSimulation.Core/World/Demo/DemoMigration.cs` | 11 | D5、D6、D7 |
| `src/WorldSimulation.Core/World/DemoObserverWorld.cs` | 10 | D3（期初存量）、D10 |
| `src/WorldSimulation.Core/World/DemoSaveSlots.cs` | 9 | D8 |
| `src/WorldSimulation.Core/World/Demo/DemoCenturyRun.cs` | 2 | c1 |
| `src/WorldSimulation.Core/World/DemoObserverWorld.Snapshot.cs` | 4 | D8 |
| `src/WorldSimulation.Core/World/Demo/DemoHexWorld.cs` | 3 | D7 |
| `src/WorldSimulation.ObserverCli/M1Acceptance.cs` | 15 | D1、D2、D3、D5、第 3 節 |
| `src/WorldSimulation.ObserverCli/Program.cs` | 10 | D8、c1 |
| `godot/Scripts/ObserverRoot.cs` | 71 | D7、D8、D10、第 3 節；#87 播放速度 |
| `godot/Scripts/DemoHexMap.cs` | 6 | D9 |
| `godot/Scripts/HexCoordinates.cs` | 1 | D7 |
| `godot/Scripts/PlaybackClock.cs` | 7 | 第 3 節 #87 播放速度 |
