namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-21 章：CK3 式政治地圖的著色器（畫在 <see cref="DemoHexMap"/> 後面的一張全畫面矩形上）。
/// 每個像素換算成「格座標」（加上一點雜訊扭曲，邊界才不是方格），取周圍四格依距離加權，
/// 權重最大的省份就是這個像素的省份；最大與次大的差距換算成離邊界幾像素，畫省界與勢力界。
/// 地形依四格加權混色，再加程式雜訊畫出森林、丘陵與草地的紋理；海岸、淺水由同樣的權重算出。
/// 資料：<c>ids</c>（RG＝省份索引、BA＝分組索引）、<c>land</c>（R＝地形、G＝地盤編號、B＝到海岸的有號距離，第 08-41 章）、<c>palette</c>（省份索引→填色）。
/// 第 08-41 章：照 CK3 的三層縮放改畫法——看全世界是紙地圖（羊皮紙、水彩勢力色、墨線山與樹），拉近是地形圖淡淡蓋上勢力色；
/// 海依離岸距離由淺到深、近岸畫等距線（取代原本一塊塊的雜訊）。
/// 第 08-59 章（地圖改寫實地形第 1 步）：中、近距離的地面改用寫實材質（從定樣的近景樣張切的草原、森林、水面無縫貼圖，<c>tex_*</c>），
/// 森林邊緣依樹冠明暗咬進草原、往右下投影子；水色照樣張；勢力界照中距離樣張（深色線＋內側亮的勢力色線）。遠看的紙地圖不變。
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
uniform int sel_nest = 0;
// 第 12-2j 章：世界時代的氛圍（黑暗時代長夜降臨、烈日偏暖、霜雪偏冷……）；a＝濃淡。
uniform vec4 age_tint = vec4(1.0, 1.0, 1.0, 0.0);
uniform float age_vignette = 0.0;
uniform float age_shimmer = 0.0;
// 第 08-59 章：地面材質（從定樣的近景樣張切的無縫貼圖）；tex_ready＝1 才用，缺貼圖時照舊用純色。
uniform sampler2D tex_grass : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_forest : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_water : source_color, filter_linear_mipmap, repeat_enable;
uniform float tex_ready = 0.0;

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

// 第 08-41 章：紙地圖的樹（墨線畫的小圓樹冠＋樹幹）：x＝墨線、y＝樹冠內。
vec2 paper_tree(vec2 c, float density) {
	vec2 g = c * density;
	vec2 id = floor(g);
	vec2 f = fract(g);
	vec2 ctr = vec2(0.5) + (vec2(hash(id + 3.1), hash(id + 7.7)) - 0.5) * 0.40;
	float r = 0.17 + 0.06 * hash(id + 1.9);
	float d = length((f - ctr) * vec2(1.0, 1.1));
	float ink = 1.0 - smoothstep(0.025, 0.06, abs(d - r));
	vec2 t = f - (ctr + vec2(0.0, r));
	ink = max(ink, step(abs(t.x), 0.022) * step(0.0, t.y) * step(t.y, r * 0.7));
	return vec2(ink, 1.0 - smoothstep(r - 0.02, r + 0.02, d));
}

