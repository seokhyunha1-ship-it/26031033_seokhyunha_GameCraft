using System.Numerics;

using System.Text.Json;


sealed class GameSettings
{

    public float PixelsPerMeter
    {
        get;
        set;
    }
 = 10;

    public float ChargeRate
    {
        get;
        set;
    }
 = 50;

    public float JumpMetersPerPoint
    {
        get;
        set;
    }
 = 0.7f;

    public float Gravity
    {
        get;
        set;
    }
 = 300;

    public float Buoyancy
    {
        get;
        set;
    }
 = 300;

    public float SwimSpeed
    {
        get;
        set;
    }
 = 135;

    public float RecoveryPerMeter
    {
        get;
        set;
    }
 = 3;

    public float WrongColorDrain
    {
        get;
        set;
    }
 = 14;

    public float DuckSpeed
    {
        get;
        set;
    }
 = 165;

    public float AttackCooldown
    {
        get;
        set;
    }
 = 5;

    public float FireSpeed
    {
        get;
        set;
    }
 = 360;

    public float ParryWindow
    {
        get;
        set;
    }
 = .20f;

    public float ParryRadius
    {
        get;
        set;
    }
 = 30;

    public float ItemChance
    {
        get;
        set;
    }
 = .65f;

    public float WaterItemChance
    {
        get;
        set;
    }
 = .4f;

    public bool UseProvidedEffects
    {
        get;
        set;
    }
 = false;

    public float FoodRecovery
    {
        get;
        set;
    }
 = 24;

    public float WaterDamageScale
    {
        get;
        set;
    }
 = 1;

    public int? Seed
    {
        get;
        set;
    }

    public string[] Player1Keys
    {
        get;
        set;
    }
 = ["W", "S", "A", "D", "Space"];

    public string[] Player2Keys
    {
        get;
        set;
    }
 = ["Up", "Down", "Left", "Right", "OemCloseBrackets"];

    public float[] StageHeights
    {
        get;
        set;
    }
 = [0, 300, 700, 2200, 4000, 6000];

    public int[] StageBonuses
    {
        get;
        set;
    }
 = [0, 100, 200, 300, 400, 500];

    public float BgmVolume
    {
        get;
        set;
    }
 = .20f;

    public float SfxVolume
    {
        get;
        set;
    }
 = .65f;

    public float WaterExitSideMargin { get; set; } = 8;
    public float WaterExitTopMargin { get; set; } = 6;
    public float WaterExitBottomMargin { get; set; } = 12;

    public float JumpDrainPerMeter { get; set; } = .1f;
    public float AirSteeringAcceleration { get; set; } = 420;
    public float AirSteeringMaxSpeed { get; set; } = 220f;
    public float JumpBiasFullSeconds { get; set; } = 1f;
    public float JumpBiasMaxDegrees { get; set; } = 30f;
    public string[] ParryKeys { get; set; } = ["Space", "Enter"];

    // 창과 표시할 세계 범위를 줄여도 플랫폼 크기와 점프의 물리 단위는 유지한다.
    public float WorldWidth { get; set; } = 2068;
    public float WorldMargin { get; set; } = 40;
    public float PlatformHorizontalGap { get; set; } = 80;
    public int PlatformVerticalGapMin { get; set; } = 120;
    public int PlatformVerticalGapMax { get; set; } = 200;
    public float PlatformScatterY { get; set; } = 90;
    public int SidePlatformsPerSide { get; set; } = 1;
    // 창의 실제 종횡비를 사용해 세로로 늘어난 영역에도 같은 배율의 세계를 표시한다.
    public float ViewHeight => WorldWidth * GameGlobal.ScreenSize.Height / GameGlobal.ScreenSize.Width;
    public float FlapDirectionWindow { get; set; } = .30f;
    public float FlapCostRatio { get; set; } = .25f;
    public float FlapSpeedMultiplier { get; set; } = 1.6f;
    public float FlapDirectionLockSeconds { get; set; } = .18f;
    public float FlapOverspeedDeceleration { get; set; } = 600f;
    public float ItemDrawSize { get; set; } = 80;
    public float DuckStunSeconds { get; set; } = 1.2f;
    public float WaterMinDamage { get; set; } = 10;
    public float FireHitStunSeconds { get; set; } = .3f;
    public float FireDrawSize { get; set; } = 112;
    public float FireWindup { get; set; } = 0.2f;
    public float FireHold { get; set; } = 0.15f;
    public float FireRecovery { get; set; } = 0.15f;
    public float MouthOpenSeconds { get; set; } = .35f;
    public float MouthOpenHoldSeconds { get; set; } = .20f;
    public float MouthCloseSeconds { get; set; } = .35f;
    public float MouthClosedHoldSeconds { get; set; } = .20f;

