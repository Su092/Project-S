using UnityEngine;

namespace S.View
{
    /// <summary>Music model selection. Gameplay strategies are still owned by the existing actor definitions.</summary>
    public static class MusicEnemyAssets
    {
        public const string Ballerina = "S16_MusicBoxBallerina";
        public const string Conductor = "S29_WanderingConductor";
        public static readonly string[] Commons =
        {
            "S02_DrumBeetle", "S03_HornHound", "S11_CymbalMoth", "S19_CastanetFish", "S07_LostScoreGhost"
        };
        public static readonly string[] Elites =
        {
            "S08_MetronomeGuard", "S08_MetronomeGuard_Rehearsal", "S08_MetronomeGuard_Ceremonial",
            "S14_XylophoneStegosaur", "S14_XylophoneStegosaur_Redwood", "S14_XylophoneStegosaur_Ebony",
            "S21_BagpipeBoar", "S21_BagpipeBoar_Parade", "S21_BagpipeBoar_Ceremony"
        };
        public static string Select(string kind, bool elite)
        {
            if (elite) return Elites[Random.Range(0, Elites.Length)];
            return kind switch
            {
                "Crawler" => Random.value < .5f ? Commons[0] : Commons[3],
                "Brute" => Commons[4],
                "Bomber" => Commons[1],
                "Turret" => Commons[2],
                _ => Commons[0]
            };
        }
        public static string SelectBoss() => Random.value < .5f ? Ballerina : Conductor;
        public static string LegacyBossAction(string asset, string action)
        {
            bool dancer = asset == Ballerina;
            return action switch
            {
                "Volley" or "TrackingFire" => dancer ? "Pluck_Beat" : "Conduct_Stagger",
                "SpikeCast" => dancer ? "Waltz_Windup" : "GunSummon_Cue",
                "Shockwave" => dancer ? "Waltz_Stop" : "Laser_Cue",
                "Summon" => dancer ? "Curtsey" : "Summon_Cue",
                "PhaseTransition" => dancer ? "Rewind_Windup" : "Finale_Windup",
                "Stagger" => "Hit",
                _ => action
            };
        }
    }
}
