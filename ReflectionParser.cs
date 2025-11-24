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

namespace ClangTest
{
    class ReflectedMember
    {
        public string Name { get; set; } = "";
        public string TypeName { get; set; } = "";
        public bool IsPrivate { get; set; }
        public string AccessLevel { get; set; } = "";
    }

    class ReflectedClass
    {
        public string ClassName { get; set; } = "";
        public string NameSpace { get; set; } = "";
        public List<ReflectedMember> Members { get; set; } = new();
        public string HeaderFile { get; set; } = "";
    }


    class ReflectionParser
    {
        static unsafe void Main()
        {
            NativeLibrary.Load(@"C:\Program Files\LLVM\bin\libclang.dll");
            var index = CXIndex.Create();
            var ufile = new CXUnsavedFile();
            var trans = new CXTranslationUnit();
            var sourceFile = "Sample.cpp";

            // 絶対パスでファイルの存在を確認する
            var sourcePath = Path.GetFullPath(sourceFile);
            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"ファイルが見つかりません: {sourcePath}");
                return;
            }

            // include は -I とパスを結合した一つの引数にする（"-I", "path" の配列分割は安全ではないことがある）
            var includePath = Path.GetFullPath(@"path\to\include");
            var args = new[] { "-std=c++17", $"-I{includePath}" };

            // エラーコードを受け取り、失敗なら表示する（TryParse を使う）
            
            var err = CXTranslationUnit.TryParse(index, sourcePath, args, Array.Empty<CXUnsavedFile>(), CXTranslationUnit_Flags.CXTranslationUnit_None, out trans);
            if (err != CXErrorCode.CXError_Success)
            {
                Console.WriteLine($"Parse failed: {err}");
                return;
            }

            var cursor = trans.Cursor;
            // 本当はここでエラーチェックが好ましい
            var pg = new ReflectionParser();

            List<ReflectedClass> classes = new List<ReflectedClass>();
            cursor.VisitChildren((cur, parent, clientData) =>
            {
                if(cur.kind == CXCursorKind.CXCursor_ClassDecl)
                {
                    ReflectedClass reflectedClass = pg.GetReflectedClass(cur);
                    if(reflectedClass != null)
                    {
                        classes.Add(reflectedClass);
                    }
                }
                return CXChildVisitResult.CXChildVisit_Recurse;

            }, new CXClientData());

            // 結果をコンソール出力
            foreach (var rc in classes)
            {
                Console.WriteLine($"Class: {rc.NameSpace}::{rc.ClassName} (Header: {rc.HeaderFile})");
                foreach (var m in rc.Members)
                {
                    Console.WriteLine($"  {m.AccessLevel} {m.TypeName} {m.Name}");
                }
            }
        }
        protected List<string> _namespace = new List<string>();
        protected String _namespaceStr = "";

        unsafe ReflectedClass GetReflectedClass(CXCursor classCursor)
        {
            ReflectedClass reflectedClass = new ReflectedClass()
            {
                ClassName = clang.getCursorSpelling(classCursor).ToString(),
                NameSpace = getNameSpace(classCursor)
            };

            List<ReflectedMember> fields = new List<ReflectedMember>();
            classCursor.VisitChildren((child, parent, clientData) =>
            {
                if (child.Kind == CXCursorKind.CXCursor_FieldDecl)
                {
                    string name = clang.getCursorSpelling(child).ToString();
                    string type = clang.getTypeSpelling(clang.getCursorType(child)).ToString();
                    CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(child);
                    fields.Add(new ReflectedMember
                    {
                        Name = name,
                        TypeName = type,
                        IsPrivate = (access == CX_CXXAccessSpecifier.CX_CXXPrivate),
                        AccessLevel = getAccessLevel(child)
                    });
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData());

            reflectedClass.Members = fields;
            reflectedClass.HeaderFile = classCursor.IncludedFile.ToString();
            return reflectedClass;
        }
        ReflectedClass? Parse(string file)
        {
            string path  = Path.GetFullPath(file);
            if (!File.Exists(path))
            {
                return null;
            }

            ReflectedClass result = new ReflectedClass()
            {
                HeaderFile = path,
                NameSpace = _namespaceStr
            };

            return result;
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
            if(parent.kind == CXCursorKind.CXCursor_Namespace)
            {
                return parent.Spelling.ToString();
            }
            return "";
        }
        string getAccessLevel(CXCursor cursor)
        {
            CX_CXXAccessSpecifier access = clang.getCXXAccessSpecifier(cursor);
            if(access == CX_CXXAccessSpecifier.CX_CXXPrivate)
            {
                return "private";
            }
            else if(access == CX_CXXAccessSpecifier.CX_CXXPublic)
            {
                return "public";
            }
            else if(access == CX_CXXAccessSpecifier.CX_CXXProtected)
            {
                return "protected";
            }
            return "";
        }
    }
}