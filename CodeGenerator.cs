using ClangSharp.Interop;
using ClangTest;
using Scriban;
using Scriban.Parsing;
using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ClangTest
{
	public readonly struct GenerateTargetInfo
	{
		public string NameSpace { get; }
		public string ClassName { get; }
		public string Directory { get; }
		public GenerateTargetInfo(string nameSpace,string className,string directory)
		{
			this.NameSpace = nameSpace;
			this.ClassName = className;
			this.Directory = directory;
		}
		public GenerateTargetInfo(ref readonly ReflectedClassInfo reflectedClass)
		{
			this.NameSpace= reflectedClass.NameSpace;
			this.ClassName = reflectedClass.ClassName;
			this.Directory = reflectedClass.Directory;
		}
    }

	public class CodeGenerator
	{
		private string _projectRoot = "";
		private ParallelAnalysisCache _cache;
		private ReflectionParser _reflectionParser;
		private Encoding _encoding;
		private AnalysisConfig _config;
		private const string CacheFilePath = ".analysis_cache.json";

        public string ProjectRoot()
		{
			return _projectRoot;
		}
		public CodeGenerator(string projRoot, AnalysisConfig? analysisConfig = null)
		{
			_config = analysisConfig ?? new AnalysisConfig();
			_projectRoot = projRoot.Trim();
			string cacheFile = CacheFilePath;
			// ファイルのハッシュ値を分析するクラス
			this._cache = new ParallelAnalysisCache(cacheFile);
			// 並列解析数
            int maxParallelism = _config.MaxDegreeOfParallelism.HasValue ? Math.Max(1, _config.MaxDegreeOfParallelism.Value) : Environment.ProcessorCount;
            // ファイルのリフレクション情報を解析するクラス
            this._reflectionParser = new ReflectionParser(_projectRoot,maxParallelism);
			// エンコーディングを取得
            this._encoding = new System.Text.UTF8Encoding(false,true);
        }
		public void ClearCache()
		{
			_cache.Clear();
		}
        public void Run()
		{
			object lockObj = new();

			// ヘッダファイルの数、生成をスキップした数、生成した数。コンソールに表示する
			int headerCount = 0, skipped = 0, regenerated = 0, failureCount = 0;

			// ファイルを取得
			var headerFiles = GetAnalysisTargetFile();

            // 再生成対象のファイルを取得
            ConcurrentBag<string> filesToRegenerate = new ConcurrentBag<string> ( _cache.FindFilesNeedingRegeneration(headerFiles));

			var parallelOptions = new ParallelOptions()
			{
				MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism.HasValue ? Math.Max(1, _config.MaxDegreeOfParallelism.Value) : Environment.ProcessorCount
			};

			// 再生成が必要なファイルなし
			if (filesToRegenerate.Count == 0)
			{
				Console.WriteLine("[Gen] All files up-to-date. No regeneration needed.");
				_cache.SaveCacheToDisk();
				return;
			}

			Console.WriteLine($"[Gen] Regenerating {filesToRegenerate.Count} files...");
			// 計測開始
			System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
			
			// ヘッダファイルの数をカウント
			headerCount = headerFiles.Count;
			Parallel.ForEach(filesToRegenerate, parallelOptions, headerFile =>
			{
				try
				{
					string relative = Path.GetRelativePath(_projectRoot, headerFile);

                    ReflectedClassInfo? reflectedClass = _reflectionParser.Parse(headerFile);
					if (reflectedClass != null)
					{
						Generate(in reflectedClass);
					}
					lock (lockObj)
					{
						// TODO: Generateで実際に生成されるとは限らないため、
						// この条件式でカウントするのは間違い。Loggerを用意して詳細にチェックすべし
						if(reflectedClass != null)
						{
                            regenerated++;
                            Console.WriteLine($"[Gen] {relative}");
                        }
                        else
						{
                            skipped++;
                            Console.WriteLine($"[Skipped] {relative}");
                        }
                    }
					_cache.UpdateCache(headerFile);
				}
				catch (Exception ex)
				{
					lock (lockObj)
					{
                        Console.Error.WriteLine($"[Error] {Path.GetFileName(headerFile)}: {ex.Message}");
                        failureCount++;
                    }
				}
			});

            // 計測終了
            stopwatch.Stop();

			// キャッシュを保存
			_cache.SaveCacheToDisk();

			// 結果を表示
			Console.WriteLine();
			Console.WriteLine($"[Gen] Complete in {stopwatch.ElapsedMilliseconds} ms");
			Console.WriteLine($"[Gen] Regenerated: {regenerated}, Skipped: {skipped}, Failure: {failureCount}");

			(_reflectionParser as IDisposable)?.Dispose();
		}
		
		private void Generate(ref readonly ReflectedClassInfo reflectedClass)
		{
			List<string> results = new ();
			
			foreach(var rule in _config.CodeGenerationRules)
			{
				string result = Render(in reflectedClass, rule);
				if (string.IsNullOrEmpty(result))
					continue;
				string fileName = $"{reflectedClass.ClassName}.generated.h";
				string filePath = Path.GetFullPath(Path.Combine(_projectRoot, reflectedClass.Directory));
				string generateFile = Path.GetFullPath(Path.Combine(filePath, fileName));
				try
				{
					File.WriteAllText(generateFile, result, _encoding);
					Console.WriteLine($"generate:{generateFile}");
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"File Write Error:{ex.GetType().Name},{ex.Message}");
				}
			}
        }
		
		private string Render(ref readonly ReflectedClassInfo classInfo,CodeGenerationRule rule)
		{
			string result = string.Empty;

			// ruleにメタデータの種類が指定されていない場合はreturnする
			if (string.IsNullOrEmpty(rule.ClassMetadataType))
				return result;
			// ruleに定められたメタデータの種類(例:UPROPERTY)、とクラスのメタデータの種類が一致しているか
            if (rule.ClassMetadataType != classInfo.MetadataType)
                return result;

			// テンプレートファイルのパス
			string templateFilePath = "";
			// 絶対パスか判定
            if (Path.IsPathFullyQualified(rule.OutputTemplate))
			{
				templateFilePath = rule.OutputTemplate;
			}
			else
			{
                templateFilePath = Path.Combine(_projectRoot, rule.OutputTemplate);
            }

			// ファイルが存在するか判定
            if (File.Exists(templateFilePath) == false)
				return result;

            List<ReflectedMember> members = new();

			// ruleに指定されたメタデータと一致しているメンバ変数を抽出
            members = classInfo.Members.
                Where(member => member.MetaOptions.Contains(rule.MetadataOptions) || string.IsNullOrEmpty(rule.MetadataOptions)).
                Where(member => member.MetadataType == rule.MemberMetadataType).ToList();

            string templateString = "";
            try
            {
				// テンプレートファイルからテキストを読み込み
                templateString = File.ReadAllText(templateFilePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"File Read Error:{ex.GetType().Name},{ex.Message}");
            }
			// テンプレートを作成
            Template template = Template.Parse(templateString);

			// テンプレートにクラスの情報を渡して、ソースを生成
            result = template.Render(new
            {
                @name_space = classInfo.NameSpace,
                @class_name = classInfo.ClassName,
                @properties = members
            });
			return result;
        }
		
        private List<string> GetAnalysisTargetFile()
		{
            // ファイルを取得
            var headerFiles = Directory.GetFiles(_projectRoot, "*.h", SearchOption.AllDirectories).ToList();

			if (_config.ExcludeDirectories != null && _config.ExcludeDirectories.Length > 0)
			{
				// 除外するディレクトリを絶対パスで取得
				var absoluteExcludes = _config.ExcludeDirectories
					.Where(s => string.IsNullOrWhiteSpace(s) == false)
                    .Select(s =>
                    {
                        // 相対パスの場合はプロジェクトのルートと結合。絶対パスならそのまま
                        string p = Path.IsPathRooted(s) ? s : Path.GetFullPath(Path.Combine(_projectRoot, s));
                        // 終端に"\"(バックスラッシュ)が付いていない場合は、付ける
                        if (p.EndsWith(Path.DirectorySeparatorChar) == false)
                        {
                            p = p + Path.DirectorySeparatorChar;
                        }
                        return p;
                    }).ToArray();

				headerFiles = headerFiles.Where(file =>
				{
					foreach (var excludePath in absoluteExcludes)
					{
						if(ContainsExcludePath(excludePath,file))
						{
							// 除外するディレクトリにある場合は解析しない
							return false;
						}
					}
					return true;
				}).ToList();

			}

			return headerFiles;
		}
        bool ContainsExcludePath(string excludePath,string filePath)
        {
			// 絶対パスに正規化し、区切り文字を統一、除外パスには末尾区切りを付ける
            var fileFullPath = Path.GetFullPath(filePath);
			var normalizedFile = fileFullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

			var normalizedExclude = Path.GetFullPath(excludePath)
				.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			// MEMO: 
			// - 末尾区切りを付けていないと "C:\foo"と "C:\foobar" の場合、部分一致により誤検知
			// - "C:\foo\" と "C:\foobar\" なら大丈夫
			if(normalizedExclude.EndsWith(Path.DirectorySeparatorChar) == false)
			{
				normalizedExclude += Path.DirectorySeparatorChar;
			}
			
			// 大文字小文字は無視
			return normalizedFile.StartsWith(normalizedExclude,StringComparison.OrdinalIgnoreCase);
        }
    }
}
