using System.Collections.Generic;
using UnityEngine;

namespace S.View
{
    /// <summary>Attach to an actual treasure chest; ordinary scene crates are not loot markers.</summary>
    public sealed class MinimapChestMarker : MonoBehaviour
    {
        public static readonly HashSet<MinimapChestMarker> Active = new();
        public bool Revealed = true;
        public bool Opened;
        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);
    }
}
