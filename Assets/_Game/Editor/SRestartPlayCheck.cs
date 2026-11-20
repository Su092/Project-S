using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using S.Core;
using S.View;

namespace S.EditorTools
{
    /// <summary>Runs the real defeat -> R -> scene reload -> weapon choice path twice.</summary>
    [InitializeOnLoad]
    public static class SRestartPlayCheck
    {
        const string Request = "Logs/restart-play.request";
        static readonly StringBuilder Log = new();
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static int stage, cycle, pass, fail;
        static double started, at;
        static GameRoot oldRoot;
        static Enemy oldEnemy;
        static BossView oldBoss;
        static ExperiencePickup oldPickup;
        static SData cachedData;
        static RunDirector oldDirector;
        static Keyboard keyboard;
        static bool pressRestart;

        static SRestartPlayCheck()
        {
            EditorApplication.update += Tick;
            InputSystem.onBeforeUpdate += () =>
            {
                if (pressRestart && keyboard != null && keyboard.added && InputState.currentUpdateType == InputUpdateType.Dynamic)
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
            };
        }

        [MenuItem("S/Checks/Restart Play Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before starting the restart check.");
            SFoundryMapBuilder.OpenMainScene();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Request, "");
        }

        static void Check(string name, bool ok)
        {
            Log.AppendLine((ok ? "PASS " : "FAIL ") + name);
            if (ok) pass++; else fail++;
            File.WriteAllText("Logs/restart-play-check.log", Log.ToString());
        }

        static void Tick()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (started == 0) started = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - started > 120)
                    throw new TimeoutException("Restart check stage " + stage + ", cycle " + cycle);
                if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
                var root = GameRoot.Instance;
                if (!root || root.Run == null || root.Menu == null) return;

                if (stage == 0)
                {
                    Check("cycle " + cycle + ": main scene and paused menu", SceneManager.GetActiveScene().path == SceneBuilder.ScenePath &&
                        root.Menu.IsOpen && root.Paused && Time.timeScale == 0);
                    Check("cycle " + cycle + ": elapsed/threat zero", root.RunElapsed == 0 && root.Run.Threat == 0);
                    Check("cycle " + cycle + ": initial countdowns", root.Director.SecondsToElite == 300 && root.Director.SecondsToBoss == 600);
                    Check("cycle " + cycle + ": first phase and no event flags", root.Director.CurrentPhase.Name == root.Data.Director.Phases[0].Name &&
                        !root.Director.BossSpawned && root.ElitesSpawned == 0);
                    Check("cycle " + cycle + ": independent director", !ReferenceEquals(root.Director, root.Data.Director) &&
                        !ReferenceEquals(root.Director, oldDirector));
                    Check("cycle " + cycle + ": full health and fresh progression", root.Hp == root.MaxHp && root.Run.Level == 1 &&
                        root.Run.Xp == 0 && root.Run.Kills == 0 && root.Run.Bag.Count == 0);
                    Check("cycle " + cycle + ": no old combat or reward state", root.AliveEnemies == 0 && root.ExperiencePickups.Count == 0 &&
                        !root.Boss && !root.InventoryOpen && !root.HasPendingOffers &&
                        !UnityEngine.Object.FindAnyObjectByType<Projectile>());
                    Check("cycle " + cycle + ": pillars hidden", root.Zones.Map.EnergyPillars.All(p => !p.gameObject.activeSelf));
                    if (cycle > 0)
                    {
                        Check("reload reuses only configuration cache", ReferenceEquals(cachedData, root.Data) && cachedData.Director.Elapsed == 0);
                        Check("previous scene objects destroyed", !oldRoot && !oldEnemy && !oldBoss && !oldPickup);
                    }
                    cachedData = root.Data;
                    root.Player.AiControlled = true;
                    root.InvulnerableForTest = true;
                    root.Menu.StartOrChoose();
                    root.Menu.SelectGun(cycle == 1 ? "smg" : "rifle");
                    root.Menu.StartOrChoose();
                    Check("weapon choice starts a fresh run", !root.Menu.IsOpen && !root.Paused && Time.timeScale == 1 &&
                        root.Weapons.GunCount == 1 && root.Weapons.Active.Base.Id == (cycle == 1 ? "smg" : "rifle"));
                    stage = 1; at = EditorApplication.timeSinceStartup;
                }
                else if (stage == 1 && EditorApplication.timeSinceStartup - at > .4)
                {
                    Check("timer and ordinary spawning resume", root.RunElapsed > 0 && root.AliveEnemies > 0);
                    root.Director.SeekTo(cycle == 0 ? 120 : 600);
                    typeof(GameRoot).GetMethod("TickDirector", Private).Invoke(root, new object[] { .01f });
                    if (cycle > 0)
                        Check("elite and Boss events fire again", root.ElitesSpawned == 4 && root.Director.BossSpawned && root.Boss &&
                            root.Zones.Map.EnergyPillars.All(p => p.gameObject.activeSelf));
                    if (cycle == 2) { Finish(); return; }
                    oldRoot = root; oldDirector = root.Director;
                    oldEnemy = root.Enemies[0]; oldBoss = root.Boss;
                    oldEnemy.ApplyDamage(100000);
                    oldPickup = root.ExperiencePickups.LastOrDefault();
                    Check("prior run has a kill and uncollected XP", root.Run.Kills > 0 && oldPickup);
                    root.Run.AddXp(50);
                    root.InvulnerableForTest = false;
                    root.DamagePlayer(100000);
                    Check("lethal damage pauses defeat screen", root.Hp <= 0 && root.Paused && Time.timeScale == 0);
                    stage = 2; at = EditorApplication.timeSinceStartup;
                }
                else if (stage == 2 && EditorApplication.timeSinceStartup - at > .2)
                {
                    keyboard = Keyboard.current;
                    if (keyboard == null) throw new InvalidOperationException("No keyboard.");
                    pressRestart = true;
                    stage = 3;
                }
                else if (stage == 3 && root != oldRoot)
                {
                    pressRestart = false;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    cycle++; stage = 0;
                }
            }
            catch (Exception ex) { Check(ex.ToString(), false); Finish(); }
        }

        static void Finish()
        {
            pressRestart = false;
            if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Log.AppendLine("RESULT " + pass + "/" + (pass + fail));
            File.WriteAllText("Logs/restart-play-check.log", Log.ToString());
            File.Delete(Request);
            Debug.Log("[S][RestartCheck] " + pass + "/" + (pass + fail));
            EditorApplication.isPlaying = false;
            stage = cycle = pass = fail = 0; started = at = 0;
            oldRoot = null; oldEnemy = null; oldBoss = null; oldPickup = null;
            cachedData = null; oldDirector = null; keyboard = null; Log.Clear();
        }
    }
}
