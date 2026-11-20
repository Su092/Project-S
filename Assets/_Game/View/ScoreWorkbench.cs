using System.Collections.Generic;
using UnityEngine;
namespace S.View
{
    /// <summary>A fixed, authored map device. All loadout editing requires an active nearby device.</summary>
    public sealed class ScoreWorkbench : MonoBehaviour
    {
        public string DisplayName = "调律谱架";
        [Min(.5f)] public float InteractionRadius = 3f;
        public Transform InteractionPoint;
        static readonly HashSet<ScoreWorkbench> Active = new();
        public Vector3 Point => InteractionPoint ? InteractionPoint.position : transform.position + Vector3.up;
        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);
        public bool InRange(GameRoot root)
        {
            if (!isActiveAndEnabled || !root || !root.PlayerTransform ||
                gameObject.scene != root.gameObject.scene) return false;
            Vector3 delta = Point - (root.PlayerTransform.position + Vector3.up);
            return delta.sqrMagnitude <= InteractionRadius * InteractionRadius;
        }
        public static ScoreWorkbench Nearest(GameRoot root)
        {
            ScoreWorkbench nearest = null;
            float distance = float.MaxValue;
            foreach (var station in Active)
            {
                if (!station || !station.InRange(root)) continue;
                float d = (station.Point - root.PlayerTransform.position).sqrMagnitude;
                if (d < distance) { distance = d; nearest = station; }
            }
            return nearest;
        }
        public bool Interact(GameRoot root) => root && root.TryOpenScoreWorkbench(this);
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f,.78f,.35f,.6f);
            Gizmos.DrawWireSphere(Point, InteractionRadius);
        }
    }
}
