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
        var f=w.Players[id]; f.Platform=-1; f.Position=new(1000,1000); w.Launch(f,40,true);
        return w;
    }
    static void Input(GameWorld w,int id,Vector2 direction,bool press=true,bool parry=false)
    {
        Control[] c=[new(),new()]; c[id]=new(direction,press,false,press,ParryPress:parry,InputTime:w.Time,DirectionTime:w.Time);
        w.Step(Tick,c);
    }
    public static void Run(Action<string,Action> Test)
    {
        Test("video: cheaper flap keeps its original speed and marker matches the paid fee",()=>
        {
            foreach(int id in new[]{0,1}) foreach(float ratio in new[]{.25f,.5f})
            {
                var w=Flight(id);var f=w.Players[id];w.Settings.FlapCostRatio=ratio;
                Check(w.ShowFlapCost(f) && w.FlapCost(f)==40*ratio && w.FlapGaugeThreshold(f)==40*ratio,"wrong airborne marker");
                float before=f.Gauge;Input(w,id,Vector2.UnitX);
                Check(before-f.Gauge==40*ratio,"display and deduction disagree");
                Check(Math.Abs(f.Velocity.Length()-MathF.Sqrt(300*40*8)*2)<.01,"cheaper fee changed movement");
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
        Test("video: scrolling itself removes the lower player; exact floor is lethal, above stays alive",()=>
        {
            var w=Flight();w.Settings.Gravity=0;
            w.Players[0].Position.Y=500;w.Players[0].Velocity=Vector2.Zero;
            var upper=w.Players[1];upper.Alive=true;upper.Platform=-1;upper.Position.Y=4000;
            w.Step(Tick,Idle);
            Check(!w.Players[0].Alive && upper.Alive,"new camera floor was checked a tick late");
            float camera=w.Camera;upper.Position.Y=w.DeathY+1;upper.Velocity=Vector2.Zero;
            w.Step(Tick,Idle);Check(upper.Alive && w.Camera==camera,"above floor died or camera went backward");
            upper.Position.Y=w.DeathY;w.Step(Tick,Idle);
            Check(w.Players.All(f=>!f.Alive),"exact boundary did not finish game");
        });
        Test("video: duck follows a high camera, cycles mouth, and slowly advances without feedback acceleration",()=>
        {
            var w=Flight();w.Settings.Gravity=0;w.Settings.DuckSpeed=13;w.Camera=2000;
            var f=w.Players[0];f.Position.Y=2300;f.Velocity=Vector2.Zero;
            var frames=new HashSet<int>();w.Step(Tick,Idle);float start=w.DuckY, camera=w.Camera;
            for(int i=0;i<240;i++){w.Step(Tick,Idle);frames.Add(w.DuckMouthFrame);}
            Check(frames.SetEquals(new[]{1,2,3}),"mouth stopped animating after scrolling");
            Check(Math.Abs(w.DuckY-start-26)<.03 && w.Camera==camera,"duck accelerated from camera feedback");
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
            w.Step(.1f,Idle);Check(w.Fires.All(p=>p.Done) && w.Items.All(p=>p.Done) && f.Gauge==60,"fast trigger skipped");
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
}
