using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 06-4 章第 2 步（試玩回饋 2026-11-03：仍看得出六邊形、邊緣抖動不舒服、地塊銜接差）：
/// 地形改由著色器畫成一整片連續地表——每個像素依「扭曲後最近的格」取地形，不再每格貼一張六角形圖塊。
/// 交界處以像素風抖點過渡，海岸一側是沙灘、海的一側變淺；顏色與紋理依位置的雜訊產生（同一位置永遠一樣）。
/// 只影響畫面；格的歸屬仍是六角格（點選、規則不變）。畫在地圖本身的圖示與建築下面（ShowBehindParent）。
/// </summary>
public partial class TerrainSurface : Control
{
    /// <summary>地表代碼（資料貼圖每格一個位元組；著色器依此上色）。</summary>
    public enum Code : byte
    {
        Grassland = 0, Forest = 1, Hills = 2, HillsOre = 3, Sea = 4, River = 5, Shallow = 6,
        Farmland = 7, Clearing = 8, Burning = 9, Scorched = 10, DryGrass = 11
    }

    /// <summary>【TEMP】像素風的像素大小（格半徑的幾分之一；至少 1 個螢幕像素）。</summary>
    public const float TempPixelsPerRadius = 10f;

    private readonly ShaderMaterial _material;
    private ImageTexture? _cells;
    private byte[]? _uploaded;
    private (int Columns, int Rows) _grid;

    /// <summary>目前是否畫地表（近景層與區域層的地形模式）。</summary>
    public bool Active { get; private set; }

