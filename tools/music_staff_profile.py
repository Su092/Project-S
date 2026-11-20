"""Smooth, subpixel centerline of five strokes. Equal weight per stroke avoids coverage-dependent centroid jumps."""
import numpy as np
from PIL import Image
from pathlib import Path

def measure(im):
    a=np.asarray(im.convert("RGBA"))[:,:,3].astype(float)
    xs=[];ys=[]
    for x in range(a.shape[1]):
        col=a[:,x]
        edges=np.flatnonzero(np.diff(np.r_[False,col>24,False]))
        spans=list(zip(edges[::2],edges[1::2]))
        spans=[(lo,hi) for lo,hi in spans if hi-lo>=2]
        if len(spans)!=5:continue
        centers=[np.dot(np.arange(lo,hi)+.5,col[lo:hi])/col[lo:hi].sum() for lo,hi in spans]
        xs.append(x+.5);ys.append(float(np.mean(centers)))
    if len(xs)<im.width*.75:raise ValueError("Five complete strokes missing in too many columns")
    xs=np.array(xs);ys=np.array(ys)
    fit=np.polynomial.Chebyshev.fit(xs,ys,22,domain=[0,im.width])
    residual=fit(xs)-ys
    samples=.5-fit(np.linspace(0,im.width,1025))/im.height
    return samples,{"validColumns":len(xs),"rmsSourcePixels":float(np.sqrt(np.mean(residual**2))),"maxSourcePixels":float(np.max(np.abs(residual)))}

def write_profile(im,path):
    values,report=measure(im)
    source="namespace S.View { public static class MusicOfferArt { public const float Aspect = "+str(round(im.width/im.height,6))+"f; public static readonly float[] Rail = new float[] {"+",".join(f"{v:.9f}f" for v in values)+"}; } }"
    Path(path).write_text(source,encoding="utf-8")
    return report

if __name__=="__main__":
    root=Path(__file__).resolve().parents[1]
    im=Image.open(root/"Assets/_Game/Res/Texture/UI/Widget/offer_staff.png")
    values,report=measure(im)
    import re,json
    source=(root/"Assets/_Game/View/MusicOfferArt.cs").read_text(encoding="utf-8")
    old=np.array([float(v) for v in re.findall(r"(-?\d+\.\d+)f",source)][1:])
    # Second finite differences expose the old coverage-centroid spikes at the same source coordinates.
    newAtOld=np.interp(np.linspace(0,1,len(old)),np.linspace(0,1,len(values)),values)
    report["oldMaxCenterSecondDifferencePx"]=float(np.max(np.abs(np.diff(old[3:-3],n=2)))*im.height)
    report["newMaxCenterSecondDifferencePx"]=float(np.max(np.abs(np.diff(newAtOld[3:-3],n=2)))*im.height)
    (root/"Logs/music-staff-profile-diagnosis.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print(json.dumps(report,indent=2))
