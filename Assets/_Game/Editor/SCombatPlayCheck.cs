using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using S.View;
using S.Core;

namespace S.EditorTools
{
    [InitializeOnLoad]
    public static class SCombatPlayCheck
    {
        const string Request = "Logs/combat-play.request";
        static readonly StringBuilder Log = new StringBuilder();
        static int _phase, _pass, _fail;
        static double _started, _at;
        static Vector3 _magRest, _frozen;
        static Quaternion _frozenRotation;
        static float _reloadLeft;
        static Enemy _enemy;
        static Vector3 _enemyPosition;
        static float _hp;
        static SCombatPlayCheck()
        {
            if (!File.Exists(Request)) return;
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        static void Check(string text, bool ok)
        { Log.AppendLine((ok ? "PASS " : "FAIL ") + text); if (ok) _pass++; else _fail++; }
        static void Next(int phase)
        { _phase = phase; _at = EditorApplication.timeSinceStartup; }
        static void Tick()
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (EditorApplication.timeSinceStartup - _started > 45) { Check("runtime check timeout", false); Finish(); return; }
                if (!EditorApplication.isPlaying)
                {
                    if (_phase == 0) { _phase = -1; EditorApplication.isPlaying = true; }
                    return;
                }
                var root = GameRoot.Instance;
                if (root == null || root.Menu == null || root.Run == null) return;
                double elapsed = EditorApplication.timeSinceStartup - _at;
                var weapons = root.Weapons;
                var holder = weapons.GunModelRoot;
                var gun = weapons.Active;
                if (_phase <= 0)
                {
                    root.InvulnerableForTest = true;
                    root.Menu.StartOrChoose();
                    Check("all guns available at start", root.Menu.RowCount == root.Data.GunOrder.Count);
                    root.Menu.SelectGun("launcher"); root.Menu.StartOrChoose();
                    Check("chosen launcher starts alone", weapons.GunCount == 1 && weapons.Active.Base.Id == "launcher" && !root.Paused);
                    Next(10); // 等待旧枪网格在帧末销毁，再记录新弹匣的静止位置。
                }
                else if (_phase == 10 && elapsed > 0.1)
                {
                    var magazine = holder.Find("WeaponRig/Joint1"); _magRest = magazine.localPosition;
                    gun.Ammo--; gun.StartReload(); Next(1);
                }
                else if (_phase == 1 && elapsed > 1.1)
                {
                    Check("real reload moves magazine", gun.Reloading && holder.Find("WeaponRig/Joint1").localPosition.y < _magRest.y - 0.1f);
                    root.SetPaused(true); _frozen = holder.localPosition; _frozenRotation = holder.localRotation; _reloadLeft = gun.ReloadLeft;
                    Next(2);
                }
                else if (_phase == 2 && elapsed > 0.35)
                {
                    Check("pause freezes reload timer and mesh", Mathf.Approximately(_reloadLeft, gun.ReloadLeft) && holder.localPosition == _frozen && holder.localRotation == _frozenRotation);
                    root.SetPaused(false); Next(3);
                }
                else if (_phase == 3 && elapsed > 1.8)
                {
                    Check("reload fills magazine and restores mesh", !gun.Reloading && gun.Ammo == gun.Stats.Mag && Vector3.Distance(holder.Find("WeaponRig/Joint1").localPosition, _magRest) < 0.001f);
                    _frozen = holder.localPosition; _frozenRotation = holder.localRotation;
                    // 向无遮挡的天空测试，避免场地障碍提前命中并销毁弹丸。
                    root.Player.InputLocked = true;
                    root.Cam.transform.rotation = Quaternion.Euler(-75f, 0f, 0f);
                    var cast = gun.TryFire(out _, () => 0.5f); weapons.SpawnCastForAI(cast);
                    Next(4);
                }
                else if (_phase == 4 && elapsed > 0.06)
                {
                    Check("actual shot spends ammunition and kicks weapon", gun.Ammo < gun.Stats.Mag && Quaternion.Angle(holder.localRotation, _frozenRotation) > 0.1f);
                    Check("actual shot creates projectile", UnityEngine.Object.FindObjectsByType<Projectile>().Length > 0);
                    _enemy = root.Enemies.FirstOrDefault(e => e != null && e.Alive && e.Speed > 0);
                    Check("enemy runtime motion initialized", _enemy != null && _enemy.GetComponent<EnemyMotion>()?.JointCount >= 3);
                    if (_enemy != null) _enemyPosition = _enemy.transform.position;
                    Next(5);
                }
                else if (_phase == 5 && elapsed > 0.5)
                {
                    Check("enemy moves and animates limbs", _enemy != null && Vector3.Distance(_enemyPosition, _enemy.transform.position) > 0.05f &&
                        _enemy.transform.Find("EnemyRig").GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("Joint") && Quaternion.Angle(t.localRotation, Quaternion.identity) > 2));
                    Check("recoil settles after firing", Quaternion.Angle(holder.localRotation, _frozenRotation) < 0.3f);
                    if (_enemy != null)
                    {
                        var cc = _enemy.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                        _enemy.transform.position = root.PlayerTransform.position + Vector3.forward * 0.7f;
                        _enemy.Speed = 0; _enemy.Hp = 10000;
                        _hp = root.Hp; root.InvulnerableForTest = false;
                    }
                    Next(6);
                }
                else if (_phase == 6 && elapsed > 0.09)
                {
                    Check("attack has windup before damage", Mathf.Approximately(root.Hp, _hp) && _enemy != null &&
                        Quaternion.Angle(_enemy.transform.Find("EnemyRig").localRotation, Quaternion.identity) > 1);
                    Next(7);
                }
                else if (_phase == 7 && elapsed > 0.25)
                {
                    Check("attack damage follows windup", root.Hp < _hp);
                    root.InvulnerableForTest = true;
                    var inv = root.GetComponent<InventoryView>();
                    inv.ShowOffers(new List<Offer> { new Offer { Id = "sniper", IsGun = true, Rarity = Rarity.Rare },
                        new Offer { Id = "std", IsBullet = true }, new Offer { Id = "light", IsBullet = true } });
                    Check("gun upgrade opens three visible reward cards", root.Paused && inv.OfferCardCount == 3 && inv.OfferIconBoundCount == 3);
                    var card = inv.GetComponentsInChildren<OfferCardRefs>().First(c => c.CardTitle.text == root.Data.Guns["sniper"].Name);
                    card.Button.onClick.Invoke();
                    Check("clicking gun reward equips second gun and resumes", weapons.GunCount == 2 && weapons.Active.Base.Id == "sniper" && !root.Paused);
                    while (root.Run.Bag.Count < root.Run.BagCapacity) root.Run.Bag.Add(root.Data.MakeItem("std"));
                    inv.ShowOffers(new List<Offer> { new Offer { Id = "rifle", IsGun = true, Rarity = Rarity.Rare } });
                    var replacement = inv.GetComponentsInChildren<OfferCardRefs>().First(c => c.CardTitle.text == root.Data.Guns["rifle"].Name);
                    replacement.Button.onClick.Invoke();
                    Check("full bag keeps reward pending", inv.IsOfferVisible && weapons.Active.Base.Id == "sniper");
                    inv.Close();
                    Check("pending reward permits bag management", inv.IsBagVisible && root.Paused);
                    root.Run.Bag.RemoveAt(0);
                    inv.Close();
                    Check("returning from bag restores pending reward", inv.IsOfferVisible && inv.OfferCardCount == 1);
                    replacement = inv.GetComponentsInChildren<OfferCardRefs>().First(c => c.CardTitle.text == root.Data.Guns["rifle"].Name);
                    var oldBullet = weapons.Active.Slots.Cells[0];
                    replacement.Button.onClick.Invoke();
                    Check("replacement completes and returns installed items", weapons.Active.Base.Id == "rifle" && root.Run.Bag.Contains(oldBullet) && !root.Paused);
                    Finish();
                }
            }
            catch (Exception e) { _fail++; Log.AppendLine("FAIL " + e); Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Tick;
            Log.AppendLine($"RESULT {_pass}/{_pass + _fail} passed");
            File.WriteAllText("Logs/combat-play-check.log", Log.ToString());
            if (File.Exists(Request)) File.Delete(Request);
            Debug.Log(Log.ToString());
            EditorApplication.isPlaying = false;
        }
    }
}
