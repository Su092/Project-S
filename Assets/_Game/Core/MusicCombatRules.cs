using System;
using System.Collections.Generic;
namespace S.Core
{
    /// <summary>Music encounter rules, independent of Unity's frame rate and random source.</summary>
    public static class MusicCombatRules
    {
        public const float FallingHold=.5f, FallingInterval=.9f, LaneInterval=.8f, DoubleDelay=.12f;
        public const float SummonHealth=.35f, GunHealth=.05f, FinaleGunHealth=.10f, LaserFollow=.30f;
        public const int GunLimit=3;
        public struct Beat { public float At; public int Lane, Slot; public bool Paired; }
        public static List<Beat> Lanes(Random random)
        {
            int count=random.Next(4,9);var beats=new List<Beat>();
            for(int slot=0;slot<count;slot++)
            {
                int lane=random.Next(4);bool pair=random.NextDouble()<.2;
                beats.Add(new Beat{At=slot*LaneInterval,Lane=lane,Slot=slot,Paired=pair});
                if(pair)beats.Add(new Beat{At=slot*LaneInterval+DoubleDelay,Lane=(lane+random.Next(1,4))%4,Slot=slot,Paired=true});
            }
            return beats;
        }
        // Each round rolls the TOTAL number, never the count per gun.
        public static int[] StaggeredRound(Random random,int liveGuns)
        {
            if(liveGuns<=0)return Array.Empty<int>();
            var shots=new int[random.Next(3,6)];
            for(int i=0;i<shots.Length;i++)shots[i]=random.Next(liveGuns);
            return shots;
        }
        public static float DamageMultiplier(bool summonAlive,bool finale)=>summonAlive||finale?.2f:1f;
        public static float RescaleHealth(float hp,float oldMax,float newMax)=>oldMax<=0?0:Math.Min(newMax,Math.Max(0,hp/oldMax*newMax));
    }
}
