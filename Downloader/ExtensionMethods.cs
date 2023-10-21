using System.Collections;
using System.Security.Cryptography;
using System.Text;

namespace Dawnlc.Module
{
    public static class ExtensionMethods
    {
        private static readonly SHA1 SHA1 = SHA1.Create();
        private static readonly SHA256 SHA256 = SHA256.Create();
        private static readonly MD5 MD5 = MD5.Create();

        public static byte[] StreamRead(this Stream stream, long a, long b)
        {
            if ((b - a) > int.MaxValue || stream.Length < 4)
            {
                throw new OverflowException();
            }
            long save = stream.Position;
            byte[] ReadData;
            stream.Seek(a, SeekOrigin.Begin);
            ReadData = stream.StreamRead((int)(b - a));
            stream.Seek(save, SeekOrigin.Begin);
            return ReadData;
        }
        public static byte[] StreamRead(this Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            int count = buffer.Length;
            int bytesRead;
            while (count > 0 && (bytesRead = stream.Read(buffer, offset, count)) > 0)
            {
                offset += bytesRead;
                count -= bytesRead;
            }
            return buffer;
        }
        public static string ToReadableString(this long bytes, int decimalPlaces)
        {
            double numBytes = Convert.ToDouble(bytes);
            numBytes = Math.Max(numBytes, 0);
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            while (numBytes >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                numBytes /= 1024;
                suffixIndex++;
            }
            return $"{numBytes.ToString($"N{decimalPlaces}")} {suffixes[suffixIndex]}";
        }

        public static string ToReadableString(this double bytes, int decimalPlaces)
        {
            bytes = Math.Max(bytes, 0);
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            while (bytes >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                bytes /= 1024;
                suffixIndex++;
            }
            return $"{bytes.ToString($"N{decimalPlaces}")} {suffixes[suffixIndex]}";
        }
        /// <summary>
        /// 字节数组是否相等
        /// </summary>
        /// <param name="byteDatas"></param>
        /// <returns></returns>
        public static bool SequenceCompare(this byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.Length != y.Length) return false;
            return x.SequenceEqual(y);
        }

        /// <summary>
        /// byte[] 不同计数
        /// </summary>
        /// <param name="byteDatas"></param>
        /// <returns></returns>
        public static int Differences(this byte[] x, byte[] y)
        {
            return new BitArray(x).Differences(new BitArray(y));
        }
        /// <summary>
        /// BitArray 不同计数
        /// </summary>
        /// <param name="byteDatas"></param>
        /// <returns></returns>
        public static int Differences(this BitArray x, BitArray y)
        {
            int differences = 0;
            BitArray xor = x.Xor(y);
            for (int i = 0; i < xor.Length; i++)
            {
                if (xor[i])
                {
                    differences++;
                }
            }
            return differences;
        }

        /// <summary>
        /// 字节数组转16进制字符串
        /// </summary>
        /// <param name="byteDatas"></param>
        /// <returns></returns>
        public static string BytesToHexString(this byte[] byteDatas)
        {
            StringBuilder builder = new();
            for (int i = 0; i < byteDatas.Length; i++)
            {
                builder.Append(string.Format("{0:X2}", byteDatas[i]));
            }
            return builder.ToString();
        }
        /// <summary>
        /// 16进制字符串转字节数组
        /// </summary>
        /// <param name="HexString"></param>
        /// <returns></returns>
        public static byte[] HexStringToBytes(this string HexString)
        {
            if (HexString.Length % 2 != 0) throw new ArgumentException("Format err", nameof(HexString));
            char[] Hex = HexString.ToCharArray();
            byte[] bytes = new byte[Hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte($"{Hex[i * 2]}{Hex[(i * 2) + 1]}", 16);
            }
            return bytes;
        }
        /// <summary>
        /// 计算MD5Hash
        /// </summary>
        /// <param name="inputStream">数据流</param>
        /// <returns>MD5Hash字节数组</returns>
        public static async Task<byte[]> MD5HashAsync(this Stream inputStream)
        {
            return await MD5.ComputeHashAsync(inputStream);
        }
        /// <summary>
        /// 计算SHA1Hash
        /// </summary>
        /// <param name="inputStream">数据流</param>
        /// <returns>SHA1Hash字节数组</returns>
        public static async Task<byte[]> SHA1HashAsync(this Stream inputStream)
        {
            return await SHA1.ComputeHashAsync(inputStream);
        }/// <summary>
         /// 计算SHA256Hash
         /// </summary>
         /// <param name="inputStream">数据流</param>
         /// <returns>SHA256Hash字节数组</returns>
        public static async Task<byte[]> SHA256HashAsync(this Stream inputStream)
        {
            return await SHA256.ComputeHashAsync(inputStream);
        }
        /// <summary>
        /// 计算MD5Hash
        /// </summary>
        /// <param name="inputStream">数据流</param>
        /// <returns>MD5Hash字节数组</returns>
        public static byte[] MD5Hash(this Stream inputStream)
        {
            return MD5.ComputeHash(inputStream);
        }
        /// <summary>
        /// 计算SHA1Hash
        /// </summary>
        /// <param name="inputStream">数据流</param>
        /// <returns>SHA1Hash字节数组</returns>
        public static byte[] SHA1Hash(this Stream inputStream)
        {
            return SHA1.ComputeHash(inputStream);
        }/// <summary>
         /// 计算SHA256Hash
         /// </summary>
         /// <param name="inputStream">数据流</param>
         /// <returns>SHA256Hash字节数组</returns>
        public static byte[] SHA256Hash(this Stream inputStream)
        {
            return SHA256.ComputeHash(inputStream);
        }

    
        public static Action<T> LimitInvocationRate<T>(this Action<T> action, int maxInvocations, TimeSpan period)
        {
            int invocationCount = 0;
            Timer? timer = null;

            void limitedEventHandler(T e)
            {
                if (Interlocked.Increment(ref invocationCount) <= maxInvocations)
                {
                    action(e);
                }

                timer ??= new Timer(_ =>
                {
                    Interlocked.Exchange(ref invocationCount, 0);
                    timer?.Change(period, Timeout.InfiniteTimeSpan);
                }, null, period, Timeout.InfiniteTimeSpan);
            }

            return limitedEventHandler;
        }

    }
}
