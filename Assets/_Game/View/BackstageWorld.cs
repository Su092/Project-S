using UnityEngine;
using UnityEngine.InputSystem;

namespace S.View
{
    public sealed class BackstageWorld : MonoBehaviour
    {
        public enum Station { None, Mirror, Score, Door }
        static readonly Vector3 Mirror = new Vector3(-3.45f, 0, 2.5f);
        static readonly Vector3 Score = new Vector3(3.45f, 0, 2.5f);
        static readonly Vector3 Door = new Vector3(0, 0, 5.6f);
        static readonly Vector3 Facing = Vector3.back;
        Camera _camera;
        CharacterController _walker;
        MeshRenderer _reflection;
        MaterialPropertyBlock _reflectionArt;
        static readonly Vector3 MirrorFront = new Vector3(.42f,0,-1).normalized;
        public float MirrorHalfHeight => Mathf.Max(1.98f, 2.48f / _camera.aspect);
        Transform _note, _mirror;
        RenderTexture _portrait;
        float _yaw, _pitch;
        public RenderTexture Portrait => _portrait;

        static GameObject Model(string id, Transform parent, Vector3 pos)
        {
            var source = Resources.Load<GameObject>("Backstage/" + id);
            var model = Instantiate(source, parent, false);
            model.name = id;
            model.transform.localPosition = pos;
            foreach (var mesh in model.GetComponentsInChildren<MeshRenderer>())
            {
                var slots = mesh.sharedMaterials;
                for (int i = 0; i < slots.Length; ++i)
                {
                    if (!slots[i]) continue;
                    var name = slots[i].name;
                    if (name.StartsWith("MAT_") && ColorUtility.TryParseHtmlString("#" + name.Substring(4), out var c))
                        slots[i] = name.EndsWith("FFD993") ? SMat.Unlit(c) : SMat.Lit(c, .32f);
                }
                mesh.sharedMaterials = slots;
                if (mesh.name == "SM_GramophoneHorn")
                {
                    var finish = new MaterialPropertyBlock();
                    finish.SetFloat("_Metallic", .52f);
                    finish.SetFloat("_Smoothness", .68f);
                    mesh.SetPropertyBlock(finish);
                }
            }
            return model;
        }

        // FBX axis conversion can flip the authored facade. Orient using its actual detail mesh.
        static void Orient(GameObject model, string detail, Vector3 direction)
        {
            foreach (var mesh in model.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mesh.name.StartsWith(detail)) continue;
                var f = Vector3.ProjectOnPlane(mesh.bounds.center - model.transform.position, Vector3.up);
                float sign = Vector3.Dot(f, Vector3.forward) < 0 ? -1 : 1;
                model.transform.rotation = Quaternion.LookRotation(direction * sign);
                break;
            }
        }

