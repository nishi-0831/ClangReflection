using ClangSharp;
using ClangSharp.Interop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.Core;
namespace ClangTest
{
    class ReflectionParser : IDisposable
    {
        public ReflectionParser(string projDir, int maxParallelism = -1) 
        {
            // 実行ファイルと同じディレクトリからdllを読み込む
            string? libclangPath = Path.Combine(AppContext.BaseDirectory, "libclang.dll");
            if (libclangPath == null)
            {
                throw new Exception("libclang.dll not found!!!!!!");
            }
            libclangHandle = NativeLibrary.Load(libclangPath);
            projectDir = projDir.Trim();

            index = CXIndex.Create();

            int parallelism = maxParallelism > 0 ? maxParallelism : Environment.ProcessorCount;
            // initialCount: 初期の解析数 , maxCount: 最大解析数
            parseThrottle = new SemaphoreSlim(initialCount: parallelism, maxCount: parallelism);
        }
        ~ReflectionParser()
        {
            // GCはアンマネージド・リソースは解放してくれないので、Disposeを呼び出す
            Dispose(false);
        }
        private bool disposed;
        private IntPtr libclangHandle = IntPtr.Zero;
        // 翻訳単位を管理するやつ。解析の開始点となる
        private readonly CXIndex index;
        protected List<string> _namespace = new List<string>();
        protected String _namespaceStr = "";
        protected static string projectDir = "";
        private static readonly object parseLock = new();

        // 並列解析数を制限するクラス
        private SemaphoreSlim parseThrottle;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if(disposing)
            {
                // マネージド(managed)・リソース解放処理をここで行う
                parseThrottle?.Dispose();
            }

            // アンマネージド(unmanaged)・リソースの解放処理
            index.Dispose();
            if(libclangHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(libclangHandle);
            }
            libclangHandle = IntPtr.Zero;

