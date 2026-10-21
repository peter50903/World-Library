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
| 存檔格式 | JSON 第 8 版（標頭＋校驗碼＋直接快照＋實體 ID 分配器＋世界配方＋正式史庫；第 04 章 b 節），第 1～7 版一律「版本太舊，請建立新世界」 | `DemoObserverWorld.Save.cs:23`、`DemoObserverWorld.Snapshot.cs:24` |

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

#### D10｜V0-01 每日示範帳——已退役（2026-10-14 使用者裁決；C05 移除程式）

| 參數 | 目前值 | 程式位置 |
|---|---|---|
| 人口、初始糧食、每日最多採收、每人每日消耗 | 5、8、0～8、1 | `src/WorldSimulation.Core/World/DemoObserverWorld.cs:24-25`（`DemoScenario.Sample`） |

- 現況：這是 V0-01 的舊帳，畫面與文字介面已標「舊版獨立 TEMP 帳，與上方聚落人口無關」（`godot/Scripts/ObserverRoot.cs:1025`），但 `DemoScenario.Sample` 本身沒有逐值標 `TEMP`。
- 選項：**A** 另開任務移除舊帳（週結算已取代它）；**B** 保留並在 `DemoScenario` 補標 TEMP。
- 推薦：**A**，可減少畫面上兩套人口數字造成的混淆；移除會改變存檔內容，需另開議題並經審查。

#### D11｜#95 播放中「重大事件」的種類（純介面 TEMP）

| 參數 | 目前 TEMP 值 | 程式位置 |
|---|---|---|
| 觸發提示與自動暫停的事件種類 | 餓死、途中餓死、外移、建立新聚落（不含飢餓、飢餓解除、抵達） | `godot/Scripts/ObserverRoot.MajorEventAlert.cs`（`TempMajorKinds`） |
| 預設是否自動暫停 | 開（玩家可取消勾選） | 同上 |

- 設定檔現況：`43E_歷史重要度與保存` 有重要度分級，但沒有「播放提示門檻」；Notion 02 未規定。
- 選項：**A** 保留目前四種；**B** 只留「有人死亡或新聚落」（餓死、途中餓死、建立新聚落），外移不暫停；**C** 改依 43E 重要度分級，待重要度實作後接上。
- 推薦：**A**（現在可見、容易理解；43E 接上後改 C）。

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
| #96 歷史事件篩選預設：全部日期（迄日 0＝不限）、全部聚落、全部類別、無關鍵字；清單顯示符合者最新 60 筆＋最新 5 筆建立新聚落（純畫面，不是世界規則） | `src/WorldSimulation.Core/World/Demo/DemoEventFilter.cs`、`godot/Scripts/ObserverRoot.HistoryFilter.cs` |
| #103 聚落承載估計：長期可養活人數＝每週再生÷每人每週消耗（每人產量低於消耗時為 0）；只讀 D3／D4 TEMP 值推導，不含存量、糧庫、遷徙、出生與腐敗，是描述性估計、不是正式承載力規則 | `src/WorldSimulation.Core/World/Demo/DemoCarryingCapacity.cs`、`DemoCenturyRun.cs`、`Program.cs`、`M1Acceptance.cs`（M1_STABILITY）；#104 畫面 `godot/Scripts/ObserverRoot.FoodDiagnostics.cs` |
| 存檔只接受已知示範情境 | `DemoObserverWorld.Save.cs:45-46` |
| `tests/` 內的期望數字 | 全部由上表 fixture 與 D3～D7 的 TEMP 值推導，D3～D7 定案時一併更新 |

---

## #110｜人口出生與自然死亡第一切片（【TEMP】，尚未接入正式示範世界）

