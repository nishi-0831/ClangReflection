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
        if (args.Length < 1)
        {
            Console.WriteLine("fatal");
            return 1;
        }

        string projectRoot = args[0];

        try
        {

            CodeGenerator codeGenerator = new CodeGenerator();
            codeGenerator.Run();
            Console.WriteLine($"Generate: success");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fatal: {ex.Message}");
            return 1;
        }
    }
}

