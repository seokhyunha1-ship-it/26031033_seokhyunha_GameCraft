using System.Numerics;

using System.Text.Json;


record struct Control(Vector2 Direction, bool Press, bool Release, bool Held, int JumpBias = 0,
    bool ParryPress = false, double InputTime = -1, double DirectionTime = -1);

enum FlightOrigin { None, PaidJump, Parry }
enum DuckState { Chasing, Windup, Holding, Recovering }

sealed class FishPlayer(int id, float x)
{

    public int Id = id;
    public Vector2 Position = new(x, 0), Velocity, Direction = Vector2.UnitY;

    public float Gauge = 100, Highest, Charge, Parry, SwimTime, SwimDelay, Cast, Effect;

    public bool Alive = true, Charging, Jumping, FacingRight = true;

    public int Platform = 0, LaunchPlatform = -1;
    public bool FallingOnly;
    public float Splash, JumpBiasAmount;
    public Vector2 SwimDirection;
    public float LastPaidJumpCost;
    public bool FlapUsed, FlapPending, JumpHeld;
    public double FlapStarted, FlapDeadline, FlapDirectionTime;
    public Vector2 FlapDirection;
    public Vector2 FlapBoostVelocity;
    public float FlapBoostRemaining;
    public FlightOrigin FlightOrigin;
    public string FlapMessage = "";
    public float FlapMessageTime;

    public void EndFlight()
    {
        FlightOrigin = FlightOrigin.None;
        LastPaidJumpCost = 0;
        FlapUsed = false;
        FlapPending = false;
        FlapDirection = Vector2.Zero;
        FlapBoostVelocity = Vector2.Zero;
        FlapBoostRemaining = 0;
    }
    public bool UsesBasicSwimPose => Platform >= 0 && SwimDirection.Y != 0;

}

sealed class WaterPlatform(int id, float x, float y, float w, int color)
{

    public int Id = id, Color = color;
    // 생성 비율은 입수로 바뀌는 현재 색과 분리해 기록한다.
    public int SpawnColor { get; private set; } = color;
    public void SetSpawnColor(int value) { SpawnColor = value; Color = value; }
    public bool IsRoute = true;
    public float X = x, Y = y, Width = w, Height = 95;

    public float EntryTime(Vector2 from, Vector2 to)
    {
        float enter = 0, leave = 1;
        float[] a = [from.X, from.Y], d = [to.X - from.X, to.Y - from.Y], lo = [X, Y - Height / 2], hi = [X + Width, Y + Height / 2];
        for (int k = 0; k < 2; k++)
        {
            if (Math.Abs(d[k]) < .00001f)
            {
                if (a[k] < lo[k] || a[k] > hi[k]) return 1;
                continue;
            }
            float t0 = (lo[k] - a[k]) / d[k], t1 = (hi[k] - a[k]) / d[k];
            enter = Math.Max(enter, Math.Min(t0, t1));
            leave = Math.Min(leave, Math.Max(t0, t1));
        }
        return enter <= leave ? Math.Clamp(enter, 0, 1) : 1;
    }

    // Only residents receive exit tolerance; airborne arrivals still use the visible water.
    public bool ContainsResident(Vector2 p, GameSettings settings) =>
        p.X >= X - settings.WaterExitSideMargin && p.X <= X + Width + settings.WaterExitSideMargin &&
        p.Y >= Y - Height / 2 - settings.WaterExitBottomMargin && p.Y <= Y + Height / 2 + settings.WaterExitTopMargin;

    public bool Contains(Vector2 p) => p.X >= X && p.X <= X + Width && p.Y >= Y - Height / 2 && p.Y <= Y + Height / 2;

}

sealed class FireProjectile
{
    public Vector2 Position, Velocity;
    public int Color;
    public bool Special, Done;
}

sealed class WaterAttack
{
    public Vector2 Position;
    public int Owner;
    public float Damage;
    public bool Done;
}

sealed class GameItem
{
    public Vector2 Position;
    public bool Water, Done;
}

sealed class ScoreSystem
{

    public float Damage;
    public HashSet<int> Awarded = [];
    public int Bonus;

