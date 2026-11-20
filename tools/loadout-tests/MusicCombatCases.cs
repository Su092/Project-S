using System;
using System.Linq;
using S.Core;
namespace LoadoutTests
{
    static class MusicCombatCases
    {
        public static void Run(Action<string,bool,string> check)
        {
            bool count=true,spacing=true,pair=true,range=true;int doubles=0,slots=0;var distribution=new int[5];
            for(int seed=0;seed<5000;seed++)
            {
                var plan=MusicCombatRules.Lanes(new Random(seed));
                var groups=plan.GroupBy(x=>x.Slot).ToArray();distribution[groups.Length-4]++;count&=groups.Length>=4&&groups.Length<=8;
                foreach(var g in groups)
                {
                    var b=g.ToArray();slots++;if(b.Length==2)doubles++;
                    pair&=b.Length==1||b.Length==2&&b[0].Lane!=b[1].Lane&&Math.Abs(b[1].At-b[0].At-.12f)<.00001f;
                    spacing&=Math.Abs(b[0].At-g.Key*.8f)<.00001f;range&=b.All(x=>x.Lane>=0&&x.Lane<4);
                }
            }
            check("Music lane 4-8 quotas",count,"");
            check("Music double consumes one slot, distinct lanes, 0.12s",pair,"");
            check("Music first hits remain 0.8s apart",spacing,"");
            check("Music lanes valid",range,"");
            check("Music count distribution near uniform",distribution.All(x=>x>800&&x<1200),string.Join(",",distribution));
            check("Music independent double frequency near 20%",(double)doubles/slots>.18&&(double)doubles/slots<.22,"");
            bool total=true,assignment=true;
            for(int seed=0;seed<1000;seed++)for(int guns=1;guns<=3;guns++)
            {
                var p=MusicCombatRules.StaggeredRound(new Random(seed),guns);total&=p.Length>=3&&p.Length<=5;assignment&=p.All(x=>x>=0&&x<guns);
            }
            check("Music round total 3-5, not per turret",total,"");
            check("Music assigns only living guns",assignment&&MusicCombatRules.StaggeredRound(new Random(1),0).Length==0,"");
            check("Music summon/finale overlapping reduction stays 80%",MusicCombatRules.DamageMultiplier(true,true)==.2f&&MusicCombatRules.DamageMultiplier(true,false)==.2f&&MusicCombatRules.DamageMultiplier(false,true)==.2f&&MusicCombatRules.DamageMultiplier(false,false)==1,"");
            check("Music finale health restore preserves damage ratio",Math.Abs(MusicCombatRules.RescaleHealth(360,600,300)-180)<.001,"");
            check("Music dead turret never revived by rescale",MusicCombatRules.RescaleHealth(0,600,300)==0,"");
            check("Music health and tracking constants",MusicCombatRules.SummonHealth==.35f&&MusicCombatRules.GunHealth==.05f&&MusicCombatRules.FinaleGunHealth==.10f&&MusicCombatRules.LaserFollow==.30f,"");
            check("Music falling wait separate from interval",MusicCombatRules.FallingHold==.5f&&MusicCombatRules.FallingInterval==.9f,"");
            var boss=new BossState(1000);boss.TakeDamage(600,false);boss.TickMusic(10);
            check("Music phase update cannot trigger old shield/summons",boss.Phase==BossPhase.P2&&!boss.ShieldUp,"");
        }
    }
}
