using ClangSharp;
using ClangSharp.Interop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Reflection;
using System.IO.Enumeration;
namespace ClangTest
{
    
    class ReflectionParser
    {
        public ReflectionParser() 
        {
            string? libclangPath = Environment.GetEnvironmentVariable("LIB_CLANG_DLL");
            if (libclangPath == null)
            {
                throw new Exception("libclang.dll not found!!!!!!");
            }
            NativeLibrary.Load(libclangPath);
            if (File.Exists("ProjectDirPath.txt"))
            {
                using (StreamReader sr = new StreamReader("ProjectDirPath.txt"))
                {
                    // 改行や空白を除去
                    projectDir = sr.ReadToEnd().Trim();
                }
            }
        }
        static unsafe void Main()
        {
            var index = CXIndex.Create();
            var trans = new CXTranslationUnit();

            // 実行ファイルのディレクトリ
            string exeDir = AppContext.BaseDirectory;

            // プロジェクトルートを推定
            string projectRoot = Path.GetFullPath(Path.Combine(exeDir, @"..\..\.."));

            // Reflection ディレクトリ内のファイルを探す
            string sourceFile = "Transform.h";
            string reflectionDir = Path.Combine(projectRoot, "Reflection");
            string sourcePath = Path.Combine(reflectionDir, sourceFile);

            // 絶対パスでファイルの存在を確認する
            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"ファイルが見つかりません: {sourcePath}");
                return;
            }

            // ソースから属性を抽出
            Dictionary<string, List<string>> attributeMap = ExtractAttributesFromSource(sourcePath);

            // include は -I とパスを結合した一つの引数にする（"-I", "path" の配列分割は安全ではないことがある）
            var includePath = Path.GetFullPath(Path.Combine(projectRoot,"Reflection"));
            var args = new[] { "-std=c++20", $"-I{includePath}","-x", "c++-header","-w" };

            // エラーコードを受け取り、失敗なら表示する（TryParse を使う）

            var err = CXTranslationUnit.TryParse(index, sourcePath, args, Array.Empty<CXUnsavedFile>(),
                CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord, out trans);
            if (err != CXErrorCode.CXError_Success)
            {
                Console.WriteLine($"Parse failed: {err}");
                var numDiagnostics = trans.NumDiagnostics;
                for (uint i = 0; i < numDiagnostics; i++)
                {
                    var diagnositc = trans.GetDiagnostic(i);
                    Console.WriteLine($"Diagnostic {i}: {diagnositc.Spelling}");
                    diagnositc.Dispose();
                }
                return;
            }
            else
            {
                
                Console.WriteLine("Parse success");
                return;
            }
                List<ReflectedClass> classes = new List<ReflectedClass>();

            CXCursor cursor = trans.Cursor;
            ReflectionParser pg = new ReflectionParser();

