using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClangTest
{
    public class ReflectedMember
    {
        public string Name { get; set; } = "";
        public string TypeName { get; set; } = "";
        public bool IsPrivate { get; set; }
        public string AccessLevel { get; set; } = "";
        public List<string> Attributes { get; set; } = new();
    }

    public class ReflectedClassInfo
    {
        public string ClassName { get; set; } = "";
        public string NameSpace { get; set; } = "";
        public List<ReflectedMember> Members { get; set; } = new();
        public string HeaderFile { get; set; } = "";
        public string Directory { get; set; } = "";
        public List<string> Attributes { get; set; } = new();

    }
}
