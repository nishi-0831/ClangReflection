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
            // 実行ファイルと同じディレクトリからdllを読み込む
            string? libclangPath = Path.Combine(AppContext.BaseDirectory, "libclang.dll");
            if (libclangPath == null)
            {
                throw new Exception("libclang.dll not found!!!!!!");
            }
            NativeLibrary.Load(libclangPath);
            // プロジェクトのディレクトリを読み取る
            if (File.Exists("ProjectDirPath.txt"))
            {
                using (StreamReader sr = new StreamReader("ProjectDirPath.txt"))
                {
                    // 改行や空白を除去
                    projectDir = sr.ReadToEnd().Trim();
                }
            }

        }

        protected List<string> _namespace = new List<string>();
        protected String _namespaceStr = "";
        protected static string projectDir = "";

        
        /// <summary>
        /// ファイルを解析し、結果を第二引数,reflectedClassに書き込む
        /// C++20のヘッダファイルを解析する
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="reflectedClass"></param>
        /// <returns>解析に成功した場合はtrue、失敗した場合、ファイルが見つからなかった場合はfalseを返す</returns>
        public unsafe bool TryParse(string filePath,out ReflectedClassInfo? reflectedClass)
        {
            reflectedClass = null;

            // 絶対パスでファイルの存在を確認する
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"ファイルが見つかりません: {filePath}");
                return false;
            }

            // ソースから属性を抽出
            Dictionary<string, List<string>> attributeMap = ExtractAttributesFromSource(filePath);
            
            // ディレクトリを取得
            string directory = Path.GetDirectoryName(filePath) ?? "";

            // C++20のヘッダファイルを読みこむ
            var args = new[] { "-std=c++20", $"-I{directory}", "-x", "c++-header", "-fsyntax-only" };

            var index = CXIndex.Create();
            var trans = new CXTranslationUnit();
            // エラーコードを受け取り、失敗なら表示する（TryParse を使う）
            var err = CXTranslationUnit.TryParse(index, filePath, args, Array.Empty<CXUnsavedFile>(),
                CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord, out trans);

            if (err != CXErrorCode.CXError_Success)
            {
                // 解析失敗
                Console.WriteLine($"TryParse failed: {err}");
                return false;
            }

            CXCursor cursor = trans.Cursor;

            // outパラメータはラムダ式内で使用できないのでローカル変数を用意
            ReflectedClassInfo? localReflectedClass = null;
            cursor.VisitChildren((cur, parent, clientData) =>
            {
                CXSourceLocation cxSourceLocation = clang.getCursorLocation(cur);
                if (cxSourceLocation.IsFromMainFile == false)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }
                if (cur.kind == CXCursorKind.CXCursor_ClassDecl)
                {
                    localReflectedClass = GetReflectedClass(cur, attributeMap);
                    
                    // GC対象から外す
                    var handle = GCHandle.Alloc(localReflectedClass);
                    try
                    {
                        // visitorにreflectedClassのポインタを渡す
                        trans.GetInclusions(new CXInclusionVisitor(InclusionVisitor), new CXClientData(GCHandle.ToIntPtr(handle)));

                    }
                    finally
                    {
                        handle.Free();
                    }
                }
                return CXChildVisitResult.CXChildVisit_Recurse;

            }, new CXClientData());

            // 解析結果を代入
            reflectedClass = localReflectedClass;
            return reflectedClass != null;
        }
        private unsafe ReflectedClassInfo GetReflectedClass(CXCursor classCursor,Dictionary<string,List<string>> attributeMap)
        {
            // クラスの情報取得
            ReflectedClassInfo reflectedClass = new ReflectedClassInfo()
            {
                ClassName = clang.getCursorSpelling(classCursor).ToString(),
                NameSpace = GetNameSpace(classCursor)
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
                        AccessLevel = GetAccessLevel(child),
                        Attributes = attrs
                    });
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData());

            reflectedClass.Members = fields;
            return reflectedClass;
        }
       
        unsafe CXChildVisitResult VisitChild(CXCursor cursor, CXCursor parent, void* client_data)
        {
            switch (cursor.kind)
            {
                // 名前空間
                case CXCursorKind.CXCursor_Namespace:
                    ReadNamespace(cursor);
                    cursor.VisitChildren(new CXCursorVisitor(VisitChild), new CXClientData());
                    RemoveNamespace();
                    break;
                // クラス
                case CXCursorKind.CXCursor_ClassDecl:
                    ReadClass(cursor);
                    break;
                // 列挙体
                case CXCursorKind.CXCursor_EnumDecl:
                    ReadEnum(cursor);
                    break;
                // 変数
                case CXCursorKind.CXCursor_FieldDecl:
                    ReadField(cursor);
                    break;
                // クラステンプレート
                case CXCursorKind.CXCursor_ClassTemplate:
                    ReadTClass(cursor);
                    break;
                default:
                    break;
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }
        unsafe CXChildVisitResult VisitEnum(CXCursor cursor, CXCursor parent, void* client_data)
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

        /// <summary>
        /// インクルードしているヘッダを解析する
        /// </summary>
        /// <param name="file">解析対象のファイル</param>
        /// <param name="stack">インクルードされているファイルのスタック</param>
        /// <param name="len">スタックの要素数</param>
        /// <param name="clientData">このメソッドに渡されたポインタ</param>
        static unsafe void InclusionVisitor(void* file, CXSourceLocation* stack,uint len, void* clientData)
        {
            // ファイルをCXFileにキャスト
            CXFile cxFile = new CXFile((IntPtr)file);
            // ファイル名を取得
            var fileName = clang.getFileName(cxFile).ToString();

            // ポインタをハンドルにキャスト
            GCHandle handle = GCHandle.FromIntPtr((IntPtr)clientData);
            // ハンドルからReflectedClassInfoにキャスト
            ReflectedClassInfo? reflectedClass = (ReflectedClassInfo?)handle.Target;
            // キャスト失敗
            if (reflectedClass == null)
            {
                Console.Error.WriteLine("ReflectedClassInfo is null !!!");
                return;
            }

            // スタックがまだ積まれていない場合、解析されているファイル(翻訳単位)自身
            if (len == 0)
            {
                // インクルードディレクティブで使用するパスを取得
                // 解析ファイルと同じディレクトリに配置するために、ファイル名のみ抽出
                reflectedClass.HeaderFile = Path.GetFileName(fileName);
                // プロジェクトのディレクトリからの相対パスを取得
                string relativeHeader = Path.GetRelativePath(ReflectionParser.projectDir.Trim(), fileName.Trim());
                // ヘッダファイル名を除いたディレクトリを取得
                string directory = Path.GetDirectoryName(relativeHeader) ?? "";
                // ディレクトリを記録
                reflectedClass.Directory = directory;                
                return;
            }
            // 翻訳単位が、推移的にではなく直接インクルードしているファイル
            if (stack->IsFromMainFile)
            {
                return;
            }
        }

        /// <summary>
        /// ファイル名を解析し、ファイルのパスに基づいて、C++のインクルードディレクティブで使用するパスを返す
        /// </summary>
        /// <param name="fileName">解析するファイルの絶対パス</param>
        /// <returns>
        /// <para> 判定基準に基づくインクルードパス</para>
        /// <para> パスに"include"が含まれる場合、ライブラリと判定しては山かっこ(&lt;&gt;)で囲む(例: &lt;stdio.h&gt;) </para>
        /// <para> プロジェクトディレクトリ配下のファイルの場合、ダブルクォーテーション( "" )で囲む(例: "header.h") </para>
        /// <para> それ以外の場合、ダブルクォーテーション("")で囲む </para>
        /// </returns>
        static string GetIncludePath(string fileName)
        {
            string includePath;
            string relativeHeader;
            // 区切り文字は'/'(スラッシュ)に統一する
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
        void ReadNamespace(CXCursor cursor)
        {
            String name = clang.getCursorSpelling(cursor).ToString();
            _namespace.Add(name);
            _namespaceStr = String.Join("::", _namespace);
        }
        void RemoveNamespace()
        {
            _namespace.RemoveAt(_namespace.Count - 1);
            _namespaceStr = String.Join("::", _namespace);
        }
        unsafe void ReadClass(CXCursor cursor)
        {
            Console.WriteLine("class ({1}){0}", clang.getCursorSpelling(cursor), _namespaceStr);
            // さらに子を検索する
            cursor.VisitChildren(new CXCursorVisitor(VisitChild), new CXClientData());
        }
        unsafe void ReadTClass(CXCursor cursor)
        {
            Console.WriteLine("テンプレートクラス定義発見 {0}", clang.getCursorSpelling(cursor));
            // さらに子を検索する
            cursor.VisitChildren(new CXCursorVisitor(VisitChild), new CXClientData());
        }
        unsafe void ReadEnum(CXCursor cursor)
        {
            Console.WriteLine("enum定義発見 {0}", clang.getCursorSpelling(cursor));
            // さらに子を検索する
            cursor.VisitChildren(new CXCursorVisitor(VisitEnum), new CXClientData());
        }
        void ReadField(CXCursor cursor)
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
        string GetNameSpace(CXCursor cursor)
        {
            CXCursor parent = cursor.SemanticParent;
            if (parent.kind == CXCursorKind.CXCursor_Namespace)
            {
                return parent.Spelling.ToString();
            }
            return "";
        }
        /// <summary>
        /// アクセス修飾子を返す
        /// </summary>
        /// <param name="cursor"></param>
        /// <returns>private,public,protectedのいずれかの文字列を返す。どれにも該当しない場合、空文字("")を返す</returns>
        string GetAccessLevel(CXCursor cursor)
        {
            // アクセス修飾子を取得
            CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(cursor);

            // private
            if (access == CX_CXXAccessSpecifier.CX_CXXPrivate)
            {
                return "private";
            }
            // public
            else if (access == CX_CXXAccessSpecifier.CX_CXXPublic)
            {
                return "public";
            }
            // protected
            else if (access == CX_CXXAccessSpecifier.CX_CXXProtected)
            {
                return "protected";
            }
            // いずれにも該当しなかった
            return "";
        }
       
        /// <summary>
        /// ファイルから、識別子(変数、関数、クラス)に付与された属性を抽出する
        /// </summary>
        /// <param name="sourceFilePath">解析するファイルのパス</param>
        /// <returns>
        /// <para> キー : 識別子 , 値 : 属性 の辞書を返す </para>
        /// <para> 例( キー : hoge , 値 : Property )</para>
        /// </returns>
        static Dictionary<string, List<string>> ExtractAttributesFromSource(string sourceFilePath)
        {
            string sourceCode = File.ReadAllText(sourceFilePath);
            Dictionary<string, List<string>> attributeMap = new Dictionary<string, List<string>>();

            // 正規表現を用いて解析する

            // TODO:属性をハードコーディングするのでなく、外部ファイルから読み取るなど別の方法を採るべき

            // メンバ用(変数、関数)
            string memberPattern = $@"(MT_PROPERTY|MT_FUNCTION)\s*\(\s*\)\s*" +
                @"(?:(?:\s*//[^\r\n]*|\s*/\*[\s\S]*?\*/|\s+))*" +   // コメント/空白を0回以上許可
                @"(?:(?:const|volatile|static|mutable|virtual)\s+)*" +
                @"(?:[\w:<>,\s&*\[\]]+?)\s+" +                     // 型(配列や<>含む)
                @"(\w+)\s*[;=]";

            // クラス・構造体用
            string classPattern = $@"(MT_COMPONENT)\s*\(\s*\)" +
                @"(?:\s*//.*?|"  + // C++の単一行コメント
                @"\s*/\*.*?\*/|" + // C++のブロックコメント
                @"\s+)*" +         // 空白文字
                @"class\s+(\w+)";


            // メンバを解析
            MatchCollection memberMatch = Regex.Matches(sourceCode, memberPattern, RegexOptions.Multiline | RegexOptions.Singleline);
            foreach (Match match in memberMatch)
            {
                if (match.Groups.Count > 0)
                {
                    // マクロ名(属性名)、変数名を取得
                    string macroName = match.Groups[1].Value;
                    string fieldName = match.Groups[2].Value;

                    if (attributeMap.ContainsKey(fieldName) == false)
                    {
                        attributeMap[fieldName] = new List<string>();
                    }
                    attributeMap[fieldName].Add(macroName);
                }
            }

            // クラス、構造体を解析
            MatchCollection classMatch = Regex.Matches(sourceCode, classPattern, RegexOptions.Multiline | RegexOptions.Singleline);
            foreach (Match match in classMatch)
            {
                if(match.Groups.Count > 0)
                {
                    // マクロ名(属性名)、クラス名を取得
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
        /// <summary>
        /// 正規表現パターンをテストするメソッド
        /// </summary>
        public static void TestRegexPattern()
        {
            // クラス用テストケース（既存）
            string[] classTestCases = new[]
            {
                "MT_COMPONENT() class MyClass { };",
                "MT_COMPONENT() // これはクラスです\nclass MyClass { };",
                "MT_COMPONENT() /* クラス定義 */\nclass AnotherClass { };",
                "MT_COMPONENT() /* 複数行\nコメント */\nclass ThirdClass { };",
                "MT_COMPONENT() public class PublicClass { };",
                "MT_COMPONENT() class FirstClass { };\nMT_COMPONENT() class SecondClass { };",
                "MT_COMPONENT() // コメント\npublic class MixedClass { };"
            };

            // メンバ用テストケース（マクロと宣言の間にコメントや空白を含むケースを追加）
            string[] memberTestCases = new[]
            {
                // シンプルな変数
                "MT_PROPERTY() int x;",
                // 単一行コメントを挟む
                "MT_PROPERTY() // コメントを挟む\nint commentedField;",
                // ブロックコメントを挟む（単一行）
                "MT_PROPERTY() /* ブロックコメント */\nint blockField;",
                // ブロックコメントを跨ぐ（複数行）
                "MT_PROPERTY() /* 複数行\nコメント */\nstatic const int multiBlockField = 0;",
                // 関数宣言（戻り値と名前）
                "MT_FUNCTION() void Foo();",
                // 関数宣言でブロックコメントを挟む
                "MT_FUNCTION() /* コメント */\nstd::vector<int> GetVector();",
                // 修飾子（static, const, virtual等）を含むケース
                "MT_PROPERTY() static const std::string name = \"hoge\";",
                // テンプレート、参照、ポインタを含むケース
                "MT_FUNCTION() std::vector<int>&& CreateList();"
            };

            // クラス用パターン（既存と同様にコメントと空白を許可）
            string classPattern = $@"(MT_COMPONENT)\s*\(\s*\)" +
                @"(?:(?:\s*//[^\r\n]*|\s*/\*[\s\S]*?\*/|\s+))*" +
                @"(?:public\s+)?" +
                @"class\s+(\w+)";

            // メンバ用パターン（マクロ → コメント/空白 → 任意の修飾子 → 型 → 識別子）
            string memberPattern = $@"(MT_PROPERTY|MT_FUNCTION)\s*\(\s*\)\s*" +
                @"(?:(?:\s*//[^\r\n]*|\s*/\*[\s\S]*?\*/|\s+))*" +   // コメント／空白を0回以上許可
                @"(?:(?:const|volatile|static|mutable|virtual)\s+)*" +
                @"(?:[\w:<>,\s&*\[\]]+?)\s+" +                     // 型（配列や<>含む）
                @"(\w+)\s*[;=]";

            Console.WriteLine("=== クラス用 正規表現テスト開始 ===\n");
            for (int i = 0; i < classTestCases.Length; i++)
            {
                string testCase = classTestCases[i];
                Console.WriteLine($"テストケース {i + 1}:");
                Console.WriteLine($"入力: {testCase.Replace("\n", "\\n")}");

                MatchCollection matches = Regex.Matches(testCase, classPattern, RegexOptions.Multiline | RegexOptions.Singleline);

                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        string macroName = match.Groups[1].Value;
                        string className = match.Groups[2].Value;
                        Console.WriteLine($"✓ マッチ - マクロ: {macroName}, クラス名: {className}");
                    }
                }
                else
                {
                    Console.WriteLine("✗ マッチなし");
                }
                Console.WriteLine();
            }

            Console.WriteLine("=== メンバ用 正規表現テスト開始 ===\n");
            for (int i = 0; i < memberTestCases.Length; i++)
            {
                string testCase = memberTestCases[i];
                Console.WriteLine($"テストケース {i + 1}:");
                Console.WriteLine($"入力: {testCase.Replace("\n", "\\n")}");

                MatchCollection matches = Regex.Matches(testCase, memberPattern, RegexOptions.Multiline | RegexOptions.Singleline);

                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        string macroName = match.Groups[1].Value;
                        string memberName = match.Groups[2].Value;
                        Console.WriteLine($"✓ マッチ - マクロ: {macroName}, 識別子: {memberName}");
                    }
                }
                else
                {
                    Console.WriteLine("✗ マッチなし");
                }
                Console.WriteLine();
            }
        }
    }
}