using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO.Hashing;
using System.Net.Http.Json;
using System.Text.Json;
using Newtonsoft.Json;
namespace ClangTest
{
    class AnalysisCache
    {
        private Dictionary<string, string> cachedHashes = new();
        private const string CACHE_FILE = ".analysis_cache.json";

        /// <summary>
        /// ハッシュ値を計算し、返す
        /// xxHash64で計算を行う
        /// </summary>
        /// <param name="filePath">ハッシュ値を計算するファイル</param>
        /// <returns>ハッシュ値</returns>
        string ComputeFileHash(string filePath)
        {
            var sha256 = SHA256.Create();
            using var file = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(file);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// JSONファイルからキャッシュを読み込む
        /// </summary>
        public void LoadCache()
        {
            if(File.Exists(CACHE_FILE))
            {
                string json = File.ReadAllText(CACHE_FILE);
                cachedHashes = JsonConvert.DeserializeObject < Dictionary<string, string> >(json) ?? new();
            }
            else
            {
                Console.Error.WriteLine("キャッシュファイルが見つかりません");
                return;
            }
        }

        /// <summary>
        /// キャッシュをJSONファイルに保存する
        /// </summary>
        public void SaveCache()
        {
            string json = JsonConvert.SerializeObject(cachedHashes, Formatting.Indented);
            File.WriteAllText(CACHE_FILE, json);
        }

        /// <summary>
        /// ファイルの再生成をする必要があるか否か
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns>生成する必要がある場合true,ない場合false</returns>
        public bool NeedsRegeneration(string sourceFile)
        {
            var currentHash = ComputeFileHash(sourceFile);
            if(cachedHashes.TryGetValue(sourceFile,out var cachedHash) == false)
            {
                // キャッシュにない。初回、もしくは削除済み
                return true;
            }
            if(currentHash != cachedHash)
            {
                // ハッシュ値が異なる。ファイル内容が変わった
                return true;
            }

            // 内容が同じ。スキップ
            return false;
        }

        /// <summary>
        /// ファイルのハッシュ値を更新
        /// </summary>
        /// <param name="sourceFile">ハッシュ値を更新するファイル</param>
        public void UpdateCache(string sourceFile)
        {
            var hash = ComputeFileHash(sourceFile);
            cachedHashes[sourceFile] = hash;
        }
    }
}
