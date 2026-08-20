using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NertyDb.Services
{
    /// <summary>
    /// Fornece acesso robusto, resiliente e seguro à área de transferência do Windows via Win32 API.
    /// Não depende do pipeline OLE/COM do WPF (evitando AccessViolation 0xc0000005 e COMException).
    /// Funciona de forma segura em qualquer thread e trata bloqueios temporários com retentativas.
    /// </summary>
    public static class ClipboardHelper
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        /// <summary>
        /// Define o texto da área de transferência de forma nativa e segura com retentativas.
        /// </summary>
        public static bool SetText(string? text)
        {
            if (text == null) text = string.Empty;

            for (int i = 0; i < 6; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();

                        // Codifica texto em UTF-16 LE com terminador nulo duplo
                        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                        var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                        if (hGlobal != IntPtr.Zero)
                        {
                            var target = GlobalLock(hGlobal);
                            if (target != IntPtr.Zero)
                            {
                                Marshal.Copy(bytes, 0, target, bytes.Length);
                                GlobalUnlock(hGlobal);

                                if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                                {
                                    // Sucesso: o sistema operacional agora é dono do hGlobal
                                    return true;
                                }
                            }
                            GlobalFree(hGlobal);
                        }
                    }
                    catch { }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(20);
            }
            return false;
        }

        /// <summary>
        /// Obtém o texto da área de transferência de forma nativa e segura.
        /// </summary>
        public static string GetText()
        {
            for (int i = 0; i < 6; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        var handle = GetClipboardData(CF_UNICODETEXT);
                        if (handle != IntPtr.Zero)
                        {
                            var pointer = GlobalLock(handle);
                            if (pointer != IntPtr.Zero)
                            {
                                try
                                {
                                    return Marshal.PtrToStringUni(pointer) ?? string.Empty;
                                }
                                finally
                                {
                                    GlobalUnlock(handle);
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(20);
            }
            return string.Empty;
        }

        /// <summary>
        /// Verifica se a área de transferência contém texto Unicode.
        /// </summary>
        public static bool ContainsText()
        {
            try
            {
                return IsClipboardFormatAvailable(CF_UNICODETEXT);
            }
            catch
            {
                return false;
            }
        }
    }
}