- 玩家 2026-10-10 23:32 選定聚合出生方案 A，且要求與死亡共同決定人口變化；Claude 額度耗盡期間 GPT 依 Notion07 §7 臨時接手，復工需異方補審。
- `DemoTempDemography.cs` 目前是**獨立且未接線的**人口身分／孕期／出生／自然死亡原型：只做人族示範，不改 `DemoWeeklySettlement.Births:0`，不改目前 Godot 觀察者的人口、不更新正式設定表；不得把這張 PR 宣稱為完整 #105 功能。
- 原有正式來源：`02_種族` 人族成年 18、典型壽命 75、月生育基準 0.08；`29_數值參數` 流產月率 0.005 仍標未平衡。本第一切片只有 HumanFixture 作人工驗證參考，日後須由正式設定權威取得且不能直接套所有種族，尤其龍族 `SPECIAL_EVENT`。
- **TEMP 待裁決**：開局全部成年人、男女交錯、初始年齡 18–22；單一孕期 39 週；育齡上限 45；自然死亡年風險曲線（典型壽命前 15 年開始 0.008 年風險，達典型壽命 0.02，往後每歲增加 0.04，年風險上限 0.95）；一年 365 日、每週 7 日、每年 12 個月做機率換算。典型壽命**不是必死年齡**。暫時缺 HP，吃飽判準由未來真實 Core 週帳提供。
- 此切片的 `Transfer` 是身份搬移小模型，未接既有在途遷徙隊伍；`RemoveStarvation` 只是外部真死亡人員清單的移除接口，尚未連動世界飢餓債與遷徙人口帳。快照只是可序列化形狀，不是已發布 v2 存檔協定。#105 下一切片必須接入兩者、修正『所有人都能勞動』的 #103 承載假設、逐筆驗證因果、遷徙與存檔原子回滾。
## #112｜森林／河流共享供糧範圍（正式地形關係，數值待裁決）

- 使用者 2026-10-11 定案：前期所有人口僅測試人類；森林一格供應自身及六個鄰接六角格，主河道水域格禁止聚落居住、只供應相鄰陸地少量漁獲。食物來源**每格一份共享存量與採收**，不能因七格同享而乘以七；第二圈無權獲得此來源食物。
- **#112 Core 獨立原型** `DemoHexFoodCatchment` 以目前 axial 六角座標算來源可達、共享一次性食物配給、成人勞力採收能力與需求限額；目前尚未接入真 DemoWeeklySettlement、Godot、Save、出生/死亡／既有遷徙，不可宣稱玩家遊戲供食已改好。
- **2026-10-11 玩家同意的 TEMP 數值**：`config/demo/terrain-food-temp.json` 是程式可讀的暫定設定，森林每來源格每週 **20** 糧食單位、明確標記的主河道水域來源每週 **5** 單位、原始未開墾農田每週 **0**。每片來源格**整個共享供應只有一份**，不因六鄰取得多份；人族適用。Core `DemoCatchmentTempSettings.FromJson/BuildPotentialSources` 驗證設定格式並僅對真森林與明確河道產生來源，仍是可得預算、受成年勞工/真存量/需求約束，現有真週帳與存檔尚未接入。這些屬測試值而非正式 Excel 數值；依 Notion03 §2.5 **不寫回正式 XLSX/CSV**。**仍待裁決**：真正來源存量、開發農田的級距/技術、共用分配優先政策（現純模型以來源 ID→聚落 ID 的 TEMP 先後順序）；單元測試的森林12或9、河流3只是守恆反例數字，與已同意的示範20/5不同。
- 森林在地與接壤格可能存在非陸地格；供應範圍幾何涵蓋六鄰接，但只有合法居住的陸地聚落能採集；主河道不是 RouteSegment 過河通行權。現在 DemoTerrain.Water 僅在明確建立 MainRiver 食物來源 ID 時才視為「主河道漁食源」，一般水域不自動生產。
- 農田並非草原預設值；在 #113 修週結算之前，現主世界仍有 `DemoWorldSites.KindFor(Grassland)=Farmland` 的舊 TEMP 行為，必須下一批移除。農田開發程度/科技上限另見 #115。
## #118｜所有自然地塊野採、人造佔格不產自然食物
- 玩家要求所有自然地塊按類型產出不同的野生可採食物，已占用的聚落／未來城市／道路地塊不得同時當自然來源。Demo 目前只有森林、草原、水域；主河道另由 Core 明確標記，未建道路與城市物件。
- 供食 TEMP 每來源格每週：森林20、草原野採8、主河道5、一般水域3，森林20/河道5沿用已同意值，新增草原8/一般水域3僅是測試預設，不升格正式值。每自然來源只分配一次給可達陸地，已存在聚落格自身不再生成自然來源；草原野採不是農田，未開墾農業產量仍0。
- machine-readable config/demo/terrain-food-temp.json 單一資料，正式 XLSX/CSV 未改動。城市、道路未建模無法驗證其佔地，未來以相同 occupied cell 接口納入。
## #113｜玩家可操作的森林／河流採集情境（初版，尚非正式世界農業）
- 2026-10-11 使用者同意前期全人族 TEMP、森林每來源格共用20/週、明確主河道共用5/週；新增 Godot 選單「森林／河流採集生存（新，TEMP）」及真 Core 週結算，另四個舊測試情境維持不變。
- 森林自身與六鄰陸地聚落共享同一來源，主河道僅接鄰陸地；一個來源每週只計一次。草原未開墾產糧0，河流／水格禁止定居；地圖青色僅為 TEMP 並且帶有河岸的部分水格（座標規則，**不是正式連通水文或 RouteSegment**）。
- 此階段糧食是每週採集的再生上限和實際需求／勞力取用，未採取的自然供應不會被聚落儲藏；已採收的實物進真聚落糧庫，未偷補糧、沒有農田產量，物資帳仍須檢查。每聚落保留0儲備的舊資源點作為週帳歸屬槽，並不代表該聚落獨占森林。
- **人口過密對照情境（TEMP）**：選單新加「森林周邊人口過密｜觀察缺糧」，只改初始人口群體範圍到 40～48 人/聚落，沒有開局糧庫；與正常採集情境使用**完全同一森林20／河流5供應量及真實採集週帳**，因此缺糧／餓死並非刪除森林糧食。這是刻意壓力測試人口配置，不代表正式史前聚落大小；出生仍未與正式週帳接線。
- **仍待下一階段**：持久性共享森林來源庫存／真源級資源帳及公平分配、正式河網、農田開墾與知識上限、合法出生／自然死亡群體真正接入週帳（#105）、新增人口不全算成年採收勞力。此切片不代表整個 RPG/人口系統完成。

