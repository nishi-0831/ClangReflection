using ClangSharp.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClangTest
{
    /// <summary>
    /// コードを生成する際の条件を保存するクラス
    /// 例 : ClassMetadataTypeがMT_COMPONENTで、MemeberMetadataTypeはMT_PROPERTY
    ///      MetadataOptionsはSerializeの場合はOutputTemplateのSerialize.sbnでコード生成...という感じ
    /// </summary>
    public class CodeGenerationRule
    {
        /// <summary>
        /// クラスに割り当てられたメタデータの種類
        /// 例 : MT_COMPONENT
        /// </summary>
        public string ClassMetadataType { get; init; } = "";
        /// <summary>
        /// メンバ変数に割り当てられたメタデータの種類
        /// 例 : MT_PROPERTY
        /// </summary>
        public string MemberMetadataType { get; init; } = "";
        /// <summary>
        /// メンバ変数のメタデータのオプション
        /// 例 : MT_PROPERTY(Serialize)のSerialize部分
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
        // 解析対象から除外するディレクトリ
        // TODO: 正規表現による指定を可能にする
        public string[] ExcludeDirectories { get; set;  } = Array.Empty<string>();
        // スレッド数
        public int? MaxDegreeOfParallelism;
        public CodeGenerationRule[] CodeGenerationRules { get; set; } = Array.Empty<CodeGenerationRule>();
        /// <summary>
        /// プロジェクトのルートディレクトリから設定ファイルを読み取る
        /// ".clangref.yaml"というファイル名にする必要がある
        /// </summary>
        /// <param name="projectRoot">解析対象のプロジェクトのルートディレクトリ</param>
        /// <returns></returns>
        public static AnalysisConfig LoadFromProjectRoot(string projectRoot)
        {
            string yamlPath = Path.Combine(projectRoot, ".clangref.yaml");

            try
            {
                if(File.Exists(yamlPath) == false)
                {
                    // ファイルが存在しない場合、例外を投げる
                    Console.Error.WriteLine($"[Config] not found \".clangref.yaml\" in: {projectRoot}. place it in root of your project.");
                }

                // yamlを読み込んで、デシリアライズ
                var yaml = File.ReadAllText(yamlPath);
                var deserializer = new DeserializerBuilder()
                    .IgnoreUnmatchedProperties()
                    .Build();
                AnalysisConfig cfg = deserializer.Deserialize<AnalysisConfig>(yaml);
                Console.WriteLine($"[Config] success to load _config");
                return cfg;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Config] failed to load _config: {ex.Message}");
            }

            return new AnalysisConfig();
        }
    }
}
