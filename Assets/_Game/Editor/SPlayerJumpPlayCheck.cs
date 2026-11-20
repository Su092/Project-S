using System;
using System.IO;
using System.Reflection;
using System.Text;
using S.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace S.EditorTools
{
    // Uses actual Input System events and the live CharacterController in Main.
    [InitializeOnLoad]
    public static class SPlayerJumpPlayCheck
    {
        const string Request = "Logs/player-jump-play.request";
        static readonly StringBuilder Log = new();
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static Keyboard keyboard, oldKeyboard;
        static Mouse mouse, oldMouse;
        static Key[] keys = Array.Empty<Key>();
        static int stage, scenario, pass, fail;
        static double started, pausedAt;
        static float at, baseline, peak;
        static readonly string[] Names = { "idle", "reload", "moving", "AI idle", "second-floor idle", "after landing" };

        static SPlayerJumpPlayCheck()
        {
            EditorApplication.update += () => Tick(false);
            InputSystem.onBeforeUpdate += () =>
            {
                if (InputState.currentUpdateType != InputUpdateType.Dynamic || keyboard == null) return;
                keyboard.MakeCurrent(); mouse.MakeCurrent();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                InputSystem.QueueStateEvent(mouse, new MouseState());
            };
            InputSystem.onAfterUpdate += () =>
            {
                if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
                keyboard?.MakeCurrent(); mouse?.MakeCurrent(); Tick(true);
            };
        }

        static void Check(string name, bool ok)
        {
            Log.AppendLine((ok ? "PASS " : "FAIL ") + name);
            if (ok) pass++; else fail++;
            File.WriteAllText("Logs/player-jump-play-check.log", Log.ToString());
        }
        static void Next(int next) { stage = next; at = Time.time; }
        static void Tick(bool runtime)
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (started == 0) started = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - started > 120) throw new TimeoutException("Jump stage " + stage);
                if (!EditorApplication.isPlaying) { EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return; }
                EditorApplication.isPaused = false;
                if (!runtime) return;
                var root = GameRoot.Instance;
                if (!root || root.Run == null || !root.Menu || !root.Player) return;
                var player = root.Player;
                var cc = player.GetComponent<CharacterController>();
                if (stage == 0)
                {
                    oldKeyboard = Keyboard.current; oldMouse = Mouse.current;
                    keyboard = InputSystem.AddDevice<Keyboard>("JumpCheckKeyboard");
                    mouse = InputSystem.AddDevice<Mouse>("JumpCheckMouse");
                    keyboard.MakeCurrent(); mouse.MakeCurrent();
                    root.InvulnerableForTest = true; root.AutoAcceptOffers = true;
                    root.Menu.StartOrChoose(); root.Menu.SelectGun("rifle"); root.Menu.StartOrChoose();
                    typeof(GameRoot).GetField("_spawnTimer", Private).SetValue(root, 10000f);
                    Next(1);
                }
                else if (stage == 1)
                {
                    player.AiControlled = scenario == 3;
                    player.AiMove = Vector3.zero; player.AiWantsJump = false;
                    keys = Array.Empty<Key>();
                    // The final case stays where the preceding jump landed.
                    if (scenario != 5)
                    {
                        cc.enabled = false;
                        player.transform.position = scenario == 4 ? new Vector3(-20.5f, 6.08f, -14) : new Vector3(0, .08f, -32);
                        cc.enabled = true;
                    }
                    Next(2);
                }
                else if (stage == 2 && Time.time - at > .8f)
                {
                    baseline = peak = player.transform.position.y;
                    Check(Names[scenario] + " settled grounded", cc.isGrounded);
                    if (scenario == 1) { root.Weapons.Active.Ammo = 0; keys = new[] { Key.R }; }
                    Next(3);
                }
                else if (stage == 3 && Time.time - at > .1f)
                {
                    if (scenario == 1) Check("reload started via R", root.Weapons.Active.Reloading);
                    keys = scenario == 2 ? new[] { Key.Space, Key.W } : new[] { Key.Space };
                    if (scenario == 3) { keys = Array.Empty<Key>(); player.AiWantsJump = true; }
                    Next(4);
                }
                else if (stage == 4)
                {
                    float elapsed = Time.time - at;
                    peak = Mathf.Max(peak, player.transform.position.y);
                    if (elapsed > .06f) keys = Array.Empty<Key>();
                    // A fresh press while airborne must not produce a second impulse.
                    if (scenario == 0 && elapsed > .2f && elapsed < .3f) keys = new[] { Key.Space };
                    if (elapsed < (scenario == 1 ? Mathf.Max(1.3f, root.Weapons.Active.Stats.Reload + .3f) : 1.3f)) return;
                    Check(Names[scenario] + " jumped, rise=" + (peak - baseline).ToString("F3"), peak - baseline > .65f);
                    Check(Names[scenario] + " landed", cc.isGrounded && Mathf.Abs(player.transform.position.y - baseline) < .15f);
                    if (scenario == 0) Check("airborne press cannot double jump", peak - baseline < player.JumpHeight + .15f);
                    if (scenario == 1) Check("reload completes normally", !root.Weapons.Active.Reloading && root.Weapons.Active.Ammo == root.Weapons.Active.Stats.Mag);
                    scenario++;
                    if (scenario < Names.Length) Next(1);
                    else
                    {
                        baseline = player.transform.position.y;
                        root.SetPaused(true); keys = new[] { Key.Space };
                        pausedAt = EditorApplication.timeSinceStartup; Next(5);
                    }
                }
                else if (stage == 5 && EditorApplication.timeSinceStartup - pausedAt > .4)
                {
                    Check("pause blocks jump", Mathf.Abs(player.transform.position.y - baseline) < .001f);
                    keys = Array.Empty<Key>(); root.SetPaused(false); Next(6);
                }
                else if (stage == 6 && Time.time - at > .25f)
                {
                    Check("resume does not replay paused jump", Mathf.Abs(player.transform.position.y - baseline) < .1f);
                    Finish();
                }
            }
            catch (Exception e) { Log.AppendLine(e.ToString()); fail++; Finish(); }
        }
        static void Finish()
        {
            keys = Array.Empty<Key>();
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            if (oldMouse != null && oldMouse.added) oldMouse.MakeCurrent();
            keyboard = oldKeyboard = null; mouse = oldMouse = null;
            Log.AppendLine($"RESULT Jump play {pass}/{pass + fail} passed");
            File.WriteAllText("Logs/player-jump-play-check.log", Log.ToString());
            File.Delete(Request); Time.timeScale = 1; EditorApplication.isPlaying = false;
            stage = scenario = pass = fail = 0; started = 0; Log.Clear();
        }
    }
}
