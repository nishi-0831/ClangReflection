using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Data.HashFunction.xxHash;
using System.Data.HashFunction;
namespace ClangTest
{
    public class XxHashComputer
    {
        // 32KBのバッファ
        private const int BUFFER_SIZE = 32768;

        public static string ComputeHash(string filePath)
        {
            // 64ビットのハッシュ値を出力するよう指定
            var hashAlgorithm = xxHashFactory.Instance.Create(new xxHashConfig { HashSizeInBits = 64 });
            // 既存のファイルを開く、読み取り専用としてアクセス、他プロセスからのアクセスも読み取りのみ許可
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE);
            IHashValue hash = hashAlgorithm.ComputeHash(fileStream);
            // "-"を除去し、小文字に変換
            return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
