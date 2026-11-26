using UnityEngine;
using S.Core;

namespace S.View
{
    public sealed partial class Projectile
    {
        LineRenderer wave;
        float waveRadius;
        int reeds;
        LineRenderer[] teeth;
        Vector3 heading;
        PlayerWeapons boxOwner;
        bool struckBoss;

        void TickGroundNote(float dt)
        {
            var root = GameRoot.Instance;
            if (!root || root.RunEnded) { Destroy(gameObject); return; }
            if (!_planted)
            {
                _life -= dt;
                if (_life <= 0f) { Destroy(gameObject); return; }
                _vel += Vector3.down * _d.Gravity * dt;
                float step = _vel.magnitude * dt;
                if (!Physics.Raycast(transform.position, _vel.normalized, out var hit, step,
                    WorldPhysics.ShotMask, QueryTriggerInteraction.Ignore))
                {
                    transform.position += _vel * dt;
                    return;
                }
                if (hit.normal.y < .58f || hit.collider.GetComponentInParent<Enemy>() ||
                    hit.collider.GetComponentInParent<BossView>() || hit.collider.GetComponentInParent<PlayerController>())
                {
                    // Slide down scenery until there is a supporting surface.
                    transform.position = hit.point + hit.normal * .06f;
                    _vel = Vector3.ProjectOnPlane(_vel, hit.normal) * .65f;
                    return;
                }
                _planted = true;
                transform.position = hit.point + Vector3.up * .045f;
                heading = Vector3.ProjectOnPlane(_dir, Vector3.up);
                if (heading.sqrMagnitude < .0001f) heading = root.PlayerTransform ? root.PlayerTransform.forward : Vector3.forward;
                heading = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
                if (heading.sqrMagnitude < .5f) heading = Vector3.forward;
                transform.rotation = Quaternion.LookRotation(heading);
                CombatFx.Impact(transform.position, PlayerWeapons.ParseColor(_d.Source.Color), Vector3.up);
                if (_d.Kind == BulletKind.Ripple)
                {
                    wave = CombatFx.Ring(transform, PlayerWeapons.ParseColor(_d.Source.Color) * 1.8f, 1f, .055f);
                    wave.transform.localScale = Vector3.one * .035f;
                }
                else
                {
                    teeth = new LineRenderer[3];
                    for (int i=0; i<teeth.Length; i++)
                    {
                        var line = CombatFx.Line(transform, "ReedIndicator", new Color(1.6f,.84f,.26f), .025f);
                        line.positionCount = 2;
                        float x = (i-1) * .10f;
                        line.SetPosition(0,new Vector3(x,.17f,-.09f));
                        line.SetPosition(1,new Vector3(x,.18f,.10f));
                        teeth[i] = line;
                    }
                    boxOwner = root.Weapons;
                    if (boxOwner) boxOwner.RegisterBox(this);
                }
                return;
            }

            _tickTimer += dt;
            if (_d.Kind == BulletKind.Ripple) ExpandWave(root);
            else
            {
                while (reeds < 3 && _tickTimer >= _d.Duration)
                {
                    _tickTimer -= _d.Duration;
                    teeth[reeds].enabled = false;
                    EmitReed();
                    reeds++;
                }
                if (reeds == 3) Destroy(gameObject);
            }
        }

        void ExpandWave(GameRoot root)
        {
            float next = Mathf.Min(1f, _tickTimer / _d.Duration) * _d.Radius;
            wave.transform.localScale = Vector3.one * Mathf.Max(.035f,next);
            var enemies = root.Enemies;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (!e || !e.Alive || _touched.Contains(e) || !WaveCrosses(e.transform.position, next)) continue;
                _touched.Add(e);
                e.TakeDamage(_d, e.AimPoint, _d.Damage, true);
            }
            var boss = root.Boss;
            if (!struckBoss && boss && boss.State != null && boss.State.Alive && WaveCrosses(boss.transform.position, next))
            {
                struckBoss = true;
                boss.OnHit(_d, boss.AimPoint, _d.Damage);
            }
            waveRadius = next;
            if (_tickTimer >= _d.Duration) Destroy(gameObject);
        }

        bool WaveCrosses(Vector3 feet, float next)
        {
            var delta = feet - transform.position;
            if (Mathf.Abs(delta.y) > .40f) return false;
            delta.y = 0f;
            float distance = delta.magnitude;
            return distance + .18f >= waveRadius && distance - .18f <= next;
        }

        void EmitReed()
        {
            const float speed = 28f;
            var reed = _d.Clone();
            reed.ModuleId = "reed";
            reed.Kind = BulletKind.Proj;
            reed.Gravity = 0f;
            reed.Speed = speed;
            reed.Count = 1;
            reed.Size = .15f;
            reed.Lifetime = _d.Range / speed;
            SfxBank.Instance?.PlayNote("reed");
            var start = transform.position + Vector3.up * .47f + heading * .24f;
            CombatFx.Muzzle(start, heading, PlayerWeapons.ParseColor(_d.Source.Color));
            var go = new GameObject("MusicBoxReed");
            go.transform.position = start;
            go.AddComponent<Projectile>().Init(reed, heading);
        }

        void OnDestroy()
        {
            if (boxOwner) boxOwner.UnregisterBox(this);
        }
    }
}
