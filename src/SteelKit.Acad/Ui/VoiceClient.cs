using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SteelKit.Acad.Ui
{
    /// <summary>
    /// Menjalankan SteelKitSuara.exe (proses terpisah) dan meneruskan barisnya: STATUS, LEVEL, DL, TEXT, ERR, DONE.
    /// Mesin suara di proses lain: pustaka Whisper tidak bentrok dengan AutoCAD dan gangguannya tidak menutup AutoCAD.
    /// </summary>
    public class VoiceClient
    {
        // harus sama dengan SteelKit.Voice/Program.cs
        public const string ModelFile = "ggml-small-q5_1.bin";
        const long ModelBytes = 190085487;
        public static int ModelMb => (int)Math.Round(ModelBytes / 1048576.0);
        public static string ModelDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteelKit", "suara");
        public static bool ModelReady()
        {
            var p = Path.Combine(ModelDir, ModelFile);
            return File.Exists(p) && new FileInfo(p).Length == ModelBytes;
        }

        static string MicFile => Path.Combine(ModelDir, "mikrofon.txt");
        /// <summary>Nama mikrofon pilihan pengguna; kosong = otomatis (mikrofon fisik yang menerima sinyal).</summary>
        public static string MicChoice
        {
            get { try { return File.Exists(MicFile) ? File.ReadAllText(MicFile).Trim() : ""; } catch { return ""; } }
            set { try { Directory.CreateDirectory(ModelDir); File.WriteAllText(MicFile, value ?? ""); } catch { } }
        }

        /// <summary>Daftar nama mikrofon (cepat, tanpa merekam).</summary>
        public static System.Collections.Generic.List<string> ListMics()
        {
            var list = new System.Collections.Generic.List<string>();
            var exe = ExePath();
            if (exe == null) return list;
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, "daftar") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 }))
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                        if (line.StartsWith("DEV ")) { var parts = line.Split(new[] { ' ' }, 4); if (parts.Length == 4) list.Add(parts[3]); }
                    p.WaitForExit(5000);
                }
            }
            catch { }
            return list;
        }

        Process proc;
        public event Action<string, string> Line;
        public event Action Exited;

        /// <summary>Bundle: Contents\R25\SteelKit.dll -> Contents\Suara\SteelKitSuara.exe. Saat pengembangan: bin proyek Voice.</summary>
        public static string ExePath()
        {
            var dir = Path.GetDirectoryName(typeof(VoiceClient).Assembly.Location) ?? "";
            var cands = new[]
            {
                Path.Combine(dir, "..", "Suara", "SteelKitSuara.exe"),
                Path.Combine(dir, "Suara", "SteelKitSuara.exe"),
            };
            foreach (var c in cands) if (File.Exists(c)) return Path.GetFullPath(c);
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            {
                var dev = Path.Combine(d.FullName, "src", "SteelKit.Voice", "bin", "Release", "net48", "SteelKitSuara.exe");
                if (File.Exists(dev)) return dev;
            }
            return null;
        }

        public bool Start(string args)
        {
            var exe = ExePath();
            if (exe == null) return false;
            try
            {
                proc = new Process
                {
                    StartInfo = new ProcessStartInfo(exe, args)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe),
                    },
                    EnableRaisingEvents = true,
                };
                proc.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    int i = e.Data.IndexOf(' ');
                    Line?.Invoke(i < 0 ? e.Data : e.Data.Substring(0, i), i < 0 ? "" : e.Data.Substring(i + 1));
                };
                proc.Exited += (s, e) => Exited?.Invoke();
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                return true;
            }
            catch { return false; }
        }

        public void Stop() { try { if (proc != null && !proc.HasExited) { proc.StandardInput.WriteLine("stop"); proc.StandardInput.Flush(); } } catch { } }
        public void Kill() { try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { } }
    }
}
