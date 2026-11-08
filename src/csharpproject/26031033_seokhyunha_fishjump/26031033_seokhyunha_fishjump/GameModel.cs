using System.Numerics;

using System.Text.Json;


record struct Control(Vector2 Direction, bool Press, bool Release, bool Held, int JumpBias = 0,
    bool ParryPress = false, double InputTime = -1, double DirectionTime = -1);

enum FlightOrigin { None, PaidJump, Parry }
enum DuckState { Chasing, Windup, Holding, Recovering, Stunned }

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
    public Vector2 FlapLockedVelocity;
    public float FlapLockRemaining;
    public double LastFlapActivatedAt = -1;
    public FlightOrigin FlightOrigin;
    public string FlapMessage = "";
    public float FlapMessageTime;
    public float DeathEffect;
    public float FireStunRemaining, FireHitEffect;
    public Vector2 FireHitPosition;
    public int FireHitColor;
    public const float FireHitEffectSeconds = .5f;
    public Vector2 DeathPosition, ParryPosition;
    public const float ParryEffectSeconds = .65f;
    public int ParryEffectFrame => Math.Clamp(1 + (int)((ParryEffectSeconds - Effect) / (ParryEffectSeconds / 5)), 1, 5);

    public void EndFlight()
    {
        FlightOrigin = FlightOrigin.None;
        LastPaidJumpCost = 0;
        FlapUsed = false;
        FlapPending = false;
        FlapDirection = Vector2.Zero;
        FlapLockedVelocity = Vector2.Zero;
        FlapLockRemaining = 0;
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
        Span<float> a = stackalloc float[] { from.X, from.Y };
        Span<float> d = stackalloc float[] { to.X - from.X, to.Y - from.Y };
        Span<float> lo = stackalloc float[] { X, Y - Height / 2 };
        Span<float> hi = stackalloc float[] { X + Width, Y + Height / 2 };
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
    public bool Water, Done, Airborne;
    public int SourcePlatform;
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
    public float DuckStunRemaining { get; private set; }
    public float DuckHitEffect { get; private set; }
    public float DuckHitX { get; private set; }
    public int DuckHitPoints { get; private set; }
    public double DuckAttackTime { get; private set; }
    public const float DeathEffectSeconds = 1.4f, DuckHitEffectSeconds = 1.2f;
    public float CameraTop => Camera + Settings.ViewHeight * .75f;
    public float CameraBottom => Camera - Settings.ViewHeight * .25f;
    public float DeathY => Math.Max(DuckY + 15, CameraBottom);
    public DuckState DuckPhase { get; private set; }
    public int DuckMouthFrame { get; private set; } = 3;
    public int ShotsFired { get; private set; }
    public int Stage;
    int nextId;
    int lastSpawnColor; // 시작 물은 빨강(0). 화면 밖으로 사라져도 생성 순서는 이어진다.
    readonly Random attackRandom, itemRandom;
    public float AirItemShare { get; }
    readonly List<(FishPlayer fish, WaterPlatform platform, float time)> contacts = new(2);
    int generatedItems;
    bool firstItemAirborne;

    public Action<string>? Sound;

    public GameWorld(GameSettings s)
    {
        Settings = s;
        s.Validate();
        Players = [new(0, s.WorldWidth / 2 - 125), new(1, s.WorldWidth / 2 + 125)];
        Seed = s.Seed ?? System.Random.Shared.Next();
        Random = new(Seed);
        attackRandom = new(Seed ^ 1729);
        itemRandom = new(Seed ^ 7919);
        AirItemShare = .30f + (float)itemRandom.NextDouble() * .40f;
        DuckY = CameraBottom - 15;
        Platforms.Add(new(nextId++, 0, 0, s.WorldWidth, 0) { Height = 180 });
        Generate();
    }

    void Generate()
    {
        bool generated = false;
        while (Platforms.Last(p => p.IsRoute).Y < CameraTop + 1000)
        {
            generated = true;
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

            // Fill the unused sides of each band while keeping the main jump route intact.
            float gap = Settings.PlatformHorizontalGap;
            int sideCount = SideCount(x - gap - Settings.WorldMargin) +
                SideCount(Settings.WorldWidth - Settings.WorldMargin - x - width - gap);
            AddSidePlatforms(Settings.WorldMargin, x - gap, y, sideCount);
            AddSidePlatforms(x + width + gap, Settings.WorldWidth - Settings.WorldMargin, y, sideCount);
            AssignSpawnColors(firstNewPlatform);
            for (int k = firstNewPlatform; k < Platforms.Count; k++)
            {
                var candidate = Platforms[k];
                float chance = candidate.IsRoute ? Settings.ItemChance : Settings.ItemChance * .5f / Math.Max(1, sideCount);
                if (itemRandom.NextDouble() < chance) AddItem(candidate);
            }
        }
        if (generated)
        {
            // 다음 묶음의 물이 이전 공중 보상을 덮은 경우 같은 원본 물 안으로 옮긴다.
            foreach (var item in Items)
            {
                if (!item.Airborne || item.Done) continue;
                if (!Platforms.Any(w => item.Position.X >= w.X - 40 && item.Position.X <= w.X + w.Width + 40 &&
                    item.Position.Y >= w.Y - w.Height / 2 - 40 && item.Position.Y <= w.Y + w.Height / 2 + 40)) continue;
                var source = Platforms.Find(w => w.Id == item.SourcePlatform);
                if (source != null) { item.Airborne = false; item.Position = new(source.X + source.Width / 2, source.Y); }
            }
        }
    }

    void AddItem(WaterPlatform source)
    {
        bool airborne = generatedItems == 1 ? !firstItemAirborne : itemRandom.NextDouble() < AirItemShare;
        if (generatedItems == 0) firstItemAirborne = airborne;
        Vector2 position = new(source.X + source.Width * (.25f + .5f * (float)itemRandom.NextDouble()), source.Y);
        if (airborne)
        {
            // 위쪽 빈 공간에 두고 아이템 크기와 획득 반경까지 물 밖에 확보한다.
            // 현재 생성 묶음의 모든 물을 만든 뒤 후보 위치를 검사한다.
            bool placed = false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                position = new(source.X + source.Width * (.2f + .6f * (float)itemRandom.NextDouble()),
                    source.Y + source.Height / 2 + 48 + 22 * (float)itemRandom.NextDouble());
                if (Platforms.Any(w => position.X >= w.X - 40 && position.X <= w.X + w.Width + 40 &&
                    position.Y >= w.Y - w.Height / 2 - 40 && position.Y <= w.Y + w.Height / 2 + 40)) continue;
                placed = true; break;
            }
            if (!placed) { airborne = false; position = new(source.X + source.Width / 2, source.Y); }
        }
        if (Items.Any(i => Vector2.DistanceSquared(i.Position, position) < 70 * 70)) return;
        Items.Add(new() { Position = position, Airborne = airborne, SourcePlatform = source.Id,
            Water = itemRandom.NextDouble() < Settings.WaterItemChance });
        generatedItems++;
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
        return to.Y > from.Y && rise <= 50 * Settings.JumpMetersPerPoint * Settings.PixelsPerMeter &&
            horizontalGap <= Settings.PlatformHorizontalGap + 20;
    }

    public void Launch(FishPlayer f, float points, bool consume, bool playSound = true)
    {
        if (!f.Alive || f.FallingOnly || f.FireStunRemaining > 0) return;
        points = Math.Min(points, f.Gauge);
        if (consume) points = Math.Min(points, Settings.MaxJumpCharge);
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
        if (playSound) Sound?.Invoke("jump");
    }

    bool CheckDeath(FishPlayer fish)
    {
        if (!fish.Alive) return true;
        if (fish.Position.Y > DeathY) return false;
        fish.Alive = false;
        fish.DeathPosition = new(Math.Clamp(fish.Position.X, 0, Settings.WorldWidth), DeathY);
        fish.DeathEffect = DeathEffectSeconds;
        fish.Effect = fish.FireStunRemaining = fish.FireHitEffect = 0;
        Sound?.Invoke("bite");
        fish.Charging = false;
        fish.Charge = fish.Parry = 0;
        fish.Jumping = false;
        fish.Platform = -1;
        fish.EndFlight();
        return true;
    }

    public void Step(float dt, Control[] controls)
    {

        if (dt <= 0) return;
        double stepStart = Time;
        AdvanceFeedback(dt);
        Time += dt;
        DuckY = Math.Max(DuckY, CameraBottom - 15);
        UpdateDuck(stepStart);

        contacts.Clear();

        for (int i = 0; i < 2; i++)
        {

            var f = Players[i];
            if (!f.Alive) continue;
            if (CheckDeath(f)) continue;
            var c = controls[i];
            bool stunned = f.FireStunRemaining > .000001f;
            f.FireStunRemaining = Math.Max(0, f.FireStunRemaining - dt);
            f.FireHitEffect = Math.Max(0, f.FireHitEffect - dt);
            // 키를 누른 채 유지하거나 자동 반복해도 새 팔딱 입력으로 소비하지 않는다.
            bool press = c.Press && !f.JumpHeld;
            if (c.Release || !c.Held) f.JumpHeld = false; else if (c.Press) f.JumpHeld = true;
            // 누름/해제 상태만 추적하고 스턴 중 입력은 복귀 시 자동 발동시키지 않는다.
            if (stunned) { c = new(); press = false; }
            f.FlapMessageTime = Math.Max(0, f.FlapMessageTime - dt);
            f.Splash = Math.Max(0, f.Splash - dt);
            f.Cast = Math.Max(0, f.Cast - dt);
            f.Effect = Math.Max(0, f.Effect - dt);
            f.Parry = Math.Max(0, f.Parry - dt);
            f.SwimDelay = Math.Max(0, f.SwimDelay - dt);

            var water = !f.Jumping && !f.FallingOnly ? Platforms.FirstOrDefault(p => p.Id == f.Platform && p.ContainsResident(f.Position, Settings)) : null;

            // 이미 물에 접촉한 상태의 입력으로 입수/거부를 취소하지 않는다.
            bool touchingWater = water == null && Platforms.Any(p => p.Contains(f.Position) &&
                !(f.Jumping && p.Id == f.LaunchPlatform && f.Velocity.Y >= 0));
            if (touchingWater) f.FlapPending = false;
            bool parryOnly = c.ParryPress && !c.Press;
            if (parryOnly) f.FlapPending = false;
            if (press || parryOnly)
                f.Parry = Math.Max(0, Settings.ParryWindow - (float)(Time - (c.InputTime >= 0 ? c.InputTime : stepStart)));
            if (!touchingWater && water == null)
            {
                if (press) BeginFlap(f, c, stepStart);
                CaptureFlapDirection(f, c, stepStart);
                ResolveFlap(f); // 이동 전에 즉시 방향을 확정한다. 패링 대기는 없다.
            }
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

            if (f.Charging && c.Held) f.Charge = Math.Min(f.Gauge, Math.Min(Settings.MaxJumpCharge, f.Charge + Settings.ChargeRate * dt));



            if (c.Direction.X != 0) f.FacingRight = c.Direction.X > 0;

            bool swim = water != null && !f.Jumping && c.Direction != Vector2.Zero && f.SwimDelay <= 0;

            f.SwimDirection = swim ? c.Direction : Vector2.Zero;
            Vector2 lockedMovement = Vector2.Zero;
            float moveTime = dt;
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
                    f.SwimDelay = .05f;
                    f.SwimTime = 0;
                }
                if (f.FlapLockRemaining > 0)
                {
                    float lockedTime = Math.Min(dt, f.FlapLockRemaining);
                    f.Velocity = f.FlapLockedVelocity;
                    lockedMovement = f.Velocity * lockedTime;
                    f.FlapLockRemaining = Math.Max(0, f.FlapLockRemaining - lockedTime);
                    moveTime = dt - lockedTime;
                }
                if (moveTime > 0)
                {
                    if (f.FlapUsed && f.Velocity.Y > Settings.FlapVerticalCarrySpeed)
                        f.Velocity.Y = Math.Max(Settings.FlapVerticalCarrySpeed,
                            f.Velocity.Y - Settings.FlapVerticalDeceleration * moveTime);
                    f.Velocity.Y -= (Settings.Gravity - (water != null && !f.Jumping && f.Gauge > 0 ? Settings.Buoyancy : 0)) * moveTime;
                    if (water != null && !f.Jumping)
                    {
                        f.Velocity.X *= MathF.Exp(-4 * moveTime);
                        if (Settings.Gravity > 0 && Settings.Buoyancy >= Settings.Gravity && f.Gauge > 0)
                            f.Velocity.Y *= MathF.Exp(-6 * moveTime);
                    }
                    else if (f.Jumping && !f.FallingOnly) f.Velocity.X = SteerAfterFlap(f, c.Direction.X, moveTime);
                }
            }

            var previousPosition = f.Position;
            f.Position += lockedMovement + f.Velocity * moveTime;
            if (f.Position.Y > CameraTop - 24)
            {
                f.Position.Y = CameraTop - 24;
                f.Velocity.Y = Math.Min(0, f.Velocity.Y);
                f.FlapLockedVelocity.Y = Math.Min(0, f.FlapLockedVelocity.Y);
            }
            float unclippedX = f.Position.X;
            f.Position.X = Math.Clamp(f.Position.X, Settings.WorldMargin, Settings.WorldWidth - Settings.WorldMargin);
            if (f.Position.X != unclippedX)
            { f.Velocity.X = 0; f.FlapLockedVelocity.X = 0; }

            float jumpDistance = f.Jumping ? Vector2.Distance(previousPosition, f.Position) : 0;
            if (swim)
            {
                float gain = Vector2.Distance(previousPosition, f.Position) / Settings.PixelsPerMeter * Settings.RecoveryPerMeter;
                var partner = Players[1 - i];
                bool together = partner.Alive && partner.Platform == water!.Id && water.ContainsResident(partner.Position, Settings);
                // 먼저 색을 바꿔 준 동료도 합류 후에는 헤엄으로 다음 점프를 준비할 수 있다.
                f.Gauge = Math.Clamp(f.Gauge + (water!.Color == f.Id || together ? gain : -Settings.WrongColorDrain * dt), 0, 100);
            }
            if (water != null && water.ContainsResident(f.Position, Settings)) f.Platform = water.Id;
            else
            {
                f.Platform = -1;
                if (f.Charging)
                {
                    f.Charging = false;
                    f.Charge = 0;
                }
                WaterPlatform? entered = null;
                float earliest = float.PositiveInfinity;
                foreach (var candidate in Platforms)
                {
                    if (water != null && candidate.Id == water.Id) continue;
                    if (f.Jumping && candidate.Id == f.LaunchPlatform &&
                        previousPosition.Y <= candidate.Y + candidate.Height / 2 && f.Velocity.Y >= 0) continue;
                    float entry = candidate.EntryTime(previousPosition, f.Position);
                    if (entry >= earliest || (entry >= 1 && !candidate.Contains(f.Position))) continue;
                    entered = candidate; earliest = entry;
                }
                if (entered != null)
                {
                    float entryTime = earliest;
                    jumpDistance *= entryTime;
                    f.Position = Vector2.Lerp(previousPosition, f.Position, entryTime);
                    contacts.Add((f, entered, entryTime));
                }
            }

            // Charge only the actual flight segment, not boundary clipping or contact correction.
            f.Gauge = Math.Max(0, f.Gauge - jumpDistance / Settings.PixelsPerMeter * Settings.JumpDrainPerMeter);
            // 화면 밑의 물·아이템·패링으로 탈락한 물고기를 되살리지 않는다.
            if (CheckDeath(f)) continue;

            foreach (var fire in Fires.Where(x => !x.Done))
            {
                // 한 불줄기가 겹쳐도 0.3초 동안 추가 피해/기절을 중첩하지 않는다.
                if (stunned || f.FireStunRemaining > 0) break;
                // 물고기와 불의 상대 이동 선분으로 검사해 빠른 직진 중에도 불을 통과하지 않는다.
                float d = SegmentDistance(previousPosition - fire.Position,
                    f.Position - (fire.Position + fire.Velocity * dt), Vector2.Zero);

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
                        f.Effect = FishPlayer.ParryEffectSeconds;
                        f.ParryPosition = f.Position; // 도약으로 사라지지 않도록 성공 지점에 효과를 남긴다.
                        f.Direction = Vector2.UnitY;
                        Launch(f, f.Gauge * .4f, false, playSound: false);
                        Sound?.Invoke("parry");
                    }
                    else
                    {
                        HitByFire(f, fire);
                    }
                }

                else if (d < 19)
                {
                    fire.Done = true;
                    HitByFire(f, fire);
                }

            }

            if (f.Charging) f.Charge = Math.Min(f.Charge, f.Gauge);
            if(c.Release&&f.Charging)Launch(f,Math.Max(1,f.Charge),true);
            foreach (var item in Items.Where(x => !x.Done && (!x.Airborne || f.Jumping) && SegmentDistance(previousPosition, f.Position, x.Position) < 35))
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

            CheckDeath(f);

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
                        FlapNotice(fish, "물 진입 거부 · 반대 색 물로 이동");
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

        // 사망 확정 뒤 후보와 직진 상태를 남기지 않는다.
        foreach (var fish in Players)
        {
            CheckDeath(fish);
        }
        foreach (var f in Fires) f.Position += f.Velocity * dt;

        foreach (var w in Waters)
        {
            if (w.Done) continue;
            // 하향 선분이 화면 하단의 입 경계를 지나면 고속 수둔도 빠짐없이 명중한다.
            w.Position.Y -= 420 * dt;
            if (w.Position.Y <= DuckMouthPosition.Y && w.Position.X >= 0 && w.Position.X <= Settings.WorldWidth)
                HitDuck(w);
        }

        // 카메라는 UpdateDuck에서만 상승한다. 먼저 뛴 플레이어가 아래의 동료를 밀어내지 않는다.
        foreach (var fish in Players) CheckDeath(fish);

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
