// Prueba del motor de emisión de un VortexsLive.exe (cargado por reflexión), con vídeo de verdad:
//   ffmpeg (hace de OBS) → motor de VortexsLive con delay → ffmpeg (hace de Twitch) → recibido.flv
// Comprueba que el delay es el pedido, que subirlo y bajarlo en directo no corta la emisión ni
// reconecta, que un clip se guarda bien y que lo recibido se puede reproducir.
// Uso: verifyengine.exe VortexsLive.exe [ffmpeg.exe] [carpeta]. Devuelve 1 si algo falla.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

static class VerifyEngine
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int PortIn = 19355;    // donde escucha el motor (lo que en casa es el 1935 de OBS)
    const int PortOut = 19356;   // donde escucha el ffmpeg que hace de plataforma

    static Assembly A;
    static object engine;
    static string ffmpeg = "ffmpeg";
    static string dir = ".";
    static int fails;
    static readonly Stopwatch clock = Stopwatch.StartNew();

    static Type T(string n) { return A.GetType("StreamDelay." + n, true); }

    static object Get(object o, string name)
    {
        Type t = o.GetType();
        FieldInfo f = t.GetField(name, F);
        if (f != null) return f.GetValue(o);
        PropertyInfo p = t.GetProperty(name, F);
        if (p != null) return p.GetValue(o, null);
        throw new MissingMemberException(t.Name, name);
    }

    static void Set(object o, string name, object v)
    {
        Type t = o.GetType();
        FieldInfo f = t.GetField(name, F);
        if (f != null) { f.SetValue(o, v); return; }
        PropertyInfo p = t.GetProperty(name, F);
        if (p != null) { p.SetValue(o, v, null); return; }
        throw new MissingMemberException(t.Name, name);
    }

    static object Call(object o, string name, params object[] args)
    {
        foreach (MethodInfo m in o.GetType().GetMethods(F))
        {
            if (m.Name == name && m.GetParameters().Length == args.Length)
            {
                try { return m.Invoke(o, args); }
                catch (TargetInvocationException e) { throw e.InnerException; }
            }
        }
        throw new MissingMethodException(o.GetType().Name, name);
    }

    static void Say(string s) { Console.WriteLine("[" + (clock.ElapsedMilliseconds / 1000.0).ToString("00.0", CultureInfo.InvariantCulture) + "] " + s); }

    static void Check(bool ok, string what)
    {
        Say((ok ? "OK     " : "FALLO  ") + what);
        if (!ok) fails++;
    }

    static object FirstSink()
    {
        foreach (object s in (IEnumerable)Get(engine, "Sinks")) return s;
        return null;
    }

    static string SinkState() { object s = FirstSink(); return s == null ? "(ninguno)" : Get(s, "State").ToString(); }
    static int Delay() { return (int)Get(engine, "CurrentDelayMs"); }

    // Espera hasta que se cumpla la condición (true) o pase el tiempo (false).
    static bool WaitFor(Func<bool> cond, int ms)
    {
        long end = clock.ElapsedMilliseconds + ms;
        while (clock.ElapsedMilliseconds < end)
        {
            if (cond()) return true;
            Thread.Sleep(50);
        }
        return cond();
    }

    static void Pause(int ms) { Thread.Sleep(ms); }

    static Process Run(string args, string tag)
    {
        ProcessStartInfo si = new ProcessStartInfo(ffmpeg, args)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        Process p = new Process { StartInfo = si };
        List<string> tail = new List<string>();
        p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (tail) { tail.Add(e.Data); if (tail.Count > 12) tail.RemoveAt(0); }
        };
        p.OutputDataReceived += delegate { };
        p.Exited += delegate
        {
            lock (tail) { foreach (string l in tail) Console.WriteLine("    " + tag + "| " + l); }
        };
        p.EnableRaisingEvents = true;
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        return p;
    }

    // Lo que dura un archivo (ffprobe junto a ffmpeg), o -1.
    static double Duration(string file)
    {
        string probe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ffmpeg)) ?? "", "ffprobe.exe");
        if (!File.Exists(probe)) probe = "ffprobe";
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(probe, "-v error -show_entries format=duration -of csv=p=0 \"" + file + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(20000);
                double d;
                return double.TryParse(o, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : -1;
            }
        }
        catch (Exception e)
        {
            Say("ffprobe: " + e.Message);
            return -1;
        }
    }

    // Errores al decodificar el archivo entero.
    static int DecodeErrors(string file)
    {
        ProcessStartInfo si = new ProcessStartInfo(ffmpeg, "-v error -i \"" + file + "\" -f null -")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (Process p = Process.Start(si))
        {
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            int n = 0;
            foreach (string l in err.Split('\n')) if (l.Trim().Length > 0) { n++; if (n <= 5) Console.WriteLine("    decodificar| " + l.Trim()); }
            return n;
        }
    }

    static void Kill(Process p)
    {
        try { if (p != null && !p.HasExited) p.Kill(); } catch { }
    }

    static int Main(string[] a)
    {
        if (a.Length > 1) ffmpeg = a[1];
        if (a.Length > 2) dir = a[2];
        Directory.CreateDirectory(dir);
        string received = Path.GetFullPath(Path.Combine(dir, "recibido.flv"));
        string clip = Path.GetFullPath(Path.Combine(dir, "clip.flv"));
        File.Delete(received);
        File.Delete(clip);

        A = Assembly.LoadFile(Path.GetFullPath(a[0]));
        engine = Activator.CreateInstance(T("Engine"), true);
        Set(engine, "Log", (Action<string>)(m => Say("motor: " + m)));
        Set(engine, "InputPort", PortIn);
        Set(engine, "TargetMs", 5000);
        object dest = Activator.CreateInstance(T("Destination"), F, null, new object[] { "otro", "Prueba", false }, null);
        Set(dest, "Server", "rtmp://127.0.0.1:" + PortOut + "/live");
        Set(dest, "Key", "prueba");
        Set(dest, "Enabled", true);
        ((IList)Get(engine, "Destinations")).Add(dest);

        // La "plataforma": recibe y guarda tal cual.
        Process receiver = Run("-hide_banner -y -listen 1 -timeout 120 -i rtmp://127.0.0.1:" + PortOut + "/live/prueba -c copy -f flv \"" + received + "\"", "plataforma");
        Pause(1500);
        Call(engine, "Start");
        Say("motor en marcha, delay 5 s");

        // El "OBS": 45 s de imagen y tono de prueba, en tiempo real, un fotograma clave cada 2 s.
        Process sender = Run("-hide_banner -re -f lavfi -i testsrc=size=640x360:rate=30 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 45 " +
            "-c:v libx264 -preset ultrafast -tune zerolatency -g 60 -pix_fmt yuv420p -b:v 1500k -c:a aac -b:a 128k -f flv rtmp://127.0.0.1:" + PortIn + "/live/clave", "obs");

        int fallos = 0;
        try
        {
            bool input = WaitFor(() => (bool)Get(engine, "InputActive"), 20000);
            long tIn = clock.ElapsedMilliseconds;
            Check(input, "el motor recibe la emisión de OBS");
            if (!input) return 1;

            Check(!WaitFor(() => (bool)Get(engine, "OutputActive"), 3500), "no sale nada antes de tiempo (delay 5 s)");
            bool live = WaitFor(() => (bool)Get(engine, "OutputActive"), 15000);
            long first = clock.ElapsedMilliseconds - tIn;
            Check(live, "el destino entra en directo (estado " + SinkState() + ")");
            if (!live) return 1;
            Check(first >= 4000 && first <= 9000, "sale a los " + first + " ms de recibirlo (5000 pedidos)");
            Pause(3000);
            int d = Delay();
            Check(d >= 4000 && d <= 6500, "delay real " + d + " ms con 5 s");

            // Subir el delay en directo: repite lo último hasta ganar los 5 s de más.
            Set(engine, "TargetMs", 10000);
            Say("delay → 10 s");
            Pause(4000);
            Check((bool)Get(engine, "OutputActive"), "sigue en directo tras subir el delay");
            bool up = WaitFor(() => Delay() >= 8500 && Delay() <= 11500, 8000);
            Check(up, "delay real " + Delay() + " ms con 10 s");

            // Bajarlo: se salta lo que sobra.
            Set(engine, "TargetMs", 2000);
            Say("delay → 2 s");
            bool down = WaitFor(() => Delay() > 0 && Delay() <= 3500, 10000);
            Check(down, "delay real " + Delay() + " ms con 2 s");
            Check((bool)Get(engine, "OutputActive"), "sigue en directo tras bajar el delay");

            object s = FirstSink();
            Call(s, "SampleRate");
            Pause(1100);
            Call(s, "SampleRate");
            int rate = (int)Get(s, "RateKbps");
            Check(rate > 300 && rate < 20000, "velocidad de subida medida: " + rate + " kb/s");
            Check((int)Get(s, "Reconnects") == 0, "ninguna reconexión (" + Get(s, "Reconnects") + ")");

            // Un clip de los últimos 10 s.
            object clips = Get(engine, "Clips");
            double secs = (double)Call(clips, "Save", clip, 10, (int)Get(engine, "MainTrack"));
            Check(secs >= 9 && secs <= 14, "clip guardado: " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Check(File.Exists(clip) && new FileInfo(clip).Length > 50000, "clip.flv ocupa " + (File.Exists(clip) ? new FileInfo(clip).Length / 1024 : 0) + " KB");
        }
        catch (Exception e)
        {
            Check(false, "excepción: " + e);
        }
        finally
        {
            Kill(sender);
            Pause(1000);
            try { Call(engine, "Stop"); } catch (Exception e) { Check(false, "al parar: " + e.Message); }
            if (!receiver.WaitForExit(15000)) { Say("la plataforma no se cerró sola"); Kill(receiver); }
            receiver.WaitForExit(5000);
        }

        double dr = Duration(received);
        Check(dr >= 12, "recibido.flv dura " + dr.ToString("0.0", CultureInfo.InvariantCulture) + " s");
        Check(DecodeErrors(received) == 0, "recibido.flv se reproduce sin errores");
        if (File.Exists(clip))
        {
            double dc = Duration(clip);
            Check(dc >= 8, "clip.flv dura " + dc.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Check(DecodeErrors(clip) == 0, "clip.flv se reproduce sin errores");
        }
        fallos = fails;
        Say(fallos == 0 ? "Prueba del motor: todo correcto." : "Prueba del motor: " + fallos + " fallos.");
        return fallos == 0 ? 0 : 1;
    }
}
