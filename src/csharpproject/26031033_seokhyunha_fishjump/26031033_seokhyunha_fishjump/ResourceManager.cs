using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.WIC;

// 원본 PNG는 보존하고 GPU에 올릴 때만 표시 크기에 맞춰 줄인다.
sealed class ResourceManager : IDisposable
{
    sealed record Entry(ID2D1Bitmap Bitmap, long Bytes, LinkedListNode<string> Node);
    readonly Dictionary<string, Entry> cache = [];
    readonly LinkedList<string> recent = new();
    readonly IWICImagingFactory factory = new();
    const long Budget = 96L * 1024 * 1024;
    public long CachedBytes { get; private set; }
    public int LoadCount { get; private set; }

    public static int TextureLimit(string name) => name.StartsWith("배경") || name == "게임시작화면" ? 1536 :
        name is "오리1" or "오리2" or "오리3" ? 2048 :
        (name.StartsWith("빨간물") || name.StartsWith("파란물")) && !name.Contains("첨벙") ? 640 : 256;

    Entry Get(string name)
    {
        if (cache.TryGetValue(name, out var entry))
        {
            recent.Remove(entry.Node); recent.AddFirst(entry.Node);
            return entry;
        }
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "images", name + ".png");
        using var decoder = factory.CreateDecoderFromFileName(path);
        using var frame = decoder.GetFrame(0);
        frame.GetSize(out uint width, out uint height);
        float ratio = Math.Min(1, TextureLimit(name) / (float)Math.Max(width, height));
        uint w = Math.Max(1, (uint)MathF.Round(width * ratio)), h = Math.Max(1, (uint)MathF.Round(height * ratio));
        using var scaler = factory.CreateBitmapScaler();
        scaler.Initialize(frame, w, h, Vortice.WIC.BitmapInterpolationMode.Fant);
        using var converter = factory.CreateFormatConverter();
        converter.Initialize(scaler, Vortice.WIC.PixelFormat.Format32bppPBGRA);
        var target = G2AppBase.Instance!.RenderTarget;
        var bitmap = target.CreateBitmapFromWicBitmap(converter);
        long bytes = (long)w * h * 4;
        var node = recent.AddFirst(name);
        entry = new(bitmap, bytes, node);
        cache.Add(name, entry); CachedBytes += bytes; LoadCount++;
        while (cache.Count > 1 && (CachedBytes > Budget || cache.Count > 96))
        {
            string old = recent.Last!.Value;
            var removed = cache[old]; removed.Bitmap.Dispose();
            CachedBytes -= removed.Bytes; cache.Remove(old); recent.RemoveLast();
        }
        return entry;
    }

    public void Preload(string name) => Get(name);

    public void PreloadGameplay()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "assets", "images"), "*.png"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("배경") && name is not "esc메뉴ui" and not "게임오버ui" and not "점프게이지") Get(name);
        }
    }

    public void Draw(string name, float x, float y, float w, float h, float opacity = 1, bool cover = false, bool stretch = false)
    {
        var bitmap = Get(name).Bitmap;
        var size = bitmap.Size;
        float ratio = cover ? Math.Max(w / size.Width, h / size.Height) : Math.Min(w / size.Width, h / size.Height);
        float dw = stretch ? w : size.Width * ratio, dh = stretch ? h : size.Height * ratio;
        G2AppBase.Instance!.RenderTarget.DrawBitmap(bitmap, new Rect(x + (w - dw) / 2, y + (h - dh) / 2, dw, dh),
            opacity, Vortice.Direct2D1.BitmapInterpolationMode.Linear, new Rect(0, 0, size.Width, size.Height));
    }

    public void Dispose()
    {
        foreach (var entry in cache.Values) entry.Bitmap.Dispose();
        cache.Clear(); recent.Clear(); CachedBytes = 0; factory.Dispose();
    }
}
