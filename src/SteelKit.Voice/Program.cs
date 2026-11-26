using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace SteelKit.Voice
{
    /// <summary>
    /// SteelKitSuara.exe - pembantu suara offline.
    ///   SteelKitSuara.exe dengar [detik] [no|auto] : rekam mikrofon, berhenti otomatis saat diam, ubah ke teks (Whisper, bahasa Indonesia)
    ///   SteelKitSuara.exe unduh    : unduh model sekali ke %LOCALAPPDATA%\SteelKit\suara
    ///   SteelKitSuara.exe status   : cek model & mikrofon
    ///   SteelKitSuara.exe berkas X : ubah berkas WAV 16 kHz mono ke teks (uji)
    ///   SteelKitSuara.exe ujimic   : rekam 1,5 detik untuk cek mikrofon
    ///   SteelKitSuara.exe perangkat: daftar mikrofon + kuat sinyal; mana yang dipilih otomatis
    /// Keluaran per baris ke stdout (UTF-8): STATUS ..., LEVEL 0.00, DL persen, TEXT ..., ERR ..., DONE
    /// Masukan stdin: "stop" = berhenti merekam sekarang.
    /// </summary>
    static class Program
    {
        public const string ModelName = "ggml-small-q5_1.bin";
        public const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin";
        public const long ModelBytes = 190085487;

        // kosakata membantu Whisper mengenali istilah baja
        // contoh kalimat + istilah teknik yang sering dipakai engineer & drafter: membuat Whisper mengeja istilah ini dengan benar
        const string Prompt = "Portal bentang 20 meter tinggi 6 meter miring 10 derajat, 5 bay jarak 6 meter. Kolom WF400, rafter WF350, " +
                              "gording C150 jarak 1,2 meter, bracing RD16, balok anak WF200, mezanin, kolom H300, pipa, siku L50, hollow. " +
                              "Kanopi kantilever, carport, menara, tower air, jembatan rangka, truss, gelagar, frame, haunch, apex, " +
                              "base plate, pelat buhul, pengaku, stiffener, turnbuckle, sag rod, girt, purlin.";

        public static string ModelDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteelKit", "suara");
        public static string ModelPath => Path.Combine(ModelDir, ModelName);

        static readonly object outLock = new object();
        static void Say(string s) { lock (outLock) { Console.Out.WriteLine(s); Console.Out.Flush(); } }

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            try
            {
                switch (cmd)
                {
                    case "unduh": return Download();
                    case "dengar": return Listen(args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 30, args.Length > 2 ? args[2] : "auto");
                    case "daftar":
                        for (int i = 0; i < Mic.DeviceCount(); i++) Say("DEV " + i + " 0 " + Mic.Name(i));
                        Say("DONE");
                        return 0;
                    case "perangkat":
                        {
                            var probe = Mic.ProbeAll(400);
                            for (int i = 0; i < probe.Count; i++)
                                Say("DEV " + i + " " + probe[i].Rms.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture) + " " + probe[i].Name);
                            Say("PILIH " + Mic.Pick(probe, "auto"));
                            Say("DONE");
                            return 0;
                        }
                    case "berkas": return FromFile(args[1]);
                    case "ujimic":
                        {
                            var smp = Mic.Record(1.5, new ManualResetEventSlim(false), 0xFFFFFFFF, lvl => Say("LEVEL " + lvl.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)), out bool _);
                            Say("MIC_OK " + smp.Length + " sampel");
                            Say("DONE");
                            return 0;
                        }
                    default:
                        Say("MODEL " + (ModelOk() ? "ADA" : "BELUM") + " " + ModelPath);
                        Say("MIC " + Mic.DeviceCount());
                        Say("DONE");
                        return 0;
                }
            }
            catch (Exception ex)
            {
                Say("ERR " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        public static bool ModelOk() => File.Exists(ModelPath) && new FileInfo(ModelPath).Length == ModelBytes;

        // ------------------------------------------------------------------ unduh model
        static int Download()
        {
            if (ModelOk()) { Say("DL 100"); Say("DONE"); return 0; }
            Directory.CreateDirectory(ModelDir);
            string part = ModelPath + ".part";
            using (var http = new HttpClient(new HttpClientHandler { UseProxy = true, Proxy = WebRequest.GetSystemWebProxy(), DefaultProxyCredentials = CredentialCache.DefaultCredentials }) { Timeout = TimeSpan.FromMinutes(60) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SteelKit/0.1");
                Say("STATUS Mengunduh model suara (" + (ModelBytes / 1048576) + " MB, sekali saja)");
                using (var resp = http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead).Result)
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? ModelBytes;
                    using (var src = resp.Content.ReadAsStreamAsync().Result)
                    using (var dst = File.Create(part))
                    {
                        var buf = new byte[1 << 16]; long got = 0; int n, last = -1;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            dst.Write(buf, 0, n); got += n;
                            int pct = (int)(got * 100 / total);
                            if (pct != last) { Say("DL " + pct); last = pct; }
                        }
                    }
                }
            }
            if (new FileInfo(part).Length != ModelBytes) { File.Delete(part); Say("ERR Unduhan tidak lengkap, coba lagi."); return 1; }
            if (File.Exists(ModelPath)) File.Delete(ModelPath);
            File.Move(part, ModelPath);
            Say("DONE");
            return 0;
        }

        // ------------------------------------------------------------------ dengar
        static int Listen(double maxSeconds, string device)
        {
            if (!ModelOk()) { Say("ERR MODEL_BELUM_ADA"); return 2; }
            if (Mic.DeviceCount() == 0) { Say("ERR TIDAK_ADA_MIC"); return 3; }
            // muat model paralel dengan perekaman supaya hasil keluar lebih cepat
            var load = Task.Run(() => WhisperFactory.FromPath(ModelPath));
            var stop = new ManualResetEventSlim(false);
            new Thread(() => { try { string l; while ((l = Console.In.ReadLine()) != null) if (l.Trim() == "stop") { stop.Set(); return; } } catch { } }) { IsBackground = true }.Start();

            // pilih mikrofon yang benar-benar menerima sinyal (mikrofon virtual seperti iVCam/OBS sering diam total)
            var probe = Mic.ProbeAll(300);
            int dev = Mic.Pick(probe, device);
            Say("MIC " + (dev >= 0 && dev < probe.Count ? probe[dev].Name : "bawaan Windows"));
            Say("STATUS Mendengarkan... bicara sekarang");
            var samples = Mic.Record(maxSeconds, stop, dev < 0 ? 0xFFFFFFFF : (uint)dev, lvl => Say("LEVEL " + lvl.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)), out bool heard);
            if (!heard) { Say("ERR TIDAK_ADA_SUARA"); return 4; }
            Say("STATUS Mengubah suara menjadi teks...");
            Say("TEXT " + Transcribe(load.Result, samples));
            Say("DONE");
            return 0;
        }

        static int FromFile(string wav)
        {
            if (!ModelOk()) { Say("ERR MODEL_BELUM_ADA"); return 2; }
            var bytes = File.ReadAllBytes(wav);
            int off = 44; var s = new float[(bytes.Length - off) / 2];
            for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(bytes, off + 2 * i) / 32768f;
            Say("TEXT " + Transcribe(WhisperFactory.FromPath(ModelPath), s));
            Say("DONE");
            return 0;
        }

        static string Transcribe(WhisperFactory factory, float[] samples)
        {
            // mikrofon pelan: naikkan ke puncak 0,9 supaya Whisper mendengar jelas (maks 20x)
            float peak = 0; foreach (var v in samples) { float a = Math.Abs(v); if (a > peak) peak = a; }
            if (peak > 1e-4f && peak < 0.9f) { float g = Math.Min(20f, 0.9f / peak); for (int i = 0; i < samples.Length; i++) samples[i] *= g; }
            using (factory)
            using (var proc = factory.CreateBuilder().WithLanguage("id").WithPrompt(Prompt)
                       .WithThreads(Math.Max(2, Math.Min(8, Environment.ProcessorCount - 1))).Build())
            {
                var sb = new StringBuilder();
                var e = proc.ProcessAsync(samples).GetAsyncEnumerator();
                try { while (e.MoveNextAsync().AsTask().Result) sb.Append(e.Current.Text); }
                finally { e.DisposeAsync().AsTask().Wait(); }
                return sb.ToString().Trim().Replace("\r", " ").Replace("\n", " ");
            }
        }
    }

    /// <summary>Rekam mikrofon lewat winmm (bawaan Windows), 16 kHz mono 16-bit; berhenti otomatis setelah diam.</summary>
    static class Mic
    {
        const int Rate = 16000, ChunkMs = 100, Buffers = 6;

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat { public ushort Tag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, Size; }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr { public IntPtr Data; public uint BufferLength, BytesRecorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }

        [DllImport("winmm.dll")] static extern uint waveInGetNumDevs();
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WaveInCaps { public ushort Mid, Pid; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Formats; public ushort Channels, Reserved; }
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(UIntPtr dev, ref WaveInCaps caps, uint size);

        public class Probe { public string Name; public double Rms; }

        static readonly string[] VirtualHints = { "ivcam", "virtual", "obs", "vb-audio", "voicemeeter", "camo", "droidcam", "cable", "stereo mix", "steam" };
        static bool IsVirtual(string n) { n = (n ?? "").ToLowerInvariant(); return VirtualHints.Any(h => n.Contains(h)); }

        public static string Name(int i)
        {
            var c = new WaveInCaps();
            return waveInGetDevCapsW((UIntPtr)(uint)i, ref c, (uint)Marshal.SizeOf(typeof(WaveInCaps))) == 0 ? c.Name : "Mikrofon " + i;
        }

        /// <summary>Rekam singkat dari semua mikrofon bersamaan; mikrofon yang diam total (RMS 0) biasanya virtual / tidak terhubung.</summary>
        public static List<Probe> ProbeAll(int ms)
        {
            int n = DeviceCount();
            var res = new Probe[n];
            var tasks = Enumerable.Range(0, n).Select(i => Task.Run(() =>
            {
                double rms = -1;
                try
                {
                    var smp = Record(ms / 1000.0, new ManualResetEventSlim(false), (uint)i, _ => { }, out bool _);
                    int skip = Math.Min(smp.Length, 1600);   // abaikan klik awal 0,1 detik
                    double sum = 0; for (int j = skip; j < smp.Length; j++) sum += smp[j] * (double)smp[j];
                    rms = smp.Length > skip ? Math.Sqrt(sum / (smp.Length - skip)) : 0;
                }
                catch { }
                res[i] = new Probe { Name = Name(i), Rms = rms };
            })).ToArray();
            Task.WaitAll(tasks);
            return res.ToList();
        }

        /// <summary>Nomor mikrofon: angka = pilihan pengguna; "auto" = sinyal hidup, utamakan mikrofon fisik.</summary>
        public static int Pick(List<Probe> p, string want)
        {
            if (int.TryParse(want, out int k) && k >= 0 && k < p.Count) return k;
            if (!string.IsNullOrEmpty(want) && want != "auto")
            {
                int byName = p.FindIndex(x => x.Name != null && x.Name.StartsWith(want, StringComparison.OrdinalIgnoreCase));
                if (byName >= 0) return byName;
            }
            var live = Enumerable.Range(0, p.Count).Where(i => p[i].Rms > 1e-6).ToList();
            var real = live.Where(i => !IsVirtual(p[i].Name)).ToList();
            if (real.Count > 0) return real.OrderByDescending(i => p[i].Rms).First();
            if (live.Count > 0) return live.OrderByDescending(i => p[i].Rms).First();
            var phys = Enumerable.Range(0, p.Count).Where(i => !IsVirtual(p[i].Name) && p[i].Rms >= 0).ToList();
            return phys.Count > 0 ? phys[0] : -1;
        }
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, uint dev, ref WaveFormat f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        public static int DeviceCount() => (int)waveInGetNumDevs();

        public static float[] Record(double maxSeconds, ManualResetEventSlim stop, uint device, Action<double> level, out bool heard)
        {
            var fmt = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSec = Rate, BitsPerSample = 16, BlockAlign = 2, AvgBytesPerSec = Rate * 2 };
            int r = waveInOpen(out IntPtr h, device, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) throw new InvalidOperationException("Mikrofon tidak bisa dibuka (kode " + r + "). Periksa Pengaturan > Privasi > Mikrofon: izinkan aplikasi desktop.");
            int bytes = Rate * 2 * ChunkMs / 1000;
            uint hs = (uint)Marshal.SizeOf(typeof(WaveHdr));
            var hdrs = new IntPtr[Buffers];
            var all = new List<short>();
            heard = false;
            try
            {
                for (int i = 0; i < Buffers; i++)
                {
                    hdrs[i] = Marshal.AllocHGlobal((int)hs);
                    Marshal.StructureToPtr(new WaveHdr { Data = Marshal.AllocHGlobal(bytes), BufferLength = (uint)bytes }, hdrs[i], false);
                    waveInPrepareHeader(h, hdrs[i], hs); waveInAddBuffer(h, hdrs[i], hs);
                }
                waveInStart(h);
                double noise = 0.004, silentMs = 0, totalMs = 0, startedMs = -1;
                var chunk = new short[bytes / 2];
                while (!stop.IsSet && totalMs < maxSeconds * 1000)
                {
                    bool any = false;
                    for (int i = 0; i < Buffers; i++)
                    {
                        var hd = (WaveHdr)Marshal.PtrToStructure(hdrs[i], typeof(WaveHdr));
                        if ((hd.Flags & 1) == 0) continue;   // WHDR_DONE
                        any = true;
                        int n = (int)hd.BytesRecorded / 2;
                        Marshal.Copy(hd.Data, chunk, 0, n);
                        double sum = 0; for (int j = 0; j < n; j++) { double v = chunk[j] / 32768.0; sum += v * v; }
                        double rms = n > 0 ? Math.Sqrt(sum / n) : 0;
                        for (int j = 0; j < n; j++) all.Add(chunk[j]);
                        totalMs += ChunkMs;
                        level(Math.Min(1, Math.Sqrt(rms) * 2.2));
                        // ambang suara adaptif terhadap derau ruangan
                        if (startedMs < 0 && totalMs <= 600) noise = Math.Max(0.002, 0.7 * noise + 0.3 * rms);
                        bool voice = rms > Math.Max(0.006, noise * 3.0);
                        if (voice) { if (startedMs < 0) startedMs = totalMs; silentMs = 0; heard = true; }
                        else silentMs += ChunkMs;
                        hd.Flags &= ~1u; hd.BytesRecorded = 0;
                        Marshal.StructureToPtr(hd, hdrs[i], false);
                        waveInAddBuffer(h, hdrs[i], hs);
                    }
                    if (heard && silentMs >= 1600) break;                    // selesai bicara
                    if (!heard && totalMs >= 9000) break;                     // tidak ada suara
                    if (!any) Thread.Sleep(15);
                }
                waveInReset(h);
            }
            finally
            {
                for (int i = 0; i < Buffers; i++)
                {
                    if (hdrs[i] == IntPtr.Zero) continue;
                    var hd = (WaveHdr)Marshal.PtrToStructure(hdrs[i], typeof(WaveHdr));
                    waveInUnprepareHeader(h, hdrs[i], hs);
                    Marshal.FreeHGlobal(hd.Data); Marshal.FreeHGlobal(hdrs[i]);
                }
                waveInClose(h);
            }
            return all.Select(s => s / 32768f).ToArray();
        }
    }
}