    public float MaxJumpCharge { get; set; } = 75;
    public float DuckSpeedPerStage { get; set; } = 3;
    public float DuckDrawWidthRatio { get; set; } = 1.9f;
    public float DuckVisibleHeight { get; set; } = 400;
    public float DuckDrawHeight => WorldWidth * DuckDrawWidthRatio * 3910f / 3461f;
    public float FlapMaxSpeed { get; set; } = 480;
    public float FlapVerticalCarrySpeed { get; set; } = 165;
    public float FlapVerticalDeceleration { get; set; } = 900;
    public float AirReleaseDrag { get; set; } = 2;

    public void Validate()
    {
        float[] positive = [FireHitStunSeconds, ItemDrawSize, DuckStunSeconds, WaterMinDamage, FireDrawSize, MaxJumpCharge, DuckDrawWidthRatio, DuckVisibleHeight, DuckDrawHeight, FlapMaxSpeed, FlapVerticalCarrySpeed, FlapVerticalDeceleration, WorldWidth, WorldMargin, PlatformHorizontalGap, PixelsPerMeter, ChargeRate, JumpMetersPerPoint,
            FlapDirectionWindow, FlapCostRatio, FlapSpeedMultiplier, FlapDirectionLockSeconds, FlapOverspeedDeceleration, ParryWindow, AttackCooldown, FireWindup, FireHold, FireRecovery,
            MouthOpenSeconds, MouthOpenHoldSeconds, MouthCloseSeconds, MouthClosedHoldSeconds];
        if (MaxJumpCharge > 100 || !float.IsFinite(DuckSpeed) || DuckSpeed < 0 ||
            !float.IsFinite(DuckSpeedPerStage) || DuckSpeedPerStage < 0 || DuckSpeedPerStage > 10 ||
            !float.IsFinite(AirReleaseDrag) || AirReleaseDrag < 0 ||
            !float.IsFinite(ItemChance) || ItemChance < 0 || ItemChance > 1 ||
            !float.IsFinite(WaterItemChance) || WaterItemChance < 0 || WaterItemChance > 1 ||
            positive.Any(v => !float.IsFinite(v) || v <= 0) || WorldWidth < 800 ||
            WorldWidth - 2 * WorldMargin < 600 || WorldMargin < 20 ||
            !float.IsFinite(Gravity) || Gravity < 0 || PlatformVerticalGapMin < 1 ||
            !float.IsFinite(PlatformScatterY) || PlatformScatterY < 0 || PlatformScatterY > PlatformVerticalGapMin - 12 ||
            // 최소 물 높이 80을 포함한 다음 생성 묶음과 중심 높이 순서가 뒤섞이지 않게 한다.
            PlatformScatterY * 2 >= PlatformVerticalGapMin + 80 ||
            SidePlatformsPerSide < 0 || SidePlatformsPerSide > 10 ||
            FlapCostRatio > 1 || FlapSpeedMultiplier < 1 || FlapSpeedMultiplier > 4 || FlapDirectionLockSeconds > 1 ||
            PlatformVerticalGapMax < PlatformVerticalGapMin || PlatformVerticalGapMax == int.MaxValue ||
            AttackCooldown <= FireWindup + FireHold + FireRecovery)
            throw new InvalidDataException("맵 폭, 물리 상수 또는 팔딱/오리 시간 설정이 올바르지 않습니다.");
    }

    public static GameSettings Load()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "game-settings.json");
        var settings = File.Exists(p) ? JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(p))
            ?? throw new InvalidDataException("설정 파일이 비어 있습니다.") : new();
        settings.Validate();
        return settings;
    }

}
