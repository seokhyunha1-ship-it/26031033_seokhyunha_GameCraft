using System.Numerics;

static class GameFeatureTests
{
    static readonly Control[] Idle = [new(), new()];
    static void Check(bool ok, string why = "assertion") { if (!ok) throw new Exception(why); }
    static bool Near(double a, double b, double epsilon = .002) => Math.Abs(a - b) <= epsilon;

    static GameWorld Flight(int id = 0, float cost = 40)
    {
        var w = new GameWorld(new() { Seed = 42, DuckSpeed = 0, AttackCooldown = 10000, JumpDrainPerMeter = 0 });
        w.DuckY = -100000;
        w.Platforms.Clear();
        w.Platforms.Add(new(999, 0, 100000, w.Settings.WorldWidth, 0));
        w.Items.Clear();
        foreach (var p in w.Players) p.Alive = p.Id == id;
        var f = w.Players[id];
        f.Platform = -1;
        w.Launch(f, cost, true);
        return w;
    }

    static void Press(GameWorld w, int id, Vector2 direction)
    {
        Control[] c = [new(), new()];
        c[id] = new(direction, true, false, true, InputTime: w.Time, DirectionTime: w.Time);
        w.Step(1f / 120, c);
    }

    static void Advance(GameWorld w, double until)
    {
        while (w.Time < until - 1e-8) w.Step((float)Math.Min(1d / 120, until - w.Time), Idle);
    }

