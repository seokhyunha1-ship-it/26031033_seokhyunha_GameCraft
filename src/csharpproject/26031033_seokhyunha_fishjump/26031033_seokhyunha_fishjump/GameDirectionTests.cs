using System.Numerics;

static class GameDirectionTests
{
    const float Tick = 1f / 120;
    static readonly Control[] Idle = [new(),new()];
    static void Check(bool ok, string why) { if(!ok) throw new Exception(why); }
    static GameWorld Flight(int id=0)
    {
        var w = new GameWorld(new(){Seed=42,DuckSpeed=0,AttackCooldown=10000,JumpDrainPerMeter=0});
        w.DuckY=-100000; w.Platforms.Clear(); w.Items.Clear();
        w.Platforms.Add(new(999,0,100000,w.Settings.WorldWidth,0));
        foreach(var fish in w.Players)fish.Alive=fish.Id==id;
        var f=w.Players[id]; f.Platform=-1; f.Position=new(1000,1000); w.Camera=400; w.Launch(f,40,true);
        return w;
    }
    static void Input(GameWorld w,int id,Vector2 direction,bool press=true,bool parry=false)
    {
        Control[] c=[new(),new()]; c[id]=new(direction,press,false,press,ParryPress:parry,InputTime:w.Time,DirectionTime:w.Time);
        w.Step(Tick,c);
    }
    public static void Run(Action<string,Action> Test)
    {
        RunFireStunUpdate(Test);
        RunParryFeedbackUpdate(Test);
        RunImpactUpdate(Test);
        RunChaseUpdate(Test);
        Test("video: cheaper flap keeps its original speed and marker matches the paid fee",()=>
        {
            foreach(int id in new[]{0,1}) foreach(float ratio in new[]{.25f,.5f})
            {
                var w=Flight(id);var f=w.Players[id];w.Settings.FlapCostRatio=ratio;
                Check(w.ShowFlapCost(f) && w.FlapCost(f)==40*ratio && w.FlapGaugeThreshold(f)==40*ratio,"wrong airborne marker");
                float before=f.Gauge;Input(w,id,Vector2.UnitX);
                Check(before-f.Gauge==40*ratio,"display and deduction disagree");
                Check(Math.Abs(f.Velocity.Length()-Math.Min(w.Settings.FlapMaxSpeed,MathF.Sqrt(300*40*7)*1.6f))<.01,"cheaper fee changed movement");
                Check(!w.ShowFlapCost(f),"used flap still appears available");
                f.EndFlight();Check(!w.ShowFlapCost(f),"landing leaves stale marker");
                f.Charging=true;f.Charge=80;f.Gauge=100;f.FlapMessageTime=0;
                Check(w.ShowFlapCost(f) && w.FlapGaugeThreshold(f)==80*(1+ratio),"charge reserve marker");
                if(ratio==.25f)Check(w.FlapHint(f).Contains("예상 잔여 0"),"exact reserve");
                else Check(w.FlapHint(f).Contains("부족"),"over-100 reserve must be insufficient");
                f.Alive=false;Check(!w.ShowFlapCost(f),"dead fish marker");
            }
        });
        Test("video: invalid flap fee ratios fail validation and legacy settings default to 25 percent",()=>
        {
            var defaults=System.Text.Json.JsonSerializer.Deserialize<GameSettings>("{}")!;
            Check(defaults.FlapCostRatio==.25f,"old settings default");
            foreach(float value in new[]{0f,-1f,1.01f,float.NaN,float.PositiveInfinity})
            {
                defaults.FlapCostRatio=value;bool rejected=false;
                try{defaults.Validate();}catch(InvalidDataException){rejected=true;}
                Check(rejected,"invalid fee accepted");
            }
        });
        Test("video: crossing scrolled screen floor dies before pickups or parry can rescue it",()=>
        {
            foreach(int id in new[]{0,1}) foreach(int frame in new[]{0,1,2})
            {
                var w=Flight(id);var f=w.Players[id];w.Camera=1500;w.Time=frame*.3;
                f.Position.Y=w.CameraBottom+2;f.Velocity=new(0,-1000);f.Parry=.2f;
                w.Items.Add(new(){Position=f.Position,Water=true});
                w.Fires.Add(new(){Position=f.Position,Special=true,Color=1-id});
                w.Step(Tick,Idle);
                Check(!f.Alive && !f.Charging && f.Parry==0 && f.FlightOrigin==FlightOrigin.None,"fell off screen but survived");
                Check(w.Score.Damage==0 && w.Waters.Count==0,"dead fish triggered rescue/pickup");
                Check(w.DuckY+15>=w.CameraBottom-.001f,"duck left behind camera");
            }
        });
        Test("chase: a leading player cannot drag the camera or kill a lower teammate; floor stays lethal",()=>
        {
            var w=Flight();w.Settings.Gravity=0;
            w.Players[0].Position.Y=500;w.Players[0].Velocity=Vector2.Zero;
            var upper=w.Players[1];upper.Alive=true;upper.Platform=-1;upper.Position.Y=4000;
            w.Step(Tick,Idle);
            Check(w.Players[0].Alive && upper.Alive && w.Camera==400,"leading player pulled camera");
            w.Players[0].Position.Y=w.DeathY;
            float camera=w.Camera;upper.Position.Y=w.DeathY+1;upper.Velocity=Vector2.Zero;
            w.Step(Tick,Idle);Check(upper.Alive && w.Camera==camera,"above floor died or camera went backward");
            upper.Position.Y=w.DeathY;w.Step(Tick,Idle);
            Check(w.Players.All(f=>!f.Alive),"exact boundary did not finish game");
        });
        Test("video: duck follows a high camera, cycles mouth, and slowly advances without feedback acceleration",()=>
        {
            var w=Flight();w.Settings.Gravity=0;w.Settings.DuckSpeed=13;w.Settings.DuckSpeedPerStage=0;w.Camera=2000;
            var f=w.Players[0];f.Position.Y=2300;f.Velocity=Vector2.Zero;
            var frames=new HashSet<int>();w.Step(Tick,Idle);float start=w.DuckY, camera=w.Camera;
            for(int i=0;i<240;i++){w.Step(Tick,Idle);frames.Add(w.DuckMouthFrame);}
            Check(frames.SetEquals(new[]{1,2,3}),"mouth stopped animating after scrolling");
            Check(Math.Abs(w.DuckY-start-26)<.03 && Math.Abs(w.Camera-camera-26)<.03,"duck accelerated from camera feedback");
            for(int i=0;i<480;i++)w.Step(Tick,Idle);
            Check(w.Camera>camera && w.Camera-camera<80 && f.Alive,"slow auto-scroll missing or too fast");
        });
        Test("JF-002 immediate 2 players x 8 directions x 4 momentum states; equal distances",()=>
        {
            foreach(int id in new[]{0,1}) foreach(var old in new[]{new Vector2(300,400),new(-300,400),new(300,-400),new(-300,-400)})
            foreach(var dir in new[]{new Vector2(1,0),new(-1,0),new(0,1),new(0,-1),new(1,1),new(-1,1),new(1,-1),new(-1,-1)})
            {
                var w=Flight(id);var f=w.Players[id];f.Velocity=old;var start=f.Position;
                Input(w,id,dir,parry:id==0);
                Check(f.FlapUsed && f.LastFlapActivatedAt<=Tick+1e-7 && f.Parry==0,"extra wait or duplicate parry");
                var normal=Vector2.Normalize(dir);
                Check(Vector2.Dot(Vector2.Normalize(f.Position-start),normal)>.9999,"first tick used old momentum");
                float speed=f.Velocity.Length();
                // 20 ticks total < 0.18s; opposite input must not bend the locked direction.
                for(int n=1;n<20;n++)Input(w,id,-dir,false);
                var delta=f.Position-start;
                Check(Vector2.Dot(Vector2.Normalize(delta),normal)>.9999,"direction bent during lock");
                Check(Math.Abs(delta.Length()-speed*20*Tick)<.02,"diagonal speed mismatch");
                Check(f.Gauge==50,"activation charged more than once");
            }
        });
        Test("JF-002 delayed direction fires on arrival, not the deadline; consumed parry cannot reuse input",()=>
        {
            foreach(int id in new[]{0,1})
            {
                var w=Flight(id);var f=w.Players[id];Input(w,id,Vector2.Zero);
                Check(!f.FlapUsed && f.FlapPending && f.Parry>0,"missing grace/parry");
                Input(w,id,Vector2.UnitX,false);
                Check(f.FlapUsed && f.LastFlapActivatedAt<.03 && f.Parry==0,"late direction was delayed");
                w=Flight(id);f=w.Players[id];
                w.Fires.Add(new(){Position=f.Position,Special=true,Color=1-id});
                Input(w,id,Vector2.Zero);
                Check(w.Score.Damage==20 && !f.FlapPending,"parry did not consume pending input");
                Input(w,id,Vector2.UnitX,false);
                Check(!f.FlapUsed && f.Gauge==60,"consumed input reused");
            }
        });
        Test("JF-002 valid directed input never parries, including insufficient gauge",()=>
        {
            foreach(int id in new[]{0,1}) foreach(bool enough in new[]{true,false})
            {
                var w=Flight(id);var f=w.Players[id];if(!enough)f.Gauge=9;
                w.Fires.Add(new(){Position=f.Position,Special=true,Color=1-id});
                Input(w,id,Vector2.UnitX,parry:id==0);
                Check(w.Score.Damage==0 && f.Parry==0,"directed input silently became parry");
                Check(f.FlapUsed==enough && f.Gauge==(enough?30:0),"fee/hit mismatch");
                if(!enough)Check(f.FlapMessage.Contains("부족"),"missing shortage feedback");
            }
        });
        Test("JF-002 already dead or touching water cannot flap out of resolved contact",()=>
        {
            foreach(int mode in new[]{0,1,2})
            {
                var w=Flight();var f=w.Players[0];
                if(mode==0)w.DuckY=f.Position.Y-15;
                else w.Platforms.Add(new(1000,950,1000,100,mode==1?1:0){IsRoute=false});
                Input(w,0,Vector2.UnitY);
                Check(!f.FlapUsed && f.Gauge==60,"resolved contact charged a flap");
                if(mode==0)Check(!f.Alive,"dead fish revived");
                if(mode==1)Check(f.Platform==1000,"landing not preserved");
                if(mode==2)Check(f.FallingOnly,"own-color rejection lost");
            }
        });
        Test("JF-002 expiry splits the tick and overspeed slows by 600 rather than snapping",()=>
        {
            var w=Flight();var f=w.Players[0];Input(w,0,Vector2.UnitX);
            w.Step(f.FlapLockRemaining,Idle);
            float speed=f.Velocity.X; Check(f.FlapLockRemaining==0 && f.Velocity.Y==0,"lock boundary");
            w.Step(Tick,Idle);
            Check(Math.Abs(f.Velocity.X-(speed-600*Tick))<.001 && f.Velocity.X>220,"speed snapped");
            Check(Math.Abs(f.Velocity.Y+300*Tick)<.001,"gravity did not resume");
        });
        Test("JF-002 fast movement detects thin water, fire, items and duck; blocked distance is free",()=>
        {
            var w=Flight();var f=w.Players[0];Input(w,0,Vector2.UnitX);
            w.Platforms.Add(new(1000,f.Position.X+20,f.Position.Y,5,1){Height=50,IsRoute=false});
            w.Step(.1f,Idle);Check(f.Platform==1000 && f.FlapLockRemaining==0,"thin water skipped");
            w=Flight();f=w.Players[0];Input(w,0,Vector2.UnitX);
            w.Fires.Add(new(){Position=f.Position+new Vector2(30,0),Color=0});
            w.Items.Add(new(){Position=f.Position+new Vector2(45,0),Water=false});
            w.Step(.1f,Idle);Check(w.Fires.All(p=>p.Done) && w.Items.All(p=>p.Done) && f.Gauge==54,"fast trigger skipped");
            w=Flight();f=w.Players[0];Input(w,0,-Vector2.UnitY);w.DuckY=f.Position.Y-45;
            w.Step(.1f,Idle);Check(!f.Alive && f.FlapLockRemaining==0,"duck skipped");
            w=Flight();f=w.Players[0];Input(w,0,Vector2.UnitX);
            f.Position.X=w.Settings.WorldWidth-w.Settings.WorldMargin;
            w.Settings.JumpDrainPerMeter=.1f;float gauge=f.Gauge;
            w.Step(Tick,Idle);w.Step(Tick,Idle);
            Check(f.Gauge==gauge && f.Velocity.X==0,"wall pushed or charged blocked distance");
        });
        Test("JF-002 feedback distinguishes charge cost, shortage, used, direction and rejection",()=>
        {
            var w=Flight();var f=w.Players[0];f.Charging=true;f.Charge=80;f.Gauge=100;
            Check(w.FlapHint(f).Contains("팔딱 20") && w.FlapHint(f).Contains("예상 잔여 0"),"exact charge budget missing");
            f.Gauge=99;Check(w.FlapHint(f).Contains("1 부족"),"charge shortage missing");f.Charging=false;f.Gauge=9;
            Check(w.FlapHint(f).Contains("부족"),"shortage missing");f.Gauge=60;f.FlapUsed=true;
            Check(w.FlapHint(f).Contains("사용 완료"),"used missing");f.FlapUsed=false;f.FallingOnly=true;
            Check(w.FlapHint(f).Contains("추락 제한"),"fall restriction missing");
            w=Flight();f=w.Players[0];Input(w,0,Vector2.Zero);w.Step(.31f,Idle);
            Check(w.FlapHint(f).Contains("방향 없음"),"direction timeout missing");
        });
    }