// 第 08-41 章：等距線（離海岸 at 格的一條細線；fw＝距離每像素變多少）。
float ring(float sd, float fw, float at, float width) {
	return 1.0 - smoothstep(width * 0.5, width * 0.5 + 1.0, abs(sd - at) / max(fw, 1e-4));
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
// 第 08-41 章：配色往 CK3 的地形圖靠——草原橄欖綠、森林深綠、丘陵土褐，彩度壓低、明暗拉開。
// 第 08-53 章（使用者 2026-11-26：世界地圖還是很醜，詳細參考 CK3）：CK3 細節層的地形一眼看得懂——草原明亮的黃綠、森林深綠、
// 丘陵土褐，色塊之間是柔和的漸層（不是一塊塊的迷彩）。
vec3 terrain_color(int t) {
	if (t == 1) return vec3(0.24, 0.38, 0.22);
	if (t == 2) return vec3(0.20, 0.30, 0.38);
	if (t == 3) return vec3(0.58, 0.51, 0.39);
	return vec3(0.60, 0.65, 0.40);
}

float luma(vec3 c) {
	return dot(c, vec3(0.30, 0.59, 0.11));
}

// 第 08-59 章：不會一眼看出重複的貼圖取樣（Inigo Quilez「texture repetition」第三法）：低頻雜訊決定錯開多少，
// 兩種錯法的交界依顏色差混合；傳入原本的導數，交界不會取錯縮小版。
vec3 tex_notile(sampler2D s, vec2 uv) {
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	float l = vnoise(uv * 0.85) * 8.0;
	float i = floor(l);
	float f = fract(l);
	vec3 a = textureGrad(s, uv + sin(vec2(3.0, 7.0) * i), dx, dy).rgb;
	vec3 b = textureGrad(s, uv + sin(vec2(3.0, 7.0) * (i + 1.0)), dx, dy).rgb;
	return mix(a, b, smoothstep(0.2, 0.8, f - 0.1 * dot(a - b, vec3(1.0))));
}

// 第 08-59 章：某一點的地形權重（x＝丘陵、y＝森林；四格平滑加權，只算陸地）——森林往右下投的影子要在偏移的位置再算一次。
vec2 land_mix(vec2 cs) {
	ivec2 size = ivec2(grid);
	vec2 qs = cs - 0.5;
	vec2 fls = floor(qs);
	vec2 fs = qs - fls;
	vec2 us = fs * fs * (3.0 - 2.0 * fs);
	float wt[4] = float[](
		(1.0 - us.x) * (1.0 - us.y), us.x * (1.0 - us.y),
		(1.0 - us.x) * us.y, us.x * us.y);
	ivec2 offs[4] = ivec2[](ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 1));
	ivec2 j0 = ivec2(fls);
	float lw = 0.0;
	vec2 hf = vec2(0.0);
	for (int k = 0; k < 4; k++) {
		int t = int(texelFetch(land, clamp(j0 + offs[k], ivec2(0), size - ivec2(1)), 0).r * 255.0 + 0.5);
		if (t == 2) continue;
		lw += wt[k];
		hf += wt[k] * vec2(t == 3 ? 1.0 : 0.0, t == 1 ? 1.0 : 0.0);
	}
	return lw > 0.0 ? hf / lw : vec2(0.0);
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
	int nest[4];
	float sdc[4]; // 第 08-41 章：到海岸的有號距離（格；海為正、陸為負）
	for (int k = 0; k < 4; k++) {
		ivec2 at = i0 + offs[k];
		if (at.x < 0 || at.y < 0 || at.x >= size.x || at.y >= size.y) {
			pid[k] = 65535;
			gid[k] = 65535;
			ter[k] = 2;
			blight[k] = 0.0;
			nest[k] = 0;
			float edge_sd = (texelFetch(land, clamp(at, ivec2(0), size - ivec2(1)), 0).b * 255.0 - 128.0) / 8.0;
			sdc[k] = max(edge_sd, 0.5);
		} else {
			vec4 a = texelFetch(ids, at, 0);
			vec4 b = texelFetch(land, at, 0);
			pid[k] = idx16(a.rg);
			gid[k] = idx16(a.ba);
			ter[k] = int(b.r * 255.0 + 0.5);
			nest[k] = int(b.g * 255.0 + 0.5);
			blight[k] = nest[k] > 0 ? 1.0 : 0.0;
			sdc[k] = (b.b * 255.0 - 128.0) / 8.0;
		}
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
	float mp = max(best_p, 0.0) - second_p;
	float dp = mp / max(fwidth(mp), 1e-4);
	float mg = max(best_g, 0.0) - second_g;
	float dg = mg / max(fwidth(mg), 1e-4);

	float zoom_fine = clamp((cell_px - 18.0) / 50.0, 0.0, 1.0);
	// 第 08-41 章（使用者 2026-11-19：地圖還是很醜，參考實際遊戲）：參考 CK3 的三層縮放——遠看是紙地圖（CK3 開發日誌 #2），
	// 拉近是看得出地形的地形圖、勢力色淡淡蓋在上面；海依離岸距離由淺到深、近岸有幾道等距線（Azgaar 地圖產生器的 ocean layers）。
	float paper = 1.0 - smoothstep(22.0, 34.0, cell_px);
	float sd = 0.0;
	for (int k = 0; k < 4; k++) sd += ws[k] * sdc[k];
	float fw = max(fwidth(sd), 1e-4);
	float is_water = clamp(sd / fw + 0.5, 0.0, 1.0); // 海岸反鋸齒（一像素）
	float sdpx = abs(sd) / fw; // 離海岸線幾像素

	// 地形（只混陸地格，海岸不會被水色染暗）。
	// 第 08-53 章：地形色用平滑的權重（不用畫邊界的扭曲座標），草原、森林、丘陵之間是柔和的漸層，不再是一塊塊鋸齒迷彩。
	vec2 cs = cell + (vec2(vnoise(cell * 1.3), vnoise(cell * 1.3 + vec2(7.1, 2.9))) - 0.5) * 0.35;
	vec2 qs = cs - 0.5;
	vec2 fls = floor(qs);
	vec2 fs = qs - fls;
	vec2 us = fs * fs * (3.0 - 2.0 * fs);
	float wt[4] = float[](
		(1.0 - us.x) * (1.0 - us.y), us.x * (1.0 - us.y),
		(1.0 - us.x) * us.y, us.x * us.y);
	ivec2 j0 = ivec2(fls);
	vec3 tcol = vec3(0.0);
	float lw = 0.0;
	float hill = 0.0;
	float forest = 0.0;
	for (int k = 0; k < 4; k++) {
		ivec2 at = clamp(j0 + offs[k], ivec2(0), size - ivec2(1));
		int t = int(texelFetch(land, at, 0).r * 255.0 + 0.5);
		if (t == 2) continue;
		tcol += wt[k] * terrain_color(t);
		lw += wt[k];
		hill += wt[k] * (t == 3 ? 1.0 : 0.0);
		forest += wt[k] * (t == 1 ? 1.0 : 0.0);
	}
	tcol = lw > 0.0 ? tcol / lw : terrain_color(0);
	hill = lw > 0.0 ? hill / lw : 0.0;
	forest = lw > 0.0 ? forest / lw : 0.0;
	float bl = 0.0;
	float ns = 0.0;
	for (int k = 0; k < 4; k++) {
		bl += ws[k] * blight[k];
		if (sel_nest > 0 && nest[k] == sel_nest) ns += ws[k];
	}
	// 很淡的低頻明暗（不再有高頻顆粒與草地雜斑）。
	float grain = (vnoise(c * 2.0) - 0.5) * 0.05;
	// 第 08-26 章：起伏——雜訊高度場的斜率當光照（左上來光）；第 08-53 章：平地幾乎平的，丘陵越密越明顯（CK3 的地勢陰影）。
	float e = 0.06;
	float h0 = fbm(c * 1.4);
	vec2 grad = vec2(fbm(c * 1.4 + vec2(e, 0.0)) - h0, fbm(c * 1.4 + vec2(0.0, e)) - h0) / e;
	float lit = clamp(0.5 - dot(grad, vec2(0.55, 0.55)) * 0.9, 0.0, 1.0);
	float shade = 1.0 + grain + (lit - 0.5) * (0.04 + hill * 0.60 + forest * 0.10);
	// 第 08-53 章：森林是柔和的深色（中心較深），樹叢只在拉近時畫；山形只在紙地圖與拉近時畫，中距離只看地勢陰影。
	float fmask = smoothstep(0.30, 0.70, forest);
	float hmask = smoothstep(0.45, 0.75, hill);
	shade -= fmask * 0.06;
	// 第 08-59 章（地圖改寫實地形第 1 步）：拿掉程式畫的樹叢、山的三角形、草地雜紋與第 08-56 章的像素樹，改用材質。
	vec3 ground = tcol * shade;
	if (tex_ready > 0.5) {
		// 草原、森林各一張無縫貼圖，貼在世界座標上（草原一張 4 格、森林 3 格，照樣張的比例），拖地圖時不會滑動；
		// 大範圍的深淺、冷暖另外加，遠看不會像同一塊磁磚。
		float macro = vnoise(cell * 0.16 + vec2(3.0, 11.0));
		float macro2 = vnoise(cell * 0.45 + vec2(17.0, 5.0));
		vec3 gtex = tex_notile(tex_grass, cell / 4.0) * mix(vec3(0.90, 0.96, 1.06), vec3(1.07, 1.03, 0.90), macro) * (0.93 + 0.14 * macro2);
		vec3 ftex = tex_notile(tex_forest, cell / 3.0) * (0.90 + 0.20 * macro2);
		// 丘陵暫時是偏土褐的乾草地（越密越褐）；真正的岩地、山與雪線是第 2 步（高度圖）。
		vec3 htex = mix(gtex, vec3(0.50, 0.45, 0.31) * (luma(gtex) / 0.47), 0.30 + 0.30 * hmask);
		vec3 base = mix(gtex, htex, clamp(hill * 1.3, 0.0, 1.0));
		// 森林邊緣依樹冠的明暗一叢叢咬進草原（高度混合），不是兩種顏色的漸層。
		float crown = (luma(ftex) / 0.25 - 1.0) * 0.30;
		float fwt = smoothstep(0.40, 0.60, forest + crown);
		// 樹林往右下投影子到草地上（光從左上）。
		float fshadow = smoothstep(0.40, 0.60, land_mix(cs - vec2(0.10, 0.10)).y + crown);
		base *= 1.0 - max(fshadow - fwt, 0.0) * 0.26;
		base = mix(base, ftex, fwt);
		// 地勢的明暗沿用雜訊高度（丘陵越密越明顯）；第 2 步改成由地形推的高度圖。
		vec3 rground = base * (1.0 + (lit - 0.5) * (0.03 + hill * 0.65));
		// 沙灘：陸地靠海的一窄條（樣張的沙色）。
		float rbeach = (1.0 - smoothstep(0.0, 0.34, -sd)) * (1.0 - is_water) * (1.0 - hill * 0.6) * (1.0 - fwt * 0.7);
		rground = mix(rground, vec3(0.84, 0.76, 0.62) * (0.95 + 0.10 * vnoise(cell * 7.0)), rbeach * 0.80);
		ground = rground;
	} else {
		// 第 08-41 章：沙灘——陸地靠海的一窄條偏暖（丘陵、森林少一點）。
		float beach = (1.0 - smoothstep(0.0, 0.42, -sd)) * (1.0 - is_water) * (1.0 - hill * 0.6) * (1.0 - fmask * 0.5);
		ground = mix(ground, vec3(0.80, 0.74, 0.54) * (0.97 + grain), beach * 0.55);
	}

	// 政治填色：省份索引查色票；荒野（0）不填。
	vec4 fill = texelFetch(palette, ivec2(bp % 256, bp / 256), 0);
	float has_fill = (bp != 0 && fill.a > 0.5) ? 1.0 : 0.0;
	float realm_w = clamp(cell_px * 0.07, 1.2, 3.2);
	// 第 08-41 章：靠近勢力界的一圈更濃（CK3 的邊界色帶）。
	// 第 08-53 章：勢力色只借地形的明暗（不借色相）——CK3 的勢力層是乾淨的勢力色，地形只是光影；原本與地形色對半混成泥巴色。
	float band_w = clamp(cell_px * 0.55, 3.0, 16.0);
	float band = (1.0 - smoothstep(realm_w, realm_w + band_w, dg)) * has_fill;
	vec3 political = fill.rgb * clamp(0.62 + 0.80 * luma(ground), 0.70, 1.18);
	// 第 08-59 章：寫實地形上照中距離樣張——勢力色只換色相、保留地面的明暗與紋理（草原、森林看得出來），濃度也淡一些。
	vec3 hue_only = clamp(fill.rgb / max(luma(fill.rgb), 0.08) * luma(ground) * 1.08, 0.0, 1.0);
	political = mix(political, hue_only, tex_ready);
	float wash = has_fill * clamp(politics * mix(1.0, 0.72, tex_ready) + band * 0.25, 0.0, 0.92);
	vec3 col = mix(ground, political, wash);

	// 第 08-41 章：紙地圖（遠看）——羊皮紙底、勢力是淡淡的水彩（邊界附近較濃）、丘陵畫墨線山形。
	vec3 parch = vec3(0.87, 0.81, 0.66) * (0.97 + 0.05 * vnoise(c * 1.3 + vec2(3.0, 7.0)));
	parch = mix(parch, vec3(0.72, 0.75, 0.56), fmask * 0.45);
	parch = mix(parch, vec3(0.80, 0.70, 0.54), hmask * 0.50);
	float pband = (1.0 - smoothstep(realm_w, realm_w + clamp(cell_px * 1.4, 3.0, 9.0), dg)) * has_fill;
	// 第 08-53 章：紙地圖的勢力色濃一點（CK3 遠看只剩獨立勢力，一塊一塊清楚），邊界色帶再加深。
	vec3 pcol = mix(parch, fill.rgb, (politics > 0.01 ? 1.0 : 0.0) * has_fill * (0.50 + 0.30 * pband));
	vec3 pm = mountain_glyph(cell, 0.6);
	pcol = mix(pcol, pcol * 1.08, hmask * pm.x * (pm.y > 0.5 ? 1.0 : 0.0));
	pcol = mix(pcol, pcol * 0.86, hmask * pm.x * (pm.y > 0.5 ? 0.0 : 1.0));
	pcol = mix(pcol, vec3(0.30, 0.23, 0.16), hmask * pm.z * 0.85);
	vec2 ptree = paper_tree(cell, 0.9);
	float tmask = smoothstep(0.45, 0.70, forest) * (1.0 - hmask);
	pcol = mix(pcol, pcol * vec3(0.86, 0.93, 0.80), tmask * ptree.y);
	pcol = mix(pcol, vec3(0.24, 0.27, 0.17), tmask * ptree.x * 0.75);
	col = mix(col, pcol, paper);

	// 選到的省份或勢力：整片提亮、邊緣金色。
	bool sel = (bp == sel_province && sel_province > 0) || (bg == sel_group && sel_group > 0);
	if (sel) {
		col = mix(col, min(col * 1.18 + 0.05, vec3(1.0)), 0.6);
	}

	// 省界（細、半透明；紙地圖不畫）與勢力界（地形圖：填色的深色；紙地圖：墨線）。
	float pline = (1.0 - smoothstep(0.35, 1.25, dp)) * province_lines * (1.0 - paper);
	col = mix(col, col * 0.50, pline * (1.0 - step(dg, realm_w + 0.5)));
	float gline = 1.0 - smoothstep(realm_w - 0.6, realm_w + 0.6, dg);
	vec3 edge = has_fill > 0.5 ? fill.rgb * mix(0.38, 0.22, tex_ready) : vec3(0.16, 0.13, 0.10);
	edge = mix(edge, vec3(0.32, 0.22, 0.16), paper);
	col = mix(col, edge, gline * (0.55 + 0.45 * max(politics, paper)));
	// 第 08-59 章：照中距離樣張——深色邊界線內側再一條亮的勢力色線（紙地圖不畫）。
	float iw = clamp(cell_px * 0.035, 1.0, 2.4);
	float inner = (1.0 - smoothstep(realm_w + iw - 0.5, realm_w + iw + 0.5, dg)) * (1.0 - gline) * has_fill * (1.0 - paper) * tex_ready;
	col = mix(col, min(fill.rgb * 1.18 + 0.05, vec3(1.0)), inner * mix(0.45, 0.85, clamp(politics / 0.8, 0.0, 1.0)));
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

	// 第 08-41 章：水——離岸越遠越深（不再是一塊塊的雜訊斑）、近岸三道等距線、岸邊一圈淺色浪花；紙地圖改成淡藍灰與墨色等距線。
	float depth = smoothstep(0.0, 1.0, pow(clamp(sd / 7.0, 0.0, 1.0), 0.7));
	vec3 sea = mix(vec3(0.33, 0.49, 0.54), vec3(0.12, 0.21, 0.31), depth);
	sea += (vnoise(c * 1.2 + vec2(TIME * 0.02, 0.0)) - 0.5) * 0.02;
	// 第 08-53 章：近岸只留兩道淡淡的等距線（原本三道很亮，遠看像一圈光暈）。
	float rw = clamp(cell_px * 0.05, 0.8, 1.6);
	float rings = ring(sd, fw, 0.85, rw) * 0.35 + ring(sd, fw, 1.9, rw) * 0.18;
	sea = mix(sea, sea * 1.18 + 0.03, rings);
	float foam = (1.0 - smoothstep(0.0, 0.3, sd)) * is_water;
	sea = mix(sea, vec3(0.62, 0.72, 0.72), foam * 0.4);
	if (tex_ready > 0.5) {
		// 第 08-59 章：水色照定樣樣張（近岸青綠、離岸深藍），水面細紋是樣張切的貼圖（拉近才看得清楚）；不再畫等距線與一圈亮邊。
		float rdepth = smoothstep(0.0, 1.0, pow(clamp(sd / 4.5, 0.0, 1.0), 0.6));
		vec3 rsea = mix(vec3(0.24, 0.55, 0.57), vec3(0.11, 0.31, 0.41), rdepth);
		rsea = mix(rsea, vec3(0.40, 0.64, 0.60), (1.0 - smoothstep(0.0, 0.45, sd)) * is_water * 0.55);
		float wl = luma(tex_notile(tex_water, cell / 1.5 + vec2(TIME * 0.006, TIME * 0.003))) / 0.286;
		rsea *= 1.0 + (wl - 1.0) * 0.55 * smoothstep(18.0, 60.0, cell_px);
		rsea *= 0.96 + 0.08 * vnoise(cell * 0.3 + vec2(TIME * 0.01, 0.0));
		sea = rsea;
	}
	vec3 psea = mix(vec3(0.71, 0.78, 0.79), vec3(0.57, 0.66, 0.71), depth);
	psea = mix(psea, vec3(0.40, 0.50, 0.55), ring(sd, fw, 0.9, 1.0) * 0.45 + ring(sd, fw, 2.1, 1.0) * 0.2);
	sea = mix(sea, psea, paper);
	col = mix(col, sea, is_water);
	// 海岸線：地形圖是深色細線，紙地圖是墨線（拉近時粗一點）。
	float cw = clamp(cell_px * 0.06, 0.7, 1.8);
	float coast_line = 1.0 - smoothstep(cw, cw + 1.0, sdpx);
	col = mix(col, mix(vec3(0.10, 0.12, 0.14), vec3(0.24, 0.19, 0.14), paper), coast_line * mix(0.85, mix(0.18, 0.85, paper), tex_ready)); // 第 08-59 章：寫實地形的海岸不畫深色線

	// 怪物地盤：暗紅、斜紋（世界座標，拖地圖時不會滑動）。第 08-41 章：移到海後面畫，海上的地盤也看得到。
	float bmask = smoothstep(0.42, 0.58, bl);
	float stripes = step(0.5, fract((c.x + c.y) * 2.5));
	col = mix(col, mix(vec3(0.30, 0.06, 0.07), vec3(0.20, 0.03, 0.04), stripes), bmask * 0.55);
	// 第 08-31 章：選到的怪物地盤——整片提亮、邊緣金色（跟選到的勢力範圍一樣，不畫格子的方框）。
	if (sel_nest > 0) {
		float nmask = smoothstep(0.42, 0.58, ns);
		col = mix(col, min(col * 1.35 + 0.06, vec3(1.0)), nmask * 0.5);
		float nd = abs(ns - 0.5) / max(fwidth(ns), 1e-4);
		float ngold = (1.0 - smoothstep(1.2, 2.6, nd)) * step(0.001, ns) * step(ns, 0.999);
		col = mix(col, vec3(0.98, 0.84, 0.36), ngold * (0.75 + 0.25 * sin(TIME * 3.0)));
	}

	// 地圖外：更深的海。
	if (cell.x < 0.0 || cell.y < 0.0 || cell.x > grid.x || cell.y > grid.y) {
		col = mix(col, mix(vec3(0.10, 0.13, 0.17), vec3(0.55, 0.62, 0.66), paper), 0.5);
	}
	// 第 12-2j 章：世界時代的氛圍——整片染色、畫面四角壓暗（黑暗時代）、緩慢流動的微光（奇蹟時代）。
	col = mix(col, col * age_tint.rgb, age_tint.a);
	float vig = smoothstep(0.30, 0.80, length(SCREEN_UV - vec2(0.5)) * 1.15);
	col *= 1.0 - age_vignette * vig;
	col += age_shimmer * (vnoise(c * 0.35 + vec2(TIME * 0.08, TIME * 0.05)) - 0.4) * vec3(0.10, 0.08, 0.02);
	// 紙張紋理（很淡；紙地圖明顯一點）。
	col *= 0.985 + (0.025 + 0.035 * paper * (1.0 - is_water)) * vnoise(c * 0.9 + vec2(11.0, 2.0)); // 第 08-53 章：海上不再斑駁
	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
""";
}