    public int Total(FishPlayer[] p, GameSettings s) => (int)(p.Sum(f => f.Highest) / s.PixelsPerMeter * 10 + Damage * 5 + Bonus);

    public void Award(int stage, GameSettings s)
    {
        for (int i = 1; i <= stage; i++) if (Awarded.Add(i)) Bonus += s.StageBonuses[i];
    }

}

sealed partial class GameWorld
{

    public readonly GameSettings Settings;
    public readonly Random Random;
    public int Seed;

    public FishPlayer[] Players;

    public List<WaterPlatform> Platforms = [];
    public List<FireProjectile> Fires = [];
    public List<WaterAttack> Waters = [];
    public List<GameItem> Items = [];

    public ScoreSystem Score = new();
    public double Time;
    public float DuckY = -220, Camera, AttackTimer;
    public float CameraTop => Camera + Settings.ViewHeight * .75f;
    public float CameraBottom => Camera - Settings.ViewHeight * .25f;
    public DuckState DuckPhase { get; private set; }
    public int DuckMouthFrame { get; private set; } = 3;
    public int ShotsFired { get; private set; }
    public int Stage;
    int nextId;
    int lastSpawnColor; // 시작 물은 빨강(0). 화면 밖으로 사라져도 생성 순서는 이어진다.
    readonly Random attackRandom;

    public Action<string>? Sound;

    public GameWorld(GameSettings s)
    {
        Settings = s;
        s.Validate();
        Players = [new(0, s.WorldWidth / 2 - 125), new(1, s.WorldWidth / 2 + 125)];
        Seed = s.Seed ?? System.Random.Shared.Next();
        Random = new(Seed);
        attackRandom = new(Seed ^ 1729);
        Platforms.Add(new(nextId++, 0, 0, s.WorldWidth, 0) { Height = 180 });
        Generate();
    }

    void Generate()
    {
        while (Platforms.Last(p => p.IsRoute).Y < CameraTop + 1000)
        {
            int firstNewPlatform = Platforms.Count;
            var last = Platforms.Last(p => p.IsRoute);
            int size = Random.Next(3);
            float width = size switch
            {
                0 => Random.Next(130, 211),
                1 => Random.Next(250, 361),
                _ => Random.Next(430, 571)
            };
            float height = Random.Next(3) switch
            {
                0 => Random.Next(80, 111),
                1 => Random.Next(140, 181),
                _ => Random.Next(210, 261)
            };
            // Keep at least 60px of shared horizontal space for a vertical jump route.
            float minX = Math.Max(Settings.WorldMargin, last.X + 60 - width);
            float maxX = Math.Min(Settings.WorldWidth - Settings.WorldMargin - width, last.X + last.Width - 60);
            if (last.Id == 0) { minX = Settings.WorldWidth / 2 - width; maxX = Settings.WorldWidth / 2; }
            // 계단/지그재그 반복 패턴 대신 매번 독립적인 위치를 뽑는다.
            float fraction = (float)Random.NextDouble();
            float x = minX + (maxX - minX) * fraction;
            // Leave a visible gap above the tallest water in the previous band.
            float y = last.Y + (last.Height + height) / 2 +
                Random.Next(Settings.PlatformVerticalGapMin, Settings.PlatformVerticalGapMax + 1);
            int color = Random.Next(2);
            var platform = new WaterPlatform(nextId++, x, y, width, color) { Height = height };
            Platforms.Add(platform);
            if (Random.NextDouble() < Settings.ItemChance)
                Items.Add(new() { Position = new(x + width / 2, y), Water = Random.NextDouble() < Settings.WaterItemChance });
            // Fill the unused sides of each band while keeping the main jump route intact.
            float gap = Settings.PlatformHorizontalGap;
            int sideCount = SideCount(x - gap - Settings.WorldMargin) +
                SideCount(Settings.WorldWidth - Settings.WorldMargin - x - width - gap);
            AddSidePlatforms(Settings.WorldMargin, x - gap, y, sideCount);
            AddSidePlatforms(x + width + gap, Settings.WorldWidth - Settings.WorldMargin, y, sideCount);
            AssignSpawnColors(firstNewPlatform);
        }
    }

