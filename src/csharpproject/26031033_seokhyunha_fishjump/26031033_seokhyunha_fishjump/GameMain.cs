using System.Numerics;

using Vortice.Mathematics;

using System.Windows.Forms;

using System.Text.Json;


class GameMain : G2AppBase
{

    public override System.Drawing.Size ScreenSize => GameGlobal.ScreenSize;
    public override string GameName => GameGlobal.GameName;

    GameSettings settings = null!;
    GameWorld world = null!;
    ResourceManager resources = null!;
    BackgroundManager background = new();
    G2Font font = null!;
    AudioManager audio = null!;

    bool assetCheck = Environment.GetCommandLineArgs().Contains("--asset-check");

    enum State
    {
        Title, Ready, Playing, Paused, GameOver, Results
    }

    State state = State.Title, returnState;
    float ready, accumulator;
    int menu, record;
    string recordPath = "";

    Keys[] parryKeys = [];
    bool[] pendingParry = new bool[2];
    int[] jumpBias = new int[2];

    Keys[][] keys = [];
    bool[] pendingPress = new bool[2], pendingRelease = new bool[2];
    Vector2[] directions = new Vector2[2];

    protected override void Initialize()
    {
        settings = GameSettings.Load();
        resources = new();
        if (assetCheck)
        {
            var lines = new List<string>();
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "assets", "images"), "*.png"))
            {
                using var texture = new G2Texture(path);
                lines.Add("PASS " + Path.GetFileName(path));
            }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "asset-check-results.txt"), lines);
        }
        font = new("Malgun Gothic", 21);
        audio = new(settings);
        keys = [settings.Player1Keys.Select(Enum.Parse<Keys>).ToArray(), settings.Player2Keys.Select(Enum.Parse<Keys>).ToArray()];
        parryKeys = settings.ParryKeys.Select(Enum.Parse<Keys>).ToArray();
        recordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JumpingFish", "record.txt");
        if (File.Exists(recordPath)) int.TryParse(File.ReadAllText(recordPath), out record);
        Reset();
        state = State.Title;
        audio.Music("title");
    }

    void Reset()
    {
        audio.Pause(false);
        audio.Music("early");
        world = new(settings);
        Array.Clear(pendingParry);
        Array.Clear(jumpBias);
        world.Sound = audio.Play;
        ready = 1.5f;
        accumulator = 0;
        background = new();
        Array.Clear(pendingPress);
        Array.Clear(pendingRelease);
        menu = 0;
    }

    bool Held(Keys k) => Input.IsKeyDown(k) || Input.IsKeyPress(k);

    Vector2 ReadDirection(int i)
    {
        float x = (Held(keys[i][3]) ? 1 : 0) - (Held(keys[i][2]) ? 1 : 0);
        float y = (Held(keys[i][0]) ? 1 : 0) - (Held(keys[i][1]) ? 1 : 0);
        var direction = new Vector2(x, y);
        return direction.LengthSquared() > 1 ? Vector2.Normalize(direction) : direction;
    }

    bool MouseInside(float x, float y, float width, float height)
    {
        var m = Input.MousePosition;
        return m.X >= x && m.X < x + width && m.Y >= y && m.Y < y + height;
    }

    int HoverMenu()
    {
        for (int i = 0; i < 4; i++)
        {
            float y = state == State.Paused ? 260 + i * 89 : i == 3 ? 650 : 380 + i * 76;
            if (MouseInside(320, y, 330, state == State.Paused ? 70 : 60)) return i;
        }
        return -1;
    }

    protected override void Update()
    {

        if (assetCheck)
        {
            Close();
            return;
        }

        float dt = Math.Min(.1f, (float)DeltaTime);
        audio.Update(dt);

        if (state == State.Title)
        {
            if (Enumerable.Range(1, 254).Any(k => Input.IsKeyDown((Keys)k)) || Input.IsButtonDown(MouseButtons.Left))
            {
                Reset();
                state = State.Ready;
                audio.Play("gamestart");
                audio.Music("early");
            }
            return;
        }

        if (state == State.Ready)
        {
            ready -= dt;
            if (ready <= 0)
            {
                state = State.Playing;
                Input.Reset();
            }
            return;
        }

        if (state == State.Results)
        {
            if (Input.IsKeyDown(Keys.Escape) || Input.IsKeyDown(Keys.Enter) ||
                (Input.IsButtonDown(MouseButtons.Left) && MouseInside(290, 450, 380, 65))) state = returnState;
            return;
        }

        if (Input.IsKeyDown(Keys.Escape))
        {
            if (state == State.Playing)
            {
                state = State.Paused;
                audio.Pause(true);
                menu = 0;
                foreach (var p in world.Players)
                {
                    p.Charging = false;
                    p.Charge = 0;
                }
            }
            else if (state == State.Paused)
            {
                state = State.Playing;
                audio.Pause(false);
                Input.Reset();
                Array.Clear(pendingPress);
                Array.Clear(pendingRelease);
            }
            audio.Play("menuclick");
            return;
        }

        if (state == State.Paused || state == State.GameOver)
        {
            int hovered = HoverMenu();
            if (hovered >= 0 && (Input.MouseDelta.X != 0 || Input.MouseDelta.Y != 0 || Input.IsButtonDown(MouseButtons.Left))) menu = hovered;
            int count = 4;
            if (Input.IsKeyDown(Keys.Down))
            {
                menu = (menu + 1) % count;
                audio.Play("menuclick");
            }
            if (Input.IsKeyDown(Keys.Up))
            {
                menu = (menu + count - 1) % count;
                audio.Play("menuclick");
            }
            if (Input.IsKeyDown(Keys.Enter) || (hovered >= 0 && Input.IsButtonDown(MouseButtons.Left)))
            {
                audio.Play("menubuttonclick");
                if (state == State.Paused)
                {
                    if (menu == 0)
                    {
                        state = State.Playing;
                        audio.Pause(false);
                        Input.Reset();
                    }
                    if (menu == 1)
                    {
                        Reset();
                        state = State.Ready;
                    }
                    if (menu == 2)
                    {
                        returnState = state;
                        state = State.Results;
                    }
                    if (menu == 3) Close();
                }
                else
                {
                    if (menu == 0)
                    {
                        Reset();
                        state = State.Ready;
                        audio.Music("early");
                    }
                    if (menu == 1)
                    {
                        returnState = state;
                        state = State.Results;
                    }
                    if (menu == 2)
                    {
                        state = State.Title;
                        audio.Music("title");
                    }
                    if (menu == 3) Close();
                }
            }
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            pendingPress[i] |= Input.IsKeyDown(keys[i][4]);
            pendingRelease[i] |= Input.IsKeyUp(keys[i][4]);
            directions[i] = ReadDirection(i);
            pendingParry[i] |= Input.IsKeyDown(parryKeys[i]);
            jumpBias[i] = Held(keys[i][0]) ? (Held(keys[i][3]) ? 1 : 0) - (Held(keys[i][2]) ? 1 : 0) : 0;
        }

        accumulator += dt;
        while (accumulator >= 1f / 120)
        {
            Control[] c = new Control[2];
            for (int i = 0; i < 2; i++) c[i] = new(directions[i], pendingPress[i], pendingRelease[i], Held(keys[i][4]), jumpBias[i], pendingParry[i]);
            world.Step(1f / 120, c);
            Array.Clear(pendingParry);
            Array.Clear(pendingPress);
            Array.Clear(pendingRelease);
            accumulator -= 1f / 120;
        }

        background.Update(world, dt);
        audio.Music(world.Stage < 2 ? "early" : world.Stage < 4 ? "middle" : "space");

        if (world.Players.All(f => !f.Alive))
        {
            state = State.GameOver;
            menu = 0;
            record = Math.Max(record, world.Score.Total(world.Players, settings));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
                File.WriteAllText(recordPath, record.ToString());
            }
            catch (Exception e)
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "runtime.log"), e.Message + "\n");
            }
            audio.Play("gameover");
            audio.Music("title");
        }

    }

    float Y(float y) => 540 - (y - world.Camera);

    void Text(string s, float x, float y, float w = 900) => font.DrawText(s, new Rect(x, y, w, 100), new Color4(1, 1, 1, 1));

    void Box(float x, float y, float w, float h, Color4 c)
    {
        using var b = RenderTarget.CreateSolidColorBrush(c);
        RenderTarget.FillRectangle(new Rect(x, y, w, h), b);
    }

    string FishImage(FishPlayer f)
    {

        string n = f.Id == 0 ? "래피" : "블피", side = f.FacingRight ? "우측" : "좌측";

        string jumpSide = f.Direction.X > 0 ? "우측" : f.Direction.X < 0 ? "좌측" : "상단";

        if (f.Cast > 0) return n + "수둔시전" + jumpSide + "점프시";

        if (f.UsesBasicSwimPose) return n;

        if (f.Charging) return n + jumpSide + "점프전동작";

        if (f.Platform < 0)
        {
            if (jumpSide == "상단") return n + "점프상단";
            string lr = jumpSide == "우측" ? "오른쪽" : "왼쪽";
            return f.Id == 0 ? n + "점프" + lr : n + lr + "점프";
        }

        int frame = (int)(world.Time * 7) % 3 + 1;
        return f.Id == 0 ? n + "헤엄" + side + frame : n + side + "헤엄" + frame;

    }

    protected override void Render()
    {

        background.Draw(resources);

        if (state == State.Title)
        {
            resources.Draw("게임시작화면", 0, 0, 960, 720);
            Box(70, 470, 820, 220, new Color4(0, .04f, .12f, .85f));
            Text("JUMPING FISH  ·  PRESS ANY BUTTON", 220, 485);
            Text("래피: WASD 헤엄 / Space 충전·떼면 점프", 140, 530);
            Text("블피: 방향키 헤엄 / ] 충전·떼면 점프", 140, 565);
            Text("충전 중 W+A/D 또는 ↑+←/→: 누른 시간만큼 편향", 120, 605);
            Text("점프 중 좌우 조절 · Space/Enter 패링 · ESC 메뉴", 240, 645);
            return;
        }

        foreach (var p in world.Platforms)
        {
            float sy = Y(p.Y);
            if (sy < -150 || sy > 850) continue;
            resources.Draw((p.Color == 0 ? "빨간물" : "파란물") + (world.Stage + 1), p.X, sy - p.Height / 2, p.Width, p.Height, stretch: true);
        }

        foreach (var it in world.Items) resources.Draw(it.Water ? "수둔" : "건사료", it.Position.X - 18, Y(it.Position.Y) - 18, 36, 36);

        foreach (var f in world.Fires)
        {
            resources.Draw(f.Color == 0 ? "오리빨간불" : "오리파란불", f.Position.X - 14, Y(f.Position.Y) - 14, 28, 28);
            if (f.Special) Text("+", f.Position.X - 8, Y(f.Position.Y) - 36, 30);
        }

        foreach (var w in world.Waters) resources.Draw(w.Owner == 0 ? "수둔시전됨래피" : "수둔시전됨블피", w.Position.X - 18, Y(w.Position.Y) - 35, 36, 70);

        foreach (var f in world.Players.Where(p => p.Alive))
        {
            float sy = Y(f.Position.Y);
            if (f.Splash > 0) resources.Draw((f.Id == 0 ? "빨간물" : "파란물") + (world.Stage + 1) + "첨벙이펙트", f.Position.X - 65, sy - 45, 130, 90, f.Splash / .35f);
            resources.Draw(FishImage(f), f.Position.X - 40, sy - 30, 80, 60);
            resources.Draw("점프게이지", f.Position.X - 40, sy - 55, 80, 20);
            Box(f.Position.X - 35, sy - 48, 70, 7, new Color4(0, 0, 0, .8f));
            Box(f.Position.X - 35, sy - 48, 70 * f.Gauge / 100, 7, f.Id == 0 ? new(1, .3f, .4f, 1) : new(.2f, .7f, 1, 1));
            if (f.Charging) Box(f.Position.X - 35, sy - 39, 70 * f.Charge / 100, 4, new(1, 1, .3f, 1));
            if (f.Effect > 0) resources.Draw((f.Id == 0 ? "rappy_parry_" : "blpy_red_parry_") + Math.Clamp(1 + (int)((.4f - f.Effect) / .08f), 1, 5).ToString("00"), f.Position.X - 60, sy - 60, 120, 120);
        }

        resources.Draw("오리입" + (1 + (int)(world.Time * 2) % 3), 0, Y(world.DuckY) - 5, 960, 330);

        Box(0, 0, 960, 65, new Color4(0, 0, .08f, .75f));
        Text($"점수 {world.Score.Total(world.Players, settings)}   최고 {record}   구간 {world.Stage + 1}", 15, 8);
        Text($"래피 {world.Players[0].Highest / 10:F1}m   블피 {world.Players[1].Highest / 10:F1}m   Seed {world.Seed}", 15, 35);

        if (state == State.Ready) Text("준비! Space / ]를 누르고 있다가 떼면 점프", 220, 300);

        if (state == State.Paused || state == State.GameOver || state == State.Results)
        {

            Box(0, 0, 960, 720, new Color4(0, 0, 0, .55f));

            if (state == State.Paused)
            {
                resources.Draw("esc메뉴ui", 0, -15, 960, 720);
                Text("▶", 290, 270 + menu * 89, 60);
            }

            else if (state == State.GameOver)
            {
                resources.Draw("게임오버ui", 50, -5, 860, 675);
                Box(327, 313, 115, 52, new Color4(.04f, .15f, .25f, 1));
                Box(530, 313, 115, 52, new Color4(.04f, .15f, .25f, 1));
                Text(world.Score.Total(world.Players, settings).ToString(), 335, 322, 110);
                Text(record.ToString(), 540, 322, 110);
                Text("▶", 285, 389 + Math.Min(menu, 2) * 76, 60);
                if (menu == 3) Text("▶ 종료", 420, 657, 180);
                else Text("종료", 440, 657, 180);
            }

            else
            {
                Box(140, 130, 680, 440, new Color4(.01f, .02f, .08f, .94f));
                Text("기록 상세", 380, 165);
                Text($"합계 {world.Score.Total(world.Players, settings)} · 최고 기록 {record}", 230, 225);
                Text($"최고 높이: 래피 {world.Players[0].Highest / 10:F1}m / 블피 {world.Players[1].Highest / 10:F1}m", 190, 285);
                Text($"공격 {world.Score.Damage * 5:F0}점 / 배경 보너스 {world.Score.Bonus}점", 230, 345);
                Text("돌아가기 (클릭 / Enter / ESC)", 310, 460);
            }

        }


    }

    public override void Dispose()
    {
        audio?.Dispose();
        resources?.Dispose();
        font?.Dispose();
        base.Dispose();
    }

}
