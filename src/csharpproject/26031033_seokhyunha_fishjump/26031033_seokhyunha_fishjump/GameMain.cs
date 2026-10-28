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
    float ready;
    double accumulator;
    int menu, record;
    string recordPath = "";

    Keys[] parryKeys = [];
    bool[] pendingParry = new bool[2];
    int[] jumpBias = new int[2];

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

        double sampleTime = world.Time + accumulator + dt;
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

        accumulator += dt;
        while (accumulator >= 1f / 120)
        {
            Control[] c = new Control[2];
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

    float WorldScale => ScreenSize.Width / settings.WorldWidth;
    float Y(float y) => (world.CameraTop - y) * WorldScale;

    void DrawWorld(string name, float x, float y, float width, float height, float opacity = 1, bool stretch = false)
        => resources.Draw(name, x * WorldScale, Y(y), width * WorldScale, height * WorldScale, opacity, stretch: stretch);

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
        if (state != State.Title)
        {
            foreach (var p in world.Platforms)
            {
                if (p.Y + p.Height / 2 < world.CameraBottom || p.Y - p.Height / 2 > world.CameraTop) continue;
                DrawWorld((p.Color == 0 ? "빨간물" : "파란물") + (world.Stage + 1),
                    p.X, p.Y + p.Height / 2, p.Width, p.Height, stretch: true);
            }
            foreach (var item in world.Items)
                DrawWorld(item.Water ? "수둔" : "건사료", item.Position.X - 18, item.Position.Y + 18, 36, 36);
            foreach (var fire in world.Fires)
            {
                DrawWorld(fire.Color == 0 ? "오리빨간불" : "오리파란불", fire.Position.X - 14, fire.Position.Y + 14, 28, 28);
                if (fire.Special) Text("+", fire.Position.X * WorldScale - 7, Y(fire.Position.Y) - 24, 30);
            }
            foreach (var water in world.Waters)
                DrawWorld(water.Owner == 0 ? "수둔시전됨래피" : "수둔시전됨블피",
                    water.Position.X - 18, water.Position.Y + 35, 36, 70);
            foreach (var fish in world.Players.Where(f => f.Alive))
            {
                float x = fish.Position.X * WorldScale, y = Y(fish.Position.Y);
                if (fish.Splash > 0) DrawWorld((fish.Id == 0 ? "빨간물" : "파란물") + (world.Stage + 1) + "첨벙이펙트",
                    fish.Position.X - 65, fish.Position.Y + 45, 130, 90, fish.Splash / .35f);
                DrawWorld(FishImage(fish), fish.Position.X - 40, fish.Position.Y + 30, 80, 60);
                Box(x - 25, y - 26, 50, 6, new Color4(0, 0, 0, .9f));
                Box(x - 25, y - 26, 50 * fish.Gauge / 100, 6,
                    fish.Id == 0 ? new(1, .3f, .4f, 1) : new(.2f, .7f, 1, 1));
                if (fish.Charging) Box(x - 25, y - 18, 50 * fish.Charge / 100, 3, new(1, 1, .3f, 1));
                if (fish.Effect > 0) DrawWorld((fish.Id == 0 ? "rappy_parry_" : "blpy_red_parry_") +
                    Math.Clamp(1 + (int)((.4f - fish.Effect) / .08f), 1, 5).ToString("00"),
                    fish.Position.X - 60, fish.Position.Y + 60, 120, 120);
            }
            DrawWorld("오리입" + world.DuckMouthFrame, 0, world.DuckY + 5, settings.WorldWidth, 500, stretch: true);
            float danger = Y(world.DuckY + 15);
            if (danger >= 0 && danger <= ScreenSize.Height)
                Box(0, danger, ScreenSize.Width, 2, new Color4(1, .25f, .1f, .6f));
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
            Text("공중에서 점프키 한 번 더 + 방향: 팔딱 (일반 점프 비용의 절반, 1회)", 160, 540, 1050);
            Text("방향 입력 0.30초 · 패링 0.20초 우선 · Space/Enter 패링 · ESC 메뉴", 160, 580, 1050);
            Text("충전 중 위+좌우로 편향 · 비행 중 좌우 조절 · 넓은 물길을 함께 탐색하세요", 160, 620, 1050);
        }
        else
        {
            // 상단 물의 색을 가리지 않도록 패널 없이 점수 숫자와 얇은 글자 그림자만 그린다.
            string score = world.Score.Total(world.Players, settings).ToString();
            font.DrawText(score, new Rect(21, 9, 300, 50), new Color4(0, 0, 0, .7f));
            Text(score, 20, 8, 300);
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
        base.Dispose();
    }

}
