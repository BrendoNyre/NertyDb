using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace NertyDb.Services
{
    /// <summary>
    /// Fornece acesso robusto, resiliente e seguro à área de transferência do Windows.
    /// Trata bloqueios temporários (RDP, Win+V, AnyDesk, Antivírus) com retentativas e fallback nativo Win32.
    /// Garante que nenhuma exceção não tratada seja propagada para a interface.
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
        /// Define o texto da área de transferência com retentativas e fallback Win32 nativo.
        /// </summary>
        public static bool SetText(string? text)
        {
            if (text == null) text = string.Empty;

            // 1. Tentar via WPF Clipboard com retentativas
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, false);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(25);
                }
                catch (Exception)
                {
                    Thread.Sleep(25);
                }
            }

            // 2. Fallback de baixo nível via Win32 API (não depende de OLE e funciona de qualquer thread)
            return SetTextWin32(text);
        }

        /// <summary>
        /// Obtém o texto da área de transferência de forma segura.
        /// </summary>
        public static string GetText()
        {
            // 1. Tentar via WPF Clipboard com retentativas
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        return Clipboard.GetText();
                    }
                    return string.Empty;
                }
                catch (COMException)
                {
                    Thread.Sleep(25);
                }
                catch (Exception)
                {
                    Thread.Sleep(25);
                }
            }

            // 2. Fallback Win32
            return GetTextWin32();
        }

        /// <summary>
        /// Verifica se a área de transferência contém texto.
        /// </summary>
        public static bool ContainsText()
        {
            try
            {
                if (Clipboard.ContainsText()) return true;
            }
            catch { }

            try
            {
                return IsClipboardFormatAvailable(CF_UNICODETEXT);
            }
            catch
            {
                return false;
            }
        }

        private static bool SetTextWin32(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                        var bytesCount = (text.Length + 1) * 2; // UTF-16 Unicode
                        var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytesCount);
                        if (hGlobal != IntPtr.Zero)
                        {
                            var target = GlobalLock(hGlobal);
                            if (target != IntPtr.Zero)
                            {
                                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                                Marshal.WriteInt16(target + (text.Length * 2), 0); // Null terminator
                                GlobalUnlock(hGlobal);
                                if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                                {
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
                Thread.Sleep(25);
            }
            return false;
        }

        private static string GetTextWin32()
        {
            for (int i = 0; i < 5; i++)
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
                Thread.Sleep(25);
            }
            return string.Empty;
        }
    }
}
