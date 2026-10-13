using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Players' Steam profile pictures (64x64), loaded like the game's own SteamUtil.LoadProfileImage. A picture Steam
    /// doesn't have yet is requested and tried again a second later.
    /// </summary>
    internal static class SteamAvatars
    {
        private static readonly Dictionary<ulong, Sprite> sprites = new Dictionary<ulong, Sprite>();
        private static readonly Dictionary<ulong, float> retryAt = new Dictionary<ulong, float>();
        private static bool warned;

        public static Sprite Get(ulong player)
        {
            if (sprites.TryGetValue(player, out Sprite sprite) && sprite) return sprite;
            if (retryAt.TryGetValue(player, out float at) && Time.unscaledTime < at) return null;
            retryAt[player] = Time.unscaledTime + 1f;
            try
            {
                var id = new CSteamID(player);
                int handle = SteamFriends.GetMediumFriendAvatar(id);
                // 0: no picture yet; -1: still loading.
                if (handle <= 0) { SteamFriends.RequestUserInformation(id, false); return null; }
                if (!SteamUtils.GetImageSize(handle, out uint width, out uint height) || width == 0 || height == 0 || width > 512 || height > 512)
                    return null;
                var pixels = new byte[width * height * 4];
                if (!SteamUtils.GetImageRGBA(handle, pixels, pixels.Length)) return null;
                // Steam rows run top to bottom, Unity textures bottom to top.
                int row = (int)width * 4;
                var flipped = new byte[pixels.Length];
                for (int y = 0; y < height; y++)
                    Buffer.BlockCopy(pixels, y * row, flipped, ((int)height - 1 - y) * row, row);
                var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(flipped);
                texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                sprites[player] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; ModLog.Warn("Steam profile picture unavailable: " + e.Message); }
                return null;
            }
        }

        public static bool Loaded(ulong player) => sprites.TryGetValue(player, out Sprite sprite) && sprite;

        /// <summary>A stable colour per player, shown with their initial while the picture loads.</summary>
        public static Color Fallback(ulong player)
        {
            float hue = (player * 2654435761UL % 1000UL) / 1000f;
            return Color.HSVToRGB(hue, 0.45f, 0.8f);
        }
    }
}
