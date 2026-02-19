using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClangTest
{
    
    public class ReflectedMember
    {
        public string Name { get; init; } = "";
        public string TypeName { get; init; } = "";
        public bool IsPrivate { get; init; }
        public string AccessLevel { get; init; } = "";
        public string MetadataType { get; init; } = "";
         public IReadOnlyList<string> MetaOptions { get; init; } = new List<string>();
        public string NameSpace { get; init; } = "";
    }

    public class ReflectedClassInfo
    {
        public string ClassName { get; init; } = "";
        public string NameSpace { get; init; } = "";
        public IReadOnlyList<ReflectedMember> Members { get; init; } = new List<ReflectedMember>();
        public string Directory { get; init; } = "";
        public string MetadataType { get; init; } = "";

        public IReadOnlyList<string> MetaOptions { get; init; } = new List<string>();
    }
}
