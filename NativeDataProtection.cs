using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OffWorkWidget
{
    // Uses the same CurrentUser DPAPI format and optional entropy as ProtectedData.
    internal static class NativeDataProtection
    {
        const uint UiForbidden = 1;

        internal static byte[] Protect(byte[] clear, byte[] entropy) { return Transform(clear, entropy, true); }
        internal static byte[] Unprotect(byte[] encrypted, byte[] entropy) { return Transform(encrypted, entropy, false); }

        static byte[] Transform(byte[] data, byte[] entropy, bool protect)
        {
            ArgumentNullException.ThrowIfNull(data);
            DataBlob input = default, optionalEntropy = default, output = default;
            IntPtr description = IntPtr.Zero;
            try
            {
                input = Allocate(data);
                if (entropy != null && entropy.Length != 0) optionalEntropy = Allocate(entropy);
                bool success = protect
                    ? CryptProtectData(ref input, string.Empty, ref optionalEntropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                    : CryptUnprotectData(ref input, out description, ref optionalEntropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                if (!success) throw new CryptographicException(Marshal.GetLastPInvokeError());
                byte[] result = new byte[output.Length];
                if (result.Length != 0) Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                ClearAndFree(ref input, false);
                ClearAndFree(ref optionalEntropy, false);
                ClearAndFree(ref output, true);
                if (description != IntPtr.Zero) LocalFree(description);
            }
        }

        static DataBlob Allocate(byte[] data)
        {
            var blob = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
            try
            {
                if (data.Length != 0) Marshal.Copy(data, 0, blob.Data, data.Length);
                return blob;
            }
            catch { ClearAndFree(ref blob, false); throw; }
        }

        static void ClearAndFree(ref DataBlob blob, bool local)
        {
            if (blob.Data == IntPtr.Zero) return;
            for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(blob.Data, i, 0);
            if (local) LocalFree(blob.Data); else Marshal.FreeHGlobal(blob.Data);
            blob = default;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob { internal int Length; internal IntPtr Data; }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CryptProtectData(ref DataBlob data, string description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CryptUnprotectData(ref DataBlob data, out IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr memory);
    }
}
