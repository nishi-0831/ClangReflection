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
        public string ClassMetadataType { get; init; } = "";
        /// <summary>
        /// <para> メンバ変数に割り当てられたメタデータの種類 </para>
        /// <para> 例 : MT_PROPERTY </para>
        /// </summary>
        public string MemberMetadataType { get; init; } = "";
        /// <summary>
        /// <para> メンバ変数のメタデータのオプション </para>
        /// <para> 例 : MT_PROPERTY(Serialize)のSerialize部分</para>
        /// </summary>
        public string MetadataOptions { get; init; } = "";
        /// <summary>
        /// コード生成に利用するテンプレートファイル
        /// Scribanを使う
        /// </summary>
        public string OutputTemplate { get; init; } = "";
    }
    /// <summary>
    /// コード解析、生成する際の設定を保持するクラス
    /// </summary>
    public class AnalysisConfig
    {
        private AnalysisConfig() { }
        public static string ConfigFileName { get; } = ".clang-src-gen.yaml";
        public string CacheFileDirectory { get; private set; } = string.Empty;
        // 解析対象から除外するディレクトリ
        // TODO: 正規表現による指定を可能にする
        public string[] ExcludeDirectories { get; private set; } = Array.Empty<string>();
        // スレッド数
        public int? MaxDegreeOfParallelism { get; private set; }
        public CodeGenerationRule[] CodeGenerationRules { get; private set; } = Array.Empty<CodeGenerationRule>();
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
                    // privateコンストラクタのオブジェクトもデシリアライズを許可
                    .EnablePrivateConstructors() 
                    .IgnoreUnmatchedProperties()
                    .Build();
                AnalysisConfig cfg = deserializer.Deserialize<AnalysisConfig>(yaml);
                Console.WriteLine($"[Config] success to load _config");
                return cfg;
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
