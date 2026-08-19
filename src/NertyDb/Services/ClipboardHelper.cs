using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace NertyDb.Services
{
    public static class ClipboardHelper
    {
        public static bool SetText(string text)
        {
            if (text == null) return false;

            // Try up to 5 times with small backoff in case another process (e.g. Windows Clipboard History / RDP) has it locked
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(30);
                }
                catch (Exception)
                {
                    Thread.Sleep(30);
                }
            }

            return false;
        }
    }
}
