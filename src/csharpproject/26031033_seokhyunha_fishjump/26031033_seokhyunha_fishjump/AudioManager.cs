sealed class AudioManager : IDisposable
{

    readonly Dictionary<string, G2Audio> sounds = [], music = [];

    readonly Dictionary<string, float> levels = [];

    readonly Dictionary<string, float> lastPlayed = [];

    string current = "";
    float time;
    bool paused;
    readonly GameSettings settings;

    public AudioManager(GameSettings s)
    {

        settings = s;
        string p = Path.Combine(AppContext.BaseDirectory, "assets", "sounds");

        foreach (var f in Directory.GetFiles(p, "sfx_*.wav"))
        {
            var a = new G2Audio(f);
            a.SetVolume(s.SfxVolume);
            sounds[Path.GetFileNameWithoutExtension(f)[4..]] = a;
        }

        if (s.UseProvidedEffects) foreach (var f in Directory.GetFiles(p, "*.mp3")) try
            {
                var a = new G2Audio(f);
                a.SetVolume(s.SfxVolume);
                string name = Path.GetFileNameWithoutExtension(f);
                if (sounds.TryGetValue(name, out var old)) old.Dispose();
                sounds[name] = a;
            }
            catch (Exception e)
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "audio-errors.log"), Path.GetFileName(f) + ": " + e.Message + "\n");
            }

        foreach (var f in Directory.GetFiles(p, "bgm_*.wav"))
        {
            string name = Path.GetFileNameWithoutExtension(f)[4..];
            music[name] = new(f);
            levels[name] = 0;
            music[name].SetVolume(0);
        }

    }

    public void Play(string name)
    {
        if (lastPlayed.TryGetValue(name, out float t) && time - t < .1f) return;
        lastPlayed[name] = time;
        if (sounds.TryGetValue(name, out var a)) a.Play();
    }

    public void Music(string name)
    {
        if (name == current) return;
        current = name;
        if (music.TryGetValue(name, out var b) && !b.IsPlaying()) b.Play(true);
    }

    public void Pause(bool value)
    {
        if (paused == value) return;
        paused = value;
        foreach (var a in sounds.Values) if (value) a.Pause();
        else a.Resume();
        foreach (var a in music.Values) if (value) a.Pause();
        else a.Resume();
    }

    public void Update(float dt)
    {
        if (paused) return;
        time += dt;
        foreach (var (name, a) in music)
        {
            float target = name == current ? settings.BgmVolume : 0;
            float v = levels[name];
            v += Math.Clamp(target - v, -dt * .2f, dt * .2f);
            levels[name] = v;
            a.SetVolume(v);
            if (v == 0 && name != current) a.Stop();
        }
    }

    public void Dispose()
    {
        foreach (var a in sounds.Values) a.Dispose();
        foreach (var a in music.Values) a.Dispose();
    }

}
