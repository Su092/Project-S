using UnityEngine;
using S.Core;

namespace S.View
{
    // Attached to the struck actor. Scaled time keeps the marker and its window in sync during pauses.
    public sealed class OrchestraMark : MonoBehaviour
    {
        GameObject halfBeat, bell;
        float answerUntil, bellUntil, bellDamage;
        PlayerWeapons bellOwner;

        internal static float DirectBonus(Component target, Vector3 point, ProjectileDesc shot, PlayerWeapons owner)
        {
            if (shot == null || !owner) return 0f;
            var mark = target.GetComponent<OrchestraMark>();
            string id = shot.ModuleId;
            bool answer = id == "answer";
            bool tagging = id == "bellmark";
            if (!mark && !answer && !tagging) return 0f;
            if (!mark) mark = target.gameObject.AddComponent<OrchestraMark>();
            mark.enabled = true;
            float bonus = 0f;
            if (tagging)
            {
                if (owner.RingingTarget && owner.RingingTarget != mark) owner.RingingTarget.ClearBell();
                mark.bellOwner = owner;
                owner.RingingTarget = mark;
                mark.bellUntil = Time.time + shot.Duration;
                mark.bellDamage = shot.Damage;
                if (!mark.bell) mark.bell = mark.Sign("bellmark", point, .58f);
            }
            else if (owner.RingingTarget == mark && mark.bell && Time.time < mark.bellUntil)
            {
                SfxBank.Instance?.PlayNote("bellmark", true);
                bonus += .5f * mark.bellDamage;
                CombatFx.Impact(point, new Color(1f,.76f,.22f), Vector3.up);
                mark.ClearBell();
            }
            if (!answer || !(shot.Read ??= new NoteRead()).TryAnswer(target)) return bonus;
            if (mark.halfBeat && Time.time < mark.answerUntil)
            {
                SfxBank.Instance?.PlayNote("answer", true);
                bonus += .6f * shot.Damage;
                CombatFx.Impact(point, new Color(1f,.32f,.14f), Vector3.up);
                mark.ClearAnswer();
            }
            else
            {
                mark.ClearAnswer();
                mark.halfBeat = mark.Sign("halfbeat", point, .50f);
                mark.answerUntil = Time.time + shot.Duration;
            }
            return bonus;
        }

        GameObject Sign(string id, Vector3 point, float scale)
        {
            var holder = new GameObject("Mark_" + id);
            holder.transform.SetParent(transform, true);
            var enemy = GetComponent<Enemy>();
            var boss = GetComponent<BossView>();
            var aim = enemy ? enemy.AimPoint : boss ? boss.AimPoint : point;
            holder.transform.position = aim + Vector3.up * (id == "halfbeat" ? .64f : 1f);
            var prop = OrchestraProps.InstantiateModel(id, holder.transform);
            if (prop) prop.transform.localScale = Vector3.one * scale;
            return holder;
        }

        void ClearAnswer()
        {
            if (halfBeat) Destroy(halfBeat);
            halfBeat = null;
        }

        void ClearBell()
        {
            if (bellOwner && bellOwner.RingingTarget == this) bellOwner.RingingTarget = null;
            bellOwner = null;
            if (bell) Destroy(bell);
            bell = null;
        }

        void Update()
        {
            if (halfBeat && Time.time >= answerUntil) ClearAnswer();
            if (bell && Time.time >= bellUntil) ClearBell();
            var cam = Camera.main;
            if (cam && halfBeat) halfBeat.transform.rotation = cam.transform.rotation * Quaternion.Euler(35f,0f,0f);
            if (cam && bell) bell.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f,0f,Mathf.Sin(Time.time * 8f) * 12f);
            if (!halfBeat && !bell) enabled = false;
        }

        void OnDestroy()
        {
            if (bellOwner && bellOwner.RingingTarget == this) bellOwner.RingingTarget = null;
        }
    }
}
