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
    G2Font hintFont = null!;
    Vortice.Direct2D1.ID2D1SolidColorBrush? solidBrush;
    AudioManager audio = null!;

    bool assetCheck = Environment.GetCommandLineArgs().Contains("--asset-check");

    enum State
    {
        Title, Ready, Playing, Paused, Dying, GameOver, Results
    }

    State state = State.Title, returnState;
    float ready, deathHold;
    BiteFeedback biteFeedback = new();
    double accumulator;
    int menu, record;
    string recordPath = "";

    Keys[] parryKeys = [];
    bool[] pendingParry = new bool[2];
    int[] jumpBias = new int[2];

    readonly Control[] stepControls = new Control[2];
    Keys[][] keys = [];
    bool[] pendingPress = new bool[2], pendingRelease = new bool[2];
    Vector2[] directions = new Vector2[2];
    Vector2[] appliedDirections = new Vector2[2];
    double[] inputTimes = new double[2], directionTimes = new double[2];
    const float UiWidth = 1280;
    float UiScale => ScreenSize.Width / UiWidth;
    float UiHeight => ScreenSize.Height / UiScale;

    protected override void Initialize()
    {
        settings = GameSettings.Load();
        resources = new();
        resources.PreloadGameplay();
        if (assetCheck)
        {
            var lines = new List<string>();
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "assets", "images"), "*.png"))
            {
                resources.Preload(Path.GetFileNameWithoutExtension(path));
                lines.Add("PASS " + Path.GetFileName(path));
            }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "asset-check-results.txt"), lines);
        }
        font = new("Malgun Gothic", 21);
        hintFont = new("Malgun Gothic", 16);
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
        biteFeedback = new();
        world.Sound = sound =>
        {
            audio.Play(sound);
            if (sound == "bite") biteFeedback.Trigger();
        };
        ready = 1.5f;
        deathHold = 0;
        accumulator = 0;
        background = new();
        Array.Clear(pendingPress);
        Array.Clear(pendingRelease);
        menu = 0;
        ClearInput();
    }

    void ClearInput()
    {
        Input.Reset();
        Array.Clear(pendingPress);
        Array.Clear(pendingRelease);
        Array.Clear(pendingParry);
        Array.Clear(directions);
        Array.Clear(appliedDirections);
        Array.Clear(inputTimes);
        Array.Clear(directionTimes);
        foreach (var fish in world.Players) fish.JumpHeld = false;
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
        float mx = m.X / UiScale, my = m.Y / UiScale;
        return mx >= 0 && my >= 0 && mx < UiWidth && my < UiHeight &&
            mx >= x && mx < x + width && my >= y && my < y + height;
    }

    int HoverMenu()
    {
        for (int i = 0; i < 4; i++)
        {
            float y = 290 + i * 75;
            if (MouseInside(UiWidth / 2 - 180, y, 360, 60)) return i;
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
        float playDt = state is State.Playing or State.Dying ? biteFeedback.Advance(dt) : dt;

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
                ClearInput();
            }
            return;
        }

        if (state == State.Dying)
        {
            world.AdvanceFeedback(playDt);
            deathHold -= playDt;
            if (deathHold <= 0) FinishGame();
            return;
        }

        if (state == State.Results)
        {
            if (Input.IsKeyDown(Keys.Escape) || Input.IsKeyDown(Keys.Enter) ||
                (Input.IsButtonDown(MouseButtons.Left) && MouseInside(UiWidth / 2 - 190, 450, 380, 65))) state = returnState;
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
                ClearInput();
            }
            else if (state == State.Paused)
            {
                state = State.Playing;
                audio.Pause(false);
                ClearInput();
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
                        ClearInput();
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

        double sampleTime = world.Time + accumulator + playDt;
        for (int i = 0; i < 2; i++)
        {
            if (Input.IsKeyDown(keys[i][4]) || Input.IsKeyUp(keys[i][4]) || Input.IsKeyDown(parryKeys[i])) inputTimes[i] = sampleTime;
            pendingPress[i] |= Input.IsKeyDown(keys[i][4]);
            pendingRelease[i] |= Input.IsKeyUp(keys[i][4]);
            var direction = ReadDirection(i);
            if (direction != directions[i]) directionTimes[i] = sampleTime;
            directions[i] = direction;
            pendingParry[i] |= Input.IsKeyDown(parryKeys[i]);
            jumpBias[i] = Held(keys[i][0]) ? (Held(keys[i][3]) ? 1 : 0) - (Held(keys[i][2]) ? 1 : 0) : 0;
        }

        accumulator += playDt;
        while (accumulator >= 1f / 120)
        {
            Control[] c = stepControls;
            for (int i = 0; i < 2; i++)
            {
                bool readyInput = inputTimes[i] <= world.Time + 1f / 120 + 1e-7;
                bool readyDirection = directionTimes[i] <= world.Time + 1f / 120 + 1e-7;
                if (readyDirection) appliedDirections[i] = directions[i];
                c[i] = new(appliedDirections[i], readyInput && pendingPress[i], readyInput && pendingRelease[i],
                    Held(keys[i][4]), jumpBias[i], readyInput && pendingParry[i], inputTimes[i], directionTimes[i]);
                if (readyInput) { pendingPress[i] = false; pendingRelease[i] = false; pendingParry[i] = false; }
            }
            world.Step(1f / 120, c);
            accumulator -= 1f / 120;
            if (biteFeedback.PauseRemaining > 0)
            {
                accumulator = 0; // 멈칫한 시간을 나중에 몰아서 진행하지 않는다.
                break;
            }
        }

        background.Update(world, playDt);
        audio.Music(world.Stage < 2 ? "early" : world.Stage < 4 ? "middle" : "space");

        if (world.Players.All(f => !f.Alive))
        {
            state = State.Dying;
            deathHold = 1.0f;
            accumulator = 0;
            ClearInput();
        }
    }

    void FinishGame()
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

    float WorldScale => ScreenSize.Width / settings.WorldWidth;
    float Y(float y) => (world.CameraTop - y) * WorldScale;

    void DrawWorld(string name, float x, float y, float width, float height, float opacity = 1, bool stretch = false)
        => resources.Draw(name, x * WorldScale, Y(y), width * WorldScale, height * WorldScale, opacity, stretch: stretch);

    void Text(string s, float x, float y, float w = 900) => font.DrawText(s, new Rect(x, y, w, 100), new Color4(1, 1, 1, 1));

    void Box(float x, float y, float w, float h, Color4 c)
    {
        solidBrush ??= RenderTarget.CreateSolidColorBrush(c);
        solidBrush.Color = c;
        RenderTarget.FillRectangle(new Rect(x, y, w, h), solidBrush);
    }

    void FeedbackLabel(string text, float x, float y, float width, Color4 color)
    {
        hintFont.DrawText(text, new Rect(x + 1, y + 1, width, 35), new(0, 0, 0, 1));
        hintFont.DrawText(text, new Rect(x, y, width, 35), color);
    }

    void Burst(Vector2 center, float radius, float progress, Color4 color)
    {
        solidBrush ??= RenderTarget.CreateSolidColorBrush(color);
        solidBrush.Color = color;
        for (int i = 0; i < 32; i++)
        {
            float a = i * MathF.Tau / 32, b = (i + 1) * MathF.Tau / 32;
            RenderTarget.DrawLine(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, solidBrush, 3);
        }
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathF.Tau / 12 + progress;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            RenderTarget.DrawLine(center + direction * (radius + 5), center + direction * (radius + 15), solidBrush, 4);
        }
    }

    void DrawFireHitFeedback(FishPlayer fish)
    {
        float progress = 1 - fish.FireHitEffect / FishPlayer.FireHitEffectSeconds;
        float alpha = Math.Min(1, fish.FireHitEffect / .18f);
        float x = fish.FireHitPosition.X * WorldScale, y = Y(fish.FireHitPosition.Y);
        Color4 color = fish.FireHitColor == 0 ? new(1, .4f, .15f, alpha) : new(.35f, .8f, 1, alpha);
        Burst(new(x, y), 15 + progress * 48, progress, color);
        if (progress < .3f)
        {
            float flash = 1 - progress / .3f;
            Box(x - 26, y - 2, 52, 4, new(1, 1, 1, flash));
            Box(x - 2, y - 26, 4, 52, new(1, 1, 1, flash));
        }
        float fishX = fish.Position.X * WorldScale, fishY = Y(fish.Position.Y);
        string label = fish.FireStunRemaining > 0 ? $"불 피격! 기절 {fish.FireStunRemaining:0.0}초" : "불 피격!";
        FeedbackLabel(label, Math.Clamp(fishX - 100, 8, ScreenSize.Width - 230),
            Math.Clamp(fishY - 80, 35, ScreenSize.Height - 65), 230, color);
        if (fish.FireStunRemaining > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                float phase = (float)world.Time * 12 + i * MathF.Tau / 3;
                FeedbackLabel("★", fishX + MathF.Cos(phase) * 25 - 6, fishY - 48 + MathF.Sin(phase) * 7, 24, new(1, 1, .4f, 1));
            }
        }
    }

    void DrawParryFeedback(FishPlayer fish)
    {
        float progress = 1 - fish.Effect / FishPlayer.ParryEffectSeconds;
        float alpha = Math.Min(1, fish.Effect / .2f);
        float x = fish.ParryPosition.X * WorldScale, y = Y(fish.ParryPosition.Y);
        DrawWorld((fish.Id == 0 ? "rappy_parry_" : "blpy_red_parry_") + fish.ParryEffectFrame.ToString("00"),
            fish.ParryPosition.X - 120, fish.ParryPosition.Y + 120, 240, 240, alpha);
        Burst(new(x, y), 18 + progress * 62, progress, new(1, .95f, .4f, alpha));
        if (progress < .25f)
        {
            float flash = 1 - progress / .25f;
            Box(x - 36, y - 2, 72, 4, new(1, 1, 1, flash));
            Box(x - 2, y - 36, 4, 72, new(1, 1, 1, flash));
        }
        FeedbackLabel("패링 성공! +100점", Math.Clamp(x - 90, 8, ScreenSize.Width - 220),
            Math.Clamp(y - 82 - progress * 25, 35, ScreenSize.Height - 75), 220, new(1, 1, .5f, alpha));
    }

    void DrawImpactFeedback()
    {
        foreach (var fish in world.Players)
        {
            if (fish.DeathEffect <= 0) continue;
            float progress = 1 - fish.DeathEffect / GameWorld.DeathEffectSeconds;
            float alpha = Math.Min(1, fish.DeathEffect * 2);
            float x = Math.Clamp(fish.DeathPosition.X * WorldScale, 55, ScreenSize.Width - 55);
            float y = ScreenSize.Height - 44 - progress * 65;
            Color4 color = fish.Id == 0 ? new(1, .3f, .25f, alpha) : new(.3f, .8f, 1, alpha);
            Box(0, ScreenSize.Height - 75, ScreenSize.Width, 75, new(1, .12f, .05f, .16f * (1 - progress)));
            Burst(new(x, y), 18 + progress * 45, progress, color);
            resources.Draw(fish.Id == 0 ? "래피" : "블피", x - 24, y - 24, 48, 48, alpha);
            FeedbackLabel("×", x - 8, y - 14, 32, new(1, 1, 1, alpha));
            float labelX = Math.Clamp(fish.Id == 0 ? x - 245 : x + 12, 8, ScreenSize.Width - 250);
            FeedbackLabel($"P{fish.Id + 1} 오리 입 충돌 · 탈락", labelX, ScreenSize.Height - 145 - fish.Id * 30, 250, color);
        }
        if (world.DuckHitEffect > 0)
        {
            float progress = 1 - world.DuckHitEffect / GameWorld.DuckHitEffectSeconds;
            float alpha = Math.Min(1, world.DuckHitEffect * 2);
            float x = Math.Clamp(world.DuckHitX * WorldScale, 60, ScreenSize.Width - 60);
            float y = ScreenSize.Height - 55;
            resources.Draw("파란물1첨벙이펙트", x - 90, y - 100, 180, 125, alpha);
            Burst(new(x, y), 20 + progress * 65, progress, new(.4f, .9f, 1, alpha));
            FeedbackLabel($"수둔 명중! +{world.DuckHitPoints}점", Math.Clamp(x - 100, 8, ScreenSize.Width - 240), y - 110 - progress * 25, 240, new(.6f, 1, 1, alpha));
        }
        if (world.DuckStunRemaining > 0)
        {
            float center = ScreenSize.Width / 2;
            for (int i = 0; i < 3; i++)
            {
                float phase = (float)world.Time * 5 + i * MathF.Tau / 3;
                FeedbackLabel("★", center + MathF.Cos(phase) * 65 - 8, ScreenSize.Height - 180 + MathF.Sin(phase) * 12, 32, new(1, 1, .3f, 1));
            }
            FeedbackLabel($"오리 기절! {world.DuckStunRemaining:0.0}초", center - 90, ScreenSize.Height - 140, 240, new(1, 1, .5f, 1));
            Box(center - 65, ScreenSize.Height - 112, 130, 5, new(0, .1f, .2f, .8f));
            Box(center - 65, ScreenSize.Height - 112, 130 * Math.Clamp(world.DuckStunRemaining / settings.DuckStunSeconds, 0, 1), 5, new(.3f, .9f, 1, 1));
        }
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

    void DrawJumpGauge(FishPlayer fish, float x, float y)
    {
        const float width = 72, height = 8;
        float left = Math.Clamp(x - width / 2, 4, ScreenSize.Width - width - 4), top = y - 30;
        Box(left - 1, top - 1, width + 2, height + 2, new(0, 0, 0, .9f));
        Box(left, top, width * fish.Gauge / 100, height,
            fish.Id == 0 ? new(1, .3f, .4f, 1) : new(.2f, .7f, 1, 1));
        if (fish.Charging) Box(left, top + 12, width * fish.Charge / 100, 3, new(1, 1, .3f, 1));
        if (!world.ShowFlapCost(fish)) return;

        float threshold = world.FlapGaugeThreshold(fish);
        bool enough = fish.Gauge >= threshold;
        Color4 color = enough ? new(1, 1, .35f, 1) : new(1, .4f, .25f, 1);
        float marker = left + width * Math.Clamp(threshold / 100, 0, 1);
        float reserve = left + width * (fish.Charging ? fish.Charge : 0) / 100;
        Box(reserve, top + height - 2, Math.Max(0, marker - reserve), 2, color);
        Box(marker - 2, top - 3, 4, height + 6, new(0, 0, 0, 1));
        Box(marker - 1, top - 3, 2, height + 6, color);
        string label = $"팔딱 {world.FlapCost(fish):0.#}" + (enough ? "" : " 부족");
        float labelX = Math.Clamp(left, 4, ScreenSize.Width - 130);
        hintFont.DrawText(label, new Rect(labelX + 1, top - 22, 130, 22), new(0, 0, 0, .9f));
        hintFont.DrawText(label, new Rect(labelX, top - 23, 130, 22), color);
    }

    void DrawFishHint(FishPlayer fish, float x, float y)
    {
        string text = $"P{fish.Id + 1} · {world.FlapHint(fish)}";
        // 함께 출발할 때 두 안내가 겹치지 않도록 좌우로 나누고 높이도 조금 다르게 둔다.
        float left = Math.Clamp(fish.Id == 0 ? x - 322 : x + 12, 8, ScreenSize.Width - 330);
        float top = Math.Clamp(y + 22 + fish.Id * 24, 34, ScreenSize.Height - 48);
        hintFont.DrawText(text, new Rect(left + 1, top + 1, 322, 44), new Color4(0, 0, 0, .9f));
        hintFont.DrawText(text, new Rect(left, top, 322, 44), new Color4(1, 1, 1, 1));
        if (fish.FlapLockRemaining <= 0) return;
        Vector2 direction = new(fish.FlapDirection.X, -fish.FlapDirection.Y);
        Vector2 start = new(x, y), tip = start + direction * 42;
        Vector2 side = new(-direction.Y, direction.X);
        using var brush = RenderTarget.CreateSolidColorBrush(new Color4(1, 1, .3f, 1));
        RenderTarget.DrawLine(start + direction * 20, tip, brush, 2);
        RenderTarget.DrawLine(tip, tip - direction * 8 + side * 4, brush, 2);
        RenderTarget.DrawLine(tip, tip - direction * 8 - side * 4, brush, 2);
    }

    protected override void Render()
    {
        background.Draw(resources);
        if (state != State.Title)
        {
            var sceneTransform = RenderTarget.Transform;
            RenderTarget.Transform = Matrix3x2.CreateTranslation(biteFeedback.Offset) * sceneTransform;
            // 물과 캐릭터 뒤에 그려 하단의 동료나 발판을 가리지 않는다.
            float duckHeight = settings.DuckDrawHeight, duckWidth = duckHeight * 3461f / 3910f;
            float shake = world.DuckHitEffect > 0 ? MathF.Sin((float)world.Time * 65) * 10 * world.DuckHitEffect : 0;
            DrawWorld("오리" + world.DuckMouthFrame, settings.WorldWidth / 2 - duckWidth * .62f + shake,
                world.DeathY + settings.DuckVisibleHeight, duckWidth, duckHeight);
            foreach (var p in world.Platforms)
            {
                if (p.Y + p.Height / 2 < world.CameraBottom || p.Y - p.Height / 2 > world.CameraTop) continue;
                DrawWorld((p.Color == 0 ? "빨간물" : "파란물") + (world.Stage + 1),
                    p.X, p.Y + p.Height / 2, p.Width, p.Height, stretch: true);
            }
            foreach (var item in world.Items)
            {
                float size = settings.ItemDrawSize;
                DrawWorld(item.Water ? "수둔" : "건사료", item.Position.X - size / 2, item.Position.Y + size / 2, size, size);
            }
            foreach (var fire in world.Fires)
            {
                float size = world.Settings.FireDrawSize, radius = size / 2;
                DrawWorld(fire.Color == 0 ? "오리빨간불" : "오리파란불", fire.Position.X - radius, fire.Position.Y + radius, size, size);
                if (fire.Special) Text("+", fire.Position.X * WorldScale - 7, Y(fire.Position.Y) - radius * WorldScale - 24, 30);
            }
            foreach (var water in world.Waters)
                DrawWorld(water.Owner == 0 ? "수둔시전됨래피" : "수둔시전됨블피",
                    water.Position.X - 36, water.Position.Y + 70, 72, 140);
            foreach (var fish in world.Players.Where(f => f.Alive))
            {
                float x = fish.Position.X * WorldScale, y = Y(fish.Position.Y);
                if (y < -40 || y > ScreenSize.Height + 30) continue;
                if (fish.Splash > 0) DrawWorld((fish.Id == 0 ? "빨간물" : "파란물") + (world.Stage + 1) + "첨벙이펙트",
                    fish.Position.X - 65, fish.Position.Y + 45, 130, 90, fish.Splash / .35f);
                float hitOpacity = fish.FireHitEffect > 0 && (int)(fish.FireHitEffect * 12) % 2 == 0 ? .35f : 1;
                DrawWorld(FishImage(fish), fish.Position.X - 40, fish.Position.Y + 30, 80, 60, hitOpacity);
                DrawJumpGauge(fish, x, y);
                DrawFishHint(fish, x, y);
            }
            foreach (var fish in world.Players)
            {
                if (fish.Effect > 0) DrawParryFeedback(fish);
                if (fish.FireHitEffect > 0) DrawFireHitFeedback(fish);
            }
            DrawImpactFeedback();
            float danger = Math.Clamp(Y(world.DeathY), 0, ScreenSize.Height - 2);
            if (danger >= 0 && danger <= ScreenSize.Height)
                Box(0, danger, ScreenSize.Width, 2, new Color4(1, .25f, .1f, .6f));
            RenderTarget.Transform = sceneTransform;
        }

        // UI는 세계 배율을 사용하지 않는다. 창 레터박스 변환은 G2AppBase가 담당한다.
        var originalTransform = RenderTarget.Transform;
        RenderTarget.Transform = Matrix3x2.CreateScale(UiScale) * originalTransform;
        if (state == State.Title)
        {
            resources.Draw("게임시작화면", 0, 0, UiWidth, UiHeight, cover: true);
            Box(100, 440, UiWidth - 200, 240, new Color4(0, .04f, .12f, .9f));
            Text("JUMPING FISH · PRESS ANY BUTTON", 380, 455);
            Text("래피: WASD / Space 충전·떼면 점프    블피: 방향키 / ] 충전·떼면 점프", 160, 500, 1050);
            Text($"공중 점프키 + 방향: 팔딱 (점프 비용의 {settings.FlapCostRatio:P0}, 1회) · 게이지 노란 선: 필요량", 160, 540, 1050);
            Text("방향+점프키: 즉시 팔딱 · 방향 없으면 0.30초 유예 · Space/Enter 패링 · ESC 메뉴", 160, 580, 1050);
            Text("계속 올라오는 오리를 피해 반대 색 물부터 입수 · 동료가 색을 바꾼 뒤 따라오세요", 160, 620, 1050);
        }
        else
        {
            // 상단 물의 색을 가리지 않도록 패널 없이 점수 숫자와 얇은 글자 그림자만 그린다.
            string score = world.Score.Total(world.Players, settings).ToString();
            font.DrawText(score, new Rect(21, 9, 300, 50), new Color4(0, 0, 0, .7f));
            Text(score, 20, 8, 300);
            foreach (var fish in world.Players)
                if (!fish.Alive) FeedbackLabel($"P{fish.Id + 1} {(fish.Id == 0 ? "래피" : "블피")} 탈락", UiWidth - 190, 12 + fish.Id * 32, 180, new(1, .6f, .5f, 1));
            if (state == State.Dying)
                FeedbackLabel("오리 입에 닿았습니다 · 두 플레이어 탈락", UiWidth / 2 - 240, UiHeight * .62f, 520, new(1, .85f, .65f, 1));
            if (state == State.Ready) Text("준비! Space / ]를 누르고 있다가 떼면 점프", UiWidth / 2 - 240, 300, 600);
            if (state == State.Paused || state == State.GameOver || state == State.Results)
            {
                Box(0, 0, UiWidth, UiHeight, new Color4(0, 0, 0, .65f));
                Box(UiWidth / 2 - 310, 150, 620, 480, new Color4(.01f, .04f, .12f, .96f));
                if (state == State.Results)
                {
                    Text("기록 상세", UiWidth / 2 - 60, 185, 400);
                    Text($"합계 {world.Score.Total(world.Players, settings)} · 최고 기록 {record}", UiWidth / 2 - 240, 250, 550);
                    Text($"래피 {world.Players[0].Highest / settings.PixelsPerMeter:0.0}m / 블피 {world.Players[1].Highest / settings.PixelsPerMeter:0.0}m", UiWidth / 2 - 240, 310, 550);
                    Text($"공격 {world.Score.Damage * 5:0}점 / 구간 보너스 {world.Score.Bonus}점", UiWidth / 2 - 240, 370, 550);
                    Text("돌아가기 (클릭 / Enter / ESC)", UiWidth / 2 - 180, 460, 450);
                }
                else
                {
                    Text(state == State.Paused ? "일시정지" : "GAME OVER", UiWidth / 2 - 80, 185, 400);
                    Text($"점수 {world.Score.Total(world.Players, settings)} · 최고 {record}", UiWidth / 2 - 180, 235, 500);
                    if (state == State.GameOver)
                        FeedbackLabel("오리 입 충돌 · 두 플레이어 탈락", UiWidth / 2 - 180, 266, 450, new(1, .65f, .45f, 1));
                    string[] options = state == State.Paused ? ["계속하기", "재시작", "기록 상세", "종료"] : ["재시작", "기록 상세", "타이틀", "종료"];
                    for (int i = 0; i < options.Length; i++)
                    {
                        if (i == menu) Box(UiWidth / 2 - 180, 290 + i * 75, 360, 60, new Color4(.08f, .28f, .4f, 1));
                        Text((i == menu ? "▶ " : "   ") + options[i], UiWidth / 2 - 140, 305 + i * 75, 340);
                    }
                }
            }
        }
        RenderTarget.Transform = originalTransform;
    }

    public override void Dispose()
    {
        audio?.Dispose();
        resources?.Dispose();
        font?.Dispose();
        hintFont?.Dispose();
        solidBrush?.Dispose();
        base.Dispose();
    }

}