    public TerrainSurface()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        Material = _material;
    }

    /// <summary>每次地圖重畫時呼叫：<paramref name="codes"/> 依「列×欄數＋欄」排列；同一個陣列不會重複上傳。</summary>
    public void Show(byte[] codes, int columns, int rows, Vector2 origin, float radius)
    {
        if (!ReferenceEquals(codes, _uploaded) || _grid != (columns, rows))
        {
            var image = Image.CreateFromData(columns, rows, false, Image.Format.R8, codes);
            if (_cells is not null && _grid == (columns, rows))
                _cells.Update(image);
            else
            {
                _cells?.Dispose();
                _cells = ImageTexture.CreateFromImage(image);
                _material.SetShaderParameter("cells", _cells);
            }
            image.Dispose();
            _uploaded = codes;
            _grid = (columns, rows);
            _material.SetShaderParameter("grid", new Vector2I(columns, rows));
        }
        _material.SetShaderParameter("origin", origin);
        _material.SetShaderParameter("radius", radius);
        _material.SetShaderParameter("pixel", Mathf.Max(1f, radius / TempPixelsPerRadius));
        Active = true;
        QueueRedraw();
    }

    public void Hide()
    {
        if (!Active)
            return;
        Active = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Active)
            DrawRect(new Rect2(Vector2.Zero, Size), Colors.White);
    }

    private const string ShaderCode = """
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D cells : filter_nearest, repeat_disable;
uniform vec2 origin;
uniform float radius = 20.0;
uniform ivec2 grid = ivec2(1, 1);
uniform float pixel = 2.0;

varying vec2 local_pos;

void vertex() {
	local_pos = VERTEX;
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	return 0.5 * vnoise(p) + 0.3 * vnoise(p * 2.03 + 7.1) + 0.2 * vnoise(p * 4.11 + 3.7);
}

int code_at(ivec2 a) {
	int r = a.y;
	if (r < 0 || r >= grid.y) {
		return 4;
	}
	int col = a.x + (r - (r & 1)) / 2;
	if (col < 0 || col >= grid.x) {
		return 4;
	}
	return int(texelFetch(cells, ivec2(col, r), 0).r * 255.0 + 0.5);
}

vec2 center_of(ivec2 a) {
	return vec2(radius * 1.7320508 * (float(a.x) + float(a.y) * 0.5), radius * 1.5 * float(a.y));
}

ivec2 cube_round(vec2 f) {
	float fs = -f.x - f.y;
	float q = round(f.x);
	float r = round(f.y);
	float s = round(fs);
	float dq = abs(q - f.x);
	float dr = abs(r - f.y);
	float ds = abs(s - fs);
	if (dq > dr && dq > ds) {
		q = -r - s;
	} else if (dr > ds) {
		r = -q - s;
	}
	return ivec2(int(q), int(r));
}

bool is_water(int c) {
	return c == 4 || c == 5 || c == 6;
}

vec3 surface(int c, vec2 p, float coast, float t) {
	vec2 u = p / radius;
	float n = fbm(u * 1.3);
	float fine = hash(floor(p / pixel));
	if (c == 0) {
		return mix(vec3(0.36, 0.52, 0.28), vec3(0.47, 0.60, 0.33), n) + (fine - 0.5) * 0.04;
	}
	if (c == 1) {
		vec3 f = mix(vec3(0.12, 0.30, 0.18), vec3(0.21, 0.41, 0.22), vnoise(u * 3.2));
		return f + step(0.86, fine) * vec3(0.06, 0.08, 0.03);
	}
	if (c == 2 || c == 3) {
		float ridge = abs(vnoise(u * 2.0) - 0.5) * 2.0;
		vec3 h = mix(vec3(0.58, 0.52, 0.38), vec3(0.42, 0.38, 0.28), ridge) + (n - 0.5) * 0.08;
		if (c == 3 && fine > 0.93) {
			h = vec3(0.74, 0.68, 0.52);
		}
		return h;
	}
	if (c == 4) {
		vec3 s = mix(vec3(0.10, 0.22, 0.40), vec3(0.14, 0.28, 0.46), vnoise(u * 1.5 + vec2(t * 0.05, 0.0)));
		return mix(s, vec3(0.24, 0.46, 0.58), coast);
	}
	if (c == 5) {
		return mix(vec3(0.14, 0.42, 0.58), vec3(0.20, 0.50, 0.64), vnoise(u * 2.0 + vec2(0.0, t * 0.1)));
	}
	if (c == 6) {
		return fine > 0.85 ? vec3(0.55, 0.55, 0.50) : vec3(0.36, 0.62, 0.66);
	}
	if (c == 7) {
		float stripe = step(0.5, fract((p.x * 0.7 + p.y * 0.7) / (radius * 0.22)));
		return mix(vec3(0.70, 0.60, 0.30), vec3(0.62, 0.52, 0.25), stripe) + (n - 0.5) * 0.05;
	}
	if (c == 8) {
		vec3 b = mix(vec3(0.50, 0.40, 0.26), vec3(0.42, 0.48, 0.28), n);
		return fine > 0.92 ? vec3(0.32, 0.24, 0.16) : b;
	}
	if (c == 9) {
		return mix(vec3(0.55, 0.18, 0.06), vec3(1.0, 0.62, 0.18), vnoise(u * 4.0 + vec2(0.0, -t * 1.5)));
	}
	if (c == 10) {
		return mix(vec3(0.22, 0.19, 0.17), vec3(0.32, 0.28, 0.24), n);
	}
	if (c == 11) {
		return mix(vec3(0.62, 0.58, 0.34), vec3(0.72, 0.66, 0.40), n);
	}
	return vec3(0.4, 0.4, 0.4);
}

void fragment() {
	vec2 p = floor((local_pos - origin) / pixel) * pixel + pixel * 0.5;
	vec2 wp = p / (radius * 0.9);
	vec2 w = p + (vec2(vnoise(wp), vnoise(wp + 37.0)) - 0.5) * radius * 0.8;
	float fr = w.y / (1.5 * radius);
	float fq = w.x / (1.7320508 * radius) - fr * 0.5;
	ivec2 a0 = cube_round(vec2(fq, fr));
	int c1 = code_at(a0);
	float d1 = distance(w, center_of(a0));
	ivec2 dirs[6] = { ivec2(1, 0), ivec2(1, -1), ivec2(0, -1), ivec2(-1, 0), ivec2(-1, 1), ivec2(0, 1) };
	float d2 = 1e9;
	int c2 = c1;
	for (int i = 0; i < 6; i++) {
		ivec2 a = a0 + dirs[i];
		int c = code_at(a);
		if (c != c1) {
			float d = distance(w, center_of(a));
			if (d < d2) {
				d2 = d;
				c2 = c;
			}
		}
	}
	float edge = (d2 - d1) / radius;
	float coast = 0.0;
	if (c1 == 4 && !is_water(c2)) {
		coast = 1.0 - smoothstep(0.0, 0.5, edge);
	}
	vec3 col = surface(c1, p, coast, TIME);
	if (c2 != c1) {
		float t = smoothstep(0.0, 0.35, edge);
		if (hash(floor(p / pixel) + 3.1) > 0.5 + 0.5 * t) {
			col = surface(c2, p, 0.0, TIME);
		}
		if (!is_water(c1) && c2 == 4 && edge < 0.12) {
			col = mix(vec3(0.80, 0.74, 0.54), col, edge / 0.12);
		}
	}
	COLOR = vec4(col, 1.0);
}
""";
}