    static void RunFireStunUpdate(Action<string,Action> Test)
    {
        GameWorld WaterWorld() => new(new(){Seed=82,DuckSpeed=0,AttackCooldown=10000});
        Test("fire stun: both fish show hit effect, cancel skills and ignore controls for 0.3 seconds",()=>
        {
            foreach(int id in new[]{0,1})
            {
                var w=WaterWorld();var f=w.Players[id];w.Players[1-id].Alive=false;
                f.Charging=true;f.Charge=40;f.FlapLockRemaining=.18f;f.FlapLockedVelocity=new(200,0);
                w.Fires.Add(new(){Position=f.Position,Color=1-id});
                w.Step(Tick,Idle);float x=f.Position.X;float gauge=f.Gauge;
                Check(f.FireStunRemaining==.3f && f.FireHitEffect==.5f && f.FireHitColor==1-id,"missing hit state");
                Check(!f.Charging && f.Charge==0 && f.Parry==0 && f.FlapLockRemaining==0,"skills not cancelled");
                f.Velocity=Vector2.Zero;
                for(int i=0;i<36;i++)
                {
                    Control[] controls=[new(),new()];controls[id]=new(Vector2.UnitX,true,false,true,ParryPress:true);
                    w.Step(Tick,controls);
                    Check(Math.Abs(f.Position.X-x)<.001 && !f.Charging && f.Parry==0 && f.Gauge==gauge,"stunned input activated");
                }
                Check(f.FireStunRemaining<.000001,"stun duration too long");
                w.Step(Tick,Idle);Control[] resume=[new(),new()];resume[id]=new(Vector2.UnitX,true,false,true);
                w.Step(Tick,resume);Check(f.Position.X>x && f.Charging,"control did not resume");
            }
        });
        Test("fire stun: overlapping flames cannot stack damage or reset active stun",()=>
        {
            var w=WaterWorld();var f=w.Players[0];var sounds=new List<string>();w.Sound=sounds.Add;
            for(int i=0;i<3;i++)w.Fires.Add(new(){Position=f.Position,Color=1});
            w.Step(Tick,Idle);Check(f.Gauge==80 && sounds.Count(s=>s=="hit")==1,"multiple damage on impact");
            for(int i=0;i<36;i++)w.Step(Tick,Idle);
            Check(f.Gauge==80 && f.FireStunRemaining<.000001,"chain stun/damage");
            w.Step(Tick,Idle);Check(f.Gauge==60 && sounds.Count(s=>s=="hit")==2,"new hit after recovery missing");
        });
        Test("fire stun: gravity continues, airborne flap is blocked, successful parry is exempt",()=>
        {
            var w=Flight();var f=w.Players[0];f.Velocity=new(0,-30);
            w.Fires.Add(new(){Position=f.Position,Color=1});w.Step(Tick,Idle);float y=f.Position.Y,g=f.Gauge;
            f.FlapPending=true;f.FlapDirection=Vector2.UnitY;f.FlapDeadline=w.Time+1;f.FlapDirectionTime=w.Time;
            Check(!w.TryFlap(f),"direct flap during stun");
            w.Step(.1f,Idle);Check(f.Position.Y<y && f.Gauge==g,"stun froze gravity or spent gauge");
            w=Flight();f=w.Players[0];w.Fires.Add(new(){Position=f.Position,Color=1,Special=true});Input(w,0,Vector2.Zero,false,true);
            Check(f.FireStunRemaining==0 && f.FireHitEffect==0 && f.Effect>0,"successful parry stunned fish");
        });
        Test("fire stun: fast flame sweep hits once and reset/death clear feedback",()=>
        {
            var w=WaterWorld();var f=w.Players[0];
            w.Fires.Add(new(){Position=f.Position-new Vector2(0,30),Velocity=new(0,360),Color=1});
            w.Step(.2f,Idle);Check(f.Gauge==80 && f.FireHitEffect>0,"fast fire tunneled");
            f.Position.Y=w.DeathY-1;w.Step(Tick,Idle);
            Check(!f.Alive && f.FireHitEffect==0 && f.FireStunRemaining==0,"dead hit feedback persists");
            var fresh=WaterWorld();Check(fresh.Players.All(p=>p.FireStunRemaining==0 && p.FireHitEffect==0),"reset carries stun");
            bool rejected=false;try{new GameSettings(){FireHitStunSeconds=0}.Validate();}catch(InvalidDataException){rejected=true;}
            Check(rejected,"invalid stun accepted");
        });
    }