        public static void DressPlayer(PlayerController player, int index)
        {
            var body = Model("Performer" + (index + 1), player.transform, Vector3.zero);
            var controller = player.GetComponent<CharacterController>();
            body.transform.localPosition = controller.center - Vector3.up * controller.height * .5f;
            Orient(body, "SM_Domino", player.transform.forward);
            foreach (var part in body.GetComponentsInChildren<Renderer>())
                if (part.bounds.center.y > body.transform.position.y + 1.39f)
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        void Block(string name, Vector3 center, Vector3 size, float angle = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(0,angle,0);
            go.AddComponent<BoxCollider>().size = size;
        }

        public void Build()
        {
            transform.position = new Vector3(0, 240, 0);
            Model("Salon", transform, Vector3.zero);
            var mirror = Model("DressingMirror", transform, Mirror);
            var direction = new Vector3(.42f,0,-1).normalized;
            Orient(mirror, "SM_VanityBulb", direction);
            _mirror = mirror.transform;
            var print = GameObject.CreatePrimitive(PrimitiveType.Quad);
            print.name = "Mirror portrait";
            print.transform.SetParent(_mirror, true);
            print.transform.position = transform.TransformPoint(Mirror + Vector3.up * 1.42f + MirrorFront * .077f);
            print.transform.rotation = Quaternion.LookRotation(-MirrorFront);
            print.transform.localScale = new Vector3(1.61f, 2.53f, 1);
            var collider = print.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            _reflectionArt = new MaterialPropertyBlock();
            _reflection = print.GetComponent<MeshRenderer>();
            _reflection.sharedMaterial = SMat.Unlit(Color.white);
            _reflection.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _reflection.receiveShadows = false;
            Orient(Model("StageDoor", transform, Door), "SM_Pull", Facing);
            Orient(Model("ScoreDesk", transform, Score), "SM_Manuscript", Facing);
            Block("Desk collision", Score + Vector3.up*.5f, new Vector3(2.22f,1,.99f));
            Block("Mirror collision", Mirror + Vector3.up, new Vector3(2.1f,2,.55f), _mirror.eulerAngles.y);
            Block("Door collision", Door + Vector3.up, new Vector3(2.7f,2,.7f));
            for (int i=0; i<4; i++)
            {
                var rotation = Quaternion.Euler(0, 90*i, 0);
                Block("Wall collision", rotation * new Vector3(0,2,5.94f), new Vector3(12,4,.28f),90*i);
            }
            var rig = new GameObject("Visitor");
            rig.transform.SetParent(transform,false);
            _walker = rig.AddComponent<CharacterController>();
            _walker.height=1.7f;
            _walker.radius=.26f;
            _walker.center=Vector3.up*.86f;
            var cameraObject=new GameObject("Backstage camera");
            cameraObject.transform.SetParent(transform,false);
            _camera=cameraObject.AddComponent<Camera>();
            _camera.clearFlags=CameraClearFlags.SolidColor;
            _camera.backgroundColor=Color.clear;
            _camera.nearClipPlane=.04f;
            _camera.farClipPlane=32;
            _camera.depth=8;
            _portrait = new RenderTexture(480,480,24,RenderTextureFormat.ARGB32);
            _portrait.Create();
            _note = Model("OvertureNote",transform,new Vector3(0,10,0)).transform;
            Lamp(new Vector3(-2,3,-1),new Color(1,.82f,.62f),6);
            Lamp(new Vector3(3,3,2),new Color(.7f,.82f,1),5);
            Lamp(new Vector3(-1,12,-2),Color.white,7);
        }

        void Lamp(Vector3 position,Color color,float intensity)
        {
            var lamp = new GameObject("Practical light").AddComponent<Light>();
            lamp.transform.SetParent(transform,false);
            lamp.transform.localPosition=position;
            lamp.type=LightType.Point;
            lamp.range=9;
            lamp.color=color;
            lamp.intensity=intensity;
        }

        public void Title()
        {
            gameObject.SetActive(true);
            _camera.targetTexture=_portrait;
            _camera.rect=new Rect(0,0,1,1);
            _camera.orthographic=true;
            _camera.orthographicSize=1.12f;
            _camera.transform.localPosition=new Vector3(.12f,10.63f,-3);
            _camera.transform.localRotation=Quaternion.identity;
            _camera.farClipPlane=5;
        }

        public void Tilt(Vector2 pointer)
        {
            _note.localRotation=Quaternion.Slerp(_note.localRotation,
                Quaternion.Euler(-pointer.y*9,pointer.x*16,0),1-Mathf.Exp(-10*Time.unscaledDeltaTime));
        }

        public void Room(bool reset)
        {
            gameObject.SetActive(true);
            _camera.targetTexture=null;
            _camera.rect=new Rect(0,0,1,1);
            _camera.orthographic=false;
            _camera.fieldOfView=62;
            _camera.farClipPlane=25;
            if (reset)
            {
                _walker.enabled=false;
                _walker.transform.localPosition=new Vector3(0,.01f,-3.75f);
                _walker.enabled=true;
                _yaw=0; _pitch=0;
            }
            SetView();
        }

        void SetView()
        {
            _camera.transform.position=_walker.transform.position+Vector3.up*1.59f;
            _camera.transform.rotation=Quaternion.Euler(_pitch,_yaw,0);
        }

        public Station Walk()
        {
            var kb=Keyboard.current;
            if (kb==null) return Station.None;
            if (Mouse.current!=null && Cursor.lockState==CursorLockMode.Locked)
            {
                var look=Mouse.current.delta.ReadValue()*.12f;
                _yaw+=look.x;
                _pitch=Mathf.Clamp(_pitch-look.y,-70,70);
            }
            var move=new Vector3((kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0),0,
                                 (kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0));
            _walker.Move(Quaternion.Euler(0,_yaw,0)*Vector3.ClampMagnitude(move,1)*3.5f*Mathf.Min(.06f,Time.unscaledDeltaTime));
            SetView();
            if (Near(Door)) return Station.Door;
            if (Near(Mirror)) return Station.Mirror;
            if (Near(Score)) return Station.Score;
            return Station.None;
        }

        bool Near(Vector3 target)
        {
            var delta=target-_walker.transform.localPosition;
            return delta.sqrMagnitude < 2.6f*2.6f &&
                Vector3.Dot(delta.normalized,Quaternion.Euler(0,_yaw,0)*Vector3.forward)>.43f;
        }

        public void SetReflection(Texture2D portrait)
        {
            _reflectionArt.SetTexture("_BaseMap", portrait);
            _reflectionArt.SetTexture("_MainTex", portrait);
            _reflection.SetPropertyBlock(_reflectionArt);
        }

        public void FrameMirror()
        {
            var focus = transform.TransformPoint(Mirror + Vector3.up * 1.53f);
            _camera.transform.position = focus + MirrorFront * 3.8f;
            _camera.transform.rotation = Quaternion.LookRotation(-MirrorFront);
            _camera.orthographic = false;
            _camera.fieldOfView = 2f * Mathf.Rad2Deg * Mathf.Atan(MirrorHalfHeight / 3.8f);
        }

        void OnDestroy()
        {
            if (_camera) _camera.targetTexture=null;
            if (_portrait) { _portrait.Release(); Destroy(_portrait); }
        }
    }
}
