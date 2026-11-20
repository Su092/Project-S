using UnityEngine;
using UnityEngine.UI;
namespace S.View
{
    /// <summary>Undecorated preview placeholder until the generated empty-slot art is approved.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlainSlotRing : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r=rectTransform.rect;
            float outer=Mathf.Min(r.width,r.height)*.5f;
            float inner=outer-Mathf.Min(r.width,r.height)*.02f;
            const int count=128;
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                var da=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var db=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                int v=vh.currentVertCount;
                vh.AddVert(r.center+da*outer,color,Vector2.zero);vh.AddVert(r.center+db*outer,color,Vector2.zero);
                vh.AddVert(r.center+db*inner,color,Vector2.zero);vh.AddVert(r.center+da*inner,color,Vector2.zero);
                vh.AddTriangle(v,v+1,v+2);vh.AddTriangle(v,v+2,v+3);
            }
        }
    }
}
