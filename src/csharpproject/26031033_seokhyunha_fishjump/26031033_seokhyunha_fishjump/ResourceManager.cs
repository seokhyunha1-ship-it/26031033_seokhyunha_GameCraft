using System.Numerics;

using Vortice.Mathematics;

using System.Windows.Forms;

using System.Text.Json;


sealed class ResourceManager : IDisposable
{

    readonly Dictionary<string, (G2Texture texture, int w, int h)> cache = [];

    readonly Queue<string> order = [];

    public void Draw(string name, float x, float y, float w, float h, float opacity = 1, bool cover = false, bool stretch = false)
    {

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "images", name + ".png");

        if (!File.Exists(path)) throw new FileNotFoundException("Missing game image", path);

        if (!cache.TryGetValue(name, out var t))
        {
            using var img = System.Drawing.Image.FromFile(path);
            t = (new(path), img.Width, img.Height);
            cache.Add(name, t);
            order.Enqueue(name);
            if (cache.Count > 28)
            {
                string old = order.Dequeue();
                cache[old].texture.Dispose();
                cache.Remove(old);
            }
        }

        float ratio = cover ? Math.Max(w / t.w, h / t.h) : Math.Min(w / t.w, h / t.h);
        float dw = stretch ? w : t.w * ratio, dh = stretch ? h : t.h * ratio;

        t.texture.Draw(new Rect(x + (w - dw) / 2, y + (h - dh) / 2, dw, dh), new Rect(0, 0, t.w, t.h), opacity);

    }

    public void Dispose()
    {
        foreach (var t in cache.Values) t.texture.Dispose();
        cache.Clear();
    }

}
