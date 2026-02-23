using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClangTest
{
    public class ParallelAnalysisCache
    {
        private Dictionary<string, CacheEntry> _memoryCache;
        private readonly string _cacheFilePath;
        private readonly object _lockObj = new();

        public class CacheEntry
        {
            public string FileHash { get; set; } = "";
            public DateTime LastAnalyzed { get; set; }
        }

        public ParallelAnalysisCache(string cacheFile)
        {
            this._cacheFilePath = cacheFile;
            this._memoryCache = new();
            LoadCacheFromDisk();
        }

        /// <summary>
        /// キャッシュをファイルから読み込む
        /// </summary>
        private void LoadCacheFromDisk()
        {
            lock (_lockObj)
            {
                if (File.Exists(_cacheFilePath))
                {
                    try
                    {
                        // JSONを読み込む
                        string json = File.ReadAllText(_cacheFilePath);
                        // 辞書に変換
                        _memoryCache = JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(json) ?? new();
                        Console.WriteLine($"[Cache] Loaded {_memoryCache.Count} entries from disk");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Cache Error] Failed to load:{ex.Message}");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"[Cache] not found {_cacheFilePath}");
                }
            }
        }

        /// <summary>
        /// キャッシュをファイルに保存する
        /// </summary>
        public void SaveCacheToDisk()
        {
            lock (_lockObj)
            {
                try
                {
                    // JSONとして書き込み
                    string json = JsonConvert.SerializeObject(_memoryCache, Formatting.Indented);
                    File.WriteAllText(_cacheFilePath, json);
                    Console.Error.WriteLine($"[Cache] Success to save: {_cacheFilePath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Cache Error] Failed to save: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 複数ファイルの再生成の要否を判定
        /// </summary>
        /// <param name="headerFiles"></param>
        /// <returns></returns>
        public List<string> FindFilesNeedingRegeneration(IEnumerable<string> headerFiles)
        {
            // スレッドセーフで扱える、順序が問題とならないときに適しているコレクション
            ConcurrentBag<string> result = new ConcurrentBag<string>();
            // 生成対象でないファイルをスキップ
            var fileList = headerFiles.Where(f => ShouldSkip(f) == false).ToList();

            Console.WriteLine($"[Analyze] Checking {fileList.Count} file..");
            // 時間の計測を開始
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 並列処理の動作を制御する設定をまとめたクラス
            var parallelOptions = new ParallelOptions()
            {
                // 利用可能な論理プロセッサ(論理コア)数を取得
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            // 並列で処理
            Parallel.ForEach(fileList, parallelOptions, headerFiles =>
            {
                if (FileNeedsRegeneration(headerFiles))
                {
                    result.Add(headerFiles);
                }
            });

            // 時間計測終了
            stopwatch.Stop();
            Console.WriteLine($"[Analyze] Complete in {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"[Analyze] {result.Count} file need regeneration");

            return result.ToList();

        }

        /// <summary>
        /// ファイルを再生成する必要があるか否か
        /// ハッシュを比較する
        /// </summary>
        /// <param name="filePath">再生成の要否を調べるファイルのパス</param>
        /// <returns>再生成する必要がある場合はtrue、ない場合はfalseを返す</returns>
        public bool FileNeedsRegeneration(string filePath)
        {
            if (File.Exists(filePath) == false)
            {
                return true;
            }

            try
            {
                var currentHash = XxHashComputer.ComputeHash(filePath);

                lock (_lockObj)
                {
                    if (_memoryCache.TryGetValue(filePath, out CacheEntry? entry) == false)
                    {
                        // キャッシュにない
                        return true;
                    }
                    if (entry == null)
                    {
                        return true;
                    }
                    if (currentHash != entry.FileHash)
                    {
                        // ハッシュが異なる
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Hash Error] {filePath}: {ex.Message}");
                // エラー時は再生成
                return true;
            }
        }

        /// <summary>
        /// 生成対象となるファイルか否か
        /// 例えば生成されたファイルなど、キャッシュする必要もないファイルか判定
        /// </summary>
        /// <param name="filePath">判定するファイルのパス</param>
        /// <returns>生成対象ではない場合はtrue、生成対象の場合はfalse</returns>
        private bool ShouldSkip(string filePath)
        {
            var name = Path.GetFileName(filePath);
            // 生成されたファイルは生成対象でない
            return name.Contains(".generated.h");
        }

        /// <summary>
        /// ファイルをキャッシュに登録
        /// </summary>
        /// <param name="filePath"></param>
        public void UpdateCache(string filePath)
        {
            try
            {
                string hash = XxHashComputer.ComputeHash(filePath);

                lock (_lockObj)
                {
                    // ハッシュとその時の時間を保存
                    _memoryCache[filePath] = new CacheEntry
                    {
                        FileHash = hash,
                        LastAnalyzed = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cache Update Error {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 複数ファイルを一括登録
        /// </summary>
        /// <param name="filePaths"></param>
        public void UpdateCacheBatch(IEnumerable<string> filePaths)
        {
            // 時間計測開始
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;

            foreach (var filePath in filePaths)
            {
                UpdateCache(filePath);
                count++;
            }

            // 時間計測終了
            stopwatch.Stop();
            Console.WriteLine($"Cache Updated {count} entries in {stopwatch.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public void Clear()
        {
            lock (_lockObj)
            {
                _memoryCache.Clear();
            }
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
            Console.WriteLine("[Cache] Cleared");
        }
    }
}
