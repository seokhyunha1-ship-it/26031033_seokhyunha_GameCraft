using System.Numerics;

using System.Text.Json;

static class GameSelfTests
{

    static readonly Control[] Idle = [new(), new()];

    public static int Run()
    {

        var results = new List<string>();
        int failed = 0;

        void Test(string name, Action a)
        {
            try
            {
                a();
                results.Add("PASS " + name);
            }
            catch (Exception e)
            {
                failed++;
                results.Add("FAIL " + name + ": " + e.Message);
            }
        }

        void Check(bool value, string why = "assertion")
        {
            if (!value) throw new Exception(why);
        }

        GameWorld World(bool frozen = false)
        {
            var w = new GameWorld(new()
            {
                Seed = 42,
                DuckSpeed = 0,
                AttackCooldown = 10000,
                Gravity = frozen ? 0 : 260,
                Buoyancy = frozen ? 0 : 210
            }
           );
            w.DuckY = -10000;
            w.Items.Clear();
            w.Players[1].Alive = false;
            return w;
        }

        Test("charge: 50 points per second", () =>
        {
            var w = World(true); w.Step(.01f, [new(Vector2.Zero, true, false, true), new()]); for (int i = 0; i < 99; i++) w.Step(.01f, [new(Vector2.Zero, false, false, true), new()]); Check(Math.Abs(w.Players[0].Charge - 50) < .01f);
        }
);

        Test("charge limited to available gauge", () =>
        {
            var w = World(true); w.Players[0].Gauge = 12; w.Step(1, [new(Vector2.Zero, true, false, true), new()]); Check(w.Players[0].Charge == 12);
        }
);

        Test("release deducts once", () =>
        {
            var w = World(true); w.Step(.5f, [new(Vector2.Zero, true, false, true), new()]); w.Step(.01f, [new(Vector2.Zero, false, true, false), new()]); float g = w.Players[0].Gauge; w.Step(.01f, Idle); Check(g == 75 && w.Players[0].Gauge == g);
        }
);

        Test("tap minimum 1", () =>
        {
            var w = World(true); w.Step(.001f, [new(Vector2.Zero, true, false, false), new()]); w.Step(.001f, [new(Vector2.Zero, false, true, false), new()]); Check(w.Players[0].Gauge == 99);
        }
);

        Test("zero gauge cannot launch", () =>
        {
            var w = World(); var f = w.Players[0]; f.Gauge = 0; w.Launch(f, 1, true); Check(!f.Jumping && f.Gauge == 0);
        }
);

        Test("100 point vertical jump reaches 50m", () =>
        {
            var w = World(); var f = w.Players[0]; w.Platforms.Clear(); w.Platforms.Add(new(100, 0, 10000, 960, 0)); w.Launch(f, 100, true); for (int i = 0; i < 240; i++) w.Step(1f / 120, Idle); Check(Math.Abs(f.Highest - 500) < 3, $"height={f.Highest}");
        }
);

        Test("direction captured on following update", () =>
        {
            var w = World(true); w.Step(.01f, [new(Vector2.UnitY, true, false, true), new()]); w.Step(.01f, [new(-Vector2.UnitX, false, false, true), new()]); Check(w.Players[0].Direction == -Vector2.UnitX);
        }
);

        Test("correct water recovers gauge", () =>
        {
            var w = World(true); w.Players[0].Gauge = 50; w.Step(.1f, [new(Vector2.UnitX, false, false, false), new()]); Check(w.Players[0].Gauge > 50);
        }
);

        Test("opposite water drains gauge", () =>
        {
            var w = World(true); w.Players[0].Gauge = 50; w.Platforms[0].Color = 1; w.Step(.1f, [new(Vector2.UnitX, false, false, false), new()]); Check(w.Players[0].Gauge < 50);
        }
);

        Test("jump entry changes opposite water", () =>
        {
            var w = World(true); var f = w.Players[0]; w.Platforms[0].Color = 1; f.Jumping = true; f.LaunchPlatform = 99; w.Step(.01f, Idle); Check(w.Platforms[0].Color == 0);
        }
);

        Test("returning to opposite source water recolors it", () =>
        {
            var w = World(true); var f = w.Players[0]; w.Platforms[0].Color = 1; f.Jumping = true; f.LaunchPlatform = 0; f.Velocity.Y = -1; w.Step(.01f, Idle); Check(w.Platforms[0].Color == 0);
        }
);

        Test("simultaneous arrivals use old color", () =>
        {
            var w = World(true); w.Players[1].Alive = true; foreach (var f in w.Players)
            {
                f.Jumping = true; f.LaunchPlatform = 99;
            }
            w.Step(.01f, Idle); Check(w.Platforms[0].Color == 1);
        }
);

        Test("parry success: damage and no gauge deduction", () =>
        {
            var w = World(true); var f = w.Players[0]; w.Fires.Add(new()
            {
                Position = f.Position,
                Color = 1,
                Special = true
            }
           ); w.Step(.01f, [new(Vector2.UnitY, true, false, false), new()]); Check(w.Score.Damage == 20 && f.Gauge == 100 && f.Jumping); w.Step(.01f, Idle); Check(w.Score.Damage == 20);
        }
);

        Test("wrong-color special costs exactly 20", () =>
        {
            var w = World(true); w.Fires.Add(new()
            {
                Position = w.Players[0].Position,
                Color = 0,
                Special = true
            }
           ); w.Step(.01f, [new(Vector2.Zero, true, false, false), new()]); w.Step(.01f, Idle); Check(w.Players[0].Gauge == 80 && w.Score.Damage == 0);
        }
);

        Test("expired parry becomes regular hit", () =>
        {
            var w = World(true); w.Step(.01f, [new(Vector2.Zero, true, false, false), new()]); w.Step(.21f, Idle); w.Fires.Add(new()
            {
                Position = w.Players[0].Position,
                Color = 1,
                Special = true
            }
           ); w.Step(.01f, Idle); Check(w.Score.Damage == 0 && w.Players[0].Gauge == 80);
        }
);

        Test("fan: seven shots, one special, fixed speed", () =>
        {
            var w = World(true); w.DuckY = -200; w.Settings.AttackCooldown = 4; for (int i = 0; i < 481; i++) w.Step(1f / 120, Idle); Check(w.Fires.Count == 7 && w.Fires.Count(f => f.Special) == 1 && w.Fires.All(f => Math.Abs(f.Velocity.Length() - 180) < .01f));
        }
);

        Test("water item scores only on duck collision", () =>
        {
            var w = World(true); w.Items.Add(new()
            {
                Position = w.Players[0].Position,
                Water = true
            }
           ); w.Step(.01f, Idle); Check(w.Score.Damage == 0 && w.Waters.Count == 1); w.DuckY = -10; w.Step(.1f, Idle); Check(w.Score.Damage == 100 && w.Waters.Count == 0);
        }
);

        Test("food clamp", () =>
        {
            var w = World(true); w.Players[0].Gauge = 90; w.Items.Add(new()
            {
                Position = w.Players[0].Position
            }
           ); w.Step(.01f, Idle); Check(w.Players[0].Gauge == 100);
        }
);

        Test("height score never decreases", () =>
        {
            var w = World(true); w.Players[0].Highest = 100; w.Step(.01f, Idle); Check(w.Players[0].Highest == 100);
        }
);

        Test("stage awards once and includes skipped stages", () =>
        {
            var s = new ScoreSystem(); var settings = new GameSettings(); s.Award(5, settings); s.Award(5, settings); Check(s.Bonus == 1500 && s.Awarded.Count == 5);
        }
);

        Test("combined score formula", () =>
        {
            var w = World(); w.Players[0].Highest = 100; w.Players[1].Highest = 200; w.Score.Damage = 20; w.Score.Bonus = 100; Check(w.Score.Total(w.Players, w.Settings) == 500);
        }
);

        Test("seed reproduces platforms", () =>
        {
            var a = World(); var b = World(); Check(a.Platforms.Select(p => (p.X, p.Y, p.Width, p.Color)).SequenceEqual(b.Platforms.Select(p => (p.X, p.Y, p.Width, p.Color))));
        }
);

        Test("generated platform gap reachable with 30 points", () =>
        {
            var w = World(); var route = w.Platforms.Where(p => p.IsRoute).ToArray(); for (int i = 1; i < route.Length; i++)
            {
                var a = route[i - 1]; var b = route[i]; Check(b.Y - a.Y <= 150 && Math.Max(a.X, b.X) < Math.Min(a.X + a.Width, b.X + b.Width));
            }
        }
);

        Test("camera monotonic and old objects culled", () =>
        {
            var w = World(true); w.Players[0].Highest = 10000; w.Players[0].Position.Y = 10000; w.Step(.01f, Idle); float camera = w.Camera; w.Players[0].Position.Y = 9000; w.Step(.01f, Idle); Check(w.Camera >= camera && w.Platforms.Count <= 54);
        }
);

        Test("gauge zero disables buoyancy", () =>
        {
            var a = World(); var b = World(); a.Players[0].Gauge = 0; a.Step(.01f, Idle); b.Step(.01f, Idle); Check(a.Players[0].Velocity.Y < b.Players[0].Velocity.Y);
        }
);

        Test("one death leaves other player active", () =>
        {
            var w = World(true); w.Players[1].Alive = true; w.DuckY = -30; w.Players[0].Position.Y = -20; w.Players[0].Highest = 400; w.Step(.01f, Idle); Check(!w.Players[0].Alive && w.Players[1].Alive && w.Players[0].Highest == 400);
        }
);

        Test("restart constructs clean world", () =>
        {
            var w = World(); w.Score.Damage = 20; w.Camera = 500; var fresh = World(); Check(fresh.Score.Damage == 0 && fresh.Camera == 0 && fresh.Time == 0 && fresh.Players[0].Gauge == 100);
        }
);

        Test("long simulation bounds projectiles and platforms", () =>
        {
            var w = World(true); w.Settings.DuckSpeed = 13; w.Settings.AttackCooldown = 4; for (int i = 0; i < 120 * 600; i++)
            {
                w.Players[0].Position.Y = w.DuckY + 250; w.Step(1f / 120, Idle);
            }
            Check(w.Platforms.Count <= 54 && w.Fires.Count < 150 && w.Items.Count <= 54);
        }
);

        Test("sub-frame contact order overrides player iteration", () =>
        {
            var w = World(true); w.Players[1].Alive = true; for (int i = 0; i < 2; i++)
            {
                var f = w.Players[i]; f.Position.Y = i == 0 ? -55 : -50; f.Velocity.Y = 100; f.Jumping = true; f.LaunchPlatform = 99;
            }
            w.Step(.1f, Idle); Check(w.Platforms[0].Color == 0);
        }
);

        Test("release simultaneous with parry does not spend gauge",()=>{var w=World(true);var f=w.Players[0];w.Step(.05f,[new(Vector2.Zero,true,false,true),new()]);w.Fires.Add(new(){Position=f.Position,Color=1,Special=true});w.Step(.01f,[new(Vector2.Zero,false,true,false),new()]);Check(f.Gauge==100&&w.Score.Damage==20);});
        Test("platform size, spacing and route variety across seeds", () =>
        {
            var sizes = new HashSet<int>(); var heights = new HashSet<float>(); var gaps = new HashSet<float>();
            float minCenter = 960, maxCenter = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var w = new GameWorld(new GameSettings { Seed = seed });
                var route = w.Platforms.Where(p => p.IsRoute).ToArray();
                for (int i = 1; i < route.Length; i++)
                {
                    var a = route[i - 1]; var b = route[i];
                    sizes.Add(b.Width <= 210 ? 0 : b.Width <= 360 ? 1 : 2);
                    heights.Add(b.Height); gaps.Add(b.Y - a.Y);
                    minCenter = Math.Min(minCenter, b.X + b.Width / 2);
                    maxCenter = Math.Max(maxCenter, b.X + b.Width / 2);
                    Check(b.X >= 20 && b.X + b.Width <= 940.01f);
                    Check(Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X) >= 59.99f);
                    Check(b.Y - a.Y > (a.Height + b.Height) / 2 && b.Y - a.Y <= 150);
                }
            }
            Check(sizes.Count == 3 && heights.Count > 15 && gaps.Count > 20);
            Check(minCenter < 250 && maxCenter > 710);
        });
        Test("dense platforms cover left center right without overlaps", () =>
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var w = new GameWorld(new GameSettings { Seed = seed });
                var generated = w.Platforms.Where(p => p.Id != 0).ToArray();
                int bands = generated.Count(p => p.IsRoute);
                Check(generated.Length >= bands * 2 && generated.Length <= bands * 3, "2-3 platforms per band");
                for (int lane = 0; lane < 3; lane++)
                    Check(generated.Count(p => (int)((p.X + p.Width / 2) / 320) == lane) >= 2, "lane coverage");
                for (int i = 0; i < generated.Length; i++)
                {
                    var a = generated[i];
                    Check(a.X >= 20 && a.X + a.Width <= 940.01f);
                    for (int j = i + 1; j < generated.Length; j++)
                    {
                        var b = generated[j];
                        Check(a.X + a.Width <= b.X || b.X + b.Width <= a.X ||
                            Math.Abs(a.Y - b.Y) > (a.Height + b.Height) / 2, "overlapping water");
                    }
                }
            }
        });
        Test("both fish reject own water repeatedly and recover only in opposite water", () =>
        {
            for (int id = 0; id < 2; id++)
            {
                var w = World(true);
                foreach (var other in w.Players) other.Alive = other.Id == id;
                var f = w.Players[id];
                w.Platforms.Clear();
                w.Platforms.Add(new(100, 20, 10000, 920, 0));
                var first = new WaterPlatform(101, 200, 200, 300, id);
                var second = new WaterPlatform(102, 200, 50, 300, id);
                var rescue = new WaterPlatform(103, 200, -100, 300, 1-id);
                w.Platforms.AddRange([first, second, rescue]);
                int beeps = 0; w.Sound = sound => { if (sound == "reject") beeps++; };
                f.Platform = -1; f.Position = new(350, 250); f.Velocity = new(0,-500);
                w.Step(.02f, Idle);
                Check(f.FallingOnly && f.Platform == -1 && f.Position.Y < first.Y-first.Height/2 && beeps == 1);
                float gauge = f.Gauge; w.Launch(f, 50, true);
                Check(f.Velocity.Y < 0 && f.Gauge == gauge);
                f.Position = new(350, 100); f.Velocity = new(0,-500);
                w.Step(.02f, Idle);
                Check(f.FallingOnly && f.Platform == -1 && beeps == 2 && second.Color == id);
                f.Position = new(350, -50); f.Velocity = new(0,-500);
                w.Step(.02f, Idle);
                Check(!f.FallingOnly && f.Platform == rescue.Id && rescue.Color == id);
                w.Step(.01f, Idle);
                Check(f.Platform == rescue.Id && !f.FallingOnly && beeps == 2);
            }
        });
        Test("rising own-color collision rejects and high-speed opposite collision catches", () =>
        {
            var w = World(true); var f = w.Players[0];
            w.Platforms.Clear(); w.Platforms.Add(new(100,20,10000,920,0));
            var water = new WaterPlatform(101,200,200,300,0) { IsRoute=false }; w.Platforms.Add(water);
            f.Platform=-1; f.Jumping=true; f.Position=new(350,100); f.Velocity=new(0,1000);
            w.Step(.1f,Idle); Check(f.FallingOnly && f.Velocity.Y<0);
            water.Color=1; f.Position=new(350,400); f.Velocity=new(0,-4000);
            w.Step(.1f,Idle); Check(f.Platform==101 && !f.FallingOnly && water.Color==0);
        });
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results.Append($"{results.Count - failed}/{results.Count} passed"));

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "settings-defaults.json"), JsonSerializer.Serialize(new GameSettings(), new JsonSerializerOptions
        {
            WriteIndented = true
        }
));

        return failed == 0 ? 0 : 1;

    }

}
