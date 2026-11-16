using UnityEngine;

namespace S.View
{
    /// <summary>
    /// 敌人死亡后留在原地的经验晶体。无物理碰撞、不计时消失；
    /// 拾取与经验结算统一由所属 GameRoot 驱动，暂停期间不推进。
    /// </summary>
    public sealed class ExperiencePickup : MonoBehaviour
    {
        public const float PickupRadius = 1.5f;

        public int Amount { get; private set; }
        public GameRoot Owner { get; private set; }

        Transform _crystal;
        float _age;

        public void Init(GameRoot owner, int amount)
        {
            Owner = owner;
            Amount = amount;

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "Crystal";
            crystal.transform.SetParent(transform, false);
            crystal.transform.localPosition = Vector3.up * 0.4f;
            crystal.transform.localScale = Vector3.one * 0.32f;
            crystal.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            RemoveCollider(crystal);
            SMat.Apply(crystal.GetComponent<Renderer>(), SMat.Unlit(new Color(0.3f, 1f, 0.55f)));
            _crystal = crystal.transform;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "GroundMarker";
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = Vector3.up * 0.03f;
            marker.transform.localScale = new Vector3(0.6f, 0.015f, 0.6f);
            RemoveCollider(marker);
            SMat.Apply(marker.GetComponent<Renderer>(), SMat.Unlit(new Color(0.12f, 0.55f, 0.28f)));
        }

        static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            // Destroy 延迟到帧末，先禁用以免生成当帧挡住玩家或子弹。
            collider.enabled = false;
            Object.Destroy(collider);
        }

        public void TickVisual(float dt)
        {
            _age += dt;
            if (_crystal == null) return;
            _crystal.localPosition = Vector3.up * (0.4f + Mathf.Sin(_age * 3f) * 0.08f);
            _crystal.Rotate(Vector3.up, 70f * dt, Space.World);
        }
    }
}
