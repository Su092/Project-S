using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using S.View;

namespace S.EditorTools
{
    [InitializeOnLoad]
    static class SBackstageCheck
    {
        const string Request="Logs/backstage-check.request";
        const string Output="_Inbox/backstage/preview/";
        static bool driving;
        static Keyboard keyboard, original;
        static SBackstageCheck() => EditorApplication.update+=Poll;
        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || driving) return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SBackstageLayout.Ensure();
                EditorApplication.isPaused=false;
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                EditorApplication.isPlaying=true;
                return;
            }
            var root=GameRoot.Instance;
            if (!root || !root.Menu || !root.Menu.Backstage) return;
            driving=true;
            root.StartCoroutine(Check(root));
        }

        static IEnumerator Check(GameRoot root)
        {
            original=Keyboard.current;
            keyboard=InputSystem.AddDevice<Keyboard>();
            var stack=new Stack<IEnumerator>();
            stack.Push(Tour(root));
            while (true)
            {
                bool more;
                var sequence=stack.Peek();
                try { more=sequence.MoveNext(); }
                catch (Exception e) { Finish(e.ToString()); yield break; }
                if (!more) { stack.Pop(); if (stack.Count>0) continue; Finish("PASS title, settings, room, mirror x6, score, archive artwork, door and combat spawn"); yield break; }
                if (sequence.Current is IEnumerator nested) stack.Push(nested);
                else yield return sequence.Current;
            }
        }

        static void Finish(string status)
        {
            if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if(original!=null && original.added) original.MakeCurrent();
            File.WriteAllText("Logs/backstage-check.log",status);
            File.Delete(Request);
            EditorApplication.isPlaying=false;
            driving=false;
        }

        static IEnumerator Tour(GameRoot root)
        {
            Directory.CreateDirectory(Output);
            var front=root.Menu.Backstage;
            var ui=front.StartButton.parent.parent;
            void Click(string path)
            {
                var button=ui.Find(path).GetComponent<Button>();
                if (!button.isActiveAndEnabled || !button.interactable) throw new Exception("Unavailable: "+path);
                button.onClick.Invoke();
            }
            IEnumerator Shot(string name)
            {
                File.WriteAllText("Logs/backstage-check.log","Capturing "+name);
                yield return null;
                EditorApplication.QueuePlayerLoopUpdate();
                yield return new WaitForEndOfFrame();
                var image=ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Output+name+".png",image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
            }
            IEnumerator Approach(float x,float z,string view=null)
            {
                var walker=GameObject.Find("Visitor").transform;
                float deadline=Time.realtimeSinceStartup+18f;
                while(true)
                {
                    var p=walker.localPosition;
                    var keys=new List<Key>();
                    if(Mathf.Abs(p.x-x)>.18f) keys.Add(p.x<x?Key.D:Key.A);
                    if(Mathf.Abs(p.z-z)>.18f) keys.Add(p.z<z?Key.W:Key.S);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));
                    if(keys.Count==0) break;
                    if(Time.realtimeSinceStartup>deadline) throw new Exception("WASD did not reach station: "+p);
                    yield return null;
                }
                yield return null;
                if (view!=null) yield return Shot(view);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));
                yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                yield return null;
                if(front.Walking) throw new Exception("F interaction failed at "+walker.localPosition);
            }
            yield return Shot("title");
            Click("Title/Settings"); Click("Settings/Back");
            Click("Title/StartBtn");
            if (!front.Walking || root.RunStarted) throw new Exception("Room entry");
            yield return Shot("room");
            yield return Approach(-3.45f,1f);
            for(int i=0;i<6;i++)
            {
                var index=(MetaSave.Load().Performer+i)%6;
                var picture=GameObject.Find("Mirror portrait").GetComponent<MeshRenderer>();
                var block=new MaterialPropertyBlock();
                picture.GetPropertyBlock(block);
                var tex=block.GetTexture("_BaseMap");
                if(!tex || tex.name!="Performer"+(index+1)) throw new Exception("Wrong reflection "+index);
                var instrument=ui.Find("Mirror/Controls/Bubbles/Instrument/Icon").GetComponent<Image>();
                if(!instrument.sprite) throw new Exception("Missing starting instrument");
                if(i==0) yield return new WaitForSecondsRealtime(.32f);
                if(i==0) yield return Shot("mirror");
                Click("Mirror/Controls/Next");
            }
            Click("Mirror/Controls/Back");
            yield return Approach(3.45f,1f,"gramophone");
            yield return Shot("score");
            Click("Score/Archive");
            var archive = UnityEngine.Object.FindFirstObjectByType<MenuRefs>();
            var entry = archive.UnlockList.Find("Row_gun_third").GetComponent<MenuRowRefs>();
            if (!entry.Icon.sprite || entry.Icon.sprite != front.StartingInstrumentIcon)
                throw new Exception("Archive starting instrument icon");
            if (!entry.BuyImage.isActiveAndEnabled || entry.BuyImage.sprite != UiAssets.BtnPrimary ||
                archive.StartButton.image.sprite != UiAssets.BtnPrimary)
                throw new Exception("Archive button artwork");
            archive.StartButton.onClick.Invoke();
            Click("Score/Back");
            yield return Approach(0,4.15f);
            if (!root.RunStarted || root.Paused || root.Menu.IsOpen || root.Weapons.GunCount!=1 ||
                !root.Player.transform.Find("Performer"+(MetaSave.Load().Performer+1))) throw new Exception("Door");
        }
    }
}