## #119｜右側世界控制面板拆出為獨立 Godot Window
- 原 sidebar 改為主地圖以外的可開閉、可拖動、可縮放 Godot 浮動視窗；主畫面保留開啟按鈕，面板小尺寸時捲動，視窗寬時自適應文字比例。
- 只變更 Godot 呈現及自動操作測試，不改 Core 世界與設定、也不把縮放或關窗當推進時間。

## #105｜真實週結算的人類生育與自然死亡（TEMP 測試世界）
- 人口示範為扶養未成年者提供真實成年勞力採集上限：本情境暫用每成年採集勞工每週可採上限 **8**（不是糧食來源自動多8），森林/草原/河道/一般水域來源總產量完全維持20/8/5/3供應預算。此為 TEMP平衡值，不寫正式設定表；人口增加仍可超載並真正缺糧。
- 新增獨立可選『人類繁衍與自然死亡』示範場景，初始人類18～22歲；男女成人且吃飽才可受孕，39週孕期，新生人口個體ID和父母ID可追蹤，嬰兒開始算耗糧人口，但18歲前不算成年採集勞工。
- 出生及年齡自然死亡由 Core 真週結算附到人口帳（Births／Deaths／NaturalDeaths），另保留真正飢餓死亡，有合法事件ID、因果與存讀回放的身分驗證。年齡／孕期／死亡風險均僅TEMP，不改正式種族數值表；自然死需要較長時間才容易出現。
- 遷徙人員的懷孕／年齡身分隨隊伍攜帶尚未安全完成；**這個新人口場景暫停遷徙**，不能宣稱人口所有動態及M1正式人口驗收已全面完成；舊遷徙/開拓場景保留供CI。舊TEMP世界仍讀舊模式，新的生育模式有獨立存檔場景識別。

## #03b｜正式事件類型對照（第 03 章 b 節，Claude 定案，TEMP）

