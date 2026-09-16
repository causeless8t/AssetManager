using System.IO;
using Causeless3t.Security;

namespace Causeless3t.AssetBundle
{
    public static class AssetBundleUtil
    {
        public static readonly string ASSET_BUNDLE_EXTENSION_NAME = "unity3d";
        public static readonly string INFO_FILE_NAME = "filesinfo.dat";
        
        public static string GetFileHash(string filePath)
        {
            FileInfo item = new FileInfo(filePath);
            using var stream = item.OpenRead();
            return CRC32.Compute(stream).ToString();
        }
    }
}

