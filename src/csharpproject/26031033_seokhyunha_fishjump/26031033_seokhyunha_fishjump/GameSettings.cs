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
 = .5f;

    public float Gravity
    {
        get;
        set;
    }
 = 260;

    public float Buoyancy
    {
        get;
        set;
    }
 = 210;

    public float SwimSpeed
    {
        get;
        set;
    }
 = 95;

    public float RecoveryPerMeter
    {
        get;
        set;
    }
 = 6;

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
 = 13;

    public float AttackCooldown
    {
        get;
        set;
    }
 = 4;

    public float FireSpeed
    {
        get;
        set;
    }
 = 180;

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
 = 30;

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
 = ["Up", "Down", "Left", "Right", "Enter"];

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

    public static GameSettings Load()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "game-settings.json");
        return File.Exists(p) ? JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(p))! : new();
    }

}
