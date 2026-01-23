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
using System.Threading.Tasks;

namespace ClangTest
{
	
	public class CodeGenerator
	{
		private string projectRoot = "";
		private ParallelAnalysisCache cache;
		private ReflectionParser reflectionParser;
		private Encoding encoding;
		public string ProjectRoot()
		{
			return projectRoot;
		}
		public CodeGenerator(string projRoot)
		{
			projectRoot = projRoot.Trim();
			string cacheFile = ".analysis_cache.json";
			// ファイルのハッシュ値を分析するクラス
			this.cache = new ParallelAnalysisCache(cacheFile);
			// ファイルのリフレクション情報を解析するクラス
			this.reflectionParser = new ReflectionParser(projectRoot);
			// エンコーディングを取得
            //this.encoding = Encoding.Default;
            this.encoding = new System.Text.UTF8Encoding(false,true);
            //this.encoding = Encoding.GetEncoding("shift_jis");
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
			var headerFiles = Directory.GetFiles(projectRoot, "*.h", SearchOption.AllDirectories).ToList();

            // 再生成対象のファイルを取得
            ConcurrentBag<string> filesToRegenerate = new ConcurrentBag<string> ( cache.FindFilesNeedingRegeneration(headerFiles));

			var parallelOptions = new ParallelOptions()
			{ 
				MaxDegreeOfParallelism = Environment.ProcessorCount
			};

			if (filesToRegenerate.Count == 0)
			{
				Console.WriteLine("[Gen] All files up-to-date. No regeneration needed.");
				cache.SaveCacheToDisk();
				return;
			}

			Console.WriteLine($"[Gen] Regenerating {filesToRegenerate.Count} files...");
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

			stopwatch.Stop();

			// キャッシュを保存
			cache.SaveCacheToDisk();

			// 結果を表示
			Console.WriteLine();
			Console.WriteLine($"[Gen] Complete in {stopwatch.ElapsedMilliseconds} ms");
			Console.WriteLine($"[Gen] Regenerated: {regenerated}, Skipped: {skipped}, Failure: {failureCount}");
		}
		private void Generate( ReflectedClassInfo reflectedClass)
		{
			// MT_COMPONENT属性(マクロ)が付与されているクラスのみ生成対象とする
			if (reflectedClass.Attributes.Contains("MT_COMPONENT") == false)
			{
				Console.WriteLine("Not Attribute MT_COMPONENT");
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
		private void GenerateCpp(ReflectedClassInfo reflectedClass)
		{
			string scribanFile = "GenerateComponentCpp.sbn";

			// 実行ファイルと同じディレクトリ内を探す
			string sourcePath = Path.Combine(AppContext.BaseDirectory, scribanFile);
			string headerTemplateText = "";
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
            string cppResult = headerTemplate.Render(new
			{
				@header_file = reflectedClass.HeaderFile,
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
			string fileName = $"{reflectedClass.ClassName}.generated.cpp";
			string filePath = Path.GetFullPath(Path.Combine(projectRoot, reflectedClass.Directory));
			string generatePath = Path.GetFullPath(Path.Combine(filePath, fileName));
			Console.WriteLine($"generate:{generatePath}");
			try
			{
                File.WriteAllText(generatePath, cppResult, encoding);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"ファイル書き込みエラー:{ex.GetType().Name},{ex.Message}");
			}
		}

	}
}