    void AssignSpawnColors(int firstNewPlatform)
    {
        // 화면 안에 중심점이 있는 물은 높이순 목록의 연속 구간이다.
        // 높이순으로 두 색을 번갈아 배정하면 어떤 화면에서도 개수 차이가 1 이하이다.
        // 2개 이상이면 30~70% 범위 안이며, 이미 등장한 물의 색은 다시 바꾸지 않는다.
        foreach (var water in Platforms.Skip(firstNewPlatform).OrderBy(p => p.Y).ThenBy(p => p.Id))
        {
            lastSpawnColor = 1 - lastSpawnColor;
            water.SetSpawnColor(lastSpawnColor);
        }
    }

    int SideCount(float space)
    {
        if (space < 130) return 0;
        float gap = Settings.PlatformHorizontalGap;
        int preferred = (int)Math.Ceiling((space + gap) / (300 + gap));
        // 간격을 넓혀도 물의 최소 폭130을 확보한다. 좁은 공간을 억지로 둘로 나누지 않는다.
        int maximum = (int)Math.Floor((space + gap) / (130 + gap));
        return Math.Min(Settings.SidePlatformsPerSide, Math.Max(1, Math.Min(preferred, maximum)));
    }

    void AddSidePlatforms(float left, float right, float y, int totalSideCount)
    {
        float available = right - left;
        if (available < 130) return;
        int count = SideCount(available);
        float routeHeight = Platforms.Last(p => p.IsRoute).Height;
        for (int i = 0; i < count; i++)
        {
            // 고정 슬롯을 채우지 않는다. x/y/크기를 독립적으로 뽑고 충돌과 연결을 검사한다.
            for (int attempt = 0; attempt < 100; attempt++)
            {
                float width = Random.Next(130, (int)Math.Min(300, available) + 1);
                float x = left + (float)Random.NextDouble() * (available - width);
                float sideY = y + ((float)Random.NextDouble() * 2 - 1) * Settings.PlatformScatterY;
                var candidate = new WaterPlatform(nextId, x, sideY, width, Random.Next(2))
                {
                    Height = Random.Next((int)Math.Max(80, Math.Min(220, routeHeight - 40)),
                        (int)Math.Min(220, routeHeight) + 1), IsRoute = false
                };
                if (Platforms.Any(p => !Separated(p, candidate))) continue;
                if (!Platforms.Any(p => CanReachSide(p, candidate))) continue;
                Platforms.Add(candidate);
                nextId++;
                if (Random.NextDouble() < Settings.ItemChance * .5f / Math.Max(1, totalSideCount))
                    Items.Add(new() { Position = new(x + width / 2, sideY), Water = Random.NextDouble() < Settings.WaterItemChance });
                break;
            }
        }
    }

    bool Separated(WaterPlatform a, WaterPlatform b) =>
        a.X + a.Width + Settings.PlatformHorizontalGap <= b.X ||
        b.X + b.Width + Settings.PlatformHorizontalGap <= a.X ||
        Math.Abs(a.Y - b.Y) >= (a.Height + b.Height) / 2 + 12;

    // 색 조건은 별개다. 50포인트 점프의 상승 여유와 수평 조절 범위 안에 있는 물만 추가한다.
    public bool CanReachSide(WaterPlatform from, WaterPlatform to)
    {
        float horizontalGap = Math.Max(0, Math.Max(from.X, to.X) - Math.Min(from.X + from.Width, to.X + to.Width));
        float rise = to.Y - to.Height / 2 - from.Y;
        return to.Y > from.Y && rise <= 45 * Settings.JumpMetersPerPoint * Settings.PixelsPerMeter &&
            horizontalGap <= Settings.PlatformHorizontalGap + 20;
    }

