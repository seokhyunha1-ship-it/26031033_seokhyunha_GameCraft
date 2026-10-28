using System.Numerics;

using Vortice.Mathematics;

using System.Windows.Forms;

using System.Text.Json;


sealed class BackgroundManager
{

    public string Name(GameWorld w)
    {

        float h = w.Players.Max(p => p.Highest);
        int s = w.Stage;
        float start = w.Settings.StageHeights[s], offset = h - start;
        int variant = (int)(offset / 500) % 3 + 1;

        if (s == 0) return "배경1";
        if (s == 1) return "배경2";

        if (s == 2)
        {
            if (h > w.Settings.StageHeights[3] - 200) return "배경3예시3";
            return offset < 150 ? "배경3" : "배경3예시" + variant;
        }

        if (s == 3)
        {
            if (h > w.Settings.StageHeights[4] - 300) return "배경4에서배경5로";
            return offset < 150 ? "배경4" : "배경4예시" + variant;
        }

        if (s == 4)
        {
            if (h > w.Settings.StageHeights[5] - 300) return "배경5에서배경6으로";
            return offset < 150 ? "배경5" : "배경5예시" + variant;
        }

        return offset < 500 ? "배경6예시1" : offset > 2500 ? "배경6예시4" : "배경6예시" + (2 + (int)(offset / 500) % 2);

    }

    string current = "배경1", previous = "배경1";
    float blend = 1, pan;

    public void Update(GameWorld w, float dt)
    {
        pan = Math.Min(40, w.Camera * .02f);
        var n = Name(w);
        if (n != current)
        {
            previous = current;
            current = n;
            blend = 0;
        }
        blend = Math.Min(1, blend + dt * .6f);
    }

    public void Draw(ResourceManager r)
    {
        r.Draw(previous, -32, -48 + pan, GameGlobal.ScreenSize.Width + 64, GameGlobal.ScreenSize.Height + 48, 1, true);
        r.Draw(current, -32, -48 + pan, GameGlobal.ScreenSize.Width + 64, GameGlobal.ScreenSize.Height + 48, blend, true);
    }

}
