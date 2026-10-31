using MelonLoader;

namespace ClassicSlimes
{
    public static class Cfg
    {
        public static MelonPreferences_Category Cat;
        public static MelonPreferences_Entry<bool> Diagnostics;
        public static MelonPreferences_Entry<bool> SafeGc;

        public static MelonPreferences_Entry<bool> EnableRad, EnableQuantum, EnableMosaic, EnableGlitch, EnableQuicksilver;
        public static MelonPreferences_Entry<float> SpawnRad, SpawnQuantum, SpawnMosaic, SpawnGlitch, SpawnQuicksilver;

        public static MelonPreferences_Entry<float> RadPerSecond;
        public static MelonPreferences_Entry<int> RadDamagePerTick;
        public static MelonPreferences_Entry<bool> RadAuraDamagesPlayer;
        public static MelonPreferences_Entry<bool> QuantumTeleports;
        public static MelonPreferences_Entry<bool> MosaicGlints;
        public static MelonPreferences_Entry<bool> MosaicGlintFire;
        public static MelonPreferences_Entry<bool> GlitchDisguise;
        public static MelonPreferences_Entry<int> GlitchDebugReward;
        public static MelonPreferences_Entry<int> QuicksilverShockDamage;

        public static void Init()
        {
            Cat = MelonPreferences.CreateCategory("ClassicSlimes", "Classic Slimes (SR1 slimes for SR2)");
            Diagnostics = Cat.CreateEntry("Diagnostics", false, "Write diagnostic dumps to UserData/ClassicSlimes");
            SafeGc = Cat.CreateEntry("PauseGcDuringLoad", true, "Pause Unity's incremental GC while slimes and largos are generated (workaround for coreclr crashes, see README)");

            EnableRad = Cat.CreateEntry("EnableRad", true, "Rad slime");
            EnableQuantum = Cat.CreateEntry("EnableQuantum", true, "Quantum slime");
            EnableMosaic = Cat.CreateEntry("EnableMosaic", true, "Mosaic slime");
            EnableGlitch = Cat.CreateEntry("EnableGlitch", true, "Glitch slime");
            EnableQuicksilver = Cat.CreateEntry("EnableQuicksilver", true, "Quicksilver slime");

            SpawnRad = Cat.CreateEntry("SpawnChanceRad", 0.12f, "Share of wild spawns (0..0.9) in Starlight Strand / Ember Valley");
            SpawnQuantum = Cat.CreateEntry("SpawnChanceQuantum", 0.12f, "Share of wild spawns in Powderfall Bluffs / Dreamland");
            SpawnMosaic = Cat.CreateEntry("SpawnChanceMosaic", 0.12f, "Share of wild spawns in Ember Valley / Lava Depths");
            SpawnGlitch = Cat.CreateEntry("SpawnChanceGlitch", 0.05f, "Share of wild spawns in Labyrinth Hub / Waterworks");
            SpawnQuicksilver = Cat.CreateEntry("SpawnChanceQuicksilver", 0.08f, "Share of wild spawns in Waterworks / Labyrinth Hub");

            RadPerSecond = Cat.CreateEntry("RadExposurePerSecond", 5f, "Radiation absorbed per second inside a rad aura (SR1 value: 5)");
            RadDamagePerTick = Cat.CreateEntry("RadDamage", 4, "Damage dealt each time accumulated radiation overflows");
            RadAuraDamagesPlayer = Cat.CreateEntry("RadAuraDamagesPlayer", true, "Rad aura hurts the player");
            QuantumTeleports = Cat.CreateEntry("QuantumTeleports", true, "Quantum slimes create ghosts and teleport");
            MosaicGlints = Cat.CreateEntry("MosaicGlints", true, "Mosaic slimes create glints");
            MosaicGlintFire = Cat.CreateEntry("MosaicGlintFire", true, "Falling glints burst into flames that hurt the player");
            GlitchDisguise = Cat.CreateEntry("GlitchDisguise", true, "Glitch slimes disguise as other slimes");
            GlitchDebugReward = Cat.CreateEntry("GlitchDebugReward", 25, "Newbucks for debugging (vacuuming) a glitch slime, 0 = none");
            QuicksilverShockDamage = Cat.CreateEntry("QuicksilverShockDamage", 10, "Damage of an overcharged quicksilver slime touch");
        }
    }
}
