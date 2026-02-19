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
	
	public class CodeGenerator
	{
		private string projectRoot = "";
		private ParallelAnalysisCache cache;
		private ReflectionParser reflectionParser;
		private Encoding encoding;
		private AnalysisConfig config;
		public string ProjectRoot()
		{
			return projectRoot;
		}
		public CodeGenerator(string projRoot, AnalysisConfig? analysisConfig = null)
		{
			config = analysisConfig ?? new AnalysisConfig();
			projectRoot = projRoot.Trim();
			string cacheFile = ".analysis_cache.json";
			// ファイルのハッシュ値を分析するクラス
			this.cache = new ParallelAnalysisCache(cacheFile);
			// 並列解析数
            int maxParallelism = config.MaxDegreeOfParallelism.HasValue ? Math.Max(1, config.MaxDegreeOfParallelism.Value) : Environment.ProcessorCount;
            // ファイルのリフレクション情報を解析するクラス
            this.reflectionParser = new ReflectionParser(projectRoot,maxParallelism);
			// エンコーディングを取得
            this.encoding = new System.Text.UTF8Encoding(false,true);
        }
		public void ClearCache()
		{
			cache.Clear();
		}
        public void Run()
		{
			object lockObj = new();

			// ヘッダファイルの数、生成をスキップした数、生成した数。コンソールに表示する
			int headerCount = 0, skipped = 0, regenerated = 0, failureCount = 0;

			// ファイルを取得
			var headerFiles = GetAnalysisTargetFile();

            // 再生成対象のファイルを取得
            ConcurrentBag<string> filesToRegenerate = new ConcurrentBag<string> ( cache.FindFilesNeedingRegeneration(headerFiles));

			var parallelOptions = new ParallelOptions()
			{
				MaxDegreeOfParallelism = config.MaxDegreeOfParallelism.HasValue ? Math.Max(1, config.MaxDegreeOfParallelism.Value) : Environment.ProcessorCount
			};

			// 再生成が必要なファイルなし
			if (filesToRegenerate.Count == 0)
			{
				Console.WriteLine("[Gen] All files up-to-date. No regeneration needed.");
				cache.SaveCacheToDisk();
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
					string relative = Path.GetRelativePath(projectRoot, headerFile);

					bool succeeded = reflectionParser.TryParse(headerFile, out ReflectedClassInfo? reflectedClass);
					if (succeeded && reflectedClass != null)
					{
						Generate(reflectedClass);
					}
					lock (lockObj)
					{
						if(succeeded && reflectedClass != null)
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
					cache.UpdateCache(headerFile);
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
			cache.SaveCacheToDisk();

			// 結果を表示
			Console.WriteLine();
			Console.WriteLine($"[Gen] Complete in {stopwatch.ElapsedMilliseconds} ms");
			Console.WriteLine($"[Gen] Regenerated: {regenerated}, Skipped: {skipped}, Failure: {failureCount}");

			(reflectionParser as IDisposable)?.Dispose();
		}
		private void Generate( ReflectedClassInfo reflectedClass)
		{
			// MT_COMPONENT属性(マクロ)が付与されているクラスのみ生成対象とする
			if (reflectedClass.Attributes.Contains("MT_COMPONENT") == false)
			{
				Console.WriteLine("[Gen] Not Attribute MT_COMPONENT");
				return;
			}
			// ヘッダの生成
			GenerateHeader(reflectedClass);
		}
		private void GenerateHeader(ReflectedClassInfo reflectedClass)
		{
			string scribanFile = "GenerateComponentHeader.sbn";
						
			// 実行ファイルと同じディレクトリ内を探す
			string sourcePath = Path.Combine(AppContext.BaseDirectory, scribanFile);
			string headerTemplateText="";
			try
			{
				headerTemplateText = File.ReadAllText(sourcePath, encoding);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"ファイル書き込みエラー:{ex.GetType().Name},{ex.Message}");
			}
			// 生成用のScribanファイルを解析
			Template headerTemplate = Template.Parse(headerTemplateText);

			// Scribanのテンプレートに変数をバインドする
			string headerResult = headerTemplate.Render(new
			{
				@name_space = reflectedClass.NameSpace,
				@class_name = reflectedClass.ClassName,
				@properties = reflectedClass.Members
				.Where(m => m.Attributes.Contains("MT_PROPERTY"))
				.Select(m => new
				{
					@name = m.Name,
					@type_name = m.TypeName
				})
				.ToList()
			});
			string fileName = $"{reflectedClass.ClassName}.generated.h";
			string filePath = Path.GetFullPath(Path.Combine(projectRoot, reflectedClass.Directory));
			string generatePath = Path.GetFullPath(Path.Combine(filePath, fileName));
			Console.WriteLine($"generate:{generatePath}");
			try
			{
                File.WriteAllText(generatePath, headerResult, encoding);
			}
			catch(Exception ex)
			{
				Console.WriteLine($"ファイル書き込みエラー:{ex.GetType().Name},{ex.Message}");
			}
		}
		

		private List<string> GetAnalysisTargetFile()
		{
            // ファイルを取得
            var headerFiles = Directory.GetFiles(projectRoot, "*.h", SearchOption.AllDirectories).ToList();

			if (config.ExcludeDirectories != null && config.ExcludeDirectories.Length > 0)
			{
				// 除外するディレクトリを絶対パスで取得
				var absoluteExcludes = config.ExcludeDirectories
					.Where(s => string.IsNullOrWhiteSpace(s) == false)
                    .Select(s =>
                    {
                        // 相対パスの場合はプロジェクトのルートと結合。絶対パスならそのまま
                        string p = Path.IsPathRooted(s) ? s : Path.GetFullPath(Path.Combine(projectRoot, s));
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
