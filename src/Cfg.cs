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
        public static MelonPreferences_Entry<bool> QuantumTeleports, QuantumPhaseWalls, QuantumHologram, GlitchTrail;
        public static MelonPreferences_Entry<bool> MosaicGlints;
        public static MelonPreferences_Entry<bool> MosaicGlintFire;
        public static MelonPreferences_Entry<bool> GlitchDisguise;
        public static MelonPreferences_Entry<int> GlitchDebugReward;
        public static MelonPreferences_Entry<int> QuicksilverShockDamage;
        public static MelonPreferences_Entry<bool> EnableFoods, WildFoods, RadHud, GlitchSR1Face, PhaseLemonTrees;
        public static MelonPreferences_Entry<float> WildFoodShare, RadRecoveryPerSecond;
        public static MelonPreferences_Entry<int> RadDomeStyle;
        public static MelonPreferences_Entry<bool> RadHaze, GlitchFlees, QuicksilverRuns, MosaicJelly, QuantumJelly, SpawnHomes, MarketBoard;
        public static MelonPreferences_Entry<bool> ClassicGordos;
        public static MelonPreferences_Entry<int> GordoFood;

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

            SpawnHomes = Cat.CreateEntry("SpawnHomes", true, "SR1-like fixed spots: in each zone about 30 % of the wild slime spawners are a classic slime's home and spawn it often (4x the share above, 25-80 %), the others never (off: the share above at every spawner)");

            RadPerSecond = Cat.CreateEntry("RadExposurePerSecond", 5f, "Radiation absorbed per second inside a rad aura (SR1 value: 5)");
            RadDamagePerTick = Cat.CreateEntry("RadDamage", 4, "Damage dealt each time accumulated radiation overflows");
            RadAuraDamagesPlayer = Cat.CreateEntry("RadAuraDamagesPlayer", true, "Rad aura hurts the player");
            QuantumTeleports = Cat.CreateEntry("QuantumTeleports", true, "Quantum slimes create ghosts and teleport");
            QuantumPhaseWalls = Cat.CreateEntry("QuantumPhaseWalls", true, "Hungry quantum slimes phase through corral walls (fed again they are solid again)");
            QuantumHologram = Cat.CreateEntry("QuantumHologram", true, "Quantum slimes now and then flicker like a hologram (horizontal scanlines)");
            GlitchTrail = Cat.CreateEntry("GlitchTrail", true, "Glitch slimes leave a trail of pixels behind them");
            MosaicGlints = Cat.CreateEntry("MosaicGlints", true, "Mosaic slimes create glints");
            MosaicGlintFire = Cat.CreateEntry("MosaicGlintFire", true, "Falling glints burst into flames that hurt the player");
            GlitchDisguise = Cat.CreateEntry("GlitchDisguise", true, "Glitch slimes disguise as other slimes");
            GlitchDebugReward = Cat.CreateEntry("GlitchDebugReward", 25, "Newbucks for debugging (vacuuming) a glitch slime, 0 = none");
            QuicksilverShockDamage = Cat.CreateEntry("QuicksilverShockDamage", 10, "Damage of an overcharged quicksilver slime touch");

            EnableFoods = Cat.CreateEntry("EnableSR1Foods", true, "Enable the SR1 foods Oca Oca, Silver Parsnip and Phase Lemon (favourites of the classic slimes; plantable in gardens)");
            WildFoods = Cat.CreateEntry("WildSR1Foods", true, "SR1 foods also grow on wild veggie patches / fruit trees");
            MarketBoard = Cat.CreateEntry("BiggerMarketBoard", true, "Make room on the plort market board for the classic plorts (uses the empty 10th column and fills the short columns; vanilla shows only 25 plorts)");
            PhaseLemonTrees = Cat.CreateEntry("PhaseLemonTrees", true, "SR1 phase lemon trees: they grow see-through phasing lemons that can't be vacuumed; shoot another fruit into the tree to knock a solid phase lemon loose");
            WildFoodShare = Cat.CreateEntry("WildSR1FoodPatchShare", 0.25f, "Share of the wild veggie patches / fruit trees in the SR1 food zones that grow an SR1 food instead (the whole patch / tree, always the same spots)");
            RadHud = Cat.CreateEntry("RadiationMeter", true, "Show the radiation meter (SR1 style) above the health bar while irradiated");
            RadRecoveryPerSecond = Cat.CreateEntry("RadRecoveryPerSecond", 3f, "Radiation that wears off per second once out of a rad aura");
            RadDomeStyle = Cat.CreateEntry("RadDomeStyle", 0, "Rad aura look: 0 refracting sphere with a soft green edge (SR1), 1 bubble (SR2 icon style), 2 shadow aura, 3 hyper aura, 4 solar shield, 5 dream bubble, 6 fresnel, 7 bubble (particle shader)");
            GlitchSR1Face = Cat.CreateEntry("GlitchSR1Face", true, "Glitch slimes use the SR1 square-pixel face");
            RadHaze = Cat.CreateEntry("RadHaze", true, "The rad aura boils with SR1-style heat haze (refraction volume inside the dome)");
            GlitchFlees = Cat.CreateEntry("GlitchFlees", true, "A glitch slime that was caught (its disguise blown) and thrown away runs off in a panic");
            QuicksilverRuns = Cat.CreateEntry("QuicksilverRuns", true, "Quicksilver slimes never hop: they race along the ground");
            MosaicJelly = Cat.CreateEntry("MosaicJelly", true, "Whole-root jelly for the legacy separate Mosaic glass shell only; the default native surface keeps SR2 contact squash");
            QuantumJelly = Cat.CreateEntry("QuantumJelly", true, "Quantum slimes are see-through golden jelly with the SR1 spiral inside (off: opaque body with a painted spiral)");
            ClassicGordos = Cat.CreateEntry("ClassicGordos", true, "SR1 gordos of the classic slimes: a Rad, a Quantum and a Mosaic gordo at fixed spots of their zones (eaten count and burst are saved like a vanilla gordo)");
            GordoFood = Cat.CreateEntry("GordoFoodCount", 50, "Foods a classic gordo eats before it bursts (SR1 value: 50)");
        }
    }
}
