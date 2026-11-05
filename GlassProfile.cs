using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace OffWorkWidget.WinUI;

// Opt-in diagnostics. OFFWORKWIDGET_GLASS_PROFILE enables a CSV beside the isolated data folder.
internal static class GlassProfile
{
    internal static readonly bool Enabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OFFWORKWIDGET_GLASS_PROFILE"));
    static readonly ConcurrentQueue<Row> queue = new();
    static readonly AutoResetEvent signal = new(false);
    static Thread? writer;
    static string? path;
    static volatile bool stopping;
    static long dropped;

    enum Kind { Render, Capture, Readback, Crop, Source, Appearance, Event, Pacing }

    readonly struct Row
    {
        internal readonly Kind Kind;
        internal readonly long At;
        internal readonly double A, B, C, D, E, F;
        internal readonly int Flags, I1, I2;
        internal readonly string? Note;

        internal Row(Kind kind, long at, double a = 0, double b = 0, double c = 0, double d = 0,
            double e = 0, double f = 0, int flags = 0, int i1 = 0, int i2 = 0, string? note = null)
        { Kind = kind; At = at; A = a; B = b; C = c; D = d; E = e; F = f; Flags = flags; I1 = i1; I2 = i2; Note = note; }
    }

    internal static long Timestamp() => Enabled ? Stopwatch.GetTimestamp() : 0;

    internal static double Ms(long started) => started == 0
        ? 0
        : (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

    internal static void Configure(string folder)
    {
        if (!Enabled || writer != null) return;
        try
        {
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, "glass-profile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
            writer = new Thread(WriteLoop) { IsBackground = true, Name = "GlassProfileWriter" };
            writer.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Shutdown();
        }
        catch
        {
            path = null;
        }
    }

    internal static void Shutdown()
    {
        if (!Enabled || writer == null || stopping) return;
        stopping = true;
        signal.Set();
        writer.Join(3000);
    }

    static void Enqueue(Row row)
    {
        if (!Enabled || path == null) return;
        if (queue.Count > 120_000)
        {
            Interlocked.Increment(ref dropped);
            return;
        }
        queue.Enqueue(row);
        signal.Set();
    }

    internal static void Render(double totalMs, double uploadMs, double appearanceMs, double intervalMs,
        double captureToRenderMs, bool visualDrag, bool changed, long pixels, int atlasVersion)
        => Enqueue(new Row(Kind.Render, Environment.TickCount64, totalMs, uploadMs, appearanceMs,
            intervalMs, captureToRenderMs, flags: (visualDrag ? 1 : 0) | (changed ? 2 : 0),
            i1: (int)Math.Min(int.MaxValue, pixels), i2: atlasVersion));

    // Callback cadence only; this is not a screen-presentation FPS measurement.
    internal static void Pacing(double intervalMs, bool moving, bool inactive, bool captureBusy, bool probeBusy)
        => Enqueue(new Row(Kind.Pacing, Environment.TickCount64, intervalMs,
            flags: (moving ? 1 : 0) | (inactive ? 2 : 0) | (captureBusy ? 4 : 0) | (probeBusy ? 8 : 0)));

    internal static void Capture(double composeMs, double metricsMs, double layoutMs, bool layoutProbed,
        bool pendingRegion, bool unchanged, bool skipped, int capturedWindows)
        => Enqueue(new Row(Kind.Capture, Environment.TickCount64, composeMs, metricsMs, layoutMs,
            flags: (pendingRegion ? 1 : 0) | (unchanged ? 2 : 0) | (skipped ? 4 : 0) | (layoutProbed ? 8 : 0),
            i1: capturedWindows));

    internal static void Readback(long started, long pixels)
        => Enqueue(new Row(Kind.Readback, Environment.TickCount64, Ms(started),
            i1: (int)Math.Min(int.MaxValue, pixels)));

    internal static void Crop(long started, long pixels)
        => Enqueue(new Row(Kind.Crop, Environment.TickCount64, Ms(started),
            i1: (int)Math.Min(int.MaxValue, pixels)));

    internal static void Appearance(double totalMs, double layoutMs, double textScanMs, double statsScanMs,
        double textLight, double glassLight, bool statsDark, bool automatic)
        => Enqueue(new Row(Kind.Appearance, Environment.TickCount64, totalMs, layoutMs, textScanMs,
            statsScanMs, textLight, glassLight, flags: (statsDark ? 2 : 0) | (automatic ? 1 : 0)));

    internal static void Event(string name)
        => Enqueue(new Row(Kind.Event, Environment.TickCount64, note: name));

    static void WriteLoop()
    {
        try
        {
            using var stream = new FileStream(path!, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var output = new StreamWriter(stream, new UTF8Encoding(false));
            output.WriteLine("kind,at_ms,ms1,ms2,ms3,ms4,ms5,ms6,flags,i1,i2,note");
            long lastFlush = Stopwatch.GetTimestamp();
            while (true)
            {
                while (queue.TryDequeue(out Row row))
                {
                    output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8},{9},{10},{11}",
                        row.Kind, row.At, row.A, row.B, row.C, row.D, row.E, row.F,
                        row.Flags, row.I1, row.I2, row.Note ?? string.Empty));
                }
                if (Stopwatch.GetTimestamp() - lastFlush >= Stopwatch.Frequency / 2)
                {
                    output.Flush();
                    lastFlush = Stopwatch.GetTimestamp();
                }
                if (stopping && queue.IsEmpty) break;
                signal.WaitOne(100);
            }
            if (dropped != 0) output.WriteLine("event,0,,,,,,,,{0},dropped", dropped);
            output.Flush();
        }
        catch
        {
            // Diagnostics must never affect the widget.
        }
    }
}
