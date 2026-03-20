using Scriban;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Text;

namespace ClangSourceGenerator
{
	public class CodeGenerator
	{
		private string _projectRoot = string.Empty;
		private string _outputDir = string.Empty;
		private AnalysisCache _cache;
		private ReflectionParser _reflectionParser;
		private Encoding _encoding;
		private AnalysisConfig _config;
		private const string CacheFileName = ".analysis_cache.json";
        
		public CodeGenerator(string projRoot, AnalysisConfig analysisConfig)
		{
			_config = analysisConfig;
			_projectRoot = projRoot.Trim();
			_outputDir = Path.Combine(_projectRoot, _config.OutputDirectory);
			// キャッシュファイルのパスを計算
			string cacheFileDirectory = Path.Combine(_projectRoot, analysisConfig.CacheFileDirectory);
			string cacheFilePath = Path.Combine(cacheFileDirectory,CacheFileName);
			// ファイルのハッシュ値を分析するクラス
			this._cache = new AnalysisCache(cacheFilePath);
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
			Directory.CreateDirectory(_outputDir);

			object lockObj = new();

			// ヘッダファイルの数、生成をスキップした数、生成した数。コンソールに表示する
			int headerCount = 0, skipped = 0, generated = 0, failureCount = 0;

			// ファイルを取得
			var headerFiles = GetAnalysisTargetFile();

            // 再生成対象のファイルを取得
            ConcurrentBag<string> filesToRegenerate = new(_cache.FindFilesNeedingRegeneration(headerFiles));

			var parallelOptions = new ParallelOptions()
			{
				MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism.HasValue ? Math.Max(1, _config.MaxDegreeOfParallelism.Value) : Environment.ProcessorCount
			};

			// 再生成が必要なファイルなし
			if (filesToRegenerate.IsEmpty)
			{
				Console.WriteLine("[Gen] All files up-to-date. No regeneration needed.");
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

                    Console.WriteLine($"[Gen] parse {relative}");
					// ヘッダを解析
                    ReflectedClass? reflectedClass = _reflectionParser.Parse(headerFile);
					bool success = false;
					if (reflectedClass != null)
					{
						// ソースコードの生成を試みる
						success = Generate(in reflectedClass);
					}
					lock (lockObj)
					{
						if(success)
						{
							// 生成に成功
                            generated++;
                        }
                        else
						{
							// 生成を行わなかった
                            skipped++;
                            Console.WriteLine($"[Gen] skipped {relative}");
                        }
                    }
					// ヘッダファイルをキャッシュ
					_cache.UpdateCache(headerFile);
				}
				catch (Exception ex)
				{
					lock (lockObj)
					{
                        Console.Error.WriteLine($"[Gen] error {Path.GetFileName(headerFile)}: {ex.Message}");
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
			Console.WriteLine($"[Gen] Generated: {generated}, Skipped: {skipped}, Failure: {failureCount}");

			(_reflectionParser as IDisposable)?.Dispose();
		}
		
		/// <summary>
		/// 解析したクラス情報に基づいて、設定済みのコード生成ルールからソースコードを生成する
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item><description>クラスにメタデータが設定されていない場合はスキップ</description>></item>
		/// <item><description>生成されたコードは、元のヘッダファイルと同じディレクトリに<c>{ClassName}.generated.h</c>として出力される</description>></item>
		/// </list>
		/// </remarks>
		/// <param name="reflectedClass">解析済みのクラス情報</param>
		/// <returns>一つ以上のファイル生成に成功した場合は<c>true</c>、すべてスキップまたは失敗した場合は<c>false</c></returns>
		private bool Generate(ref readonly ReflectedClass reflectedClass)
		{
			bool result = false;
			// MetadaTypeが空の場合はスキップ
			if (string.IsNullOrEmpty(reflectedClass.MetadataType))
				return result;

			foreach(var rule in _config.CodeGenerationRules)
			{
				// コード生成ルールをもとに、生成された文字列
				string renderedText = Render(in reflectedClass, rule);

				// 空の場合は生成を行っていないので、continue
				if (string.IsNullOrEmpty(renderedText))
					continue;

				// 生成ファイル名を計算
				string fileNameTemplate = rule.OutputFileName;
				string fileName = fileNameTemplate.Replace(CodeGenerationRule.ReplaceString,reflectedClass.ClassName,StringComparison.Ordinal);
				// 生成ファイルのパス
				string generateFilePath = Path.Combine(_outputDir, fileName);
				// 生成した文字列を書き込んでいく
				try
				{
					File.WriteAllText(generateFilePath, renderedText, _encoding);
					Console.WriteLine($"[Gen] generate:{generateFilePath}");
					result = true;
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[Gen] File Write Error:{ex.GetType().Name},{ex.Message}");
				}
			}
			return result;
        }

        /// <summary>
        /// 指定されたコード生成ルールに基づいて、Scribanテンプレートで文字列をレンダリングする
        /// </summary>
		/// <remarks>
		/// ファイル読み込みの例外が発生しても、スローせずに空文字を返す
		/// </remarks>
        /// <param name="reflectedClass">解析済みのクラス情報</param>
        /// <param name="rule">適用するコード生成ルール</param>
        /// <returns>レンダリング結果の文字列。レンダリングが行われなかった場合、空文字列を返す</returns>
        private string Render(ref readonly ReflectedClass reflectedClass,CodeGenerationRule rule)
		{
			string result = string.Empty;

			// ruleにメタデータの種類が指定されていない場合はreturnする
			if (string.IsNullOrEmpty(rule.ClassMetadataType))
				return result;
			// ruleに定められたメタデータの種類(例:UCOMPONENT)、とクラスのメタデータの種類が一致しているか
            if (rule.ClassMetadataType != reflectedClass.MetadataType)
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
            members = reflectedClass.Members.
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
			string parsedFileRelativePath = _outputDir;
			if(string.IsNullOrEmpty(reflectedClass.Directory) == false)
			{
				parsedFileRelativePath = Path.GetRelativePath(parsedFileRelativePath, reflectedClass.Directory).Replace("\\", "/");
            }
            // テンプレートにクラスの情報を渡して、ソースを生成
            result = template.Render(new
            {
                @name_space = reflectedClass.NameSpace,
                @class_name = reflectedClass.ClassName,
                @properties = members,
				@parsedFileRelativePath = parsedFileRelativePath
            });
			return result;
        }
        /// <summary>
        /// 解析対象のファイル群を取得
        /// </summary>
        /// <returns>解析対象のファイル群</returns>
        private List<string> GetAnalysisTargetFile()
		{
            // ファイルを取得
            var headerFiles = Directory.GetFiles(_projectRoot, "*.h", SearchOption.AllDirectories).ToList();

			if (_config.ExcludeDirectories != null && _config.ExcludeDirectories.Count > 0)
			{
                // 解析対象外のファイルを除外する
                headerFiles = headerFiles.Where(file =>
				{
					return ContainsExcludePath(file) == false;
				}).ToList();
			}

			return headerFiles;
		}
		/// <summary>
		/// ファイルのパスが、解析から除外する対象か否かを返す
		/// </summary>
		/// <param name="filePath">ファイルのパス</param>
		/// <returns>除外対象の場合、またはファイルのディレクトリが取得できなかった場合は<c>true</c>を返す</returns>
        bool ContainsExcludePath(string filePath)
        {
			// 絶対パスに正規化し、区切り文字を統一
            var fileFullPath = Path.GetFullPath(filePath);
			var normalizedFile = fileFullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

			// ディレクトリを取得
			var directoryName = Path.GetDirectoryName(normalizedFile);
			// 取得できなかった場合は除外対象とする
			if (directoryName == null)
				return true;

			// ディレクトリを区切り文字によってセグメントに分割
			// home/user/doc の場合、 home,user,docに分かれる
			string[] segments = directoryName.Split(Path.DirectorySeparatorChar);
			// セグメントと除外ディレクトリが一致するか確認
			foreach(var segment in segments)
			{
				foreach(var excludeSegment in _config.ExcludeDirectories)
				{
					// 大文字小文字は無視
					if(string.Equals(segment,excludeSegment,StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
			return false;	
        }
    }
}
