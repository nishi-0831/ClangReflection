using YamlDotNet.Serialization;

namespace ClangSourceGenerator
{
    /// <summary>
    /// コードを生成する際の条件を保存するクラス
    /// <para> 例 : ClassMetadataTypeがMT_COMPONENTで、MemeberMetadataTypeがMT_PROPERTY
    /// MetadataOptionsがSerializeの場合は OutputTemplateのSerialize.sbnでコード生成...という感じ</para>
    /// </summary>
    public class CodeGenerationRule
    {
        /// <summary>
        /// <para> クラスに割り当てられたメタデータの種類</para>
        /// <para> 例 : MT_COMPONENT</para>
        /// </summary>
        public string ClassMetadataType { get; init; } = string.Empty;
        /// <summary>
        /// <para> メンバ変数に割り当てられたメタデータの種類 </para>
        /// <para> 例 : MT_PROPERTY </para>
        /// </summary>
        public string MemberMetadataType { get; init; } = string.Empty;
        /// <summary>
        /// <para> メンバ変数のメタデータのオプション </para>
        /// <para> 例 : MT_PROPERTY(Serialize)のSerialize部分</para>
        /// </summary>
        public string MetadataOptions { get; init; } = string.Empty;
        /// <summary>
        /// コード生成に利用するテンプレートファイル
        /// Scribanを使う
        /// </summary>
        public string OutputTemplate { get; init; } = string.Empty;

        /// <summary>
        /// <para> 生成されるファイルの名前。<see cref="CodeGenerationRule.ReplaceString"/>を解析したクラス名で置換する </para>
        /// <para> 例 : "{}.generated.h" で解析クラスが Hogeの場合、 Hoge.generated.hとなる </para>
        /// </summary>
        public string OutputFileName { get; init; } = string.Empty;
        /// <summary>
        /// <para> OutputFileNameの特定箇所を解析したクラス名で置き換える文字 </para>
        /// <para> 例 : "{}.generated.h" で解析クラスが Hogeの場合、 Hoge.generated.hとなる </para>
        /// </summary>
        public const string ReplaceString = "{}";
    }
    /// <summary>
    /// コード解析、生成する際の設定を保持するクラス
    /// </summary>
    public class AnalysisConfig
    {
        private AnalysisConfig(RawAnalysisConfig rawAnalysisConfig) 
        {
            CacheFileDirectory = rawAnalysisConfig.CacheFileDirectory;
            ExcludeDirectories = rawAnalysisConfig.ExcludeDirectories;
            OutputDirectory = rawAnalysisConfig.OutputDirectory;
            MaxDegreeOfParallelism = rawAnalysisConfig.MaxDegreeOfParallelism;
            CodeGenerationRules = rawAnalysisConfig.CodeGenerationRules;
        }
        public static string ConfigFileName { get; } = ".clang-src-gen.yaml";
        public string CacheFileDirectory { get; } = string.Empty;
        // 解析対象から除外するディレクトリ
        public IReadOnlyList<string> ExcludeDirectories { get;}
        // スレッド数
        public int? MaxDegreeOfParallelism { get; }
        public string OutputDirectory { get;} = string.Empty;
        public IReadOnlyList<CodeGenerationRule> CodeGenerationRules { get; }

        /// <summary>
        /// デシリアライズ専用のクラス
        /// </summary>
        private sealed class RawAnalysisConfig
        {
            public string CacheFileDirectory { get; set; } = string.Empty;
            public string[] ExcludeDirectories { get; set; } = [];
            public int? MaxDegreeOfParallelism { get; set; }
            public string OutputDirectory { get; set; } = string.Empty;
            public CodeGenerationRule[] CodeGenerationRules { get; set; } = [];
        }
        /// <summary>
        /// <para> ファクトリメソッド </para>
        /// <para> プロジェクトのルートディレクトリから設定ファイルを読み取る </para>
        /// </summary>
        /// <param name="projectRoot">解析対象のプロジェクトのルートディレクトリ</param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException">ファイルが存在しない場合にスローされる</exception>
        public static AnalysisConfig Load(string projectRoot,string configFilePath)
        {
            string yamlPath = CalculateFilePath(projectRoot, configFilePath);
            try
            {
                if(File.Exists(yamlPath) == false)
                {
                    // ファイルが存在しない場合、例外を投げる
                    string msg = $"[Config] not found \"{ConfigFileName}\" in: {yamlPath}.";
                    throw new FileNotFoundException(msg);
                }

                // yamlを読み込んで、デシリアライズ
                var yaml = File.ReadAllText(yamlPath);
                var deserializer = new DeserializerBuilder()
                    .IgnoreUnmatchedProperties()
                    .Build();
                RawAnalysisConfig rawCfg = deserializer.Deserialize<RawAnalysisConfig>(yaml);
                Console.WriteLine($"[Config] success to load _config");
                return new AnalysisConfig(rawCfg);
            }
            catch (Exception)
            {
                throw;
            }
        }
        private static string CalculateFilePath(string projectRoot, string configFilePath)
        {
            string configFileFullPath = Path.Combine(projectRoot, configFilePath);
            var normalizedFile = configFileFullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if ( Path.GetFileName(normalizedFile) != ConfigFileName)
            {
                // ファイル名を結合する
                return Path.Combine(normalizedFile, ConfigFileName);
            }
            return normalizedFile;
        }
    }
}
