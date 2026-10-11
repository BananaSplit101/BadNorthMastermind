using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace BadNorthMP.UI
{
    /// <summary>
    /// A Viking type's picture for the mastermind's buttons. The game only fills a type's picture in (sprite2) once it
    /// has set the type up (VikingReference.Start), which on the mastermind's game can come after the buttons are made
    /// (on an island while the defenders choose their commanders, or on the map before any island); the same picture
    /// is on the type's unit template, so that's used meanwhile.
    /// </summary>
    internal static class UnitIcons
    {
        private static readonly Dictionary<int, Sprite> known = new Dictionary<int, Sprite>();

        public static Sprite Of(VikingReference reference)
        {
            if (!reference) return null;
            int type = (int)reference.type;
            if (reference.sprite2) return known[type] = reference.sprite2;
            if (known.TryGetValue(type, out Sprite sprite) && sprite) return sprite;
            var template = Traverse.Create(reference).Field<VikingAgent>("viking").Value;
            var animator = template ? template.GetComponentInChildren<SpriteAnimator>(true) : null;
            sprite = animator ? (animator.sprite2 ? animator.sprite2 : animator.sprite) : null;
            if (sprite) known[type] = sprite;
            return sprite;
        }

        /// <summary>Has a picture to show, from the game or from its template.</summary>
        public static bool Has(VikingReference reference) => Of(reference);
    }
}
