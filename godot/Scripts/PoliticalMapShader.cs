namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-21 章：CK3 式政治地圖的著色器（畫在 <see cref="DemoHexMap"/> 後面的一張全畫面矩形上）。
/// 每個像素換算成「格座標」（加上一點雜訊扭曲，邊界才不是方格），取周圍四格依距離加權，
/// 權重最大的省份就是這個像素的省份；最大與次大的差距換算成離邊界幾像素，畫省界與勢力界。
/// 地形依四格加權混色，再加程式雜訊畫出森林、丘陵與草地的紋理；海岸、淺水由同樣的權重算出。
/// 資料：<c>ids</c>（RG＝省份索引、BA＝分組索引）、<c>land</c>（R＝地形、G＝旗標）、<c>palette</c>（省份索引→填色）。
/// </summary>
public static class PoliticalMapShader
{
    /// <summary>水（海、湖）的省份與分組索引：與任何省份之間不畫省界，改畫海岸線。</summary>
    public const int WaterIndex = 65535;

    public const string Code = """
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D ids : filter_nearest, repeat_disable;
uniform sampler2D land : filter_nearest, repeat_disable;
uniform sampler2D palette : filter_nearest, repeat_disable;
uniform sampler2D occupy : filter_nearest, repeat_disable;
uniform vec2 grid = vec2(1.0, 1.0);
uniform vec2 origin = vec2(0.0, 0.0);
uniform float cell_px = 16.0;
uniform float politics = 0.85;
uniform float province_lines = 0.6;
uniform int sel_province = -1;
uniform int sel_group = -1;
// 第 12-2j 章：世界時代的氛圍（黑暗時代長夜降臨、烈日偏暖、霜雪偏冷……）；a＝濃淡。
uniform vec4 age_tint = vec4(1.0, 1.0, 1.0, 0.0);
uniform float age_vignette = 0.0;
uniform float age_shimmer = 0.0;

varying vec2 map_pos;

void vertex() {
	map_pos = VERTEX;
}

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// 第 08-26 章：碎形雜訊（起伏的高度場）。
float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * vnoise(p);
		p = p * 2.03 + vec2(1.7, 9.2);
		a *= 0.5;
	}
	return v;
}

// 第 08-26 章：紙地圖的山形（每 1/density 格一座，位置與大小依座標雜湊）：x＝在山形裡、y＝左半邊（向光）、z＝輪廓。
vec3 mountain_glyph(vec2 c, float density) {
	vec2 g = c * density;
	vec2 id = floor(g);
	vec2 f = fract(g);
	float cx = 0.5 + (hash(id) - 0.5) * 0.30;
	float base = 0.82 - hash(id + 4.7) * 0.10;
	float h = 0.42 + 0.20 * hash(id + 8.1);
	float w = 0.30 + 0.10 * hash(id + 2.3);
	float apex = base - h;
	float t = clamp((f.y - apex) / h, 0.0, 1.0);
	float half_w = t * w;
	float dx = abs(f.x - cx);
	float in_y = step(apex, f.y) * step(f.y, base);
	float inside = in_y * (1.0 - smoothstep(half_w - 0.015, half_w + 0.015, dx));
	float edge = in_y * (1.0 - smoothstep(0.0, 0.035, abs(dx - half_w)));
	return vec3(inside, step(f.x, cx), edge);
}

// 第 08-26 章：一叢樹（每 1/density 格一棵）：x＝樹冠、y＝影子（樹冠外的右下）、z＝亮面。
vec3 tree_glyph(vec2 c, float density) {
	vec2 g = c * density;
	vec2 id = floor(g);
	vec2 f = fract(g);
	vec2 ctr = vec2(0.5) + (vec2(hash(id + 1.3), hash(id + 5.9)) - 0.5) * 0.36;
	float r = 0.24 + 0.10 * hash(id + 9.4);
	vec2 d = f - ctr;
	float crown = 1.0 - smoothstep(r - 0.04, r + 0.02, length(d * vec2(1.0, 1.15)));
	float shadow = (1.0 - smoothstep(r - 0.04, r + 0.02, length((d - vec2(0.09, 0.12)) * vec2(1.0, 1.15)))) * (1.0 - crown);
	float lit = clamp(1.0 - length(d + vec2(0.08, 0.09)) / max(r, 0.001), 0.0, 1.0) * crown;
	return vec3(crown, shadow, lit);
}

vec2 warp(vec2 c) {
	vec2 w = vec2(vnoise(c * 1.7), vnoise(c * 1.7 + vec2(17.3, 5.1))) - 0.5;
	vec2 w2 = vec2(vnoise(c * 4.1 + vec2(3.1, 8.2)), vnoise(c * 4.1 + vec2(9.7, 1.3))) - 0.5;
	return c + w * 0.44 + w2 * 0.12;
}

int idx16(vec2 rg) {
	return int(rg.x * 255.0 + 0.5) + int(rg.y * 255.0 + 0.5) * 256;
}

// 第 08-26 章：森林更深、丘陵偏褐、草原偏亮，遠看也分得出來。
vec3 terrain_color(int t) {
	if (t == 1) return vec3(0.27, 0.39, 0.22);
	if (t == 2) return vec3(0.20, 0.27, 0.33);
	if (t == 3) return vec3(0.58, 0.50, 0.38);
	return vec3(0.63, 0.65, 0.40);
}

void fragment() {
	vec2 cell = (map_pos - origin) / cell_px;
	vec2 c = warp(cell);
	vec2 q = c - 0.5;
	vec2 fl = floor(q);
	vec2 f = q - fl;
	ivec2 i0 = ivec2(fl);
	ivec2 size = ivec2(grid);
	float ws[4] = float[](
		(1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y),
		(1.0 - f.x) * f.y, f.x * f.y);
	ivec2 offs[4] = ivec2[](ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 1));
	int pid[4];
	int gid[4];
	int ter[4];
	float blight[4];
	for (int k = 0; k < 4; k++) {
		ivec2 at = i0 + offs[k];
		if (at.x < 0 || at.y < 0 || at.x >= size.x || at.y >= size.y) {
			pid[k] = 65535;
			gid[k] = 65535;
			ter[k] = 2;
			blight[k] = 0.0;
		} else {
			vec4 a = texelFetch(ids, at, 0);
			vec4 b = texelFetch(land, at, 0);
			pid[k] = idx16(a.rg);
			gid[k] = idx16(a.ba);
			ter[k] = int(b.r * 255.0 + 0.5);
			blight[k] = b.g > 0.5 ? 1.0 : 0.0;
		}
	}
	// 水與陸：水的權重和陸上最大的省份權重比。
	float water = 0.0;
	for (int k = 0; k < 4; k++) {
		if (pid[k] == 65535) water += ws[k];
	}
	float best_p = -1.0;
	float second_p = 0.0;
	int bp = 0;
	float best_g = -1.0;
	float second_g = 0.0;
	int bg = 0;
	for (int a = 0; a < 4; a++) {
		if (pid[a] == 65535) continue;
		float sp = 0.0;
		float sg = 0.0;
		for (int b = 0; b < 4; b++) {
			if (pid[b] == pid[a]) sp += ws[b];
			if (gid[b] == gid[a]) sg += ws[b];
		}
		if (sp > best_p) { best_p = sp; bp = pid[a]; }
		if (sg > best_g) { best_g = sg; bg = gid[a]; }
	}
	for (int a = 0; a < 4; a++) {
		if (pid[a] == 65535) continue;
		float sp = 0.0;
		float sg = 0.0;
		for (int b = 0; b < 4; b++) {
			if (pid[b] == pid[a]) sp += ws[b];
			if (gid[b] == gid[a]) sg += ws[b];
		}
		if (pid[a] != bp) second_p = max(second_p, sp);
		if (gid[a] != bg) second_g = max(second_g, sg);
	}
	float coast = water - max(best_p, 0.0);
	float dc = abs(coast) / max(fwidth(coast), 1e-4);
	float mp = max(best_p, 0.0) - second_p;
	float dp = mp / max(fwidth(mp), 1e-4);
	float mg = max(best_g, 0.0) - second_g;
	float dg = mg / max(fwidth(mg), 1e-4);

	float zoom_fine = clamp((cell_px - 18.0) / 50.0, 0.0, 1.0);
	// 地形（只混陸地格，海岸不會被水色染暗）。
	vec3 tcol = vec3(0.0);
	float lw = 0.0;
	float hill = 0.0;
	float forest = 0.0;
	float bl = 0.0;
	for (int k = 0; k < 4; k++) {
		bl += ws[k] * blight[k];
		if (ter[k] == 2) continue;
		tcol += ws[k] * terrain_color(ter[k]);
		lw += ws[k];
		hill += ws[k] * (ter[k] == 3 ? 1.0 : 0.0);
		forest += ws[k] * (ter[k] == 1 ? 1.0 : 0.0);
	}
	tcol = lw > 0.0 ? tcol / lw : terrain_color(0);
	hill = lw > 0.0 ? hill / lw : 0.0;
	forest = lw > 0.0 ? forest / lw : 0.0;
	float grain = vnoise(c * 3.0) * 0.06 + vnoise(c * 14.0) * 0.05 * zoom_fine - 0.055;
	float meadow = (vnoise(c * 0.55 + vec2(4.0, 1.0)) - 0.5) * 0.12;
	// 第 08-26 章（使用者 2026-11-18：從地圖上看不出地形）：
	// ① 起伏：雜訊高度場的斜率當光照（左上來光），平地淡淡的、丘陵明顯。
	float e = 0.06;
	float h0 = fbm(c * 1.4);
	vec2 grad = vec2(fbm(c * 1.4 + vec2(e, 0.0)) - h0, fbm(c * 1.4 + vec2(0.0, e)) - h0) / e;
	float lit = clamp(0.5 - dot(grad, vec2(0.55, 0.55)) * 0.9, 0.0, 1.0);
	float shade = 1.0 + grain + meadow * (1.0 - forest) + (lit - 0.5) * (0.10 + hill * 0.55);
	// ② 符號從每格 9 像素開始淡入：森林一叢叢的樹（有影子與亮面），丘陵是紙地圖的山形（左亮右暗、深色輪廓）。
	float sym = clamp((cell_px - 9.0) / 15.0, 0.0, 1.0);
	float canopy = clamp((vnoise(c * 5.5 + vec2(2.0, 9.0)) - 0.45) * 5.0, 0.0, 1.0);
	vec3 tr = tree_glyph(c, 3.0);
	float fmask = smoothstep(0.35, 0.65, forest);
	shade -= fmask * canopy * 0.16;
	shade -= fmask * sym * (tr.y * 0.30 + tr.x * 0.10);
	shade += fmask * sym * tr.z * 0.22;
	vec3 mtn = mountain_glyph(c, 1.6);
	float hmask = smoothstep(0.40, 0.70, hill);
	shade += hmask * sym * mtn.x * (mtn.y > 0.5 ? 0.18 : -0.20);
	shade -= hmask * sym * mtn.z * 0.35;
	vec3 ground = tcol * shade;

	// 政治填色：省份索引查色票；荒野（0）不填。第 08-26 章：填色乘上地形的明暗與符號（森林偏深、草原偏亮），地形透得出來。
	vec4 fill = texelFetch(palette, ivec2(bp % 256, bp / 256), 0);
	float has_fill = (bp != 0 && fill.a > 0.5) ? 1.0 : 0.0;
	float tone = 1.0 + 0.35 * (dot(tcol, vec3(0.299, 0.587, 0.114)) / 0.55 - 1.0);
	vec3 political = fill.rgb * shade * tone;
	vec3 col = mix(ground, political, politics * has_fill);

	// 勢力界內側的亮帶（CK3 的邊界光暈）。
	float glow_w = clamp(cell_px * 0.35, 4.0, 14.0);
	float realm_w = clamp(cell_px * 0.07, 1.4, 3.2);
	float glow = (1.0 - smoothstep(realm_w, realm_w + glow_w, dg)) * politics * has_fill;
	col = mix(col, min(col * 1.25 + 0.06, vec3(1.0)), glow * 0.35);

	// 選到的省份或勢力：整片提亮、邊緣金色。
	bool sel = (bp == sel_province && sel_province > 0) || (bg == sel_group && sel_group > 0);
	if (sel) {
		col = mix(col, min(col * 1.18 + 0.05, vec3(1.0)), 0.6);
	}

	// 省界（細、半透明；拉近時淡出）與勢力界（粗、深色的填色）。
	float pline = (1.0 - smoothstep(0.35, 1.25, dp)) * province_lines;
	col = mix(col, col * 0.45, pline * (1.0 - step(dg, realm_w + 0.5)));
	float gline = 1.0 - smoothstep(realm_w - 0.6, realm_w + 0.6, dg);
	vec3 edge = has_fill > 0.5 ? fill.rgb * 0.38 : vec3(0.16, 0.13, 0.10);
	col = mix(col, edge, gline * (0.55 + 0.45 * politics));
	if (sel) {
		float gold_w = realm_w + 1.4;
		float gd = (bg == sel_group && sel_group > 0) ? dg : dp;
		float gold = 1.0 - smoothstep(gold_w - 0.7, gold_w + 0.7, gd);
		col = mix(col, vec3(0.98, 0.84, 0.36), gold * (0.75 + 0.25 * sin(TIME * 3.0)));
	}

	// 戰爭中被圍攻的省份：攻方顏色的斜紋（CK3 的佔領斜紋）。
	vec4 occ = texelFetch(occupy, ivec2(bp % 256, bp / 256), 0);
	float occ_stripe = step(0.55, fract((c.x - c.y) * 3.0));
	col = mix(col, occ.rgb, occ.a * occ_stripe * 0.7 * (bp != 0 ? 1.0 : 0.0));

	// 怪物地盤：暗紅、斜紋（世界座標，拖地圖時不會滑動）。
	float bmask = smoothstep(0.42, 0.58, bl);
	float stripes = step(0.5, fract((c.x + c.y) * 2.5));
	col = mix(col, mix(vec3(0.30, 0.06, 0.07), vec3(0.20, 0.03, 0.04), stripes), bmask * 0.55);

	// 水：深淺變化、近岸較淺；海岸線深色細線。
	float depth = vnoise(c * 0.8 + vec2(7.0, 3.0));
	vec3 sea = mix(vec3(0.17, 0.24, 0.31), vec3(0.22, 0.30, 0.37), depth);
	sea += (vnoise(c * 9.0 + vec2(TIME * 0.05, 0.0)) - 0.5) * 0.025 * zoom_fine;
	float shallow = 1.0 - smoothstep(0.0, clamp(cell_px * 0.55, 5.0, 26.0), dc);
	sea = mix(sea, vec3(0.30, 0.40, 0.44), shallow * 0.55);
	float is_water = step(0.0, coast);
	col = mix(col, sea, is_water);
	float coast_line = 1.0 - smoothstep(0.4, 1.4, dc);
	col = mix(col, vec3(0.10, 0.12, 0.14), coast_line * 0.85);

	// 地圖外：更深的海。
	if (cell.x < 0.0 || cell.y < 0.0 || cell.x > grid.x || cell.y > grid.y) {
		col = mix(col, vec3(0.10, 0.13, 0.17), 0.5);
	}
	// 第 12-2j 章：世界時代的氛圍——整片染色、畫面四角壓暗（黑暗時代）、緩慢流動的微光（奇蹟時代）。
	col = mix(col, col * age_tint.rgb, age_tint.a);
	float vig = smoothstep(0.30, 0.80, length(SCREEN_UV - vec2(0.5)) * 1.15);
	col *= 1.0 - age_vignette * vig;
	col += age_shimmer * (vnoise(c * 0.35 + vec2(TIME * 0.08, TIME * 0.05)) - 0.4) * vec3(0.10, 0.08, 0.02);
	// 紙張紋理（很淡）。
	col *= 0.97 + 0.05 * vnoise(c * 0.9 + vec2(11.0, 2.0));
	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
""";
}
