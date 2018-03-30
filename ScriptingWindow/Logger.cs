using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ScriptingWindow
{
    static public class Logger
    {
        static public void Write(string message, object sender = null)
        {
            StackTrace stack = new StackTrace(true);
            StackFrame frame = stack.GetFrame(1); // should be frame that called this
            string toWrite = $"[{DateTime.Now} {System.IO.Path.GetFileName(frame.GetFileName())}:{frame.GetFileLineNumber()}] {message}";

            LogWritten?.Invoke(sender, toWrite);
        }

        static public event EventHandler<string> LogWritten;
    }
}
