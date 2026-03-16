using ClangSharp.Interop;
using System.Runtime.InteropServices;

namespace ClangSourceGenerator
{
    class ReflectionParser : IDisposable
    {
        public ReflectionParser(string projDir, int maxParallelism = -1) 
        {
            // 実行ファイルと同じディレクトリからdllを読み込む
            string? libclangPath = Path.Combine(AppContext.BaseDirectory, "libclang.dll");
            if (libclangPath == null)
            {
                throw new Exception("[Parser] Error: libclang.dll not found!");
            }
            _libclangHandle = NativeLibrary.Load(libclangPath);
            _projectDir = projDir.Trim();

            int parallelism = maxParallelism > 0 ? maxParallelism : Environment.ProcessorCount;
            // initialCount: 初期の解析数 , maxCount: 最大解析数
            _parseThrottle = new SemaphoreSlim(initialCount: parallelism, maxCount: parallelism);
            // excludeDeclarationsFromPch: PCHに含まれる宣言を解析対象から除外するかどうか
            _index = CXIndex.Create(excludeDeclarationsFromPch: true);
        }
        ~ReflectionParser()
        {
            // GCはアンマネージド・リソースを解放してくれないので、Disposeを呼び出す
            Dispose(false);
        }
        private bool _disposed;
        private IntPtr _libclangHandle = IntPtr.Zero;
        protected List<string> _namespace = new List<string>();
        protected String _namespaceStr = "";
        protected static string _projectDir = "";
        private static readonly object _parseLock = new();
        private CXIndex _index;
        // 並列解析数を制限するクラス
        private SemaphoreSlim _parseThrottle;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if(disposing)
            {
                // マネージド(managed)・リソース解放処理をここで行う
                _parseThrottle?.Dispose();
                _index.Dispose();
            }
            
            // アンマネージド(unmanaged)・リソースの解放処理
            if(_libclangHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_libclangHandle);
            }
            _libclangHandle = IntPtr.Zero;

