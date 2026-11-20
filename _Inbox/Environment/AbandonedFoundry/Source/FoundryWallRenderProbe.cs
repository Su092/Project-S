using System.Collections.Generic;using UnityEngine;
namespace FoundryDelivery{
[ExecuteAlways]public class FoundryWallRenderProbe:MonoBehaviour{
public static Camera Target;public static HashSet<string> Seen=new HashSet<string>();
void OnWillRenderObject(){if(Camera.current==Target)Seen.Add(gameObject.name);}
}
}