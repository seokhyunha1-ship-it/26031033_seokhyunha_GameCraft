using System.Numerics;

using System.Text.Json;


record struct Control(Vector2 Direction, bool Press, bool Release, bool Held, int JumpBias = 0, bool ParryPress = false);

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
    public bool UsesBasicSwimPose => Platform >= 0 && SwimDirection.Y != 0;

}

sealed class WaterPlatform(int id, float x, float y, float w, int color)
{

    public int Id = id, Color = color;
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

sealed class GameWorld
{

    public readonly GameSettings Settings;
    public readonly Random Random;
    public int Seed;

    public FishPlayer[] Players = [new(0, 350), new(1, 600)];

    public List<WaterPlatform> Platforms = [];
    public List<FireProjectile> Fires = [];
    public List<WaterAttack> Waters = [];
    public List<GameItem> Items = [];

    public ScoreSystem Score = new();
    public float Time, DuckY = -220, Camera, AttackTimer;
    public int Stage;
    int nextId;
    readonly Random attackRandom;

    public Action<string>? Sound;

    public GameWorld(GameSettings s)
    {
        Settings = s;
        Seed = s.Seed ?? System.Random.Shared.Next();
        Random = new(Seed);
        attackRandom = new(Seed ^ 1729);
        Platforms.Add(new(nextId++, 0, 0, 960, 0) { Height = 180 });
        Generate();
    }

    int layoutPattern, patternRemaining, patternStep, patternColor;

    void Generate()
    {
        while (Platforms.Last(p => p.IsRoute).Y < Camera + 1000)
        {
            var last = Platforms.Last(p => p.IsRoute);
            if (patternRemaining-- <= 0)
            {
                layoutPattern = Random.Next(4); // left staircase, right staircase, zigzag, scattered
                patternRemaining = Random.Next(3, 7);
                patternStep = 0;
                patternColor = Random.Next(2);
            }
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
            float minX = Math.Max(20, last.X + 60 - width);
            float maxX = Math.Min(940 - width, last.X + last.Width - 60);
            float choice = (float)Random.NextDouble();
            float fraction = layoutPattern switch
            {
                0 => choice * .35f,
                1 => .65f + choice * .35f,
                2 => patternStep % 2 == 0 ? choice * .25f : .75f + choice * .25f,
                _ => choice
            };
            float x = minX + (maxX - minX) * fraction;
            // Leave a visible gap above the tallest water in the previous band.
            float previousTop = Platforms.Where(p => p.Id >= last.Id).Max(p => p.Y + p.Height / 2);
            int minGap = Math.Max(110, (int)Math.Ceiling(previousTop - last.Y + Math.Max(height / 2, 115) + 12));
            float y = last.Y + Random.Next(minGap, minGap + 25);
            int color = layoutPattern switch
            {
                0 or 1 => (patternColor + patternStep / 2) % 2,
                2 => (patternColor + patternStep) % 2,
                _ => Random.Next(2)
            };
            patternStep++;
            var platform = new WaterPlatform(nextId++, x, y, width, color) { Height = height };
            Platforms.Add(platform);
            if (Random.NextDouble() < Settings.ItemChance)
                Items.Add(new() { Position = new(x + width / 2, y), Water = Random.NextDouble() < Settings.WaterItemChance });
            // Fill the unused sides of each band while keeping the main jump route intact.
            // Wide spaces split into two slots, distributing choices across the screen.
            AddSidePlatforms(20, x - 24, y, 1 - color, 940 - (x + width + 24) < 130);
            AddSidePlatforms(x + width + 24, 940, y, 1 - color, x - 44 < 130);

        }
    }

