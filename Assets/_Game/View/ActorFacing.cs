using UnityEngine;
namespace S.View
{
    public static class ActorFacing
    {
        public static bool AlongMovement(Transform actor,Vector3 actualDisplacement)
        {
            actualDisplacement.y=0;
            if(actualDisplacement.sqrMagnitude<.00000001f)return false;
            actor.rotation=Quaternion.LookRotation(actualDisplacement,Vector3.up);
            return true;
        }
    }
}
