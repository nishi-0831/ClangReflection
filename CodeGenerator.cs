using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Scriban;
using System.IO;
using ClangTest;
using System.CodeDom.Compiler;
namespace ClangTest
{
    
    public class CodeGenerator
    {
        private string projectRoot = "";
        private AnalysisCache cache;
        private ReflectionParser reflectionParser;

        public CodeGenerator()
        {
            
            // プロジェクトのディレクトリが書かれたファイルを読み取る
            if (File.Exists("ProjectDirPath.txt"))
            {
                using (StreamReader sr = new StreamReader("ProjectDirPath.txt"))
                {
                    // 改行や空白を除去し、ディレクトリを取得
                    projectRoot = sr.ReadToEnd().Trim();
                }
            }
            else
            {
                // ファイルがなければ終了させる
                string currentDir = Directory.GetCurrentDirectory();
                throw new FileNotFoundException($"ProjectDirPath.txt が見つかりません。探索ディレクトリ:{currentDir}");
            }

            // ファイルのハッシュ値を分析するクラス
            this.cache = new AnalysisCache();
            // ファイルのリフレクション情報を解析するクラス
            this.reflectionParser = new ReflectionParser();

            // ハッシュ値のキャッシュを読み取る
            cache.LoadCache();
        }
        public void Run()
        {
            // ヘッダファイルの数、生成をスキップした数、生成した数。コンソールに表示する
            int headerCount = 0, skipped = 0, regenerated = 0;

            // ファイルを取得
            var headerFiles = Directory.GetFiles(projectRoot, "*.h", SearchOption.AllDirectories).ToList();

            // ヘッダファイルの数をカウント
            headerCount = headerFiles.Count;

            foreach (var headerFile in headerFiles)
            {
                // ファイルが存在するか
                if (File.Exists(headerFile) == false)
                {
                    Console.WriteLine($"{headerFile}が見つかりません");
                    skipped++;
                    continue;
                }

                // 生成する必要があるか
                if(cache.NeedsRegeneration(headerFile))
                {
                    ReflectedClassInfo? reflectedClass = null;
                    // ヘッダファイルを解析し、クラスの情報を取得
                    if (reflectionParser.TryParse(headerFile, out reflectedClass) == false)
                    {
                        skipped++;
                        continue;
                    }
                    if (reflectedClass == null)
                    {
                        skipped++;
                        continue;
                    }

                    // ファイルを生成
                    Generate(reflectedClass);
                    // ファイルを更新したのでキャッシュを更新
                    cache.UpdateCache(headerFile);
                    // 生成数をカウント
                    regenerated++;
                }
                else
                {
                    // スキップ数をカウント
                    skipped++;
                }
            }
            // キャッシュを保存
            cache.SaveCache();
            

            // 結果を表示
            Console.WriteLine();
            Console.WriteLine($"Result:headerCount,{headerCount} regenerated,{regenerated} skipped,{skipped}");
        }
        private void Generate(ReflectedClassInfo reflectedClass)
        {
            // MT_COMPONENT属性(マクロ)が付与されているクラスのみ生成対象とする
            if(reflectedClass.Attributes.Contains("MT_COMPONENT") == false)
            {
                Console.WriteLine("Not Attribute MT_COMPONENT");
                return;
            }
            string scribanFile = "GenerateComponentHeader.sbn";
                        
            // 実行ファイルと同じディレクトリ内を探す
            string sourcePath = Path.Combine(AppContext.BaseDirectory, scribanFile);
            string headerTemplateText="";
            try
            {
                headerTemplateText = File.ReadAllText(sourcePath);
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
                File.WriteAllText(generatePath, headerResult);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"ファイル書き込みエラー:{ex.GetType().Name},{ex.Message}");
            }
        }
    }
}
