using System.Text;
using ClangSourceGenerator;
using System.CommandLine;
class Program
{
    static int Main(string[] args)
    {
        // 文字コードのプロバイダーを登録
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // オプションの作成

        // 強制的に全解析
        var forceOption = new Option<bool>("--force", "--f")
        {
            Description = "Clear the cache and force all files to be generated",
            DefaultValueFactory = parseResult => false
        };
        // 設定ファイルのディレクトリ
        var configOption = new Option<string>("--config")
        {
            Description = $"Analysis configuration file path. File name must be {AnalysisConfig.ConfigFileName}",
            DefaultValueFactory = parseResult => Directory.GetCurrentDirectory()
        };
        // このツールを実行しているプロジェクトのディレクトリ
        var projectDirectoryOption = new Option<string>("--projectDirectory", "--projDir")
        {
            Description = "Project directory",
            DefaultValueFactory = parseResult => Directory.GetCurrentDirectory()
        };

        // オプションを追加
        var rootCommand = new RootCommand("Source Generate Tool");
        rootCommand.Options.Add(forceOption);
        rootCommand.Options.Add(configOption);
        rootCommand.Options.Add(projectDirectoryOption);

        ParseResult parseResult = rootCommand.Parse(args);
        
        if(parseResult.Errors.Count != 0)
        {
            foreach(var error in parseResult.Errors)
            {
                Console.Error.WriteLine(error.Message);
            }
            return 1;
        }
        
        string? configFilePath = parseResult.GetValue(configOption);
        if(configFilePath == null)
        {
            Console.Error.WriteLine("--config is null");
            return 1;
        }

        string projectDirectory = parseResult.GetValue(projectDirectoryOption) ?? Directory.GetCurrentDirectory();

        rootCommand.SetAction(parseResult =>
        {
            try
            {
                // 設定を読み込む
                AnalysisConfig config = AnalysisConfig.Load(projectDirectory, configFilePath);
                CodeGenerator codeGenerator = new CodeGenerator(projectDirectory, config);
                
                bool forceRegenerate = parseResult.GetValue(forceOption);
                if (forceRegenerate)
                {
                    Console.WriteLine($"Force Regenerate Header");
                    codeGenerator.ClearCache();
                }
                codeGenerator.Run();
                Console.WriteLine($"GenerateHeader: success");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"fatal: {ex.Message}");
                return 1;
            }
        });

        return parseResult.Invoke();
    }
}

