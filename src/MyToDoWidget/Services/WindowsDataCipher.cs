using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MyToDoWidget.Services;

public sealed class WindowsDataCipher : IStateCipher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }

    public byte[] Protect(byte[] bytes) => Transform(bytes, true);
    public byte[] Unprotect(byte[] bytes) => Transform(bytes, false);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        IntPtr input = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        var source = new Blob { Length = bytes.Length, Data = input };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input, bytes.Length);
            // CRYPTPROTECT_UI_FORBIDDEN. No LOCAL_MACHINE flag: current user only.
            bool success = protect
                ? CryptProtectData(ref source, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).NativeErrorCode);
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(input, i, 0);
            Marshal.FreeHGlobal(input);
            if (output.Data != IntPtr.Zero)
            {
                for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
