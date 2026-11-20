using UnityEngine;
namespace S.View
{
    public sealed class MusicImpact:MonoBehaviour
    {
        Material _material;float _age;
        public static void Show(Vector3 at)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="MusicImpact";SAssets.RemoveColliders(go);
            go.transform.position=at;go.transform.localScale=Vector3.one*.8f;
            var f=go.AddComponent<MusicImpact>();f._material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            f._material.SetTexture("_BaseMap",SRes.Get<Texture2D>("T_MusicImpact"));
            f._material.SetFloat("_Surface",1);f._material.SetFloat("_SrcBlend",5);f._material.SetFloat("_DstBlend",1);f._material.SetFloat("_ZWrite",0);f._material.SetFloat("_Cull",0);
            f._material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");f._material.renderQueue=3000;
            go.GetComponent<Renderer>().sharedMaterial=f._material;
        }
        void Update()
        {
            if(!MusicCombatWorld.Active){Destroy(gameObject);return;}
            _age+=Time.deltaTime;if(_age>.3f){Destroy(gameObject);return;}
            _material.SetColor("_BaseColor",new Color(1,.55f,.18f,1-_age/.3f));
            var cam=GameRoot.Instance.Cam;if(cam)transform.rotation=cam.transform.rotation;
            transform.localScale=Vector3.one*(.6f+_age*2);
        }
        void OnDestroy(){if(_material)Destroy(_material);}
    }
}
