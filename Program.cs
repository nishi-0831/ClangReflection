using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClangTest;
class Program
{
    static int Main(string[] args)
    {
        // 文字コードのプロバイダーを登録
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (args.Length < 1)
        {
            Console.WriteLine("fatal");
            return 1;
        }

        string projectRoot = args[0];
        List<string> splitArgs = args.ToList();
        if (args.Length == 1)
        {
            splitArgs = args[0].Split(' ').ToList();
            projectRoot = splitArgs[0];
        }
        bool forceRegenerate = splitArgs.Contains("--force");

        foreach (string arg in splitArgs)
        {
            Console.WriteLine($"{arg}");
        }
        try
        {
            CodeGenerator codeGenerator = new CodeGenerator();
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
    }
}

