using System;
using UnityEngine;
namespace S.View
{
    /// <summary>Shared query masks for authored stairs: smooth movement, visible shot surfaces.</summary>
    public static class WorldPhysics
    {
        public const string MovementLayer = "FoundryMovement";
        public const string ShotLayer = "FoundryShot";
        public const string ActorLayer = "FoundryPlayer";
        static int Bit(string name) { int layer = LayerMask.NameToLayer(name); return layer < 0 ? 0 : 1 << layer; }
        public static int ShotMask => ~Bit(MovementLayer);
        public static int GroundMask => ~Bit(ShotLayer);
        public static void ConfigureActor(GameObject actor)
        {
            int layer = LayerMask.NameToLayer(ActorLayer), shot = LayerMask.NameToLayer(ShotLayer);
            if (layer < 0 || shot < 0 || LayerMask.NameToLayer(MovementLayer) < 0)
                throw new InvalidOperationException("Foundry collision layers missing. Run S/Map/Build Foundry Map.");
            actor.layer = layer;
            Physics.IgnoreLayerCollision(layer, shot, true);
        }
    }
}
