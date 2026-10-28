using System.Numerics;

// GameWorld의 공중 기술과 오리 상태. 기본 이동/충돌 처리는 GameModel.cs에 유지한다.
sealed partial class GameWorld
{
    static void FlapNotice(FishPlayer f, string message)
    {
        f.FlapMessage = message;
        f.FlapMessageTime = 1.2f;
    }

    void BeginFlap(FishPlayer f, Control c, double stepStart)
    {
        if (f.FlapPending) return;
        if (!f.Alive || !f.Jumping || f.FallingOnly || f.FlightOrigin != FlightOrigin.PaidJump)
        { FlapNotice(f, "일반 점프 중에만 팔딱 가능"); return; }
        if (f.FlapUsed) { FlapNotice(f, "이번 점프의 팔딱 사용 완료"); return; }
        f.FlapStarted = c.InputTime >= 0 ? c.InputTime : stepStart;
        f.FlapDeadline = f.FlapStarted + Settings.FlapDirectionWindow;
        f.FlapDirection = c.Direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(c.Direction);
        f.FlapDirectionTime = f.FlapStarted;
        f.FlapPending = true;
    }

    void CaptureFlapDirection(FishPlayer f, Control c, double stepStart)
    {
        if (!f.FlapPending || f.FlapDirection != Vector2.Zero || c.Direction == Vector2.Zero) return;
        double at = c.DirectionTime >= 0 ? c.DirectionTime : stepStart;
        // 부동소수점 오차만 허용한다. 0.301초 입력을 틱 반올림으로 허용하지 않는다.
        if (at >= f.FlapStarted - 1e-7 && at <= f.FlapDeadline + 1e-7)
        {
            f.FlapDirection = Vector2.Normalize(c.Direction);
            f.FlapDirectionTime = at;
        }
    }

    void ResolveFlap(FishPlayer f)
    {
        if (!f.FlapPending) return;
        if (!f.Alive || !f.Jumping || f.FallingOnly || f.FlightOrigin != FlightOrigin.PaidJump)
        { f.FlapPending = false; return; }
        if (Time + 1e-7 < f.FlapStarted + Settings.ParryWindow) return;
        if (f.Parry > .000001f) return;
        if (f.FlapDirection != Vector2.Zero) TryFlap(f);
        else if (Time > f.FlapDeadline + 1e-7)
        { f.FlapPending = false; FlapNotice(f, "팔딱 방향 입력 시간 초과"); }
    }

    public bool TryFlap(FishPlayer f)
    {
        if (!f.FlapPending || !f.Alive || !f.Jumping || f.FallingOnly || f.FlapUsed ||
            f.FlightOrigin != FlightOrigin.PaidJump || f.LastPaidJumpCost <= 0 ||
            f.FlapDirection == Vector2.Zero || f.FlapDirectionTime > f.FlapDeadline + 1e-7 ||
            Time + 1e-7 < f.FlapStarted + Settings.ParryWindow || f.Parry > .000001f) return false;
        float cost = f.LastPaidJumpCost * .5f;
        f.FlapPending = false;
        if (f.Gauge < cost) { FlapNotice(f, "팔딱 게이지 부족"); return false; }
        f.Gauge -= cost;
        float speed = MathF.Sqrt(2 * Settings.Gravity * cost * Settings.JumpMetersPerPoint * Settings.PixelsPerMeter);
        f.Direction = Vector2.Normalize(f.FlapDirection);
        f.FlapBoostVelocity = f.Direction * speed * (Settings.FlapSpeedMultiplier - 1);
        f.FlapBoostRemaining = Settings.FlapBurstDuration;
        // 기존 관성을 더하지 않고 입력 방향으로 즉시 교체한다. 중력은 계속 적용한다.
        f.Velocity = f.Direction * speed + f.FlapBoostVelocity;
        f.FlapUsed = true;
        FlapNotice(f, $"팔딱! −{cost:0.#}");
        Sound?.Invoke("jump");
        return true;
    }

    static void DecayFlapBoost(FishPlayer f, float dt)
    {
        if (f.FlapBoostRemaining <= 0) return;
        float elapsed = Math.Min(dt, f.FlapBoostRemaining);
        Vector2 reduction = f.FlapBoostVelocity * (elapsed / f.FlapBoostRemaining);
        f.Velocity -= reduction;
        f.FlapBoostVelocity -= reduction;
        f.FlapBoostRemaining = Math.Max(0, f.FlapBoostRemaining - elapsed);
        if (f.FlapBoostRemaining == 0) f.FlapBoostVelocity = Vector2.Zero;
    }

    // t초까지 공격 때문에 멈춘 시간을 계산한다. 고정 틱이 경계를 넘겨도 속도 오차가 누적되지 않는다.
    double StoppedTime(double t)
    {
        double period = Settings.AttackCooldown;
        int count = Math.Max(0, (int)Math.Floor((t + Settings.FireWindup) / period));
        if (count == 0) return 0;
        double duration = Settings.FireWindup + Settings.FireHold + Settings.FireRecovery;
        double lastStart = count * period - Settings.FireWindup;
        return (count - 1) * duration + Math.Clamp(t - lastStart, 0, duration);
    }

    void UpdateDuck(double previousTime)
    {
        DuckY += Settings.DuckSpeed * (float)((Time - previousTime) - (StoppedTime(Time) - StoppedTime(previousTime)));
        int volleys = (int)Math.Floor((Time + 1e-7) / Settings.AttackCooldown);
        while (ShotsFired < volleys)
        {
            ShotsFired++;
            Sound?.Invoke("fire");
            int color = attackRandom.Next(2), special = attackRandom.Next(7);
            for (int i = 0; i < 7; i++)
            {
                float angle = (-60 + i * 20) * MathF.PI / 180;
                Fires.Add(new() { Position = new(Settings.WorldWidth / 2, DuckY),
                    Velocity = new(MathF.Sin(angle) * Settings.FireSpeed, MathF.Cos(angle) * Settings.FireSpeed),
                    Color = i == special ? 1 - color : color, Special = i == special });
            }
        }
        double phase = Time - volleys * Settings.AttackCooldown;
        AttackTimer = (float)phase;
        if (volleys > 0 && phase < Settings.FireHold)
        { DuckPhase = DuckState.Holding; DuckMouthFrame = 1; }
        else if (volleys > 0 && phase < Settings.FireHold + Settings.FireRecovery)
        {
            DuckPhase = DuckState.Recovering;
            DuckMouthFrame = phase - Settings.FireHold < Settings.FireRecovery / 2 ? 2 : 3;
        }
        else if (phase >= Settings.AttackCooldown - Settings.FireWindup)
        {
            DuckPhase = DuckState.Windup;
            DuckMouthFrame = phase < Settings.AttackCooldown - Settings.FireWindup / 2 ? 2 : 1;
        }
        else
        {
            DuckPhase = DuckState.Chasing;
            double cycle = Settings.MouthClosedHoldSeconds + Settings.MouthOpenSeconds +
                Settings.MouthOpenHoldSeconds + Settings.MouthCloseSeconds;
            double mouth = Time % cycle;
            double open = Settings.MouthClosedHoldSeconds;
            double hold = open + Settings.MouthOpenSeconds;
            double close = hold + Settings.MouthOpenHoldSeconds;
            DuckMouthFrame = mouth < open ? 3 : mouth < hold ? 2 : mouth < close ? 1 : 2;
        }
    }
}
