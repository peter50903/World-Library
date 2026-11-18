namespace WorldSimulation.GodotClient;

/// <summary>第 08-18d 章：像素素材（res://assets/px2/，密度每格半徑 <see cref="TempTexelsPerRadius"/> 圖像素）。
/// 第 08-29 章：只登記程式用到與 assets/保留素材.csv 保留的（由 tools/asset_usage.py --registry 產生、--check 檢查）。</summary>
public static partial class PixelParts
{
    public static readonly string[] Batch5 =
    {
        // 第 09-1a 章：第十一批（地圖符號；第 08-29 章領地面板保留的建築、圍牆、地面、小物件）
        "b11_archery", "b11_barracks", "b11_bell_tower", "b11_boat_1", "b11_boat_2", "b11_bonfire", "b11_brick_corner_ne", "b11_brick_corner_nw",
        "b11_brick_corner_se", "b11_brick_corner_sw", "b11_brick_end_e", "b11_brick_end_n", "b11_brick_end_s", "b11_brick_end_w", "b11_brick_gate_h", "b11_brick_gate_v",
        "b11_brick_h", "b11_brick_house_1", "b11_brick_house_2", "b11_brick_tee", "b11_brick_v", "b11_farmhouse_1", "b11_farmhouse_2", "b11_fence_corner_ne",
        "b11_fence_corner_nw", "b11_fence_corner_se", "b11_fence_corner_sw", "b11_fence_end_e", "b11_fence_end_n", "b11_fence_end_s", "b11_fence_end_w", "b11_fence_gate_h",
        "b11_fence_gate_v", "b11_fence_h", "b11_fence_tee", "b11_fence_v", "b11_fishing_hut", "b11_fountain", "b11_g_ash_1", "b11_g_cobble_1",
        "b11_g_field_fallow", "b11_g_field_flax", "b11_g_field_green", "b11_g_field_plowed", "b11_g_field_sown", "b11_g_field_sprout", "b11_g_field_vegetable", "b11_g_field_wheat",
        "b11_g_forest_floor_1", "b11_g_forest_floor_2", "b11_g_forest_floor_3", "b11_g_garden_1", "b11_g_garden_2", "b11_g_grass_meadow_1", "b11_g_grass_meadow_2", "b11_g_grass_meadow_3",
        "b11_g_grass_meadow_4", "b11_g_grass_steppe_1", "b11_g_grass_steppe_2", "b11_g_grass_steppe_3", "b11_g_grass_steppe_4", "b11_g_grass_temperate_1", "b11_g_grass_temperate_2", "b11_g_grass_temperate_3",
        "b11_g_grass_temperate_4", "b11_g_hill_1", "b11_g_hill_2", "b11_g_hill_3", "b11_g_plaza_dirt_1", "b11_g_plaza_stone_1", "b11_g_plaza_stone_2", "b11_g_road_brick_1",
        "b11_g_road_brick_2", "b11_g_road_dirt_1", "b11_g_road_dirt_2", "b11_g_road_stone_1", "b11_g_road_stone_2", "b11_g_sand_1", "b11_g_sand_2", "b11_g_sand_wet_1",
        "b11_g_water_deep_1", "b11_g_water_deep_2", "b11_g_water_river_1", "b11_g_water_river_2", "b11_g_water_sea_1", "b11_g_water_sea_2", "b11_g_water_shallow_1", "b11_g_water_shallow_2",
        "b11_g_yard_1", "b11_g_yard_2", "b11_granary", "b11_hall_town", "b11_hall_village", "b11_house_1", "b11_house_2", "b11_house_3",
        "b11_hunting_lodge", "b11_hut_1", "b11_hut_2", "b11_hut_3", "b11_inn", "b11_keep", "b11_kiln", "b11_n_bush_1",
        "b11_n_bush_2", "b11_n_log", "b11_n_orchard_apple", "b11_n_orchard_pear", "b11_n_orchard_plum", "b11_n_reeds", "b11_n_weeds", "b11_p_awning",
        "b11_p_barrel", "b11_p_bench", "b11_p_bucket", "b11_p_cart", "b11_p_clothesline", "b11_p_coop", "b11_p_crate", "b11_p_doghouse",
        "b11_p_flagpole", "b11_p_flowerbed", "b11_p_haystack", "b11_p_scarecrow", "b11_p_shed", "b11_p_signboard", "b11_p_torch", "b11_p_woodpile",
        "b11_palisade_corner_ne", "b11_palisade_corner_nw", "b11_palisade_corner_se", "b11_palisade_corner_sw", "b11_palisade_end_e", "b11_palisade_end_n", "b11_palisade_end_s", "b11_palisade_end_w",
        "b11_palisade_gate_h", "b11_palisade_gate_v", "b11_palisade_h", "b11_palisade_tee", "b11_palisade_v", "b11_pier_end", "b11_pier_mid", "b11_pier_root",
        "b11_row_left_1", "b11_row_left_2", "b11_row_mid_1", "b11_row_mid_2", "b11_row_right_1", "b11_row_right_2", "b11_ruin_big", "b11_ruin_small",
        "b11_school", "b11_scriptorium", "b11_shrine", "b11_site_l_1", "b11_site_l_2", "b11_site_l_3", "b11_site_m_1", "b11_site_m_2",
        "b11_site_m_3", "b11_site_s_1", "b11_site_s_2", "b11_site_s_3", "b11_smithy", "b11_stable", "b11_stall_1", "b11_stall_2",
        "b11_stall_3", "b11_statue", "b11_stockpile", "b11_stone_corner_ne", "b11_stone_corner_nw", "b11_stone_corner_se", "b11_stone_corner_sw", "b11_stone_end_e",
        "b11_stone_end_n", "b11_stone_end_s", "b11_stone_end_w", "b11_stone_gate_h", "b11_stone_gate_v", "b11_stone_h", "b11_stone_house_1", "b11_stone_house_2",
        "b11_stone_house_3", "b11_stone_tee", "b11_stone_v", "b11_sym_arrow_head", "b11_sym_battle", "b11_sym_siege", "b11_temple", "b11_tent_1",
        "b11_tent_2", "b11_w_grass_brick", "b11_w_grass_dirt", "b11_w_grass_field", "b11_w_grass_forest", "b11_w_grass_hill", "b11_w_grass_sand", "b11_w_grass_stone",
        "b11_w_hedge", "b11_w_sand_shallow", "b11_w_shallow_deep", "b11_warehouse", "b11_watchtower", "b11_watermill", "b11_well", "b11_windmill",
        "b11_workshop",
        // 第 08-29 章：第十二批（領地面板缺的建築、廢墟、施工、圖示與棋子）
        "b12_academy", "b12_bank", "b12_clinic", "b12_glassworks", "b12_hospital", "b12_ic_cat_admin", "b12_ic_cat_defense", "b12_ic_cat_edu",
        "b12_ic_cat_field", "b12_ic_cat_health", "b12_ic_cat_housing", "b12_ic_cat_industry", "b12_ic_cat_justice", "b12_ic_cat_military", "b12_ic_cat_religion", "b12_ic_cat_resource",
        "b12_ic_cat_siege", "b12_ic_cat_storage", "b12_ic_cat_trade", "b12_ic_cat_transport", "b12_ic_res_clay", "b12_ic_res_cloth", "b12_ic_res_iron", "b12_ic_res_leather",
        "b12_ic_res_mana", "b12_ic_res_parchment", "b12_ic_state_building", "b12_ic_state_crowded", "b12_ic_state_damaged", "b12_ic_state_destroyed", "b12_ic_state_idle", "b12_jeweler",
        "b12_kitchen", "b12_lumberyard", "b12_magic_academy", "b12_magic_workshop", "b12_map_lair", "b12_mine", "b12_n_tree_oak_1", "b12_n_tree_oak_2",
        "b12_n_tree_pine_1", "b12_n_tree_pine_2", "b12_p_cow", "b12_p_sheep", "b12_port", "b12_prison", "b12_quarry", "b12_ruin_palisade_gate_h",
        "b12_ruin_palisade_h", "b12_ruin_palisade_v", "b12_ruin_stone_gate_h", "b12_ruin_stone_h", "b12_ruin_stone_l", "b12_ruin_stone_m", "b12_ruin_stone_v", "b12_ruin_tower",
        "b12_ruin_wood_m", "b12_ruin_wood_s", "b12_siege_workshop", "b12_site_palisade_h", "b12_site_palisade_v", "b12_site_stone_h", "b12_site_stone_v", "b12_smelter",
        "b12_stone_tower", "b12_tannery", "b12_tok_army", "b12_tok_army_cloth", "b12_tok_monster", "b12_tok_siege", "b12_tok_swarm", "b12_ui_plate",
        "b12_wainwright", "b12_weavery",
        // 第 08-18d 章：第五、六批（圖示、棋子、介面、時代插圖；第 08-29 章領地面板保留的火、煙、裂痕、瓦礫）
        "dec_ash", "dec_crack", "dec_debris", "dec_scorch", "fx_fire", "fx_fire_big", "fx_smoke", "ic_stat_01",
        "ic_stat_02", "ic_stat_03", "ic_stat_04", "ic_stat_05", "ic_stat_06", "ic_stat_07", "ic_stat_08", "ic_stat_09",
        "ic_stat_10", "ic_stat_11", "ic_stat_12", "ic_stat_13", "ic_stat_14", "ic_stat_15", "ic_stat_16", "ic_stat_17",
        "ic_stat_18", "ic_stat_19", "ic_stat_20", "ic_stat_21", "ic_stat_22", "ic_stat_23", "ic_stat_24", "ic_stat_25",
        "ic_stat_26", "ic_stat_27", "ic_stat_28", "ic_stat_29", "ic_stat_30", "ic_stat_31", "ic_stat_32", "ic_stat_33",
        "ic_stat_34", "ic_stat_35", "ic_stat_36", "ui_era_conquest", "ui_era_hope", "ui_era_pioneer", "ui_tooltip"
    };
}