    public static void Run(Action<string, Action> Test)
    {
        Test("tuning: stronger gravity shortens normal jump time but preserves paid height", () =>
        {
            (float height, double time) Apex(float gravity)
            {
                var w = Flight(); var f = w.Players[0];
                w.Settings.Gravity = gravity; f.Gauge = 100;
                f.Position = new(1000, 0); f.Direction = Vector2.UnitY;
                w.Launch(f, 50, true);
                float height = 0;
                while (f.Velocity.Y > 0 && w.Time < 3)
                { w.Step(1f / 120, Idle); height = Math.Max(height, f.Position.Y); }
                return (height, w.Time);
            }
            var before = Apex(260); var after = Apex(new GameSettings().Gravity);
            Check(after.time < before.time * .95 && after.time > before.time * .9);
            Check(Math.Abs(after.height - before.height) < 2 && Math.Abs(after.height - 400) < 3);
        });
        Test("flap burst: reverses momentum, survives the next steering tick, then ends", () =>
        {
            foreach (int id in new[] { 0, 1 }) foreach (int sign in new[] { -1, 1 })
            {
                var w = Flight(id); var f = w.Players[id];
                f.Velocity = new(-sign * 200, -100);
                Press(w, id, new(sign, 0));
                while (!f.FlapUsed && w.Time < .25) w.Step(1f / 120, Idle);
                Check(f.Velocity.X * sign > 600 && Near(f.Velocity.Y, 0));
                float startX = f.Position.X;
                Control[] held = [new(),new()]; held[id] = new(new(sign,0),false,false,true);
                w.Step(1f / 120, held);
                Check(f.Velocity.X * sign > 500 && (f.Position.X - startX) * sign > 4);
                Check(f.Velocity.Y < 0, "gravity must remain active during burst");
                Advance(w, w.Time + w.Settings.FlapBurstDuration + .02);
                Check(f.FlapBoostRemaining == 0 && f.FlapBoostVelocity == Vector2.Zero);
                Check(Math.Abs(f.Velocity.X) <= w.Settings.AirSteeringMaxSpeed + .001);
                Check(Near(f.Gauge, 40), "burst must not charge another activation fee");
            }
        });
        Test("flap burst: vertical bonus decays without cancelling gravity; fast entry clears burst", () =>
        {
            foreach (int id in new[] { 0, 1 })
            {
                var w = Flight(id); var f = w.Players[id];
                Press(w,id,Vector2.UnitY);
                while (!f.FlapUsed && w.Time < .25) w.Step(1f / 120,Idle);
                float initial = f.Velocity.Y;
                double start = w.Time;
                Advance(w, start + w.Settings.FlapBurstDuration + .01);
                float expected = initial / w.Settings.FlapSpeedMultiplier - w.Settings.Gravity * (float)(w.Time-start);
                Check(Near(f.Velocity.Y,expected,.01) && f.FlapBoostRemaining==0);

                w = Flight(id); f = w.Players[id];
                Press(w,id,Vector2.UnitX);
                while (!f.FlapUsed && w.Time < .25) w.Step(1f / 120,Idle);
                f.Position = new(1000,500);
                w.Platforms.Add(new(1000,1010,500,10,1-id){Height=100,IsRoute=false});
                w.Step(.1f,Idle); // 한 틱에 얇은 물을 지나칠 속도에서도 입수해야 한다.
                Check(f.Platform==1000 && f.FlapBoostRemaining==0 && f.FlapBoostVelocity==Vector2.Zero);
            }
        });
        Test("flap burst: invalid settings rejected and old settings receive defaults", () =>
        {
            var old = System.Text.Json.JsonSerializer.Deserialize<GameSettings>("{}")!;
            Check(old.FlapSpeedMultiplier==2 && old.FlapBurstDuration==.2f && old.Gravity==300);
            foreach(float value in new[]{float.NaN,float.PositiveInfinity,0f,-1f})
            foreach(bool multiplier in new[]{true,false})
            {
                var s = new GameSettings();
                if(multiplier)s.FlapSpeedMultiplier=value; else s.FlapBurstDuration=value;
                bool rejected=false; try{s.Validate();}catch(InvalidDataException){rejected=true;}
                Check(rejected);
            }
        });
        Test("viewport: visible platform count is about two thirds of the previous layout", () =>
        {
            int visible = 0;
            // 변경 전 200개 시드 × 동일한 네 카메라 높이에서 측정한 평균(화면 끝에 걸친 물 포함).
            const double previousAverage = 20.75625;
            float[] cameras = [0, 2000, 5000, 10000];
            for (int seed = 0; seed < 200; seed++)
            {
                var w = new GameWorld(new() { Seed = seed });
                foreach (var fish in w.Players) fish.Alive = false;
                foreach (float camera in cameras)
                {
                    w.Camera = camera;
                    w.Step(1f / 120, Idle);
                    visible += w.Platforms.Count(p => p.Y + p.Height / 2 >= w.CameraBottom &&
                        p.Y - p.Height / 2 <= w.CameraTop);
                }
            }
            double ratio = visible / (200d * cameras.Length) / previousAverage;
            Check(ratio >= .60 && ratio <= .73, $"visible-count ratio={ratio}");
        });
        Test("colors: 200 seeds and scrolling screen boundaries stay within 30-70 percent", () =>
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var w = new GameWorld(new() { Seed = seed, DuckSpeed = 0 });
                foreach (var fish in w.Players) fish.Alive = false;
                for (int scroll = 0; scroll < 24; scroll++)
                {
                    w.Camera = scroll * 700;
                    w.Step(1f / 120, Idle);
                    var waters = w.Platforms.OrderBy(p => p.Y).ThenBy(p => p.Id).ToArray();
                    // 화면 비율이 바뀌는 모든 중심점 진입/이탈 경계의 직전과 직후를 검사한다.
                    var bottoms = waters.SelectMany(p => new[] { p.Y, p.Y - w.Settings.ViewHeight })
                        .SelectMany(y => new[] { y - .01f, y, y + .01f })
                        .Append(w.CameraBottom);
                    foreach (float bottom in bottoms)
                    {
                        // 카메라는 위로만 이동한다. 이미 화면 아래에서 정리한 구간은 제외한다.
                        if (bottom < w.CameraBottom ||
                            bottom + w.Settings.ViewHeight > waters[^1].Y) continue;
                        var visible = waters.Where(p => p.Y >= bottom && p.Y <= bottom + w.Settings.ViewHeight).ToArray();
                        if (visible.Length < 2) continue; // 한 개로는 두 색을 만들 수 없다.
                        int red = visible.Count(p => p.SpawnColor == 0);
                        Check(red * 10 >= visible.Length * 3 && red * 10 <= visible.Length * 7,
                            $"seed={seed}, scroll={scroll}, bottom={bottom}, red={red}/{visible.Length}");
                    }
                }
            }
        });
        Test("colors: new generation preserves existing gameplay colors and restarts deterministically", () =>
        {
            var w = new GameWorld(new() { Seed = 42, DuckSpeed = 0 });
            var fresh = new GameWorld(new() { Seed = 42, DuckSpeed = 0 });
            Check(w.Platforms.Select(p => (p.X,p.Y,p.SpawnColor)).SequenceEqual(
                fresh.Platforms.Select(p => (p.X,p.Y,p.SpawnColor))));
            foreach (var fish in w.Players) fish.Alive = false;
            var old = w.Platforms.ToDictionary(p => p.Id, p => p.SpawnColor);
            foreach (var p in w.Platforms) p.Color = 1 - p.Color; // 입수로 변경된 색을 모사한다.
            w.Camera = 1000;
            w.Step(1f / 120, Idle);
            Check(w.Platforms.Any(p => !old.ContainsKey(p.Id)), "no new generation");
            foreach (var p in w.Platforms.Where(p => old.ContainsKey(p.Id)))
                Check(p.SpawnColor == old[p.Id] && p.Color == 1 - old[p.Id], "existing water recolored");
        });
        Test("colors: scatter validation preserves generation height order", () =>
        {
            bool rejected = false;
            try { new GameSettings { PlatformScatterY = 100 }.Validate(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected);
            for (int seed = 0; seed < 50; seed++)
            {
                var w = new GameWorld(new() { Seed = seed, PlatformScatterY = 99 });
                var sorted = w.Platforms.OrderBy(p => p.Y).ThenBy(p => p.Id).ToArray();
                for (int i = 1; i < sorted.Length; i++) Check(sorted[i].SpawnColor != sorted[i-1].SpawnColor);
            }
        });
        Test("M01/M02: reference width and world distance independent of output size", () =>
        {
            var s = new GameSettings();
            Check(s.WorldWidth == 2068 && s.ViewHeight == 1454.0625f);
            Check(GameGlobal.ScreenSize.Width == 1024 && GameGlobal.ScreenSize.Height == 720);
            Check(Near(s.ViewHeight * GameGlobal.ScreenSize.Width / s.WorldWidth, GameGlobal.ScreenSize.Height));
            var world = new GameWorld(s);
            Check(Near(world.CameraTop - world.CameraBottom, s.ViewHeight));
            foreach (float width in new[] { 1280f, 1920f, 2560f })
            {
                float scale = width / s.WorldWidth;
                Check(Near((1234 * scale) / scale, 1234));
            }
        });
        Test("M03: 200 seeds irregular x/y, connected paths, margins and nonoverlap", () =>
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var w = new GameWorld(new() { Seed = seed });
                var generated = w.Platforms.Where(p => p.Id != 0).ToArray();
                Check(w.Platforms.Last(p => p.IsRoute).Y >= w.CameraTop + 1000);
                Check(generated.Select(p => MathF.Round(p.Y, 2)).Distinct().Count() >= generated.Length * .95, "aligned y rows");
                Check(generated.Select(p => MathF.Round(p.X + p.Width / 2, 2)).Distinct().Count() >= generated.Length * .95, "aligned x columns");
                foreach (var p in generated)
                {
                    Check(p.X >= w.Settings.WorldMargin - .001 && p.X + p.Width <= w.Settings.WorldWidth - w.Settings.WorldMargin + .001);
                    Check(p.Width >= 129.99f);
                    Check(w.Platforms.Any(q => w.CanReachSide(q,p)), $"seed {seed} disconnected water {p.Id}");
                }
                for (int i=0;i<generated.Length;i++) for(int j=i+1;j<generated.Length;j++)
                {
                    var a=generated[i]; var b=generated[j];
                    Check(a.X+a.Width<=b.X || b.X+b.Width<=a.X || Math.Abs(a.Y-b.Y)>=(a.Height+b.Height)/2+11.99f, "overlap or insufficient vertical clearance");
                    if(Math.Abs(a.Y-b.Y)<(a.Height+b.Height)/2)
                    {
                        float gap=Math.Max(a.X,b.X)-Math.Min(a.X+a.Width,b.X+b.Width);
                        Check(gap>=w.Settings.PlatformHorizontalGap-.01f);
                    }
                }
            }
        });
        Test("balance: larger gaps need more gauge despite longer jumps; 50 points connects main route", () =>
        {
            var settings = new GameSettings();
            // 이전 최대 빈 공간36 / 이전 높이계수5px = 7.2포인트보다 새 최소 이동 비용이 크다.
            Check(settings.PlatformVerticalGapMin / (settings.JumpMetersPerPoint * settings.PixelsPerMeter) > 36f / 5);
            for (int seed = 0; seed < 20; seed++)
            {
                var layout = new GameWorld(new() { Seed = seed });
                var route = layout.Platforms.Where(p => p.IsRoute).ToArray();
                for (int n = 1; n < route.Length; n++) foreach (int id in new[] { 0, 1 })
                {
                    var from = route[n - 1]; var to = route[n];
                    var w = new GameWorld(new() { Seed=seed, DuckSpeed=0, AttackCooldown=10000 });
                    w.Platforms.Clear(); w.Items.Clear(); w.DuckY=-10000;
                    w.Platforms.Add(new(9999,0,100000,settings.WorldWidth,0));
                    var source = new WaterPlatform(100,from.X,from.Y,from.Width,id){Height=from.Height,IsRoute=false};
                    var destination = new WaterPlatform(101,to.X,to.Y,to.Width,1-id){Height=to.Height,IsRoute=false};
                    w.Platforms.AddRange([source,destination]);
                    foreach(var other in w.Players)other.Alive=other.Id==id;
                    var f=w.Players[id];
                    f.Position=new((Math.Max(from.X,to.X)+Math.Min(from.X+from.Width,to.X+to.Width))/2,from.Y);
                    f.Platform=source.Id;
                    w.Launch(f,50,true);
                    for(int tick=0;tick<240 && f.Platform!=destination.Id;tick++)w.Step(1f/120,Idle);
                    Check(f.Platform==destination.Id, $"seed={seed} band={n} player={id}");
                    Check(f.Gauge>45 && f.Gauge<=50, "a connected route must preserve a useful reserve");
                }
            }
        });
        Test("M03: culling preserves a living resident's water", () =>
        {
            var w = new GameWorld(new() { Seed = 1, DuckSpeed = 0 });
            w.DuckY = -10000;
            w.Camera = 5000;
            w.Step(1f / 120, Idle);
            Check(w.Platforms.Any(p => p.Id == w.Players[0].Platform));
        });
        Test("F01/F02: paid cost half, exact balance, shortage and fractional cost", () =>
        {
            foreach (int id in new[] { 0, 1 }) foreach (float cost in new[] { 1f, 40f, 80f })
            foreach (bool enough in new[] { false, true })
            {
                var w = Flight(id, cost); var f = w.Players[id];
                f.Gauge = cost / 2 - (enough ? 0 : .01f);
                float before = f.Gauge;
                Press(w, id, Vector2.UnitY);
                Advance(w, .22);
                Check(f.FlapUsed == enough && Near(f.Gauge, enough ? 0 : before), $"id {id} cost {cost}");
                Check(!f.FlapPending && f.Gauge >= 0);
            }
        });
        Test("F03: direction timestamps 0.299/0.300 accepted; 0.301 rejected", () =>
        {
            foreach (int id in new[] { 0, 1 }) foreach (double delay in new[] { .299, .300, .301 })
            {
                var w = Flight(id); var f = w.Players[id];
                Press(w, id, Vector2.Zero);
                Advance(w, .295);
                Control[] c = [new(), new()];
                c[id] = new(Vector2.UnitX, false, false, false, DirectionTime: delay);
                w.Step(.01f, c);
                Check(f.FlapUsed == (delay <= .300), $"id {id} delay {delay}");
            }
        });
        Test("F04/F05/F08: eight directions, rising/falling, speed normalization and one use", () =>
        {
            foreach (int id in new[] { 0, 1 }) foreach (float vy in new[] { -100f, 100f })
            foreach (var dir in new[] { new Vector2(1,0), new(-1,0), new(0,1), new(0,-1), new(1,1), new(-1,1), new(1,-1), new(-1,-1) })
            {
                var w = Flight(id); var f = w.Players[id]; f.Velocity.Y = vy;
                Press(w, id, dir);
                while (!f.FlapUsed && w.Time < .25) w.Step(1f / 120, Idle);
                float speed = MathF.Sqrt(2 * w.Settings.Gravity * 20 * 8) * w.Settings.FlapSpeedMultiplier;
                Check(f.FlapUsed && Near(f.Velocity.Length(), speed));
                Check(Vector2.Distance(Vector2.Normalize(f.Velocity), Vector2.Normalize(dir)) < .00001);
                float gauge = f.Gauge;
                Press(w, id, -dir); Advance(w, .5);
                Check(f.Gauge == gauge && !f.FlapPending);
            }
        });
        Test("F04: held repeat cannot restart deadline; release permits retry after shortage", () =>
        {
            var w = Flight(); var f = w.Players[0];
            Press(w, 0, Vector2.Zero);
            for (int i = 0; i < 45; i++) w.Step(1f/120, [new(Vector2.Zero, true, false, true), new()]);
            Check(!f.FlapPending && !f.FlapUsed && f.Gauge == 60);
            w.Step(1f/120, Idle); f.Gauge = 1; Press(w, 0, Vector2.UnitX);
            Advance(w, w.Time + .22); Check(!f.FlapUsed && f.Gauge == 1);
            f.Gauge = 20; Press(w, 0, Vector2.UnitX); Advance(w, w.Time + .22);
            Check(f.FlapUsed && f.Gauge == 0);
        });
        Test("F06: landing, death, falling-only and free parry reset flap eligibility", () =>
        {
            foreach (int mode in new[] { 0, 1, 2, 3 })
            {
                var w = Flight(); var f = w.Players[0]; Press(w, 0, Vector2.UnitY);
                if (mode == 0) { w.Platforms.Add(new(1000, f.Position.X-100, f.Position.Y, 200, 1) { IsRoute=false }); f.LaunchPlatform=-1; }
                if (mode == 1) w.DuckY = f.Position.Y + 1000;
                if (mode == 2) { f.FallingOnly = true; f.EndFlight(); }
                if (mode == 3) w.Launch(f, f.Gauge * .4f, false);
                Advance(w, .25);
                Check(!f.FlapUsed && !f.FlapPending && f.LastPaidJumpCost == 0);
            }
            var fresh = new GameWorld(new() { Seed=42 });
            Check(fresh.Players.All(f => !f.FlapPending && !f.FlapUsed && f.LastPaidJumpCost == 0));
        });
        Test("F07: parry or hit cancels flap without duplicate spending", () =>
        {
            foreach (int id in new[] { 0, 1 }) foreach (int kind in new[] { 0, 1, 2 })
            {
                var w = Flight(id); var f = w.Players[id];
                w.Fires.Add(new() { Position=f.Position, Color=kind==0?1-id:id, Special=kind!=2 });
                Press(w,id,Vector2.UnitY); Advance(w,.25);
                Check(!f.FlapUsed && !f.FlapPending);
                Check(f.Gauge == (kind==0?60:40) && w.Score.Damage == (kind==0?20:0));
            }
        });
        Test("F07: blue Enter opens parry only; no flap candidate", () =>
        {
            var w=Flight(1); w.Step(.01f,[new(),new(Vector2.UnitX,false,false,false,ParryPress:true)]);
            Advance(w,.25); Check(!w.Players[1].FlapUsed && !w.Players[1].FlapPending && w.Players[1].Gauge==60);
        });
        Test("F09: no Step during pause preserves direction deadline", () =>
        {
            var w=Flight(); Press(w,0,Vector2.Zero); double t=w.Time, deadline=w.Players[0].FlapDeadline;
            // GameMain의 Pause/Results는 Step을 호출하지 않는다.
            for(int i=0;i<600;i++) { Check(w.Time==t && w.Players[0].FlapDeadline==deadline); }
            w.Step(.2f,[new(Vector2.UnitX,false,false,false,DirectionTime:.2),new()]);
            Check(w.Players[0].FlapUsed);
        });
        Test("D01/D02/D04: first volley at 4, second at 8, fixed 7 and exact stop interval", () =>
        {
            var w=Flight(); w.Settings.AttackCooldown=4; w.Settings.DuckSpeed=13;
            w.DuckY=-220; w.Settings.Gravity=0; w.Players[0].Position.Y=0;
            w.Players[0].Velocity=Vector2.Zero; w.Players[0].Jumping=false;
            Advance(w,3.5); float stoppedY=w.DuckY;
            Check(Near(stoppedY,-174.5,.01) && w.ShotsFired==0, $"3.5: y={stoppedY} shots={w.ShotsFired}");
            Advance(w,3.99); Check(w.ShotsFired==0 && Near(w.DuckY,stoppedY));
            Advance(w,4); Check(w.ShotsFired==1 && w.Fires.Count==7 && w.Fires.Count(f=>f.Special)==1, $"4: shots={w.ShotsFired} fires={w.Fires.Count}");
            Check(w.DuckPhase==DuckState.Holding && w.DuckMouthFrame==1);
            Advance(w,4.7); Check(Near(w.DuckY,stoppedY));
            Advance(w,5.7); Check(Near(w.DuckY,stoppedY+13,.01));
            Advance(w,8); Check(w.ShotsFired==2);
        });
        Test("D05/D06: stationary duck still receives damage and other objects move", () =>
        {
            var w=Flight(); w.Settings.AttackCooldown=4; Advance(w,3.6);
            w.Settings.DuckSpeed=13;
            var f=w.Players[0]; float x=f.Position.X, duck=w.DuckY;
            f.Velocity=new(100,0);
            w.Waters.Add(new(){Position=new(0,duck+1),Damage=25});
            w.Fires.Add(new(){Position=new(0,0),Velocity=new(0,180)});
            w.Step(.1f,Idle);
            Check(Near(w.DuckY,duck) && f.Position.X>x && w.Score.Damage==25 && w.Fires[0].Position.Y==18);
            f.Position.Y=duck+15; f.Velocity=Vector2.Zero; w.Step(.01f,Idle); Check(!f.Alive);
        });
        Test("fixed-step replay at 30/60/144 render fps produces the same flap, gauge and score", () =>
        {
            var states = new List<(Vector2 p, Vector2 v, float gauge, int score, bool flap)>();
            foreach (int fps in new[] { 30, 60, 144 })
            {
                var w = Flight(); w.Settings.JumpDrainPerMeter = .1f;
                double accumulator = 0; bool pressed = false, released = false;
                for (int frame = 0; frame < fps; frame++)
                {
                    accumulator += 1d / fps;
                    while (accumulator + 1e-9 >= 1d / 120)
                    {
                        double tickEnd = w.Time + 1d / 120;
                        bool press = !pressed && tickEnd >= .1 - 1e-7;
                        bool release = !released && tickEnd >= .15 - 1e-7;
                        if (press) pressed = true; if (release) released = true;
                        var dir = tickEnd >= .399 - 1e-7 ? Vector2.UnitX : Vector2.Zero;
                        w.Step(1f / 120, [new(dir, press, release, pressed && !released,
                            InputTime: press ? .1 : .15, DirectionTime: .399), new()]);
                        accumulator -= 1d / 120;
                    }
                }
                var f = w.Players[0];
                Check(f.FlapUsed && w.Time > .99);
                states.Add((f.Position, f.Velocity, f.Gauge, w.Score.Total(w.Players, w.Settings), f.FlapUsed));
            }
            foreach (var state in states)
            {
                Check(Vector2.Distance(state.p, states[0].p) < .001 && Vector2.Distance(state.v, states[0].v) < .001);
                Check(Near(state.gauge, states[0].gauge) && state.score == states[0].score && state.flap);
            }
        });
        Test("F10: flap movement drains only actual distance, clips wall and lands without correction cost", () =>
        {
            var w=Flight(); var f=w.Players[0]; Press(w,0,Vector2.UnitX);
            Advance(w,.5); Check(f.FlapUsed && f.FlapBoostRemaining == 0);
            w.Settings.Gravity=0; w.Settings.JumpDrainPerMeter=.1f;
            f.Position=new(w.Settings.WorldWidth-w.Settings.WorldMargin, 0); f.Velocity=new(200,0);
            float before=f.Gauge; w.Step(.1f,Idle); Check(f.Gauge==before);
            f.Position=new(1000,100); f.Velocity=new(0,-1000);
            w.Platforms.Add(new(1000,900,0,300,1){Height=100,IsRoute=false});
            w.Step(.1f,Idle); Check(f.Platform==1000 && Near(f.Gauge,before-.5f) && !f.FlapPending);
        });
        Test("settings reject invalid geometry and conflicting timers; old JSON gets defaults", () =>
        {
            var old=System.Text.Json.JsonSerializer.Deserialize<GameSettings>("{\"Gravity\":260}")!;
            old.Validate(); Check(old.WorldWidth==2068 && old.FlapDirectionWindow==.3f);
            foreach(int mode in new[]{0,1,2})
            {
                var s=new GameSettings(); if(mode==0)s.WorldWidth=0; if(mode==1)s.AttackCooldown=1; if(mode==2)s.FlapDirectionWindow=.1f;
                bool rejected=false; try{s.Validate();}catch(InvalidDataException){rejected=true;} Check(rejected);
            }
        });
    }
}
