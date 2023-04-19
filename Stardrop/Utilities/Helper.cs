using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Stardrop.Utilities
{
    internal class Helper
    {
        // Log file related
        private string basePath = AppDomain.CurrentDomain.BaseDirectory;
        private string logFileName = "log";
        private string logFileExtension = ".txt";

        // Listener and stat related
        private TraceListener listener;
        private int totalDebug = 0, totalAlert = 0, totalWarning = 0, totalInfo = 0;

        // Used for identifying different statuses when logging
        public enum Status { Debug, Alert, Warning, Info };

        public Helper(string fileName = "log", string fileExtension = ".txt", string path = null)
        {
            // Set the log file name and extension
            logFileName = fileName;
            logFileExtension = fileExtension;
            basePath = String.IsNullOrEmpty(path) ? basePath : path;

            // Delete any previous log file
            if (File.Exists(GetLogPath()))
            {
                File.Delete(GetLogPath());
            }

            // Create and enable the listener
            listener = new DelimitedListTraceListener(GetLogPath());
            Trace.Listeners.Add(listener);

            // This makes the Debug.WriteLine() calls always write to the text file
            // Rather than waiting for a Debug.Flush() call
            Trace.AutoFlush = true;
        }

        public string GetLogPath()
        {
            return Path.Combine(basePath, String.Concat(logFileName, logFileExtension));
        }

        public void DisableTracing()
        {
            // If listener exists and still is active, remove it
            if (!(listener is null) && Trace.Listeners.Contains(listener))
            {
                Trace.Listeners.Remove(listener);
                listener.Close();
            }
        }

        public bool IsActive()
        {
            return listener != null;
        }

        // Handles the Debug.WriteLine calls
        // It will grab the calling method and line as well via CompilerServices 
        public void Log(string message, Status status = Status.Debug, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string path = "")
        {
            // Tracking status info
            TrackStatus(status);

            string fileName = Path.GetFileName(path).Split('.')[0];
            Trace.WriteLine(string.Format("[{0}][{1}][{2}.{3}: Line {4}] {5}", DateTime.Now.ToString(), status.ToString(), fileName, caller.ToString(), line, message));
        }

        public void Log(object messageObj, Status status = Status.Debug, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string path = "")
        {
            try
            {
                Log(messageObj.ToString(), status, caller, line, path);
            }
            catch
            {
                Log(String.Format($"Unable to parse {messageObj} to string!"), Status.Warning);
            }
        }

        #region Status related tracking
        public bool HasAlert()
        {
            return totalAlert > 0 ? true : false;
        }

        public bool HasWarning()
        {
            return totalWarning > 0 ? true : false;
        }

        public bool HasInfo()
        {
            return totalInfo > 0 ? true : false;
        }

        public bool HasDebug()
        {
            return totalDebug > 0 ? true : false;
        }
        #endregion

        // Handles tracking the count of different Status counts
        private void TrackStatus(Status status)
        {
            switch (status)
            {
                case Status.Debug:
                    totalDebug += 1;
                    break;
                case Status.Alert:
                    totalAlert += 1;
                    break;
                case Status.Warning:
                    totalWarning += 1;
                    break;
                case Status.Info:
                    totalInfo += 1;
                    break;
            }
        }
    }
}
