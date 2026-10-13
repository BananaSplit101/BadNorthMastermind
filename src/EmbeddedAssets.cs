using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BadNorthMP
{
    /// <summary>Pictures and sounds shipped inside the DLL (assets/ in the repo), so an update that only swaps the DLL brings them too.</summary>
    internal static class EmbeddedAssets
    {
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static byte[] Bytes(string name)
        {
            using (Stream stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream("BadNorthMP.Assets." + name))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int got = stream.Read(bytes, read, bytes.Length - read);
                    if (got <= 0) break;
                    read += got;
                }
                return bytes;
            }
        }

        public static Texture2D Texture(string name, FilterMode filter = FilterMode.Bilinear)
        {
            if (textures.TryGetValue(name, out Texture2D cached) && cached) return cached;
            byte[] bytes = Bytes(name);
            if (bytes == null) { ModLog.Problem("missing_asset", "Missing embedded asset " + name); return null; }
            var texture = new Texture2D(2, 2) { filterMode = filter };
            texture.LoadImage(bytes);
            textures[name] = texture;
            return texture;
        }

        /// <summary>A copy on disk (Unity loads sounds from a file URL), rewritten only when missing or a different size.</summary>
        public static string File(string name)
        {
            byte[] bytes = Bytes(name);
            if (bytes == null) return null;
            string path = Path.Combine(Application.temporaryCachePath, "BadNorthMP-" + name);
            try
            {
                if (!System.IO.File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                    System.IO.File.WriteAllBytes(path, bytes);
                return path;
            }
            catch (IOException e)
            {
                ModLog.Warn("Couldn't unpack " + name + ": " + e.Message);
                return null;
            }
        }
    }
}