    static void RunParryFeedbackUpdate(Action<string,Action> Test)
    {
        Test("parry feedback: both players get anchored five-frame effect and one dedicated sound",()=>
        {
            foreach(int id in new[]{0,1})
            {
                var w=Flight(id);var f=w.Players[id];var sounds=new List<string>();w.Sound=sounds.Add;
                w.Fires.Add(new(){Position=f.Position,Color=1-id,Special=true});
                Input(w,id,Vector2.Zero,false,true);
                Check(f.Effect==FishPlayer.ParryEffectSeconds && f.ParryEffectFrame==1 && w.Score.Damage==20,"success effect/score");
                Check(sounds.Count(s=>s=="parry")==1 && !sounds.Contains("jump") && !sounds.Contains("splash"),"success sound overlapped or missing");
                var anchor=f.ParryPosition;w.Step(.1f,Idle);
                Check(f.ParryPosition==anchor && f.Position!=anchor,"effect follows boosted fish");
                f.Effect=.01f;Check(f.ParryEffectFrame==5,"last frame unreachable");
                w.Step(.02f,Idle);Check(f.Effect==0 && sounds.Count(s=>s=="parry")==1,"effect/sound repeated");
            }
        });
        Test("parry feedback: failed color and ordinary fire never play success cue",()=>
        {
            foreach(bool special in new[]{true,false})
            {
                var w=Flight();var f=w.Players[0];var sounds=new List<string>();w.Sound=sounds.Add;
                w.Fires.Add(new(){Position=f.Position,Color=0,Special=special});
                Input(w,0,Vector2.Zero,false,true);
                Check(f.Effect==0 && w.Score.Damage==0 && !sounds.Contains("parry") && sounds.Contains("hit"),"false success cue");
            }
        });
        Test("bite feedback: stop is 0.09 seconds at 30/60/144 fps without catch-up and shake ends",()=>
        {
            foreach(int fps in new[]{30,60,144})
            {
                var feedback=new BiteFeedback();feedback.Trigger();float active=0;
                Check(feedback.Offset.Length()>0,"initial impact missing");
                for(int i=0;i<fps;i++) active+=feedback.Advance(1f/fps);
                Check(Math.Abs(active-.91f)<.00001f && feedback.PauseRemaining==0 && feedback.Offset==Vector2.Zero,"pause length or residual shake");
            }
            var f=new BiteFeedback();f.Trigger();f.Advance(.04f);f.Trigger();
            Check(Math.Abs(f.PauseRemaining-.09f)<.00001 && Math.Abs(f.Advance(.2f)-.11f)<.00001,"multiple bites stack pauses");
        });
    }