    public void Launch(FishPlayer f, float points, bool consume)
    {
        if (!f.Alive || f.FallingOnly) return;
        points = Math.Min(points, f.Gauge);
        if (consume && f.Gauge < 1) { f.Charging=false; f.Charge=0; return; }
        float distance = points * Settings.JumpMetersPerPoint * Settings.PixelsPerMeter;
        float speed = MathF.Sqrt(2 * Settings.Gravity * distance);
        f.Direction = Vector2.Normalize(new Vector2(f.Direction.X, Math.Max(.8660254f, f.Direction.Y)));
        f.Velocity = f.Direction * speed;
        f.LaunchPlatform = Platforms.FirstOrDefault(p => p.Id == f.Platform && p.ContainsResident(f.Position, Settings))?.Id
            ?? Platforms.FirstOrDefault(p => p.Contains(f.Position))?.Id ?? -1;
        f.Jumping = true;
        f.Charging = false;
        f.Charge = 0;
        f.EndFlight();
        f.FlightOrigin = consume ? FlightOrigin.PaidJump : FlightOrigin.Parry;
        if (consume) f.LastPaidJumpCost = points;
        if (consume) f.Gauge -= points;
        Sound?.Invoke("jump");
    }

    public void Step(float dt, Control[] controls)
    {

        if (dt <= 0) return;
        double stepStart = Time;
        Time += dt;
        UpdateDuck(stepStart);

        var contacts = new List<(FishPlayer fish, WaterPlatform platform, float time)>();

        for (int i = 0; i < 2; i++)
        {

            var f = Players[i];
            if (!f.Alive) continue;
            var c = controls[i];
            // 키를 누른 채 유지하거나 자동 반복해도 새 팔딱 입력으로 소비하지 않는다.
            bool press = c.Press && !f.JumpHeld;
            if (c.Release || !c.Held) f.JumpHeld = false; else if (c.Press) f.JumpHeld = true;
            f.FlapMessageTime = Math.Max(0, f.FlapMessageTime - dt);
            f.Splash = Math.Max(0, f.Splash - dt);
            f.Cast = Math.Max(0, f.Cast - dt);
            f.Effect = Math.Max(0, f.Effect - dt);
            f.Parry = Math.Max(0, f.Parry - dt);
            f.SwimDelay = Math.Max(0, f.SwimDelay - dt);

            var water = !f.Jumping && !f.FallingOnly ? Platforms.FirstOrDefault(p => p.Id == f.Platform && p.ContainsResident(f.Position, Settings)) : null;

            if (press || c.ParryPress)
                f.Parry = Math.Max(0, Settings.ParryWindow - (float)(Time - (c.InputTime >= 0 ? c.InputTime : stepStart)));
            if (press && water == null) BeginFlap(f, c, stepStart);
            CaptureFlapDirection(f, c, stepStart);
            if (press && water != null)
            {
                f.Charging = water != null;
                f.Charge = 0;
                f.Direction = Vector2.UnitY;
                f.JumpBiasAmount = 0;
                if (f.Charging) Sound?.Invoke("jumpholding");
            }
            // Signed held duration selects the launch angle; opposite input unwinds it.
            if (f.Charging && c.Held)
            {
                f.JumpBiasAmount = Math.Clamp(f.JumpBiasAmount + c.JumpBias * dt / Math.Max(.01f, Settings.JumpBiasFullSeconds), -1, 1);
                float angle = f.JumpBiasAmount * Settings.JumpBiasMaxDegrees * MathF.PI / 180;
                f.Direction = new Vector2(MathF.Sin(angle), MathF.Cos(angle));
            }

            if (f.Charging && c.Held) f.Charge = Math.Min(f.Gauge, Math.Min(100, f.Charge + Settings.ChargeRate * dt));



            if (c.Direction.X != 0) f.FacingRight = c.Direction.X > 0;

            bool swim = water != null && !f.Jumping && c.Direction != Vector2.Zero && f.SwimDelay <= 0;

            f.SwimDirection = swim ? c.Direction : Vector2.Zero;
            if (swim)
            {
                if (f.SwimTime == 0) Sound?.Invoke("swim");
                f.SwimTime += dt;
                f.Velocity = c.Direction * Settings.SwimSpeed;


            }

            else
            {
                if (f.SwimTime > 0)
                {
                    f.SwimDelay = .1f + Math.Min(.4f, f.SwimTime * .12f);
                    f.SwimTime = 0;
                }
                f.Velocity.Y -= (Settings.Gravity - (water != null && !f.Jumping && f.Gauge > 0 ? Settings.Buoyancy : 0)) * dt;
                if (water != null && !f.Jumping) f.Velocity.X *= MathF.Exp(-4 * dt);
                else if (f.Jumping && !f.FallingOnly)
                {
                    bool bursting = f.FlapBoostRemaining > 0;
                    DecayFlapBoost(f, dt);
                    // 짧은 추가 속도를 다음 틱의 공중 속도 제한으로 즉시 잘라내지 않는다.
                    float limit = bursting ? Math.Max(Settings.AirSteeringMaxSpeed, Math.Abs(f.Velocity.X)) : Settings.AirSteeringMaxSpeed;
                    f.Velocity.X = Math.Clamp(f.Velocity.X + c.Direction.X * Settings.AirSteeringAcceleration * dt, -limit, limit);
                }
            }

            var previousPosition = f.Position;
            f.Position += f.Velocity * dt;
            f.Position.X = Math.Clamp(f.Position.X, Settings.WorldMargin, Settings.WorldWidth - Settings.WorldMargin);

            float jumpDistance = f.Jumping ? Vector2.Distance(previousPosition, f.Position) : 0;
            if(swim){float gain=Vector2.Distance(previousPosition,f.Position)/Settings.PixelsPerMeter*Settings.RecoveryPerMeter;f.Gauge=Math.Clamp(f.Gauge+(water!.Color==f.Id?gain:-Settings.WrongColorDrain*dt),0,100);}
            if (water != null && water.ContainsResident(f.Position, Settings)) f.Platform = water.Id;
            else
            {
                f.Platform = -1;
                if (f.Charging)
                {
                    f.Charging = false;
                    f.Charge = 0;
                }
                var entered = Platforms
                    .Where(p => water == null || p.Id != water.Id)
                    .Where(p => !(f.Jumping && p.Id == f.LaunchPlatform && previousPosition.Y <= p.Y + p.Height / 2 && f.Velocity.Y >= 0))
                    .Where(p => p.Contains(f.Position) || p.EntryTime(previousPosition, f.Position) < 1)
                    .OrderBy(p => p.EntryTime(previousPosition, f.Position)).FirstOrDefault();
                if (entered != null)
                {
                    float entryTime = entered.EntryTime(previousPosition, f.Position);
                    jumpDistance *= entryTime;
                    contacts.Add((f, entered, entryTime));
                }
            }

            // Charge only the actual flight segment, not boundary clipping or contact correction.
            f.Gauge = Math.Max(0, f.Gauge - jumpDistance / Settings.PixelsPerMeter * Settings.JumpDrainPerMeter);

            foreach (var fire in Fires.Where(x => !x.Done))
            {

                float d = Vector2.Distance(f.Position, fire.Position);

                if (f.Parry > 0 && fire.Special && d <= Settings.ParryRadius)
                {
                    fire.Done = true;
                    f.Parry = 0;
                    f.FlapPending = false;
                    f.Charging = false;
                    f.Charge = 0;
                    if (fire.Color != f.Id)
                    {
                        Score.Damage += 20;
                        Sound?.Invoke("parry");
                        f.Effect = .4f;
                        f.Direction = Vector2.UnitY;
                        Launch(f, f.Gauge * .4f, false);
                        Sound?.Invoke("splash");
                    }
                    else
                    {
                        f.Gauge = Math.Max(0, f.Gauge - 20);
                        Sound?.Invoke("hit");
                    }
                }

                else if (d < 19)
                {
                    fire.Done = true;
                    f.FlapPending = false;
                    f.Gauge = Math.Max(0, f.Gauge - 20);
                    Sound?.Invoke("hit");
                }

            }

            if (f.Charging) f.Charge = Math.Min(f.Charge, f.Gauge);
            if(c.Release&&f.Charging)Launch(f,Math.Max(1,f.Charge),true);
            foreach (var item in Items.Where(x => !x.Done && Vector2.Distance(x.Position, f.Position) < 35))
            {
                item.Done = true;
                if (item.Water)
                {
                    Waters.Add(new()
                    {
                        Position = f.Position,
                        Owner = f.Id,
                        Damage = f.Gauge * Settings.WaterDamageScale
                    }
                   );
                    f.Cast = .4f;
                    Sound?.Invoke("watercannon");
                }
                else
                {
                    f.Gauge = Math.Min(100, f.Gauge + Settings.FoodRecovery);
                    Sound?.Invoke("eatfood");
                }
            }

            f.Highest = Math.Max(f.Highest, f.Position.Y);

            if (f.Position.Y <= DuckY + 15)
            {
                f.Alive = false;
                f.Charging = false;
                f.EndFlight();
            }

        }

        // Group contacts before changing colors, so simultaneous arrivals use the same old color.
        foreach (var g in contacts.GroupBy(x => x.platform.Id))
        {
            var p = g.First().platform;
            var ordered = g.OrderBy(x => x.time).ToArray();
            for (int j = 0; j < ordered.Length;)
            {
                int end = j + 1;
                while (end < ordered.Length && Math.Abs(ordered[end].time - ordered[j].time) < .0001f) end++;
                int old = p.Color;
                bool accepted = false;
                for (int k = j; k < end; k++)
                {
                    var fish = ordered[k].fish;
                    if (!fish.Alive) continue;
                    fish.EndFlight();
                    fish.Charging = false;
                    fish.Charge = 0;
                    if (fish.Id == old)
                    {
                        fish.FallingOnly = true;
                        fish.Jumping = false;
                        fish.Platform = -1;
                        fish.Position.Y = p.Y - p.Height / 2 - 2;
                        fish.Velocity.Y = -Math.Max(120, Math.Abs(fish.Velocity.Y) * .5f);
                        Sound?.Invoke("reject");
                    }
                    else
                    {
                        accepted = true;
                        fish.FallingOnly = false;
                        fish.Jumping = false;
                        fish.Platform = p.Id;
                        fish.Position.Y = Math.Clamp(fish.Position.Y, p.Y - p.Height / 2 + 2, p.Y + p.Height / 2 - 2);
                        fish.Velocity *= .25f;
                        fish.Splash = .35f;
                        Sound?.Invoke("splash");
                    }
                }
                if (accepted) p.Color = 1 - old;
                j = end;
            }
        }

        // 입수/사망/패링이 확정된 뒤에만 팔딱을 실행하여 한 입력의 중복 결과를 막는다.
        foreach (var fish in Players)
        {
            if (fish.Position.Y <= DuckY + 15) { fish.Alive = false; fish.EndFlight(); }
            ResolveFlap(fish);
        }
        foreach (var f in Fires) f.Position += f.Velocity * dt;

        foreach (var w in Waters)
        {
            w.Position.Y -= 420 * dt;
            if (w.Position.Y <= DuckY)
            {
                Score.Damage += w.Damage;
                w.Done = true;
            }
        }

        Camera = Math.Max(Camera, Math.Max(DuckY + Settings.ViewHeight * (2f / 9f),
            Players.Where(f => f.Alive).Select(f => f.Position.Y - Settings.ViewHeight * (5f / 12f)).DefaultIfEmpty(Camera).Max()));

        float highest = Players.Max(f => f.Highest);
        Stage = Array.FindLastIndex(Settings.StageHeights, h => highest >= h);
        Stage = Math.Max(0, Stage);
        Score.Award(Stage, Settings);

        Generate();
        Platforms.RemoveAll(p => p.Y + p.Height / 2 < CameraBottom - 650 &&
            !Players.Any(f => f.Alive && (f.Platform == p.Id || p.ContainsResident(f.Position, Settings))));
        Fires.RemoveAll(p => p.Done || p.Position.Y > CameraTop + 1000 || p.Position.Y < CameraBottom - 650 || p.Position.X < -100 || p.Position.X > Settings.WorldWidth + 100);
        Waters.RemoveAll(p => p.Done || p.Position.Y < Math.Min(CameraBottom - 800, DuckY - 10));
        Items.RemoveAll(p => p.Done || p.Position.Y < CameraBottom - 650);

    }

}