- 出生→正式 `EV_BIRTH`；餓死、途中餓死、自然死亡→正式 `EV_DEATH`（死因放根因與摘要）。
- 24 表尚無正式列的 5 種暫用：`TEMP_EV_HUNGER`（飢餓）、`TEMP_EV_HUNGER_RECOVERED`（飢餓解除）、`TEMP_EV_EMIGRATION`（外移）、`TEMP_EV_ARRIVAL`（抵達）、`TEMP_EV_SETTLEMENT_FOUNDED`（建立新聚落）。單一對照表在 `DemoEventTypes`。
- 根因：飢餓類、遷徙類、建村＝資源；出生、自然死亡＝生命週期【TEMP】。不給重要度（第 13 章）。
- 待使用者或後續章節在 24 表新增正式列後替換；替換時存檔版本＋1。

## 附錄 A｜`TEMP` 標記檔案清單（盤點基準 `0f0e76f`）

每個檔案的標記已歸入上面的項目；其餘標記是說明文字、錯誤訊息或畫面標籤（說明該資料是 TEMP），不另含數值。

| 檔案 | 標記數 | 歸入項目 |
|---|---|---|
| `src/WorldSimulation.Core/World/Demo/DemoTempDemography.cs` | 15 | #110 獨立出生／死亡 TEMP 原型（尚未接入世界） |
| `src/WorldSimulation.Core/World/Demo/DemoPopulationDraws.cs` | 2 | 第 04 章 a 節 TEMP 人類繁衍抽樣命名空間（規則仍 TEMP，亂數協定正式） |
| `src/WorldSimulation.Core/World/Demo/DemoCatchmentTempSettings.cs` | 6 | #112 讀取森林20／河流5的 TEMP JSON，非正式設定值 |
| `src/WorldSimulation.Core/World/Demo/DemoForagingWorld.cs` | 4 | #113 可玩示範的 TEMP 主河道位置與共享資源來源，正式水系尚未實作 |
| `src/WorldSimulation.Core/World/Demo/DemoHumanLifecycle.cs` | 9 | #105 TEMP人族週帳出生／老死／身分重播驗證 |
| `src/WorldSimulation.Core/World/Demo/DemoHexFoodCatchment.cs` | 9 | #112 森林／河流共享供糧的獨立 TEMP 模型 |
| `src/WorldSimulation.Core/World/Demo/DemoWeeklySettlement.cs` | 37 | D3、D4、D5、D6、第 3 節；#88 事件「至多一個直接原因」簡化 |
| `src/WorldSimulation.Core/World/DemoObserverWorld.Save.cs` | 23 | D8、第 3 節 |
| `src/WorldSimulation.Core/World/DemoSaveManifest.cs` | 5 | 第 04 章 b 節存檔標頭檢查（TEMP 存檔、TEMP 示範設定雜湊；檢查本身為正式規則） |
| `src/WorldSimulation.Core/World/Demo/DemoWorldSites.cs` | 16 | D7 |
| `src/WorldSimulation.Core/World/Demo/DemoMigration.cs` | 11 | D5、D6、D7 |
| `src/WorldSimulation.Core/World/DemoObserverWorld.cs` | 16 | D3（期初存量）、第 3 節 #103 承載估計（C05：D10 已退役） |
| `src/WorldSimulation.Core/World/DemoSaveSlots.cs` | 9 | D8 |
| `src/WorldSimulation.Core/World/Demo/DemoCenturyRun.cs` | 4 | c1、第 3 節 #103 承載估計 |
| `src/WorldSimulation.Core/World/DemoObserverWorld.Snapshot.cs` | 9 | D8；#88 舊版無事件編號存檔拒絕讀取；第 03 章 b 節史庫重建失敗訊息 |
| `src/WorldSimulation.Core/World/Demo/DemoCausalIndex.cs` | 2 | #88 43F 因果索引 M1 示範切片（每筆至多一個直接原因） |
| `src/WorldSimulation.Core/World/Demo/DemoHexWorld.cs` | 3 | D7 |
| `src/WorldSimulation.Core/World/Demo/DemoCellQuery.cs` | 15 | D7（C07 點格查詢：自 Godot 搬入的 TEMP 地形／資源名與資訊框文字） |
| `src/WorldSimulation.ObserverCli/M1Acceptance.cs` | 16 | D1、D2、D3、D5、第 3 節 |
| `src/WorldSimulation.ObserverCli/Program.cs` | 13 | D8、c1、第 3 節 #103 承載估計；C05 開場說明 |
| `godot/Scripts/ObserverRoot.cs` | 61 | D7、D8、第 3 節；#87 播放速度；#95 重大事件種類（C05：D10 已退役）；第 02 章 a 節種子輸入 |
| `src/WorldSimulation.Core/World/WorldRecipe.cs` | 4 | 第 02 章 a 節：規則版本 `TEMP-RULES-0`（設定雜湊已於 b 節換成正式雜湊） |
| `src/WorldSimulation.Core/Config/OfficialConfig.cs` | 4 | 第 02 章 b 節：config/demo 的 TEMP 設定雜湊與 29 表【TEMP】列計數（只計數，不是待裁決值） |
| `src/WorldSimulation.Core/Config/TunableParameterRegistry.cs` | 4 | 第 02 章 b 節：辨識 29 表說明開頭【TEMP】的暫定參數（只標示，不是待裁決值） |
| `godot/Scripts/ObserverRoot.MajorEventAlert.cs` | 4 | #95 重大事件種類（TEMP，待裁決） |
| `godot/Scripts/DemoHexMap.cs` | 9 | D9；第 05 章 b 節糧食狀況地圖配色 |
| `godot/Scripts/HexCoordinates.cs` | 1 | D7 |
| `godot/Scripts/PlaybackClock.cs` | 7 | 第 3 節 #87 播放速度 |
| `src/WorldSimulation.Core/World/Demo/DemoEventFilter.cs` | 2 | 第 3 節 #96 歷史篩選畫面預設 |
| `src/WorldSimulation.Core/World/Demo/DemoCarryingCapacity.cs` | 9 | 第 3 節 #103 承載估計（D3／D4 推導） |
| `godot/Scripts/ObserverRoot.HumanSmoke.cs` | 1 | #105 真Godot出生/存讀冒煙 |
| `godot/Scripts/HudLayout.cs` | 15 | 第 05 章觀察者介面版面尺寸（間距、面板寬高、事件列比例、工具列與地圖模式列） |
| `godot/Scripts/ObserverRoot.Hud.cs` | 5 | 第 05 章面板配色、速度檔位說明、背景音樂音量 |
| `godot/Scripts/ArtAssets.cs` | 3 | 第 05 章 b 節九宮格面板、頂部木條與工具列木板的邊框寬度 |
| `godot/Scripts/ObserverRoot.EventDetail.cs` | 2 | 第 05 章 b2 世界紀錄以 TEMP 重大事件種類標紅；第 03 章 b 節冒煙核對 TEMP 事件類型 |
| `src/WorldSimulation.Core/History/WorldEvent.cs` | 1 | 第 03 章 b 節：根因分類「生命週期」暫定 |
| `src/WorldSimulation.Core/World/Demo/DemoEventTypes.cs` | 12 | 第 03 章 b 節 TEMP 事件類型對照（見下方 #03b） |
| `godot/Scripts/MapFoodStatus.cs` | 2 | 第 05 章 b 節糧食狀況分級門檻（存糧不足 2 週＝緊張，顯示用） |
| `godot/Scripts/ObserverRoot.Toolbar.cs` | 7 | 第 05 章 b 節工具列按鈕尺寸、視窗大小、糧食圖例說明 |
| `godot/Scripts/ObserverRoot.ForagingSmoke.cs` | 4 | #113 Godot 真引擎可選採集生存情境、共享取食與存讀測試 |
| `godot/Scripts/ObserverRoot.HistoryFilter.cs` | 2 | 第 3 節 #96 歷史篩選畫面預設 |
| `godot/Scripts/ObserverRoot.FoodDiagnostics.cs` | 5 | 第 3 節 #103 承載估計（#104 畫面只讀顯示） |