    void AddSidePlatforms(float left, float right, float y, int color, bool split)
    {
        float available = right - left;
        if (available < 130) return;
        int count = split && available >= 360 ? 2 : 1;
        float slot = (available - (count - 1) * 24) / count;
        for (int i = 0; i < count; i++)
        {
            float width = Random.Next(130, (int)Math.Min(300, slot) + 1);
            float x = left + i * (slot + 24) + (float)Random.NextDouble() * (slot - width);
            float sideY = y + Random.Next(-5, 6);
            Platforms.Add(new(nextId++, x, sideY, width, (color + i) % 2)
            {
                Height = Math.Min(Platforms.Last(p => p.IsRoute).Height, Random.Next(3) switch { 0 => Random.Next(80, 111), 1 => Random.Next(130, 171), _ => Random.Next(190, 221) }), IsRoute = false
            });
            // Extra routes should not multiply the original item supply.
            if (Random.NextDouble() < Settings.ItemChance * .25f)
                Items.Add(new() { Position = new(x + width / 2, sideY), Water = Random.NextDouble() < Settings.WaterItemChance });
        }
    }

    public void Launch(FishPlayer f, float points, bool consume)
    {
        if (f.FallingOnly) return;
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
        if (consume) f.Gauge -= points;
        Sound?.Invoke("jump");
    }

    public void Step(float dt, Control[] controls)
    {

        Time += dt;
        DuckY += Settings.DuckSpeed * dt;
        AttackTimer += dt;

        if (AttackTimer >= Settings.AttackCooldown)
        {
            AttackTimer -= Settings.AttackCooldown;
            Sound?.Invoke("fire");
            int color = attackRandom.Next(2), special = attackRandom.Next(7);
            for (int i = 0; i < 7; i++)
            {
                float a = (-60 + i * 20) * MathF.PI / 180;
                Fires.Add(new()
                {
                    Position = new(480, DuckY),
                    Velocity = new(MathF.Sin(a) * Settings.FireSpeed, MathF.Cos(a) * Settings.FireSpeed),
                    Color = i == special ? 1 - color : color,
                    Special = i == special
                }
               );
            }
        }

        var contacts = new List<(FishPlayer fish, WaterPlatform platform, float time)>();

        for (int i = 0; i < 2; i++)
        {

            var f = Players[i];
            if (!f.Alive) continue;
            var c = controls[i];
            f.Splash = Math.Max(0, f.Splash - dt);
            f.Cast = Math.Max(0, f.Cast - dt);
            f.Effect = Math.Max(0, f.Effect - dt);
            f.Parry = Math.Max(0, f.Parry - dt);
            f.SwimDelay = Math.Max(0, f.SwimDelay - dt);

            var water = !f.Jumping && !f.FallingOnly ? Platforms.FirstOrDefault(p => p.Id == f.Platform && p.ContainsResident(f.Position, Settings)) : null;

            if (c.Press || c.ParryPress) f.Parry = Settings.ParryWindow;
            if (c.Press)
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
                    f.Velocity.X = Math.Clamp(f.Velocity.X + c.Direction.X * Settings.AirSteeringAcceleration * dt,
                        -Settings.AirSteeringMaxSpeed, Settings.AirSteeringMaxSpeed);
                }
            }

            var previousPosition = f.Position;
            f.Position += f.Velocity * dt;
            f.Position.X = Math.Clamp(f.Position.X, 20, 940);

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

        Camera = Math.Max(Camera, Math.Max(DuckY + 160, Players.Where(f => f.Alive).Select(f => f.Position.Y - 300).DefaultIfEmpty(Camera).Max()));

        float highest = Players.Max(f => f.Highest);
        Stage = Array.FindLastIndex(Settings.StageHeights, h => highest >= h);
        Stage = Math.Max(0, Stage);
        Score.Award(Stage, Settings);

        Generate();
        Platforms.RemoveAll(p => p.Y < Camera - 650);
        Fires.RemoveAll(p => p.Done || p.Position.Y > Camera + 1100 || p.Position.Y < Camera - 650 || p.Position.X < -100 || p.Position.X > 1060);
        Waters.RemoveAll(p => p.Done || p.Position.Y < Camera - 800);
        Items.RemoveAll(p => p.Done || p.Position.Y < Camera - 650);

    }

}