            disposed = true;
        }
        public unsafe bool TryParse(string filePath, out ReflectedClassInfo? reflectedClass)
        {
            reflectedClass = null;

            // 絶対パスでファイルの存在を確認する
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"ファイルが見つかりません: {filePath}");
                return false;
            }

            // 実行数を制限する
            parseThrottle.Wait();
            try
            {
                return TryParseImpl(filePath, out reflectedClass);
            }
            finally
            {
                // 解析が終わったら解放して、使用可能にする
                parseThrottle.Release(); 
            }
            
        }
        /// <summary>
        /// ファイルを解析し、結果を第二引数,reflectedClassに書き込む
        /// C++20のヘッダファイルを解析する
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="reflectedClass"></param>
        /// <returns>解析に成功した場合はtrue、失敗した場合、ファイルが見つからなかった場合はfalseを返す</returns>
        private unsafe bool TryParseImpl(string filePath,out ReflectedClassInfo? reflectedClass)
        {
            reflectedClass = null;
            
            // ディレクトリを取得
            string directory = Path.GetDirectoryName(filePath) ?? "";

            // C++20のヘッダファイルを読みこむ
            var args = new[] { "-std=c++20", $"-I{directory}", "-x", "c++-header", "-fsyntax-only" };
            using CXIndex index = CXIndex.Create();

            CXTranslationUnit trans = new CXTranslationUnit();
            try
            {
                // エラーコードを受け取り、失敗なら表示する（TryParse を使う）
                var err = CXTranslationUnit.TryParse(index, filePath, args, Array.Empty<CXUnsavedFile>(),
                    CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies, out trans);

                if (err != CXErrorCode.CXError_Success)
                {
                    // 解析失敗
                    Console.WriteLine($"TryParse failed: {err}");
                    return false;
                }
                // 解析結果を代入
                reflectedClass = GetReflectedClass(trans);
            }
            finally
            {
                trans.Dispose();
            }

            return reflectedClass != null;
        }
        private unsafe  ReflectedClassInfo GetReflectedClass(CXTranslationUnit trans)
        {
            CXCursor cursor = trans.Cursor;
            List<string> classAnnotations = GetAnnotations(cursor);
            List<string> classAttributes = new();
            string metaType = "";
            foreach (string anno in classAnnotations)
            {
                var (macroName, args) = ParseAnnotation(anno);
                classAttributes.AddRange(args);
                metaType = macroName;
            }

            // outパラメータはラムダ式内で使用できないのでローカル変数を用意
            // メンバの情報取得
            List<ReflectedMember> fields = new List<ReflectedMember>();
            string directory ="";
            cursor.VisitChildren((child, parent, clientData) =>
            {
                CXSourceLocation cxSourceLocation = clang.getCursorLocation(child);
                if (cxSourceLocation.IsFromMainFile == false)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }
                if (child.kind == CXCursorKind.CXCursor_ClassDecl)
                {
                    directory = GetDirectory(trans);
                }
                else if(child.kind == CXCursorKind.CXCursor_FieldDecl)
                {
                    fields.Add(GetReflectedMember(child));
                }
                return CXChildVisitResult.CXChildVisit_Recurse;

            }, new CXClientData());

            // 解析結果を代入
            ReflectedClassInfo reflectedClass = new ReflectedClassInfo
            {
                ClassName = clang.getCursorSpelling(cursor).ToString(),
                NameSpace = GetNameSpace(cursor),
                MetadataType = metaType,
                MetaOptions = classAttributes,
                Members = fields,
                Directory = directory,
            };
            return reflectedClass;
        }
        static ReflectedMember GetReflectedMember(CXCursor cursor)
        {
            // 変数名
            string name = clang.getCursorSpelling(cursor).ToString();
            // 型名
            string type = clang.getTypeSpelling(clang.getCursorType(cursor)).ToString();
            // アクセス修飾子
            CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(cursor);

            List<string> fieldAnnotations = GetAnnotations(cursor);
            List<string> attrs = new();
            string metadataType = "";
            foreach (string anno in fieldAnnotations)
            {
                var (macroName, args) = ParseAnnotation(anno);
                metadataType = macroName;
                attrs.AddRange(args);
            }

            return new ReflectedMember
            {
                Name = name,
                TypeName = type,
                IsPrivate = (access == CX_CXXAccessSpecifier.CX_CXXPrivate),
                AccessLevel = GetAccessLevel(cursor),
                MetadataType = metadataType,
                MetaOptions = attrs
            };
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
            // ハンドルからstringにキャスト
            string? directory = (string?)handle.Target;
            // キャスト失敗
            if (directory == null)
            {
                Console.Error.WriteLine("ReflectedClassInfo is null !!!");
                return;
            }

            // スタックがまだ積まれていない場合、解析されているファイル(翻訳単位)自身
            if (len == 0)
            {
                // インクルードディレクティブで使用するパスを取得
                // 解析ファイルと同じディレクトリに配置するために、ファイル名のみ抽出
                //reflectedClass.HeaderFile = Path.GetFileName(fileName);
                // プロジェクトのディレクトリからの相対パスを取得
                string relativeHeader = Path.GetRelativePath(ReflectionParser.projectDir.Trim(), fileName.Trim());
                // ヘッダファイル名を除いたディレクトリを取得
                directory = Path.GetDirectoryName(relativeHeader) ?? "";
                // ディレクトリを記録
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
        static string GetAccessLevel(CXCursor cursor)
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

        static unsafe string GetDirectory(CXTranslationUnit trans)
        {
            string result = string.Empty;
            // GC対象から外す
            var handle = GCHandle.Alloc(result);
            try
            {
                // visitorにreflectedClassのポインタを渡す
                trans.GetInclusions(new CXInclusionVisitor(InclusionVisitor), new CXClientData(GCHandle.ToIntPtr(handle)));

            }
            finally
            {
                handle.Free();
            }
            return result;
        }

        /// <summary>
        /// カーソルに付与されたannotate属性を取得する
        /// </summary>
        /// <param name="cursor"></param>
        /// <returns></returns>
        static unsafe List<string> GetAnnotations(CXCursor cursor)
        {
            List<string> annotations = new();
            cursor.VisitChildren((child, parent, clientData) =>
            {
                if(child.kind == CXCursorKind.CXCursor_AnnotateAttr)
                {
                    annotations.Add(clang.getCursorSpelling(child).ToString());
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData());
            return annotations;
        }

        /// <summary>
        /// annotate文字列をパースして、マクロ名と引数を分離する
        /// 例 : "MT_PROPERTY,Serializable,Min=0,Max=100"
        ///    - macroname = "MT_PROPERTY"
        ///    - args = ["Serializable","Min=0","Max=100"]
        /// </summary>
        /// <param name="annotation">解析するannotate文字列</param>
        /// <returns></returns>
        static (string macroName, List<string> args) ParseAnnotation(string annotation)
        {
            // 最初のカンマでマクロ名と残りの引数を分割
            int firstComma = annotation.IndexOf(',');
            // カンマがない場合は-1が返される
            if(firstComma < 0)
            {
                // 引数なし、マクロ名のみ
                return (annotation.Trim(), new List<string>());
            }
            // 先頭からfirstCommaまでの範囲を取得
            string macroName = annotation[..firstComma].Trim();
            // firstComma直後から、末尾までの範囲を取得
            string rest = annotation[(firstComma + 1)..];
            // カンマ区切りで、空文字列や空白を除去して取得
            List<string> args = rest.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            return (macroName, args);
        }
    }
}