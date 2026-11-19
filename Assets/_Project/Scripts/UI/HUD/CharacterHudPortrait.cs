using System;
using UnityEngine;

namespace BlueReaper.UI
{
    [Serializable]
    internal struct CharacterHudPortrait
    {
        [Min(1)] public int CharacterId;
        public Sprite Neutral;
        public Sprite[] Variants;

        internal Sprite Resolve(int variant) => variant > 0 && Variants != null && variant <= Variants.Length
            && Variants[variant - 1] ? Variants[variant - 1] : Neutral;
    }
}
