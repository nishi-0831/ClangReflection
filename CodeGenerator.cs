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
        public static void Generate(ReflectedClass reflectedClass)
        {
            string exeDir = AppContext.BaseDirectory;
            // プロジェクトルートを推定
            string projectRoot = Path.GetFullPath(Path.Combine(exeDir, @"..\..\.."));

            // Reflection ディレクトリ内のファイルを探す
            string sourceFile = "Header.sbn";
            string reflectionDir = Path.Combine(projectRoot, "Sbn");
            string sourcePath = Path.Combine(reflectionDir, sourceFile);

            string headerTemplateText = File.ReadAllText(sourcePath);
            Template headerTemplate = Template.Parse(headerTemplateText);

            var model = new { @class = reflectedClass };

            string headerResult = headerTemplate.Render(model, member => member.Name);

            File.WriteAllText($"{reflectedClass.ClassName}.h", headerResult);
        }
    }
}