    static void RunImpactUpdate(Action<string,Action> Test)
    {
        GameWorld World() => new(new(){Seed=71,DuckSpeedPerStage=0,AttackCooldown=10000});
        void Hit(GameWorld w, float damage=20, float? x=null)
        {
            w.Waters.Add(new(){Position=new(x ?? w.Settings.WorldWidth/2,w.DuckMouthPosition.Y+2),Damage=damage});
            w.Step(Tick,Idle);
        }
        Test("impact: death is immediate, feedback and sound occur once, partner remains alive",()=>
        {
            var w=World();int sounds=0;w.Sound=s=>{if(s=="bite")sounds++;};
            var f=w.Players[0];f.Position=new(200,w.DeathY-1);f.Charging=true;f.Charge=40;
            w.Step(Tick,Idle);
            Check(!f.Alive && !f.Charging && f.Charge==0 && f.Platform==-1 && f.DeathEffect>1.3f,"death state/feedback missing");
            Check(w.Players[1].Alive && sounds==1 && f.DeathPosition.X==200,"partner killed or impact missing");
            w.Step(Tick,Idle);Check(sounds==1,"repeated death sound");
            float camera=w.Camera;w.AdvanceFeedback(2);
            Check(f.DeathEffect==0 && w.Camera==camera && !f.Alive,"feedback-only update advances game or revives fish");
        });
        Test("impact: water hits visible mouth once, scores, stuns and emits splash",()=>
        {
            var w=World();int sounds=0;w.Sound=s=>{if(s=="splash")sounds++;};
            Hit(w,20);
            Check(w.Waters.Count==0 && w.Score.Damage==20 && w.DuckHitPoints==100,"water score/removal");
            Check(w.DuckPhase==DuckState.Stunned && w.DuckHitEffect>1.1 && w.DuckStunRemaining>1.19 && sounds==1,"hit feedback/stun missing");
            w.Step(.2f,Idle);Check(w.Score.Damage==20 && sounds==1,"duplicate score/sound");
        });
        Test("impact: swept water cannot tunnel and outside-screen water does not hit",()=>
        {
            var w=World();w.Waters.Add(new(){Position=new(10,w.DuckMouthPosition.Y+100),Damage=30});
            w.Waters.Add(new(){Position=new(-20,w.DuckMouthPosition.Y+100),Damage=50});
            w.Waters.Add(new(){Position=new(w.Settings.WorldWidth+20,w.DuckMouthPosition.Y+100),Damage=50});
            w.Step(.5f,Idle);
            Check(w.Score.Damage==30 && w.Waters.Count==2,"swept hit/miss boundaries");
        });
        Test("impact: stun freezes scroll and attack clock, then resumes only unused tick time",()=>
        {
            var w=World();Hit(w);double clock=w.DuckAttackTime;float camera=w.Camera;
            w.Step(.8f,Idle);
            Check(Math.Abs(w.Camera-camera)<.001 && Math.Abs(w.DuckAttackTime-clock)<.001,"stun allowed chase/attack clock");
            w.Step(.6f,Idle);
            Check(w.DuckStunRemaining==0 && w.DuckPhase==DuckState.Chasing,"stun did not end");
            Check(Math.Abs(w.Camera-camera-165*.2)<.02 && Math.Abs(w.DuckAttackTime-clock-.2)<.001,"partial-tick resume incorrect");
        });
        Test("impact: stun delays imminent fire without catch-up volleys",()=>
        {
            var w=new GameWorld(new(){Seed=72,DuckSpeed=0});
            w.Step(4.95f,Idle);Hit(w);
            for(int i=0;i<144;i++)w.Step(Tick,Idle);
            Check(w.ShotsFired==0,"fire fired during stun");
            w.Step(.1f,Idle);
            Check(w.ShotsFired==1 && w.Fires.Count==7,"delayed volley duplicated or lost");
        });
        Test("impact: zero-gauge water still rewards, simultaneous hits refresh instead of stacking stun",()=>
        {
            var w=World();Hit(w,0);Check(w.DuckHitPoints==50,"minimum reward missing");
            w.Step(.6f,Idle);Hit(w,10);
            w.Waters.Add(new(){Position=new(300,w.DuckMouthPosition.Y),Damage=10});
            w.Step(Tick,Idle);
            Check(w.Score.Damage==30 && w.DuckHitPoints==150,"multiple hits not scored separately");
            Check(Math.Abs(w.DuckStunRemaining-1.2)<.001,"stun accumulated rather than refreshed");
        });
        Test("impact: reset clears effects and new settings reject invalid values",()=>
        {
            var w=World();Check(w.DuckStunRemaining==0 && w.DuckHitEffect==0 && w.Players.All(f=>f.DeathEffect==0),"stale effects");
            foreach(var s in new[]{new GameSettings(){DuckStunSeconds=0},new(){ItemDrawSize=float.NaN},new(){WaterMinDamage=-1}})
            { bool rejected=false;try{s.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid feedback setting accepted"); }
        });
    }

    static void RunChaseUpdate(Action<string,Action> Test)
    {
        var metrics = new List<string>();
        Test("chase update: camera follows duck constantly and stops for exactly 0.50 of every 5 seconds",()=>
        {
            var w=new GameWorld(new(){Seed=12,DuckSpeedPerStage=0});
            void Until(double t){while(w.Time<t-1e-7){w.Step((float)Math.Min(Tick,t-w.Time),Idle);}}
            Until(4.8);float before=w.Camera;
            Check(Math.Abs(before-165*4.8)<.03,"continuous scroll rate");
            Until(5.3);Check(Math.Abs(w.Camera-before)<.03 && w.ShotsFired==1,"attack pause duration/volley");
            Until(10);Check(Math.Abs(w.Camera-165*9.3)<.05 && w.ShotsFired==2,"chase integral/cooldown");
            Check(Math.Abs(w.DuckY+15-w.CameraBottom)<.03,"duck not anchored at bottom");
        });
        Test("chase update: both fish can wait and charge in water without sinking; empty gauge still falls",()=>
        {
            var w=new GameWorld(new(){Seed=1,DuckSpeed=0});
            for(int i=0;i<360;i++) w.Step(Tick,Idle);
            Check(w.Players.All(f=>f.Alive && f.Platform==0 && Math.Abs(f.Position.Y)<.01),"waiting partner sank");
            w.Players[0].Gauge=0;for(int i=0;i<180;i++)w.Step(Tick,Idle);
            Check(w.Players[0].Position.Y< -100,"empty gauge floats indefinitely");
        });
        Test("chase update: shared recovery rewards cooperation; solo wrong-color entry still rejects",()=>
        {
            var w=new GameWorld(new(){Seed=1,DuckSpeed=0});
            foreach(var f in w.Players)f.Gauge=40;
            Control[] swimming=[new(Vector2.UnitX,false,false,false),new(-Vector2.UnitX,false,false,false)];
            for(int n=0;n<60;n++)w.Step(Tick,swimming);
            Check(w.Players.All(f=>f.Gauge>59 && f.Gauge<61),"helper cannot recover after teammate joins");
            w.Players[0].Alive=false;float before=w.Players[1].Gauge;
            w.Step(Tick,swimming);Check(w.Players[1].Gauge<before,"solo wrong-color recovery allowed");
            var solo=Flight();var fish=solo.Players[0];
            solo.Platforms.Add(new(1000,950,1000,100,0){Height=80,IsRoute=false});
            solo.Step(Tick,Idle);Check(fish.FallingOnly && fish.Platform==-1,"cooperation recovery bypassed entry rule");
        });
        Test("chase update: default jump cap and upward flap limit prevent unlimited climb",()=>
        {
            var w=Flight();var f=w.Players[0];f.Gauge=100;f.Position.Y=500;w.Camera=500;
            w.Launch(f,100,true);
            Check(f.LastPaidJumpCost==75 && f.Gauge==25,"jump cap does not preserve flap budget");
            f.Gauge=100;f.LastPaidJumpCost=75;f.Velocity=new(0,600);float start=f.Position.Y;
            Input(w,0,Vector2.UnitY);Check(f.Velocity.Length()<=480.001,"flap cap");
            float peak=start;for(int i=0;i<360 && f.Alive;i++){w.Step(Tick,Idle);peak=Math.Max(peak,f.Position.Y);}
            Check(peak-start<240 && peak-start>100,"upward flap is excessive or unusable");
            metrics.Add($"Default maximum-cost upward flap additional height: {peak-start:F2} world units");
        });
        Test("chase update: 100 seeds mix internal and airborne items; types and share vary without changing map RNG",()=>
        {
            var shares=new HashSet<float>();int air=0,inside=0,waterAir=0,foodAir=0;
            for(int seed=0;seed<100;seed++)
            {
                var w=new GameWorld(new(){Seed=seed,ItemChance=1,DuckSpeed=0});
                var noItems=new GameWorld(new(){Seed=seed,ItemChance=0,DuckSpeed=0});
                Check(w.Platforms.Select(x=>(x.X,x.Y,x.Width,x.Height,x.Color)).SequenceEqual(noItems.Platforms.Select(x=>(x.X,x.Y,x.Width,x.Height,x.Color))),"item RNG changed route");
                shares.Add(w.AirItemShare);
                Check(w.Items.Any(i=>i.Airborne) && w.Items.Any(i=>!i.Airborne),$"missing placement class seed {seed}");
                for(int scroll=0;scroll<8;scroll++)
                {
                    foreach(var item in w.Items)
                    {
                        if(item.Airborne)
                        {
                            air++;if(item.Water)waterAir++;else foodAir++;
                            Check(!w.Platforms.Any(p=>item.Position.X>=p.X-35 && item.Position.X<=p.X+p.Width+35 &&
                                item.Position.Y>=p.Y-p.Height/2-35 && item.Position.Y<=p.Y+p.Height/2+35),$"air pickup overlaps water seed {seed}");
                            var source=w.Platforms.Find(p=>p.Id==item.SourcePlatform);
                            if(source!=null)Check(item.Position.Y-source.Y-source.Height/2<=70.001,"unreachable airborne reward");
                        }
                        else {inside++;Check(w.Platforms.Any(p=>p.Contains(item.Position)),"inside item orphaned");}
                    }
                    w.Camera+=500;w.Step(Tick,Idle);
                }
            }
            Check(shares.Count>90 && waterAir>0 && foodAir>0 && inside>0,"missing random variety");
            metrics.Add($"100 seeded runs: {shares.Count} different air-placement probabilities; both item types verified inside/outside.");
        });
        Test("chase update: airborne food and water require flight and trigger during a swept flap",()=>
        {
            foreach(bool water in new[]{false,true})
            {
                var w=Flight();var f=w.Players[0];f.Jumping=false;f.Velocity=Vector2.Zero;
                var item=new GameItem(){Position=f.Position,Airborne=true,Water=water};w.Items.Add(item);
                w.Step(Tick,Idle);Check(!item.Done,"swimming/walking pickup consumed air reward");
                f.Jumping=true;f.FlightOrigin=FlightOrigin.PaidJump;f.LastPaidJumpCost=40;
                item.Position=f.Position+new Vector2(45,0);Input(w,0,Vector2.UnitX);float gauge=f.Gauge;
                w.Step(.1f,Idle);Check(item.Done,"flap skipped air reward");
                if(water)Check(w.Waters.Count==1,"water item did not fire");
                else Check(f.Gauge==gauge+24,"food recovery amount");
            }
        });
        Test("chase update: baseline speed 90: 120 real two-player transfers flip color then let the waiting teammate land",()=>
        {
            float slowest=0,lowest=10000;int scenarios=0;
            for(int seed=0;seed<30;seed++)
            {
                var layout=new GameWorld(new(){Seed=seed});var route=layout.Platforms.Where(p=>p.IsRoute).Take(5).ToArray();
                for(int n=1;n<route.Length;n++)
                {
                    var from=route[n-1];var to=route[n];
                    var w=new GameWorld(new(){Seed=seed,DuckSpeed=90,DuckSpeedPerStage=0});w.Platforms.Clear();w.Items.Clear();
                    w.Platforms.Add(new(9999,0,100000,w.Settings.WorldWidth,0));
                    w.Platforms.Add(new(100,from.X,0,from.Width,0){Height=from.Height,IsRoute=false});
                    var destination=new WaterPlatform(101,to.X,to.Y-from.Y,to.Width,to.Color){Height=to.Height,IsRoute=false};w.Platforms.Add(destination);
                    float x=(Math.Max(from.X,to.X)+Math.Min(from.X+from.Width,to.X+to.Width))/2;
                    foreach(var fish in w.Players){fish.Position=new(x,0);fish.Platform=100;}
                    int first=1-destination.Color, follower=1-first, active=first;
                    float held=0;bool pressing=false,released=false,firstLanded=false;
                    Control[] controls=[new(),new()];
                    for(int tick=0;tick<960;tick++)
                    {
                        controls[0]=new();controls[1]=new();
                        if(!released){controls[active]=new(Vector2.Zero,!pressing,held>=1,held<1);pressing=true;held+=Tick;if(held>1+Tick)released=true;}
                        w.Step(Tick,controls);
                        if(!firstLanded && w.Players[first].Platform==101)
                        {
                            Check(destination.Color==first,"leader did not flip color");firstLanded=true;active=follower;held=0;pressing=false;released=false;
                        }
                        if(firstLanded && w.Players[follower].Platform==101)break;
                    }
                    Check(w.Players.All(f=>f.Alive && f.Platform==101),$"cooperation failed seed={seed}, leg={n}, t={w.Time:F2}, y={w.Players[0].Position.Y:F1}/{w.Players[1].Position.Y:F1}");
                    slowest=Math.Max(slowest,(float)w.Time);lowest=Math.Min(lowest,w.Players.Min(f=>f.Position.Y-w.DeathY));scenarios++;
                }
            }
            metrics.Add($"Baseline speed 90 cooperative transfers: {scenarios}; slowest {slowest:F3}s; minimum remaining death-floor clearance {lowest:F1} world units.");
        });
        Test("chase update: speed 165: 120 precharged jump-and-flap transfers still require color cooperation",()=>
        {
            float slowest=0,lowest=10000;int scenarios=0;
            for(int seed=0;seed<30;seed++)
            {
                var layout=new GameWorld(new(){Seed=seed});var route=layout.Platforms.Where(p=>p.IsRoute).Take(5).ToArray();
                for(int n=1;n<route.Length;n++)
                {
                    var from=route[n-1];var to=route[n];
                    var w=new GameWorld(new(){Seed=seed,DuckSpeedPerStage=0});w.Platforms.Clear();w.Items.Clear();
                    w.Platforms.Add(new(9999,0,100000,w.Settings.WorldWidth,0));
                    w.Platforms.Add(new(100,from.X,0,from.Width,0){Height=from.Height,IsRoute=false});
                    var destination=new WaterPlatform(101,to.X,to.Y-from.Y,to.Width,to.Color){Height=to.Height,IsRoute=false};w.Platforms.Add(destination);
                    float x=(Math.Max(from.X,to.X)+Math.Min(from.X+from.Width,to.X+to.Width))/2;
                    foreach(var fish in w.Players){fish.Position=new(x,0);fish.Platform=100;}
                    int first=1-destination.Color, follower=1-first, active=first;
                    float held=0;bool released=false,firstLanded=false;
                    Control[] controls=[new(),new()];
                    for(int tick=0;tick<960;tick++)
                    {
                        controls[0]=new();controls[1]=new();
                        if(!firstLanded)
                        {
                            controls[first]=new(Vector2.Zero,tick==0,held>=1,held<1);held+=Tick;
                            controls[follower]=new(Vector2.Zero,tick==0,false,true);
                        }
                        else if(!released){controls[follower]=new(Vector2.Zero,false,true,false);released=true;}
                        foreach(int id in new[]{first,follower})
                            if(w.Players[id].Jumping && !w.Players[id].FlapUsed && w.Players[id].Position.Y>160)
                                controls[id]=new(Vector2.UnitY,true,false,true);
                        w.Step(Tick,controls);
                        if(!firstLanded && w.Players[first].Platform==101)
                        {
                            Check(destination.Color==first,"leader did not flip color");firstLanded=true;
                        }
                        if(firstLanded && w.Players[follower].Platform==101)break;
                    }
                    Check(w.Players.All(f=>f.Alive && f.Platform==101),$"cooperation failed seed={seed}, leg={n}, t={w.Time:F2}, y={w.Players[0].Position.Y:F1}/{w.Players[1].Position.Y:F1}");
                    slowest=Math.Max(slowest,(float)w.Time);lowest=Math.Min(lowest,w.Players.Min(f=>f.Position.Y-w.DeathY));scenarios++;
                }
            }
            metrics.Add($"Speed 165 precharged jump-and-flap cooperative transfers: {scenarios}; slowest {slowest:F3}s; minimum remaining death-floor clearance {lowest:F1} world units.");
        });
        Test("chase update: model profiling and bounded object retention over 10000 fixed steps",()=>
        {
            var w=new GameWorld(new(){Seed=123});
            void Step(){w.Camera+=1;foreach(var f in w.Players){f.Alive=true;f.Position=new(800+f.Id*250,w.Camera+300);f.Velocity=new(40,-10);f.Platform=-1;f.Jumping=true;}w.Step(Tick,Idle);}
            for(int i=0;i<500;i++)Step();
            long allocation=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<10000;i++)Step();timer.Stop();allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
            Check(w.Platforms.Count<80 && w.Items.Count<80 && w.Fires.Count<150 && w.Waters.Count<80,"unbounded world retention");
            metrics.Add($"Model only: 10000 steps {timer.Elapsed.TotalMilliseconds:F2}ms; {allocation/10000.0:F1} allocated bytes/step; retained platforms={w.Platforms.Count}, items={w.Items.Count}, fire={w.Fires.Count}. Not a rendered FPS measurement.");
        });
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"balance-profile-results.txt"),metrics);
    }
}
