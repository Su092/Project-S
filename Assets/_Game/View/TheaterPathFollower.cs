using UnityEngine;
using UnityEngine.AI;
namespace S.View
{
    /// <summary>Uses the authored theater navigation; movement stays on the existing character controller.</summary>
    public sealed class TheaterPathFollower : MonoBehaviour
    {
        NavMeshPath _path;
        Vector3[] _corners;
        int _count;
        int _next;
        float _repath;
        public Vector3 Direction(Vector3 target)
        {
            if (_path == null) { _path = new NavMeshPath(); _repath = 0; }
            if (_corners == null) { _corners = new Vector3[256]; _count = 0; }
            if (Time.time >= _repath)
            {
                _repath = Time.time + .55f + Random.Range(0f, .175f);
                if (NavMesh.SamplePosition(transform.position, out var start, 1.8f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(target, out var end, 2f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path) &&
                    _path.status != NavMeshPathStatus.PathInvalid)
                { _count = _path.GetCornersNonAlloc(_corners); _next = _count > 1 ? 1 : 0; }
                else { _count = 0; _next = 0; }
            }
            while (_next < _count)
            {
                var d = _corners[_next] - transform.position;
                float height = Mathf.Abs(d.y); d.y = 0;
                if (d.magnitude > .2f || height > .5f) return d.normalized;
                _next++;
            }
            return Vector3.zero;
        }
        public bool ClearSight(Vector3 from, Vector3 to)
        {
            var delta = to - from;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, WorldPhysics.ShotMask, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<FoundryArena>()) return false;
            return true;
        }
    }
}
