using UnityEngine;
namespace S.View
{
    /// <summary>Authored break pieces, cosmetic only: never obstruct shots or movement.</summary>
    public sealed class MusicGunDebris:MonoBehaviour
    {
        Vector3 _velocity;float _age;
        public static void Spawn(Vector3 position)
        {
            foreach(var part in new[]{"Clamp","Core","Horn","Housing"})
            {
                var go=MusicCombatWorld.Model("FXM_S29_"+part+"Shard");
                go.transform.position=position;go.transform.rotation=Random.rotation;
                var d=go.AddComponent<MusicGunDebris>();d._velocity=Random.insideUnitSphere*1.5f+Vector3.up*2;
            }
        }
        void Update()
        {
            if(!MusicCombatWorld.Active){Destroy(gameObject);return;}
            float dt=Time.deltaTime;_age+=dt;if(_age>1.2f){Destroy(gameObject);return;}
            _velocity+=Vector3.down*9*dt;transform.position+=_velocity*dt;
            transform.Rotate(new Vector3(60,80,25)*dt);
            if(_age>.8f)transform.localScale=Vector3.one*Mathf.Clamp01((1.2f-_age)/.4f);
        }
    }
}