            _disposed = true;
        }
        public ReflectedClass? Parse(string filePath)
        {
            // 絶対パスでファイルの存在を確認する
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"[Parser] Error: {filePath} not found");
                return null;
            }

            // 実行数を制限する
            _parseThrottle.Wait();
            try
            {
                return ParseImpl(filePath);
            }
            finally
            {
                // 解析が終わったら解放して、使用可能にする
                _parseThrottle.Release(); 
            }
            
        }
        /// <summary>
        /// ファイルを解析し、結果を第二引数,reflectedClassに書き込む
        /// C++20のヘッダファイルを解析する
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="reflectedClass"></param>
        /// <returns>解析に成功した場合はtrue、失敗した場合、ファイルが見つからなかった場合はfalseを返す</returns>
        private ReflectedClass? ParseImpl(string filePath)
        {            
            
            CXTranslationUnit trans = new CXTranslationUnit();
            try
            {
                // ディレクトリを取得
                string directory = Path.GetDirectoryName(filePath) ?? "";

                // C++20のヘッダファイルを読みこむ
                var args = new[] { "-std=c++20", $"-I{directory}", "-x", "c++-header", "-fsyntax-only" };
                
                // エラーコードを受け取り、失敗なら表示する
                var err = CXTranslationUnit.TryParse(_index, filePath, args, Array.Empty<CXUnsavedFile>(),
                    CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies, out trans);

                if (err != CXErrorCode.CXError_Success)
                {
                    // 解析失敗
                    Console.Error.WriteLine($"[Parser] Error: Parse failed: {err}");
                    return null;
                }
                // 解析結果を代入
                return GetReflectedClass(trans);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Parser] Error: Parse failed: {ex.Message}");
            }
            finally
            {
                trans.Dispose();
            }
            return null;
        }
        private unsafe  ReflectedClass GetReflectedClass(CXTranslationUnit trans)
        {
            CXCursor cursor = trans.Cursor;
            string classAnnotation = string.Empty;
            List<string> classAttributes = new();
            string metaType = "";
            string className = "";
            string nameSpace = "";
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
                // クラス定義の場合のみ情報を生成する
                if (child.kind == CXCursorKind.CXCursor_ClassDecl && child.IsDefinition)
                {
                    className = clang.getCursorSpelling(child).ToString();
                    nameSpace = GetTypeNameSpace(child);
                    classAnnotation = GetAnnotations(child);

                    var (macroName, args) = ParseAnnotation(classAnnotation);
                    classAttributes.AddRange(args);
                    metaType = macroName;

                    directory = GetDirectory(trans);
                }
                else if(child.kind == CXCursorKind.CXCursor_FieldDecl)
                {
                    fields.Add(GetReflectedMember(child));
                }
                return CXChildVisitResult.CXChildVisit_Recurse;

            }, new CXClientData());

            // 解析結果を代入
            ReflectedClass reflectedClass = new ReflectedClass
            {
                ClassName = className,
                NameSpace = nameSpace,
                MetadataType = metaType,
                MetaOptions = classAttributes,
                Members = fields,
                Directory = directory,
            };
            return reflectedClass;
        }
        /// <summary>
        /// メンバ変数の型情報を取得
        /// </summary>
        /// <param name="cursor">メンバ変数のカーソル</param>
        /// <returns></returns>
        static ReflectedMember GetReflectedMember(CXCursor cursor)
        {
            // 変数名
            using CXString nameSpelling = clang.getCursorSpelling(cursor);
            string name = nameSpelling.ToString();
          
            // 型名(名前空間を含まない、型の名前のみ)
            string type = string.Empty;
            CXType cxType = clang.getCursorType(cursor);
            // 宣言部分のカーソルを取得
            CXCursor typeDeclaration = clang.getTypeDeclaration(cxType);
            // 組み込み型の場合、NoDeclFoundが返される
            if (typeDeclaration.kind == CXCursorKind.CXCursor_NoDeclFound)
            {
                using CXString typeSpelling = clang.getTypeSpelling(clang.getCursorType(cursor));
                type = typeSpelling.ToString();
            }
            else
            {
                using CXString typeSpelling = clang.getCursorSpelling(typeDeclaration);
                type  = typeSpelling.CString;
            }
            // アクセス修飾子
            CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(cursor);

            // アノテーションを取得
            string fieldAnnotation = GetAnnotations(cursor);

            // アノテーションに付与されたメタデータを取得
            var (macroName, args) = ParseAnnotation(fieldAnnotation);            

            return new ReflectedMember
            {
                Name = name,
                TypeName = type,
                IsPrivate = (access == CX_CXXAccessSpecifier.CX_CXXPrivate),
                AccessLevel = GetAccessLevel(cursor),
                MetadataType = macroName,
                MetaOptions = args,
                NameSpace = GetMemberNameSpace(cursor)
            };
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
            using CXString fileSpelling = clang.getFileName(cxFile);
            var fileName = fileSpelling.ToString();

            // ポインタをハンドルにキャスト
            GCHandle handle = GCHandle.FromIntPtr((IntPtr)clientData);
            // ハンドルからstringにキャスト
            DirectoryHolder? holder = (DirectoryHolder?)handle.Target;
            // キャスト失敗
            if (holder == null)
            {
                Console.Error.WriteLine("[Parser] Error: handle.target is null");
                return;
            }

            // スタックがまだ積まれていない場合、解析されているファイル(翻訳単位)自身
            if (len == 0)
            {
                // インクルードディレクティブで使用するパスを取得
                // 解析ファイルと同じディレクトリに配置するために、ファイル名のみ抽出
                // プロジェクトのディレクトリからの相対パスを取得
                string relativeHeader = Path.GetRelativePath(ReflectionParser._projectDir.Trim(), fileName.Trim());
                // ヘッダファイル名を除いたディレクトリを取得
                holder.Value = Path.GetDirectoryName(relativeHeader) ?? "";
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
            else if (fileName.StartsWith(ReflectionParser._projectDir, StringComparison.OrdinalIgnoreCase))
            {
                // プロジェクト配下のファイル
                relativeHeader = Path.GetRelativePath(ReflectionParser._projectDir.Trim(), fileName.Trim());
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

        /// <summary>
        /// 型のカーソルから、名前空間を取得
        /// 名前空間がない場合は空文字
        /// </summary>
        /// <param name="cursor">型(ClassDecl)のカーソル</param>
        /// <returns></returns>
        static string GetTypeNameSpace(CXCursor cursor)
        {
            CXCursor parent = cursor.SemanticParent;
            if (parent.kind == CXCursorKind.CXCursor_Namespace)
            {
                return parent.Spelling.ToString();
            }
            return "";
        }

        /// <summary>
        /// メンバ変数のカーソルから、名前空間を取得
        /// 名前空間がない場合は空文字
        /// </summary>
        /// <param name="cursor">メンバ変数(FieldDecl)のカーソル</param>
        /// <returns></returns>
        static string GetMemberNameSpace(CXCursor cursor)
        {
            // メンバ変数のカーソルから型(CXType)を取得
            CXType type = clang.getCursorType(cursor);
            // 型の宣言のカーソルを取得
            CXCursor typeDeclaration =  type.Declaration;

            return GetTypeNameSpace(typeDeclaration);
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

        class DirectoryHolder
        {
            public string Value { get; set; } = string.Empty;
        }
        
        static unsafe string GetDirectory(CXTranslationUnit trans)
        {
            var holder = new DirectoryHolder();
            // GC対象から外す
            var handle = GCHandle.Alloc(holder);
            try
            {
                // visitorにreflectedClassのポインタを渡す
                trans.GetInclusions(new CXInclusionVisitor(InclusionVisitor), new CXClientData(GCHandle.ToIntPtr(handle)));
            }
            finally
            {
                handle.Free();
            }
            return holder.Value;
        }

        /// <summary>
        /// カーソルに付与されたannotate属性を取得する
        /// </summary>
        /// <param name="cursor"></param>
        /// <returns></returns>
        static unsafe string GetAnnotations(CXCursor cursor)
        {
            string annotation = string.Empty;
            
            cursor.VisitChildren((child, parent, clientData) =>
            {
                if(child.kind == CXCursorKind.CXCursor_AnnotateAttr)
                {
                    using CXString annotateSpelling = clang.getCursorSpelling(child);
                    annotation = annotateSpelling.ToString();
                    return CXChildVisitResult.CXChildVisit_Break;
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData());
            return annotation;
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