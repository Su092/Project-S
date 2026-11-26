using System.Collections.Generic;
using UnityEngine;

namespace S.View
{
    internal static class OrchestraProps
    {
        static readonly Dictionary<string,GameObject> models = new();
        static readonly Dictionary<Mesh,Material[]> palettes = new();

        internal static GameObject InstantiateModel(string id, Transform parent)
        {
            if (!models.TryGetValue(id, out var model))
                models[id] = model = Resources.Load<GameObject>("Orchestra/" + id);
            if (!model) return null;
            var prop = Object.Instantiate(model, parent, false);
            foreach (var renderer in prop.GetComponentsInChildren<MeshRenderer>())
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!palettes.TryGetValue(mesh, out var palette))
                {
                    palette = renderer.sharedMaterials;
                    for (int i = 0; i < palette.Length; i++)
                    {
                        var name = palette[i] ? palette[i].name : "";
                        if (name.StartsWith("Tone_") && ColorUtility.TryParseHtmlString("#" + name.Substring(5), out var color))
                            palette[i] = SMat.Lit(color, .48f);
                    }
                    palettes[mesh] = palette;
                }
                renderer.sharedMaterials = palette;
            }
            return prop;
        }
    }
}
