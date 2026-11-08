using System.Numerics;

// GameWorld의 공중 기술과 오리 상태. 기본 이동/충돌 처리는 GameModel.cs에 유지한다.
sealed partial class GameWorld
{
    void HitByFire(FishPlayer fish, FireProjectile fire)
    {
        fish.Gauge = Math.Max(0, fish.Gauge - 20);
        fish.FireStunRemaining = Settings.FireHitStunSeconds;
        fish.FireHitEffect = FishPlayer.FireHitEffectSeconds;
        fish.FireHitPosition = fish.Position;
        fish.FireHitColor = fire.Color;
        fish.Charging = fish.FlapPending = false;
        fish.Charge = fish.Parry = fish.FlapLockRemaining = fish.Effect = 0;
        fish.FlapLockedVelocity = Vector2.Zero;
        fish.SwimDirection = Vector2.Zero;
        Sound?.Invoke("hit");
    }

    static void FlapNotice(FishPlayer f, string message)
    {
        f.FlapMessage = message;
        f.FlapMessageTime = .8f;
    }

    void BeginFlap(FishPlayer f, Control c, double stepStart)
    {
        if (f.FlapPending) return;
        if (f.FallingOnly) { FlapNotice(f, "추락 제한 · 반대 색 물로 이동"); return; }
        if (!f.Alive || !f.Jumping || f.FlightOrigin != FlightOrigin.PaidJump)
        { FlapNotice(f, "일반 점프 중에만 팔딱 가능"); return; }
        if (f.FlapUsed) { FlapNotice(f, "이번 점프의 팔딱 사용 완료"); return; }
        f.FlapStarted = c.InputTime >= 0 ? c.InputTime : stepStart;
        f.FlapDeadline = f.FlapStarted + Settings.FlapDirectionWindow;
        f.FlapDirection = c.Direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(c.Direction);
        f.FlapDirectionTime = f.FlapStarted;
        f.FlapPending = true;
        if (f.FlapDirection == Vector2.Zero) FlapNotice(f, "방향 입력 대기 (0.30초)");
    }

    void CaptureFlapDirection(FishPlayer f, Control c, double stepStart)
    {
        if (!f.FlapPending || f.FlapDirection != Vector2.Zero || c.Direction == Vector2.Zero) return;
        double at = c.DirectionTime >= 0 ? c.DirectionTime : stepStart;
        // 부동소수점 오차만 허용한다. 0.301초 입력을 틱 반올림으로 허용하지 않는다.
        if (at >= f.FlapStarted - 1e-7 && at <= f.FlapDeadline + 1e-7 && at <= Time + 1e-7)
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
        if (f.FlapDirection != Vector2.Zero) TryFlap(f);
        else if (Time > f.FlapDeadline + 1e-7)
        { f.FlapPending = false; FlapNotice(f, "방향 없음 · 입력 시간 초과"); }
    }

    public bool TryFlap(FishPlayer f)
    {
        if (!f.FlapPending || !f.Alive || f.FireStunRemaining > 0 || !f.Jumping || f.FallingOnly || f.FlapUsed ||
            f.FlightOrigin != FlightOrigin.PaidJump || f.LastPaidJumpCost <= 0 ||
            f.FlapDirection == Vector2.Zero || f.FlapDirectionTime > f.FlapDeadline + 1e-7) return false;
        float cost = f.LastPaidJumpCost * Settings.FlapCostRatio;
        f.FlapPending = false;
        f.Parry = 0; // 방향이 있는 팔딱 시도는 이 입력을 소유한다. 비용 부족도 패링으로 바꾸지 않는다.
        if (f.Gauge < cost) { FlapNotice(f, "팔딱 게이지 부족"); return false; }
        f.Gauge -= cost;
        // 사용료를 낮춰도 이동 거리는 줄지 않는다. 기존 J×0.5 기준 속도를 유지한다.
        float speed = MathF.Sqrt(Settings.Gravity * f.LastPaidJumpCost * Settings.JumpMetersPerPoint * Settings.PixelsPerMeter);
        f.Direction = Vector2.Normalize(f.FlapDirection);
        f.FlapLockedVelocity = f.Direction * Math.Min(Settings.FlapMaxSpeed, speed * Settings.FlapSpeedMultiplier);
        f.FlapLockRemaining = Settings.FlapDirectionLockSeconds;
        f.LastFlapActivatedAt = Time;
        f.Velocity = f.FlapLockedVelocity;
        f.FlapUsed = true;
        FlapNotice(f, $"팔딱! −{cost:0.#}");
        Sound?.Invoke("jump");
        return true;
    }

