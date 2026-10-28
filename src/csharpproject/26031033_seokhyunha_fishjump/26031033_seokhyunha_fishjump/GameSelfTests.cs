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
                WorldWidth = 960,
                WorldMargin = 20,
                DuckSpeed = 0,
                AttackCooldown = 10000,
                Gravity = frozen ? 0 : 260,
                Buoyancy = frozen ? 0 : 210
            }
           );
            w.DuckY = -10000;
            w.Players[0].Position.X = 350;
            w.Players[1].Position.X = 600;
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

        Test("100 point vertical jump reaches 80m", () =>
        {
            var w = World(); var f = w.Players[0]; w.Platforms.Clear(); w.Platforms.Add(new(100, 0, 10000, 960, 0)); w.Launch(f, 100, true); for (int i = 0; i < 360; i++) w.Step(1f / 120, Idle); Check(Math.Abs(f.Highest - 800) < 3, $"height={f.Highest}");
        }
);

        Test("movement keys do not override upward charge", () =>
        {
            var w = World(true); w.Step(.01f, [new(Vector2.UnitY, true, false, true), new()]); w.Step(.01f, [new(-Vector2.UnitX, false, false, true), new()]); Check(w.Players[0].Direction == Vector2.UnitY);
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
                var a = route[i - 1]; var b = route[i]; Check(b.Y - b.Height / 2 - (a.Y + a.Height / 2) <= 30 * w.Settings.JumpMetersPerPoint * w.Settings.PixelsPerMeter && Math.Max(a.X, b.X) < Math.Min(a.X + a.Width, b.X + b.Width));
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
                var f = w.Players[i]; f.Position.Y = -w.Platforms[0].Height / 2 - (i == 0 ? 7.5f : 2.5f); f.Velocity.Y = 100; f.Jumping = true; f.LaunchPlatform = 99;
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
                    Check(b.X >= w.Settings.WorldMargin && b.X + b.Width <= w.Settings.WorldWidth - w.Settings.WorldMargin + .01f);
                    Check(Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X) >= 59.99f);
                    float gap = b.Y - b.Height / 2 - (a.Y + a.Height / 2);
                    Check(gap >= w.Settings.PlatformVerticalGapMin && gap <= w.Settings.PlatformVerticalGapMax);
                }
            }
            Check(sizes.Count == 3 && heights.Count > 15 && gaps.Count > 20);
            Check(minCenter < new GameSettings().WorldWidth / 2 && maxCenter > new GameSettings().WorldWidth / 2);
        });
        Test("sparse platforms cover left center right without overlaps", () =>
        {
            int[] lanes = new int[3];
            for (int seed = 0; seed < 200; seed++)
            {
                var w = new GameWorld(new GameSettings { Seed = seed });
                var generated = w.Platforms.Where(p => p.Id != 0).ToArray();
                int bands = generated.Count(p => p.IsRoute);
                Check(generated.Length >= bands && generated.Length <= bands * 3, "at most one branch on each side");
                for (int lane = 0; lane < 3; lane++)
                    lanes[lane] += generated.Count(p => (int)((p.X + p.Width / 2) / (w.Settings.WorldWidth / 3)) == lane);
                for (int i = 0; i < generated.Length; i++)
                {
                    var a = generated[i];
                    Check(a.X >= w.Settings.WorldMargin && a.X + a.Width <= w.Settings.WorldWidth - w.Settings.WorldMargin + .01f);
                    for (int j = i + 1; j < generated.Length; j++)
                    {
                        var b = generated[j];
                        Check(a.X + a.Width <= b.X || b.X + b.Width <= a.X ||
                            Math.Abs(a.Y - b.Y) > (a.Height + b.Height) / 2, "overlapping water");
                    }
                }
            }
            Check(lanes.All(count => count >= 200), "all three lanes remain available across seeds");
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
                var first = new WaterPlatform(101, 200, 200, 300, id) { IsRoute = false };
                var second = new WaterPlatform(102, 200, 50, 300, id) { IsRoute = false };
                var rescue = new WaterPlatform(103, 200, -100, 300, 1-id) { IsRoute = false };
                w.Platforms.AddRange([first, second, rescue]);
                int beeps = 0; w.Sound = sound => { if (sound == "reject") beeps++; };
                f.Platform = -1; f.Position = new(350, 250); f.Velocity = new(0,-500);
                w.Step(.02f, Idle);
                Check(f.FallingOnly && f.Platform == -1 && f.Position.Y < first.Y-first.Height/2 && beeps == 1, $"first id={id} y={f.Position.Y} beeps={beeps} platform={f.Platform}");
                float gauge = f.Gauge; w.Launch(f, 50, true);
                Check(f.Velocity.Y < 0 && f.Gauge == gauge);
                f.Position = new(350, 100); f.Velocity = new(0,-500);
                w.Step(.02f, Idle);
                Check(f.FallingOnly && f.Platform == -1 && beeps == 2 && second.Color == id, $"second id={id} beeps={beeps} alive={f.Alive} y={f.Position.Y} platform={f.Platform} falling={f.FallingOnly} color={second.Color}");
                f.Position = new(350, -50); f.Velocity = new(0,-500);
                w.Step(.02f, Idle);
                Check(!f.FallingOnly && f.Platform == rescue.Id && rescue.Color == id, $"rescue id={id} y={f.Position.Y} platform={f.Platform}");
                w.Step(.01f, Idle);
                Check(f.Platform == rescue.Id && !f.FallingOnly && beeps == 2, $"after rescue id={id} y={f.Position.Y} platform={f.Platform} beeps={beeps}");
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
        Test("charge bias switches while held and resets for next charge", () =>
        {
            var w = World(); var f = w.Players[0];
            w.Step(.1f, [new(Vector2.Zero, true, false, true, -1), new()]);
            Check(f.Direction.X < 0 && f.Direction.Y > .8f);
            w.Step(.2f, [new(Vector2.Zero, false, false, true, 1), new()]);
            Check(f.Direction.X > 0 && f.Direction.Y > .8f);
            w.Step(.01f, [new(Vector2.Zero, false, true, false), new()]);
            Check(f.Jumping && f.Velocity.X > 0 && f.Velocity.Y > 0);
            f.Jumping=false; f.Platform=0; f.Position=new(350,0);
            w.Step(.01f, [new(Vector2.Zero, true, false, true), new()]);
            Check(f.Direction == Vector2.UnitY);
        });
        Test("both players move while charging and preserve upward or diagonal launch", () =>
        {
            for (int id=0; id<2; id++) for (int bias=-1; bias<=1; bias++)
            {
                var w=World(); w.Platforms[0].Color=id; foreach(var f0 in w.Players) f0.Alive=f0.Id==id;
                var f=w.Players[id]; var origin=f.Position;
                Control[] controls=[new(),new()];
                controls[id]=new(Vector2.UnitX,true,false,true,bias); w.Step(.1f,controls);
                controls[id]=new(Vector2.UnitX,false,false,true);
                for(int i=0;i<9;i++) w.Step(.1f,controls);
                Check(f.Charge==50 && Math.Abs(f.Position.X-origin.X-95)<.01f && f.Position.Y==origin.Y);
                controls[id]=new(Vector2.UnitX,false,true,false); w.Step(.01f,controls);
                Check(Math.Sign(f.Velocity.X)==bias && f.Velocity.Y>0 && Math.Abs(f.Gauge-50)<.01f);
            }
        });
        Test("air steering works beyond two seconds and during descent", () =>
        {
            var w=World(); var f=w.Players[0];
            w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            f.Direction=Vector2.UnitY; w.Launch(f,100,true);
            for(int i=0;i<60;i++) w.Step(1f/120,[new(Vector2.UnitX,false,false,false),new()]);
            Check(f.Velocity.X>170 && f.Velocity.Y>0);
            float vy=f.Velocity.Y;
            for(int i=0;i<120;i++) w.Step(1f/120,[new(-Vector2.UnitX,false,false,false),new()]);
            Check(f.Velocity.X<0 && Math.Abs(f.Velocity.Y-(vy-260))<.1f);
            for(int i=0;i<140;i++) w.Step(1f/120,Idle);
            float vx=f.Velocity.X; Check(f.Velocity.Y<0);
            w.Step(.1f,[new(Vector2.UnitX,false,false,false),new()]);
            Check(f.Velocity.X>vx);
        });
        Test("separate parry key does not begin charge", () =>
        {
            var w=World(true); var f=w.Players[0];
            w.Step(.01f,[new(Vector2.Zero,false,false,false,0,true),new()]);
            Check(f.Parry>0 && !f.Charging && f.Charge==0);
        });
        Test("new bindings use movement arrows and separate jump keys", () =>
        {
            var s=new GameSettings();
            Check(s.Player1Keys[4]=="Space" && s.Player2Keys[4]=="OemCloseBrackets");
            foreach(var key in s.Player1Keys.Concat(s.Player2Keys).Concat(s.ParryKeys))
                Check(Enum.TryParse<System.Windows.Forms.Keys>(key,out _));
        });
        Test("flight gauge drain follows actual distance for both fish and is frame-rate independent", () =>
        {
            for (int id=0; id<2; id++) foreach (int steps in new[]{10,120})
            {
                var w=World(true); foreach(var fish in w.Players) fish.Alive=fish.Id==id;
                w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
                var f=w.Players[id]; f.Position=new(300,0); f.Platform=-1; f.Jumping=true;
                f.Velocity=new(60,80); f.Gauge=50;
                for(int i=0;i<steps;i++) w.Step(1f/steps,Idle);
                Check(Math.Abs(f.Gauge-49)<.002f, $"distance charge {f.Gauge}");
                f.Position=new(300,0); f.Velocity=new(120,160); f.Gauge=50;
                for(int i=0;i<steps;i++) w.Step(1f/steps,Idle);
                Check(Math.Abs(f.Gauge-48)<.002f);
            }
        });
        Test("stationary and wall-clipped flight do not spend movement gauge", () =>
        {
            var w=World(true); w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            var f=w.Players[0]; f.Platform=-1; f.Jumping=true; f.Gauge=50; f.Position=new(940,0);
            w.Step(.1f,Idle); Check(f.Gauge==50);
            f.Velocity=new(100,0); w.Step(.1f,Idle); Check(f.Gauge==50);
        });
        Test("flight drain clamps to zero and does not disable steering", () =>
        {
            var w=World(true); w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            var f=w.Players[0]; f.Platform=-1; f.Jumping=true; f.Gauge=.01f; f.Velocity=new(0,100);
            w.Step(.1f,[new(Vector2.UnitX,false,false,false),new()]);
            Check(f.Gauge==0 && f.Velocity.X>0);
            float vx=f.Velocity.X;
            w.Step(.1f,[new(-Vector2.UnitX,false,false,false),new()]);
            Check(f.Gauge==0 && f.Velocity.X<vx);
        });
        Test("landing charges only flight distance then stops flight drain", () =>
        {
            var w=World(true); w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            w.Platforms.Add(new(101,200,0,300,1){Height=100,IsRoute=false});
            var f=w.Players[0]; f.Platform=-1; f.Jumping=true; f.Position=new(350,100);
            f.Velocity=new(0,-1000); f.Gauge=50;
            w.Step(.1f,Idle); Check(f.Platform==101 && Math.Abs(f.Gauge-49.5f)<.001f);
            float gauge=f.Gauge; w.Step(.01f,Idle); Check(f.Gauge==gauge);
        });
        Test("charging has identical idle and swim physics for both fish and water colors", () =>
        {
            for(int id=0;id<2;id++) for(int color=0;color<2;color++)
            foreach(float gauge in new[]{0f,50f})
            {
                var normal=World(); var charging=World();
                foreach(var w in new[]{normal,charging})
                {
                    foreach(var fish in w.Players) fish.Alive=fish.Id==id;
                    w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
                    w.Platforms.Add(new(101,20,0,920,color){Height=2000,IsRoute=false});
                    var f=w.Players[id]; f.Position=new(480,0); f.Platform=101; f.Gauge=gauge;
                }
                for(int i=0;i<120;i++)
                {
                    // Idle sink, lateral swim, release/delay, reverse, dive, then idle again.
                    Vector2 move=i<20?Vector2.Zero:i<40?Vector2.UnitX:i<50?Vector2.Zero:
                        i<70?-Vector2.UnitX:i<90?-Vector2.UnitY:Vector2.Zero;
                    Control[] a=[new(),new()], b=[new(),new()];
                    a[id]=new(move,false,false,false);
                    b[id]=new(move,i==0,false,true,-1);
                    normal.Step(1f/120,a); charging.Step(1f/120,b);
                    var nf=normal.Players[id]; var cf=charging.Players[id];
                    Check(Vector2.Distance(nf.Position,cf.Position)<.0001f && Vector2.Distance(nf.Velocity,cf.Velocity)<.0001f);
                    Check(Math.Abs(nf.Gauge-cf.Gauge)<.0001f && nf.SwimDelay==cf.SwimDelay && nf.SwimTime==cf.SwimTime);
                    Check(cf.Charging && cf.Charge<=cf.Gauge && cf.Direction.X<0);
                    if(i==19) Check(cf.Position.Y<0 && cf.Velocity.Y<0);
                }
            }
        });
        Test("idle charge falls out of water and cancels without a midair jump", () =>
        {
            var w=World(); w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            w.Platforms.Add(new(101,200,0,300,0){IsRoute=false});
            var f=w.Players[0]; f.Position=new(350,0); f.Platform=101;
            w.Step(1f/120,[new(Vector2.Zero,true,false,true),new()]);
            for(int i=0;i<360;i++) w.Step(1f/120,[new(Vector2.Zero,false,false,true),new()]);
            Check(f.Position.Y < -47.5f && f.Platform==-1 && !f.Charging && f.Charge==0);
            w.Step(1f/120,[new(Vector2.Zero,false,true,false),new()]); Check(!f.Jumping && f.Velocity.Y<0);
        });
        Test("leaving water during charge cancels airborne charge and release", () =>
        {
            var w=World(true); w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            w.Platforms.Add(new(101,200,0,130,0){IsRoute=false});
            var f=w.Players[0]; f.Platform=101; f.Position=new(337,0);
            w.Step(.1f,[new(Vector2.UnitX,true,false,true),new()]);
            Check(f.Platform==-1 && !f.Charging && f.Charge==0);
            w.Step(.1f,[new(Vector2.Zero,false,true,false),new()]);
            Check(!f.Jumping && f.Gauge==100);
        });
        Test("residents retain water in exit margin and can jump upward from below surface", () =>
        {
            for(int id=0;id<2;id++)
            {
                var w=World(); foreach(var f0 in w.Players) f0.Alive=f0.Id==id;
                w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
                var water=new WaterPlatform(101,200,0,300,id){Height=100,IsRoute=false}; w.Platforms.Add(water);
                var f=w.Players[id]; f.Platform=101; f.Position=new(350,-55);
                Control[] c=[new(),new()]; c[id]=new(Vector2.Zero,true,false,true);
                w.Step(.1f,c); Check(f.Charging && f.Platform==101 && f.Velocity.Y<0);
                f.Charge=50; c[id]=new(Vector2.Zero,false,true,false); w.Step(.01f,c);
                Check(f.Jumping && f.LaunchPlatform==101);
                for(int i=0;i<25;i++) w.Step(1f/120,Idle);
                Check(f.Jumping && !f.FallingOnly && f.Position.Y>-50);
            }
        });
        Test("exit padding does not enlarge airborne water entry or same-color rejection", () =>
        {
            var w=World(true); w.Platforms.Clear(); w.Platforms.Add(new(100,0,10000,960,0));
            w.Platforms.Add(new(101,200,0,300,0){Height=100,IsRoute=false});
            var f=w.Players[0]; f.Platform=-1; f.Jumping=true; f.Position=new(350,60);
            w.Step(.01f,Idle); Check(f.Platform==-1 && !f.FallingOnly);
            f.Velocity.Y=-200; w.Step(.1f,Idle); Check(f.FallingOnly && f.Platform==-1);
            w.Step(.01f,Idle); Check(f.Platform==-1 && f.FallingOnly);
        });
        Test("water exit grace is small and bounded on every edge", () =>
        {
            var water = new WaterPlatform(101,200,0,300,0){Height=180};
            var settings = new GameSettings();
            Check(water.ContainsResident(new(193,0),settings));
            Check(!water.ContainsResident(new(191,0),settings));
            Check(water.ContainsResident(new(507,0),settings));
            Check(!water.ContainsResident(new(509,0),settings));
            Check(water.ContainsResident(new(350,95),settings));
            Check(!water.ContainsResident(new(350,97),settings));
            Check(water.ContainsResident(new(350,-101),settings));
            Check(!water.ContainsResident(new(350,-103),settings));
        });
        Test("generated depth varies independently of width while keeping route gaps", () =>
        {
            var depth=new HashSet<int>(); bool narrowDeep=false,wideShallow=false;
            for(int seed=0;seed<100;seed++)
            foreach(var water in new GameWorld(new(){Seed=seed}).Platforms.Where(p=>p.Id!=0))
            {
                Check(water.Height>=80 && water.Height<=260);
                depth.Add((int)water.Height);
                narrowDeep |= water.Width<=210 && water.Height>=210;
                wideShallow |= water.Width>=430 && water.Height<=110;
            }
            Check(depth.Count>50 && narrowDeep && wideShallow);
        });
        Test("up and down swimming use base pose for both fish including while charging", () =>
        {
            for(int id=0;id<2;id++) foreach(bool charge in new[]{false,true})
            foreach(float vertical in new[]{-1f,1f})
            {
                var w=World(); foreach(var fish in w.Players) fish.Alive=fish.Id==id;
                w.Platforms[0].Color=id;
                var f=w.Players[id]; f.Gauge=50; var origin=f.Position;
                Control[] c=[new(),new()]; c[id]=new(new Vector2(0,vertical),charge,false,charge);
                w.Step(.1f,c);
                Check(Math.Abs(f.Position.Y-origin.Y-vertical*9.5f)<.001f && f.Position.X==origin.X);
                Check(f.UsesBasicSwimPose && f.Gauge>50 && f.Charging==charge);
                c[id]=new(Vector2.UnitX,false,false,charge); w.Step(.1f,c);
                Check(!f.UsesBasicSwimPose && f.Velocity.X>0);
            }
        });
        Test("jump bias is proportional to held duration, capped and reversible", () =>
        {
            var w=World(true); var f=w.Players[0];
            w.Step(.25f,[new(Vector2.Zero,true,false,true,1),new()]);
            Check(Math.Abs(f.JumpBiasAmount-.25f)<.001f && Math.Abs(f.Direction.X-MathF.Sin(MathF.PI/24))<.001f);
            w.Step(.75f,[new(Vector2.Zero,false,false,true,1),new()]);
            Check(f.JumpBiasAmount==1 && Math.Abs(f.Direction.X-.5f)<.001f);
            w.Step(.5f,[new(Vector2.Zero,false,false,true,1),new()]); Check(f.JumpBiasAmount==1);
            w.Step(1f,[new(Vector2.Zero,false,false,true,-1),new()]); Check(f.Direction==Vector2.UnitY);
            w.Step(.5f,[new(Vector2.Zero,false,false,true,-1),new()]); Check(f.Direction.X<0);
            float bias=f.JumpBiasAmount;
            w.Step(.1f,[new(Vector2.UnitX,false,false,true),new()]); Check(f.JumpBiasAmount==bias);
        });
        GameFeatureTests.Run(Test);
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results.Append($"{results.Count - failed}/{results.Count} passed"));

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "settings-defaults.json"), JsonSerializer.Serialize(new GameSettings(), new JsonSerializerOptions
        {
            WriteIndented = true
        }
));

        return failed == 0 ? 0 : 1;

    }

}
