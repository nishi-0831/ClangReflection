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
    
    class CodeGenerator
    {
        private string projectRoot;
        private AnalysisCache cache;
        private ReflectionParser reflectionParser;

        CodeGenerator(string projectRoot)
        {
            this.projectRoot = projectRoot;
            this.cache = new AnalysisCache();
            this.reflectionParser = new ReflectionParser();
            cache.LoadCache();
        }
        void Run()
        {
            int headerCount = 0, skipped = 0, regenerated = 0;

            var headerFiles = Directory.GetFiles(projectRoot, "*.h", SearchOption.AllDirectories)
            .ToList();
            headerCount = headerFiles.Count;
            foreach (var headerFile in headerFiles)
            {
                // ファイルが存在するか
                if (File.Exists(headerFile))
                    continue;

                // 生成する必要があるか
                if(cache.NeedsRegeneration(headerFile))
                {
                    ReflectedClass reflectedClass = reflectionParser.Parse(headerFile);
                    Generate(reflectedClass);
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

            Console.WriteLine();
            Console.WriteLine($"Result:headerCount,{headerCount} regenerated,{regenerated} skipped,{skipped}");
        }
        public static void Generate(ReflectedClass reflectedClass)
        {
            if(reflectedClass.Attributes.Contains("MT_COMPONENT") == false)
            {
                Console.WriteLine("Not Attribute MT_COMPONENT");
                return;
            }
            string exeDir = AppContext.BaseDirectory;
            // プロジェクトルートを推定
            string projectRoot = Path.GetFullPath(Path.Combine(exeDir, @"..\..\.."));

            // Reflection ディレクトリ内のファイルを探す
            string sourceFile = "GenerateComponentHeader.sbn";
            string reflectionDir = Path.Combine(projectRoot, "Sbn");
            string sourcePath = Path.Combine(reflectionDir, sourceFile);

            string headerTemplateText = File.ReadAllText(sourcePath);
            Template headerTemplate = Template.Parse(headerTemplateText);

            var model = new { @class = reflectedClass };

            //string headerResult = headerTemplate.Render(model, member => member.Name);
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
            string fileName = $"{reflectedClass.ClassName}.h";
            string filePath = Path.GetFullPath(Path.Combine(ReflectionParser.ProjectDir, reflectedClass.Directory));
            string generatePath = Path.GetFullPath(Path.Combine(filePath, fileName));
            
            File.WriteAllText(generatePath, headerResult);
        }
    }
}
