using UnityEngine;
namespace S.View
{
    /// <summary>Mechanical aim uses authored sockets; the ceiling mount stays stationary.</summary>
    public sealed class MusicFollowSpotRig : MonoBehaviour
    {
        public Animator Animator;
        public Transform Yaw, Pitch, LightOrigin, HeadPivot;
        Quaternion _yawRest, _pitchRest;
        float _focusUntil;
        bool _closing;
        void Awake(){_yawRest=Yaw.localRotation;_pitchRest=Pitch.localRotation;}
        public void Focus(bool on)
        {
            _closing=!on;_focusUntil=Time.time+.5f;
            Animator.Play(on?"FocusIn":"FocusOut",0,0);
        }
        public void Aim(Vector3 target)
        {
            if(!_closing&&_focusUntil>0&&Time.time>=_focusUntil)
            {Animator.Play("Idle",0,0);_focusUntil=0;}
            Yaw.localRotation=_yawRest;Pitch.localRotation=_pitchRest;
            var forward=Vector3.ProjectOnPlane(LightOrigin.forward,Vector3.up);
            var direction=Vector3.ProjectOnPlane(target-HeadPivot.position,Vector3.up);
            if(forward.sqrMagnitude>.0001f&&direction.sqrMagnitude>.0001f)
                Yaw.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(forward,direction,Vector3.up),Vector3.up)*Yaw.rotation;
            var aim=target-HeadPivot.position;
            if(aim.sqrMagnitude>.0001f)Pitch.rotation=Quaternion.FromToRotation(LightOrigin.forward,aim.normalized)*Pitch.rotation;
        }
    }
}
