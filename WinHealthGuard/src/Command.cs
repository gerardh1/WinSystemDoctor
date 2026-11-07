using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinHealthGuard
{
    static class Command
    {
        public static int Run(string file, string arguments, out string output, int timeoutMs)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();
                    string stdout = "";
                    string stderr = "";
                    ThreadOut a = new ThreadOut(process.StandardOutput);
                    ThreadOut b = new ThreadOut(process.StandardError);
                    System.Threading.Thread t1 = new System.Threading.Thread(a.Read);
                    System.Threading.Thread t2 = new System.Threading.Thread(b.Read);
                    t1.IsBackground = true;
                    t2.IsBackground = true;
                    t1.Start();
                    t2.Start();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); }
                        catch { }
                        output = "Timed out.";
                        return -1;
                    }
                    process.WaitForExit();
                    t1.Join(3000);
                    t2.Join(3000);
                    stdout = a.Text ?? "";
                    stderr = b.Text ?? "";
                    output = (stdout + " " + stderr).Trim();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        public static RepairResult RunElevated(string body, int timeoutMs, bool hidden)
        {
            RepairResult result = new RepairResult();
            try
            {
                string dir = AppPaths.Ensure();
                string job = Path.Combine(dir, "repair-job.ps1");
                string launch = Path.Combine(dir, "repair-launch.ps1");
                string resultPath = Path.Combine(dir, "repair-result.txt");
                try { if (File.Exists(resultPath)) File.Delete(resultPath); }
                catch { }

                string quotedResult = resultPath.Replace("'", "''");
                string script = "$resultFile = '" + quotedResult + "'\r\n"
                    + "$ErrorActionPreference = 'Stop'\r\n"
                    + "try {\r\n"
                    + body + "\r\n"
                    + "  if (-not (Test-Path -LiteralPath $resultFile)) { [System.IO.File]::WriteAllText($resultFile, 'OK') }\r\n"
                    + "} catch {\r\n"
                    + "  [System.IO.File]::WriteAllText($resultFile, ('ERROR: ' + $_.Exception.Message))\r\n"
                    + "}\r\n";
                File.WriteAllText(job, script, new UTF8Encoding(true));

                string style = hidden ? "Hidden" : "Normal";
                string quotedJob = job.Replace("'", "''");
                string launcher = "$ErrorActionPreference = 'Stop'\r\n"
                    + "try {\r\n"
                    + "  Start-Process -FilePath 'powershell.exe' -WindowStyle " + style + " -Verb RunAs -Wait -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File','" + quotedJob + "')\r\n"
                    + "} catch {\r\n"
                    + "  [System.IO.File]::WriteAllText('" + quotedResult + "', ('CANCEL: ' + $_.Exception.Message))\r\n"
                    + "  exit 1\r\n"
                    + "}\r\n";
                File.WriteAllText(launch, launcher, new UTF8Encoding(true));

                string output;
                Run(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell\\v1.0\\powershell.exe"),
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + launch + "\"", out output, timeoutMs);

                string text = "";
                try
                {
                    if (File.Exists(resultPath)) text = File.ReadAllText(resultPath, new UTF8Encoding(true));
                }
                catch { }
                text = (text ?? "").Replace("\0", "").Trim();
                if (text.StartsWith("OK", StringComparison.Ordinal))
                {
                    result.Ok = true;
                    string rest = text.Length > 2 ? text.Substring(2).Trim() : "";
                    result.Message = rest.Length == 0 ? "Finished." : rest;
                    return result;
                }
                if (text.StartsWith("CANCEL", StringComparison.Ordinal) || text.Length == 0)
                {
                    result.Cancelled = true;
                    result.Message = "Administrator approval was cancelled.";
                    return result;
                }
                result.Message = text.StartsWith("ERROR:", StringComparison.Ordinal) ? text.Substring(6).Trim() : text;
                if (result.Message.Length == 0) result.Message = TextUtil.OneLine(output);
                if (result.Message.Length == 0) result.Message = "The repair did not finish.";
                return result;
            }
            catch (Exception ex)
            {
                return RepairResult.Fail(ex.Message);
            }
        }

        sealed class ThreadOut
        {
            readonly StreamReader _reader;
            public string Text;
            public ThreadOut(StreamReader reader) { _reader = reader; }
            public void Read()
            {
                try { Text = _reader.ReadToEnd(); }
                catch { Text = ""; }
            }
        }
    }
}