    float SteerAfterFlap(FishPlayer f, float input, float dt)
    {
        float vx = f.Velocity.X, cap = Settings.AirSteeringMaxSpeed;
        if (f.FlapUsed && Math.Abs(vx) > cap)
        {
            float sign = Math.Sign(vx);
            float speed = Math.Max(cap, Math.Abs(vx) - Settings.FlapOverspeedDeceleration * dt);
            // 같은 방향 입력은 초과 속도를 늘리지 않는다. 반대 입력으로는 제동할 수 있다.
            if (input * sign < 0) speed = Math.Max(0, speed - Math.Abs(input) * Settings.AirSteeringAcceleration * dt);
            return sign * speed;
        }
        if (input == 0) return vx * MathF.Exp(-Settings.AirReleaseDrag * dt);
        return Math.Clamp(vx + input * Settings.AirSteeringAcceleration * dt, -cap, cap);
    }

    public float FlapCost(FishPlayer f) => (f.Charging ? f.Charge : f.LastPaidJumpCost) * Settings.FlapCostRatio;

    // 충전 중에는 아직 점프 비용도 남아 있으므로 두 비용을 합친 위치에 예약선을 표시한다.
    public float FlapGaugeThreshold(FishPlayer f) => (f.Charging ? f.Charge : 0) + FlapCost(f);

    public bool ShowFlapCost(FishPlayer f) => f.Alive && !f.FallingOnly &&
        (f.Charging || (f.Jumping && f.FlightOrigin == FlightOrigin.PaidJump && !f.FlapUsed)) && FlapCost(f) > 0;

    public string FlapHint(FishPlayer f)
    {
        if (!f.Alive) return "탈락";
        if (f.FireStunRemaining > 0) return $"불 피격 · 기절 {f.FireStunRemaining:0.0}초";
        if (f.FlapMessageTime > 0) return f.FlapMessage;
        if (f.Charging)
        {
            float remaining = f.Gauge - FlapGaugeThreshold(f);
            string budget = remaining >= 0 ? $"예상 잔여 {remaining:0.#}" : $"팔딱 {-remaining:0.#} 부족";
            return $"점프 {f.Charge:0.#} / 팔딱 {FlapCost(f):0.#} / {budget}";
        }
        if (f.FallingOnly) return "추락 제한 · 반대 색 물로 이동";
        if (f.FlapUsed) return "팔딱 사용 완료";
        if (f.FlightOrigin != FlightOrigin.PaidJump) return "물에서 충전하세요";
        float cost = FlapCost(f);
        return f.Gauge >= cost ? $"팔딱 가능 · 비용 {cost:0.#}" : $"게이지 부족 · 팔딱 비용 {cost:0.#}";
    }

    // 게임 오버 직전에는 물리를 멈추고 이 시각 효과만 진행한다.
    public void AdvanceFeedback(float dt)
    {
        foreach (var fish in Players) fish.DeathEffect = Math.Max(0, fish.DeathEffect - dt);
        DuckHitEffect = Math.Max(0, DuckHitEffect - dt);
    }

    void HitDuck(WaterAttack water)
    {
        if (water.Done) return;
        water.Done = true;
        int previousScore = Score.Total(Players, Settings);
        Score.Damage += Math.Max(Settings.WaterMinDamage, water.Damage);
        int points = Score.Total(Players, Settings) - previousScore;
        DuckHitPoints = (DuckHitEffect > 0 ? DuckHitPoints : 0) + points;
        DuckHitX = water.Position.X;
        DuckHitEffect = DuckHitEffectSeconds;
        // 연속 명중은 남은 스턴을 갱신한다. 동시 명중 횟수만큼 시간을 쌓지 않는다.
        DuckStunRemaining = Math.Max(DuckStunRemaining, Settings.DuckStunSeconds);
        DuckPhase = DuckState.Stunned;
        DuckMouthFrame = 1;
        Sound?.Invoke("splash");
    }

