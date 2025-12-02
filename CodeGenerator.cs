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
