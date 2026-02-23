using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClangTest
{
    /// <summary>
    /// 解析したメンバ変数の情報を保存するクラス
    /// </summary>
    public class ReflectedMember
    {
        /// <summary>
        /// 変数名
        /// </summary>
        public string Name { get; init; } = "";
        /// <summary>
        /// 型名
        /// </summary>
        public string TypeName { get; init; } = "";
        /// <summary>
        /// アクセス修飾子がprivateか否か
        /// </summary>
        public bool IsPrivate { get; init; }
        /// <summary>
        /// アクセス修飾子の文字列
        /// </summary>
        public string AccessLevel { get; init; } = "";
        /// <summary>
        /// メタデータの種類。UPROPERTY(tag="hoge")などのUPROPERTY部分
        /// </summary>
        public string MetadataType { get; init; } = "";
        /// <summary>
        /// メタデータに設定されたオプション。
        /// UPROPERTY(tag="hoge")などのtag="hoge"部分
        /// </summary>
        public IReadOnlyList<string> MetaOptions { get; init; } = new List<string>();
        /// <summary>
        /// 名前空間の文字列
        /// </summary>
        public string NameSpace { get; init; } = "";
    }
    /// <summary>
    /// 解析したクラスの情報を保存するクラス
    /// </summary>
    public class ReflectedClassInfo
    {
        /// <summary>
        /// 型名
        /// </summary>
        public string ClassName { get; init; } = "";
        /// <summary>
        /// 名前空間
        /// </summary>
        public string NameSpace { get; init; } = "";
        /// <summary>
        /// メンバ変数の情報
        /// </summary>
        public IReadOnlyList<ReflectedMember> Members { get; init; } = new List<ReflectedMember>();
        /// <summary>
        /// 解析したクラスが書かれているファイルのディレクトリ
        /// </summary>
        public string Directory { get; init; } = "";
        /// <summary>
        /// メタデータの種類
        /// UCOMPONENT(tag="hoge")などのUCOMPONENT部分
        /// </summary>
        public string MetadataType { get; init; } = "";
        /// <summary>
        /// メタデータに設定されたオプション
        /// UCOMPONENT(tag="hoge")などのtag="hoge"部分
        /// </summary>
        public IReadOnlyList<string> MetaOptions { get; init; } = new List<string>();
    }
}
