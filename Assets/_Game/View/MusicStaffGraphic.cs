using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;
namespace S.View
{
    /// <summary>One fixed spatial curve. All material points share one arc-distance offset; no stretching or per-point tween.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MusicStaffGraphic : MaskableGraphic
    {
        public Sprite Sprite;
        public float Clock, FlightProgress;
        public int FlightMode; // 1 entrance, -1 departure, 0 resting
        public Vector2 ViewSize;
        const int Samples=768;
        readonly Vector2[] route=new Vector2[Samples+1];
        readonly float[] lengths=new float[Samples+1];
        Vector2 cachedSize;
        float restStart,restEnd,entryDistance,exitDistance;
        public float ExitToEntryDistanceRatio { get { EnsureRoute();return exitDistance/Mathf.Max(.001f,entryDistance); } }
        public override Texture mainTexture => Sprite ? Sprite.texture : Texture2D.whiteTexture;
        public float CurveY(float x)
        {
            float w=Mathf.Max(1,ViewSize.x);
            return ViewSize.y*(.085f*Mathf.Sin(x/w*Mathf.PI*1.85f+.25f)
                +.025f*Mathf.Sin(x/w*Mathf.PI*3.1f-.4f));
        }
        void EnsureRoute()
        {
            if(cachedSize==ViewSize&&restEnd>restStart)return;
            cachedSize=ViewSize;float w=Mathf.Max(1,ViewSize.x);
            lengths[0]=0;
            for(int i=0;i<=Samples;i++){
                float x=Mathf.Lerp(-2.5f*w,2.5f*w,(float)i/Samples);
                route[i]=new Vector2(x,CurveY(x));
                if(i>0)lengths[i]=lengths[i-1]+Vector2.Distance(route[i],route[i-1]);
            }
            restStart=DistanceAtX(-.56f*w);restEnd=DistanceAtX(.56f*w);
            entryDistance=restEnd-DistanceAtX(-.66f*w);
            exitDistance=DistanceAtX(.66f*w)-restStart;
        }
        float DistanceAtX(float x)
        {
            float n=Mathf.Clamp01((x/ViewSize.x+2.5f)/5f)*Samples;
            int i=Mathf.Min((int)n,Samples-1);
            return Mathf.Lerp(lengths[i],lengths[i+1],n-i);
        }
        Vector2 AtDistance(float distance)
        {
            int lo=0,hi=Samples;
            while(hi-lo>1){int mid=(lo+hi)/2;if(lengths[mid]<distance)lo=mid;else hi=mid;}
            float x=Mathf.Lerp(route[lo].x,route[hi].x,Mathf.InverseLerp(lengths[lo],lengths[hi],distance));
            return new Vector2(x,CurveY(x));
        }
        // Short acceleration / long steady cruise / gentle braking. Unlike smoothstep, no high mid-flight speed peak.
        public static float Travel(float t)
        {
            t=Mathf.Clamp01(t);const float a=.15f,b=.25f,area=1-(a+b)*.5f;
            if(t<a)return t*t/(2*a*area);
            if(t>1-b)return 1-(1-t)*(1-t)/(2*b*area);
            return (t-a*.5f)/area;
        }
        public float DistanceFor(float u)
        {
            EnsureRoute();
            float offset=FlightMode==1?-entryDistance*(1-Travel(FlightProgress)):
                FlightMode==-1?exitDistance*Travel(FlightProgress):0;
            return Mathf.LerpUnclamped(restStart,restEnd,u)+offset;
        }
        public float MaterialAtX(float x)
        {
            EnsureRoute();return Mathf.InverseLerp(restStart,restEnd,DistanceAtX(x));
        }
        public Vector2 Point(float u)=>AtDistance(DistanceFor(u));
        public Vector2 RestPoint(float u)
        {
            EnsureRoute();return AtDistance(Mathf.Lerp(restStart,restEnd,u));
        }
        public Vector2 Normal(float u)
        {
            float x=Point(u).x,w=Mathf.Max(1,ViewSize.x);
            float slope=ViewSize.y/w*(.085f*Mathf.PI*1.85f*Mathf.Cos(x/w*Mathf.PI*1.85f+.25f)
                +.025f*Mathf.PI*3.1f*Mathf.Cos(x/w*Mathf.PI*3.1f-.4f));
            return new Vector2(-slope,1).normalized;
        }
        public float Bank(float u){var n=Normal(u);return Mathf.Atan2(-n.x,n.y)*Mathf.Rad2Deg;}
        public float SourceCenter(float u)
        {
            var a=MusicOfferArt.Rail;float n=Mathf.Clamp01(u)*(a.Length-1);
            int k=Mathf.Min((int)n,a.Length-2);float t=n-k;
            float p0=k>0?a[k-1]:2*a[k]-a[k+1],p1=a[k],p2=a[k+1];
            float p3=k+2<a.Length?a[k+2]:2*p2-p1;
            return .5f*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t
                +(-p0+3*p1-3*p2+p3)*t*t*t)*rectTransform.rect.height;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!Sprite)return;
            var r=rectTransform.rect;var uv=DataUtility.GetOuterUV(Sprite);
            const int steps=512;
            for(int i=0;i<=steps;i++){
                float u=(float)i/steps,profile=SourceCenter(u);
                Vector2 p=Point(u),normal=Normal(u);
                // Remove the baked center bend; retain the approved five lines and their fixed transverse spacing.
                vh.AddVert(p+normal*(r.yMin-profile),color,new Vector2(Mathf.Lerp(uv.x,uv.z,u),uv.y));
                vh.AddVert(p+normal*(r.yMax-profile),color,new Vector2(Mathf.Lerp(uv.x,uv.z,u),uv.w));
                if(i>0){int k=i*2;vh.AddTriangle(k-2,k-1,k);vh.AddTriangle(k,k-1,k+1);}
            }
        }
    }
}
