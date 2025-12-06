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
        // 1MBのバッファ
        private const int BUFFER_SIZE = 1024 * 1024;

        public static string ComputeHash(string filePath)
        {
            var hashAlgorithm = xxHashFactory.Instance.Create(new xxHashConfig { HashSizeInBits = 64 });
            // 既存のファイルを開く、読み取り専用としてアクセス、他プロセスからのアクセスも読み取りのみ許可
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE);
            IHashValue hash = hashAlgorithm.ComputeHash(fileStream);
            // "-"を除去し、小文字に変換
            return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