            cursor.VisitChildren((cur, parent, clientData) =>
            {

                CXSourceLocation cxSourceLocation = clang.getCursorLocation(cur);
                if (cxSourceLocation.IsFromMainFile == false)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }
                if (cur.kind == CXCursorKind.CXCursor_ClassDecl)
                {
                    ReflectedClass reflectedClass = pg.GetReflectedClass(cur, attributeMap);
                    if (reflectedClass != null)
                    {
                        classes.Add(reflectedClass);
                    }
                    var handle = GCHandle.Alloc(reflectedClass);
                    try
                    {
                        trans.GetInclusions(new CXInclusionVisitor(InclusionVisitor), new CXClientData(GCHandle.ToIntPtr(handle)));

                    }
                    finally
                    {
                        handle.Free();
                    }
                }
                return CXChildVisitResult.CXChildVisit_Recurse;

            }, new CXClientData());
            
            foreach (ReflectedClass reflectedClass in classes)
            {
                //CodeGenerator.Generate(reflectedClass);
            }
        }

        protected List<string> _namespace = new List<string>();
        protected String _namespaceStr = "";
        protected static string projectDir = "";
        public string ClassName { get; set; } = "";

        public static string ProjectDir 
            {
                get { return  projectDir; }
            }

        public unsafe ReflectedClass Parse(string filePath)
        {
            var index = CXIndex.Create();
            var trans = new CXTranslationUnit();

            // Reflection ディレクトリ内のファイルを探す
            string reflectionDir = Path.Combine(projectDir, "Reflection");
            string sourcePath = Path.Combine(reflectionDir, filePath);

            // 絶対パスでファイルの存在を確認する
            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"ファイルが見つかりません: {sourcePath}");
                //return;
            }

            // ソースから属性を抽出
            Dictionary<string, List<string>> attributeMap = ExtractAttributesFromSource(sourcePath);

            // include は -I とパスを結合した一つの引数にする（"-I", "path" の配列分割は安全ではないことがある）
            var includePath = Path.GetFullPath(Path.Combine(reflectionDir, "Reflection"));
            var args = new[] { "-std=c++20", $"-I{includePath}", "-x", "c++-header", "-w" };

            // エラーコードを受け取り、失敗なら表示する（TryParse を使う）

            var err = CXTranslationUnit.TryParse(index, sourcePath, args, Array.Empty<CXUnsavedFile>(),
                CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord, out trans);
            if (err != CXErrorCode.CXError_Success)
            {
                Console.WriteLine($"Parse failed: {err}");
                var numDiagnostics = trans.NumDiagnostics;
                for (uint i = 0; i < numDiagnostics; i++)
                {
                    var diagnositc = trans.GetDiagnostic(i);
                    Console.WriteLine($"Diagnostic {i}: {diagnositc.Spelling}");
                    diagnositc.Dispose();
                }
                //return;
            }
            else
            {
                Console.WriteLine("Parse success");
                //return;
            }
            List<ReflectedClass> classes = new List<ReflectedClass>();
            ReflectedClass result = new ReflectedClass();
            CXCursor cursor = trans.Cursor;
            ReflectionParser pg = new ReflectionParser();

            cursor.VisitChildren((cur, parent, clientData) =>
            {
                CXSourceLocation cxSourceLocation = clang.getCursorLocation(cur);
                if (cxSourceLocation.IsFromMainFile == false)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }
                if (cur.kind == CXCursorKind.CXCursor_ClassDecl)
                {
                    ReflectedClass reflectedClass = pg.GetReflectedClass(cur, attributeMap);
                    if (reflectedClass != null)
                    {
                        result = reflectedClass;
                    }
                    var handle = GCHandle.Alloc(reflectedClass);
                    try
                    {
                        trans.GetInclusions(new CXInclusionVisitor(InclusionVisitor), new CXClientData(GCHandle.ToIntPtr(handle)));

                    }
                    finally
                    {
                        handle.Free();
                    }
                }
                return CXChildVisitResult.CXChildVisit_Recurse;

            }, new CXClientData());
            return result;
        }
        private unsafe ReflectedClass GetReflectedClass(CXCursor classCursor,Dictionary<string,List<string>> attributeMap)
        {
            // クラスの情報取得
            ReflectedClass reflectedClass = new ReflectedClass()
            {
                ClassName = clang.getCursorSpelling(classCursor).ToString(),
                NameSpace = getNameSpace(classCursor)
            };
            if(attributeMap.ContainsKey(reflectedClass.ClassName))
            {
                reflectedClass.Attributes = attributeMap[reflectedClass.ClassName];
            }

            // メンバの情報取得
            List<ReflectedMember> fields = new List<ReflectedMember>();
            classCursor.VisitChildren((child, parent, clientData) =>
            {
                if (child.Kind == CXCursorKind.CXCursor_FieldDecl)
                {
                    string name = clang.getCursorSpelling(child).ToString();
                    string type = clang.getTypeSpelling(clang.getCursorType(child)).ToString();
                    CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(child);

                    List<string> attrs = new List<string>();
                    if(attributeMap.ContainsKey(name))
                    {
                        attrs = attributeMap[name];
                    }
                    fields.Add(new ReflectedMember
                    {
                        Name = name,
                        TypeName = type,
                        IsPrivate = (access == CX_CXXAccessSpecifier.CX_CXXPrivate),
                        AccessLevel = getAccessLevel(child),
                        Attributes = attrs
                    });
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData());

            reflectedClass.Members = fields;
            return reflectedClass;
        }
       
        unsafe CXChildVisitResult visitChild(CXCursor cursor, CXCursor parent, void* client_data)
        {
            switch (cursor.kind)
            {
                // 名前空間
                case CXCursorKind.CXCursor_Namespace:
                    readNamespace(cursor);
                    cursor.VisitChildren(new CXCursorVisitor(visitChild), new CXClientData());
                    removeNamespace();
                    break;
                // クラス
                case CXCursorKind.CXCursor_ClassDecl:
                    readClass(cursor);
                    break;
                // 列挙体
                case CXCursorKind.CXCursor_EnumDecl:
                    readEnum(cursor);
                    break;
                // 変数
                case CXCursorKind.CXCursor_FieldDecl:
                    readField(cursor);
                    break;
                // クラステンプレート
                case CXCursorKind.CXCursor_ClassTemplate:
                    readTClass(cursor);
                    break;
                default:
                    break;
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }
        unsafe CXChildVisitResult visitEnum(CXCursor cursor, CXCursor parent, void* client_data)
        {
            CXType itype = clang.getEnumDeclIntegerType(cursor);
            // 符号なし整数の場合
            if (itype.kind == CXTypeKind.CXType_UInt)
            {
                Console.WriteLine("{0} {1}", clang.getCursorSpelling(cursor), clang.getEnumConstantDeclUnsignedValue(cursor));
            }
            else
            {
                Console.WriteLine("{0} {1}", clang.getCursorSpelling(cursor), clang.getEnumConstantDeclValue(cursor));
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }

        static unsafe void InclusionVisitor(void* file, CXSourceLocation* stack,uint len, void* clientData)
        {
            CXFile cxFile = new CXFile((IntPtr)file);
            var fileName = clang.getFileName(cxFile).ToString();
            

            GCHandle handle = GCHandle.FromIntPtr((IntPtr)clientData);
            ReflectedClass? reflectedClass = (ReflectedClass?)handle.Target;
            if (reflectedClass == null)
            {
                Console.Error.WriteLine("ReflectedClass is null !!!");
                return;
            }

            if (len == 0)
            {
                string relativeHeader = Path.GetRelativePath(ReflectionParser.projectDir.Trim(), fileName.Trim());
                string directory = Path.GetDirectoryName(relativeHeader) ?? "";
                reflectedClass.Directory = directory;
                
                return;
            }
            if (stack->IsFromMainFile)
            {
                string relativeHeader = ParseFileName(fileName);
                reflectedClass.HeaderFile.Add(relativeHeader);
                return;
            }
        }
        static string ParseFileName(string fileName)
        {
            string includePath;
            string relativeHeader;
            string lowerFileName = fileName.Replace('\\', '/').ToLower();

            if (lowerFileName.Contains("/include/"))
            {
                // includeディレクトリ以降を抽出
                int idx = lowerFileName.IndexOf("/include/");
                relativeHeader = fileName.Substring(idx + "/include/".Length);
                includePath = ($"<{relativeHeader}>");
            }
            else if (fileName.StartsWith(ReflectionParser.projectDir, StringComparison.OrdinalIgnoreCase))
            {
                // プロジェクト配下のファイル
                relativeHeader = Path.GetRelativePath(ReflectionParser.projectDir.Trim(), fileName.Trim());
                includePath = ($"\"{relativeHeader}\"");
            }
            else
            {
                // 絶対パスのまま
                relativeHeader = fileName;
                includePath = ($"\"{relativeHeader}\"");
            }
            return includePath;
        }
        void readNamespace(CXCursor cursor)
        {
            String name = clang.getCursorSpelling(cursor).ToString();
            _namespace.Add(name);
            _namespaceStr = String.Join("::", _namespace);
        }
        void removeNamespace()
        {
            _namespace.RemoveAt(_namespace.Count - 1);
            _namespaceStr = String.Join("::", _namespace);
        }
        unsafe void readClass(CXCursor cursor)
        {
            Console.WriteLine("class ({1}){0}", clang.getCursorSpelling(cursor), _namespaceStr);
            // さらに子を検索する
            cursor.VisitChildren(new CXCursorVisitor(visitChild), new CXClientData());
        }
        unsafe void readTClass(CXCursor cursor)
        {
            Console.WriteLine("テンプレートクラス定義発見 {0}", clang.getCursorSpelling(cursor));
            // さらに子を検索する
            cursor.VisitChildren(new CXCursorVisitor(visitChild), new CXClientData());
        }
        unsafe void readEnum(CXCursor cursor)
        {
            Console.WriteLine("enum定義発見 {0}", clang.getCursorSpelling(cursor));
            // さらに子を検索する
            cursor.VisitChildren(new CXCursorVisitor(visitEnum), new CXClientData());
        }
        void readField(CXCursor cursor)
        {
            CXType type = clang.getCursorType(cursor);
            String typeName;
            long arrCount = clang.getArraySize(type);
            if (arrCount > 0)
            {
                CXType arrType = clang.getArrayElementType(type);
                typeName = String.Format("{0}=>({1})", arrType, arrCount);
            }
            else
            {
                typeName = type.ToString();
            }
            Console.WriteLine("{0} {1}", typeName, clang.getCursorSpelling(cursor));
        }
        string getNameSpace(CXCursor cursor)
        {
            CXCursor parent = cursor.SemanticParent;
            if (parent.kind == CXCursorKind.CXCursor_Namespace)
            {
                return parent.Spelling.ToString();
            }
            return "";
        }
        string getAccessLevel(CXCursor cursor)
        {
            CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(cursor);
            if (access == CX_CXXAccessSpecifier.CX_CXXPrivate)
            {
                return "private";
            }
            else if (access == CX_CXXAccessSpecifier.CX_CXXPublic)
            {
                return "public";
            }
            else if (access == CX_CXXAccessSpecifier.CX_CXXProtected)
            {
                return "protected";
            }
            return "";
        }
       
        static Dictionary<string, List<string>> ExtractAttributesFromSource(string sourceFilePath)
        {
            string sourceCode = File.ReadAllText(sourceFilePath);
            Dictionary<string, List<string>> attributeMap = new Dictionary<string, List<string>>();

            // 変数・関数用
            string memberPattern = $@"(MT_PROPERTY|MT_FUNCTION)\s*\(\s*\)\s*" +
                @"(?:(?:const|volatile|static|mutable|virtual)\s+)*" +
                @"(?:[\w:<>,\s&*]+?)\s+" +
                @"(\w+)\s*[;=]";

            // クラス・構造体用
            string classPattern = $@"(MT_COMPONENT)\s*\(\s*\)\s*class\s+(\w+)";


            MatchCollection memberMatch = Regex.Matches(sourceCode, memberPattern, RegexOptions.Multiline);
            foreach (Match match in memberMatch)
            {
                if (match.Groups.Count > 0)
                {
                    string macroName = match.Groups[1].Value;
                    string fieldName = match.Groups[2].Value;

                    if (attributeMap.ContainsKey(fieldName) == false)
                    {
                        attributeMap[fieldName] = new List<string>();
                    }
                    attributeMap[fieldName].Add(macroName);
                }
            }
            MatchCollection classMatch = Regex.Matches(sourceCode, classPattern, RegexOptions.Multiline);
            foreach (Match match in classMatch)
            {
                if(match.Groups.Count > 0)
                {
                    string macroName = match.Groups[1].Value;
                    string className = match.Groups[2].Value;
                    if(attributeMap.ContainsKey(className) == false)
                    {
                        attributeMap[className] = new List<string>();
                    }
                    attributeMap[className].Add(macroName);
                }
            }
            return attributeMap;
        }
    }
}