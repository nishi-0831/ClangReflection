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
    public class AnalysisConfig
    {
        // 解析対象から除外するディレクトリ
        // TODO: 正規表現による指定を可能にする
        public string[] ExcludeDirectories { get; set;  } = Array.Empty<string>();
        // スレッド数
        public int? MaxDegreeOfParallelism;

        public static AnalysisConfig LoadFromProjectRoot(string projectRoot)
        {
            string yamlPath = Path.Combine(projectRoot, ".clangref.yaml");

            try
            {
                if(File.Exists(yamlPath) == false)
                {
                    // ファイルが存在しなくても、エラーにはしない
                    Console.WriteLine($"[Config] not found \".clangref.yaml\" in: {projectRoot}. place it in root of your project.");
                    return new AnalysisConfig();
                }

                // yamlを読み込んで、デシリアライズ
                var yaml = File.ReadAllText(yamlPath);
                var deserializer = new DeserializerBuilder()
                    .IgnoreUnmatchedProperties()
                    .Build();
                AnalysisConfig cfg = deserializer.Deserialize<AnalysisConfig>(yaml);
                Console.WriteLine($"[Config] success to load config");
                return cfg;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Config] failed to load config: {ex.Message}");
            }

            return new AnalysisConfig();
        }
    }
}
