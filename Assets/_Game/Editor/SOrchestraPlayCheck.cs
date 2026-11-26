using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using S.Core;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    static class SOrchestraPlayCheck
    {
        const string Request = "Logs/orchestra-play.request";
        static bool driving;
        static readonly List<string> results = new();
        static SOrchestraPlayCheck() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (driving || !File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPaused = false;
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                EditorApplication.isPlaying = true;
                return;
            }
            var root = GameRoot.Instance;
            if (!root || !root.Menu || root.Weapons == null) return;
            driving = true;
            results.Clear();
            root.StartCoroutine(Run(root));
        }

        static void Check(string label, bool ok)
        {
            results.Add((ok ? "PASS " : "FAIL ") + label);
            if (!ok) throw new InvalidOperationException(label);
        }

        static IEnumerator Run(GameRoot root)
        {
            var suite = Cases(root);
            for (;;)
            {
                bool more;
                try { more = suite.MoveNext(); }
                catch (Exception e) { results.Add(e.ToString()); break; }
                if (!more) break;
                yield return suite.Current;
            }
            File.WriteAllLines("Logs/orchestra-play.log", results);
            File.Delete(Request);
            EditorApplication.isPlaying = false;
        }

        static IEnumerator Cases(GameRoot root)
        {
            root.Menu.StartForTest();
            yield return null;
            var w = root.Weapons;
            foreach (var id in new[]{"castanets","handpan","handbells","musicbox"})
            {
                Check(id + " acquired", w.AcquireGun(id, root.Run, out _));
                Check(id + " first slot", w.Active.Slots.Cells[0].Id == w.Active.Base.BaseBullet);
                Check(id + " 3D prop", w.GunModelRoot.GetComponentInChildren<MeshRenderer>() != null);
                Check(id + " HUD sprite", UiAssets.Named("hud_gun_" + id));
                Check(id + " score / reward sprites", UiAssets.ItemIcon(w.Active.Base.BaseBullet) && UiAssets.Named("offer_item_" + w.Active.Base.BaseBullet));
            }
            Vector3 origin = new Vector3(200,300,200);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = origin - Vector3.up * .5f;
            floor.transform.localScale = new Vector3(24,1,30);
            Enemy Target(Vector3 offset)
            {
                var target = root.SpawnMusicAuxiliary(origin + offset, 1000);
                var collider = target.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(.6f,1.3f,.6f);
                collider.center = Vector3.up * .65f;
                collider.isTrigger = true;
                return target;
            }
            Projectile Shoot(ProjectileDesc d, Vector3 pos, Vector3 dir)
            {
                var go = new GameObject("Probe_" + d.ModuleId);
                go.transform.position = origin + pos;
                var p = go.AddComponent<Projectile>();
                p.Init(d,dir);
                return p;
            }
            var a = Target(new Vector3(0,0,3));
            var b = Target(new Vector3(2,0,3));
            Physics.SyncTransforms();
            var first = root.Data.Bullets["answer"].ToDesc();
            Vector3 muzzle = Vector3.up * .65f;
            Shoot(first,muzzle,Vector3.forward);
            Shoot(first,muzzle,Vector3.forward);
            yield return new WaitForSeconds(.14f);
            Check("parallel answer cannot resolve itself", Mathf.Abs(1000 - a.Hp - 2*first.Damage)<.01f);
            Shoot(root.Data.Bullets["answer"].ToDesc(),muzzle,Vector3.forward);
            yield return new WaitForSeconds(.14f);
            Check("next read resolves +60%", Mathf.Abs(1000-a.Hp-3.6f*first.Damage)<.01f);
            var bell = root.Data.Bullets["bellmark"].ToDesc();
            var beam = root.Data.Bullets["laser"].ToDesc();
            Shoot(bell,muzzle,Vector3.forward);
            yield return new WaitForSeconds(.12f);
            Shoot(bell,muzzle+Vector3.right*2,Vector3.forward);
            yield return new WaitForSeconds(.12f);
            float hp = a.Hp;
            Shoot(beam,muzzle,Vector3.forward);
            Check("only latest target holds a bell", Mathf.Abs(hp-a.Hp-beam.Damage)<.01f);
            hp = b.Hp;
            Shoot(beam,muzzle+Vector3.right*2,Vector3.forward);
            Shoot(beam,muzzle+Vector3.right*2,Vector3.forward);
            Check("beam rings exactly once", Mathf.Abs(hp-b.Hp-2*beam.Damage-.5f*bell.Damage)<.01f);

            var ground = Target(Vector3.right*6);
            var air = Target(Vector3.right*6 + Vector3.up*2);
            var far = Target(Vector3.right*11);
            var ripple = root.Data.Bullets["ripple"].ToDesc();
            Shoot(ripple,Vector3.right*7 + Vector3.up,Vector3.down);
            yield return new WaitForSeconds(.72f);
            Check("ground ring hits once and excludes air / outside radius", Mathf.Abs(ground.Hp-1000+ripple.Damage)<.01f && air.Hp==1000 && far.Hp==1000);

            var box = root.Data.Bullets["barrelroll"].ToDesc();
            var receiver = Target(new Vector3(-5,0,5));
            var p1 = Shoot(box,new Vector3(-5,.12f,0),new Vector3(0,-.65f,1));
            yield return new WaitForSeconds(.15f);
            w.SwitchTo(0);
            yield return new WaitForSeconds(2.15f);
            Check("deployed cylinder plays three reeds across a switch", !p1 && Mathf.Abs(receiver.Hp-1000+3*box.Damage)<.01f);
            // The beam reflects from a real wall with the remaining length.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = origin + new Vector3(-9,1,0);
            wall.transform.localScale = new Vector3(.12f,2,3);
            var reflection = Target(new Vector3(-6,0,0));
            Physics.SyncTransforms();
            var reflected = root.Data.Bullets["laser"].ToDesc();
            reflected.Bounce = 1;
            Shoot(reflected, new Vector3(-8,.65f,0),Vector3.left);
            Check("harp reflection hits behind the beam origin", Mathf.Abs(reflection.Hp-1000+reflected.Damage)<.01f);

            var timer = root.Data.Bullets["timer"].ToDesc();
            timer.Speed = 2f;
            timer.Payload = new List<ProjectileDesc> { root.Data.Bullets["light"].ToDesc() };
            timer.Payload[0].Speed = 1f;
            Shoot(timer,new Vector3(0,5,-5),Vector3.forward);
            yield return new WaitForSeconds(.25f);
            Check("timer waits before releasing", !GameObject.Find("Payload"));
            yield return new WaitForSeconds(.26f);
            Check("timer releases in flight", GameObject.Find("Payload"));

            Projectile oldest = null;
            for (int i=0;i<3;i++)
            {
                var p = Shoot(box,new Vector3(-9+i,.14f,-8),Vector3.down);
                if (i==0) oldest=p;
                yield return new WaitForSeconds(.12f);
            }
            Check("third cylinder stops oldest", !oldest);
            Shoot(root.Data.Bullets["answer"].ToDesc(),muzzle,Vector3.forward);
            yield return new WaitForSeconds(1.42f);
            hp = a.Hp;
            Shoot(root.Data.Bullets["answer"].ToDesc(),muzzle,Vector3.forward);
            yield return new WaitForSeconds(.15f);
            Check("expired half-beat starts a new phrase", Mathf.Abs(hp-a.Hp-first.Damage)<.01f);
        }
    }
}