    static float SegmentDistance(Vector2 from, Vector2 to, Vector2 point)
    {
        Vector2 path = to - from;
        float length = path.LengthSquared();
        float t = length > .000001f ? Math.Clamp(Vector2.Dot(point - from, path) / length, 0, 1) : 0;
        return Vector2.Distance(point, from + path * t);
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

    public float ChaseSpeed => Settings.DuckSpeed == 0 ? 0 : Settings.DuckSpeed + Stage * Settings.DuckSpeedPerStage;
    public double DuckFlightTime { get; private set; }
    public Vector2 DuckMouthPosition => new(Settings.WorldWidth / 2, DeathY + 45);

    void UpdateDuck(double previousTime)
    {
        double dt = Time - previousTime;
        double stunnedTime = Math.Min(dt, DuckStunRemaining);
        DuckStunRemaining = (float)Math.Max(0, DuckStunRemaining - dt);
        double previousAttackTime = DuckAttackTime;
        DuckAttackTime += dt - stunnedTime;
        double movingTime = Math.Max(0, (DuckAttackTime - previousAttackTime) -
            (StoppedTime(DuckAttackTime) - StoppedTime(previousAttackTime)));
        float rise = ChaseSpeed * (float)movingTime;
        DuckY += rise;
        Camera += rise; // 오리의 추격이 화면을 움직인다. 선행 플레이어의 높이로 가속하지 않는다.
        DuckFlightTime += movingTime;
        int volleys = (int)Math.Floor((DuckAttackTime + 1e-7) / Settings.AttackCooldown);
        while (ShotsFired < volleys)
        {
            ShotsFired++;
            Sound?.Invoke("fire");
            int color = attackRandom.Next(2), special = attackRandom.Next(7);
            for (int i = 0; i < 7; i++)
            {
                float angle = (-60 + i * 20) * MathF.PI / 180;
                Fires.Add(new() { Position = DuckMouthPosition,
                    Velocity = new(MathF.Sin(angle) * Settings.FireSpeed, MathF.Cos(angle) * Settings.FireSpeed),
                    Color = i == special ? 1 - color : color, Special = i == special });
            }
        }
        double phase = DuckAttackTime - volleys * Settings.AttackCooldown;
        AttackTimer = (float)phase;
        if (DuckStunRemaining > 0)
        {
            DuckPhase = DuckState.Stunned;
            DuckMouthFrame = 1;
            return;
        }
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
            double mouth = DuckFlightTime % cycle;
            double open = Settings.MouthClosedHoldSeconds;
            double hold = open + Settings.MouthOpenSeconds;
            double close = hold + Settings.MouthOpenHoldSeconds;
            DuckMouthFrame = mouth < open ? 3 : mouth < hold ? 2 : mouth < close ? 1 : 2;
        }
    }
}

// 렌더 프레임 시간으로만 진행하는 충돌 감각. 실제 사망 판정은 GameWorld에서 즉시 끝난다.
sealed class BiteFeedback
{
    public const float PauseSeconds = .09f, ShakeSeconds = .24f;
    public float PauseRemaining { get; private set; }
    public float ShakeRemaining { get; private set; }
    public void Trigger() { PauseRemaining = PauseSeconds; ShakeRemaining = ShakeSeconds; }
    public float Advance(float dt)
    {
        dt = Math.Max(0, dt);
        float active = Math.Max(0, dt - PauseRemaining);
        PauseRemaining = Math.Max(0, PauseRemaining - dt);
        ShakeRemaining = Math.Max(0, ShakeRemaining - dt);
        return active;
    }
    public Vector2 Offset
    {
        get
        {
            float fade = ShakeRemaining / ShakeSeconds, elapsed = ShakeSeconds - ShakeRemaining;
            return new(MathF.Cos(elapsed * 85) * 6 * fade, MathF.Cos(elapsed * 110) * 4 * fade);
        }
    }
}
