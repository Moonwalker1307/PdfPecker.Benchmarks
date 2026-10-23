using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

// Compares PdfPecker with other .NET HTML-to-PDF converters on the same documents and machine.
// Usage: dotnet run -c Release [-- [--runs N] [--engines a,b] [--json file]]
//                                               run every engine (or the ones named), each in a fresh process, N times,
//                                               and print Markdown tables of the medians and their ratios to PdfPecker
//        dotnet run -c Release -- <engine>      run one engine in this process and print its result as JSON
// Chromium-based engines use CHROMIUM_PATH (default /opt/pw-browsers/chromium); wkhtmltopdf must be on PATH.
// PDFPECKER_BENCH_WARMUP sets the warm-up in seconds (default 3).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

if (args.Length > 0 && args[0] != "all" && !args[0].StartsWith("--", StringComparison.Ordinal))
{
    var result = await Bench.Run(args[0]);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return;
}

int rounds = 1;
string? jsonPath = null;
var engines = new[] { "pdfpecker", "htmlrenderer", "itext", "wkhtmltopdf", "puppeteer", "playwright", "syncfusion" };
for (int i = args.Length > 0 && args[0] == "all" ? 1 : 0; i < args.Length; i++)
{
    if (args[i] == "--runs" && i + 1 < args.Length) rounds = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture));
    else if (args[i] == "--engines" && i + 1 < args.Length) engines = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    else if (args[i] == "--json" && i + 1 < args.Length) jsonPath = args[++i];
    else throw new ArgumentException($"Unknown argument '{args[i]}'");
}

// Rounds go through every engine in turn, so a slow spell on a shared machine spreads over all engines instead of
// landing on one of them.
var runs = engines.ToDictionary(e => e, _ => new List<Result>());
for (int round = 1; round <= rounds; round++)
    foreach (var engine in engines)
    {
        Console.Error.WriteLine(rounds > 1 ? $"Running {engine} (round {round} of {rounds})..." : $"Running {engine}...");
        var result = Bench.RunInChild(typeof(Bench).Assembly.Location, engine);
        if (result == null) { Console.Error.WriteLine($"  {engine} failed"); continue; }
        runs[engine].Add(result);
    }

var results = engines.Where(e => runs[e].Count > 0).Select(e => Result.Median(runs[e])).ToList();
if (jsonPath != null)
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { Machine = Bench.MachineLine(), WarmupSeconds = Bench.WarmupSeconds, Rounds = rounds, Medians = results, Runs = runs }));

Console.WriteLine(Bench.MachineLine());
Console.WriteLine();
if (rounds > 1) Console.WriteLine($"Medians of {rounds} runs per engine, each in a fresh process.\n");
Console.WriteLine("| Engine | First PDF (cold) | Invoice, 1 page | Feature tour, 5 pages | Report, 30 pages | Parallel docs/s | Peak memory |");
Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
foreach (var r in results)
    Console.WriteLine($"| {Name(r)} | {Ms(r.ColdMs)} | {Ms(r.Medians[0])} | {Ms(r.Medians[1])} | {Ms(r.Medians[2])} | {r.ParallelDocsPerSecond:F0} | {r.PeakMemoryMb:F0} MB |");

// Ratios to PdfPecker carry over between machines far better than the times themselves: how many times slower than
// PdfPecker each engine is (or how many times its memory, and PdfPecker's throughput divided by the engine's).
if (results.FirstOrDefault(r => r.Engine == "pdfpecker") is { } baseline)
{
    Console.WriteLine();
    Console.WriteLine("Compared with PdfPecker (how many times the time, the memory, or PdfPecker's throughput):");
    Console.WriteLine();
    Console.WriteLine("| Engine | First PDF (cold) | Invoice, 1 page | Feature tour, 5 pages | Report, 30 pages | Parallel docs/s | Peak memory |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
    foreach (var r in results.Where(r => r != baseline))
        Console.WriteLine($"| {Name(r)} | {X(r.ColdMs / baseline.ColdMs)} | {X(r.Medians[0] / baseline.Medians[0])} | {X(r.Medians[1] / baseline.Medians[1])} | " +
            $"{X(r.Medians[2] / baseline.Medians[2])} | {X(baseline.ParallelDocsPerSecond / r.ParallelDocsPerSecond)} | {X(r.PeakMemoryMb / baseline.PeakMemoryMb)} |");
}

Console.WriteLine();
Console.WriteLine("Pages and size per document:");
foreach (var r in results)
    Console.WriteLine($"  {r.Engine,-13} {string.Join("  ", r.Pages.Zip(r.SizesKb, (p, s) => $"{p,3} p {s,6:F0} KB"))}");

string Name(Result r) => runs[r.Engine].Count < rounds ? $"{r.Engine} ({runs[r.Engine].Count} of {rounds} runs)" : r.Engine;

static string Ms(double ms) => ms >= 100 ? $"{ms:F0} ms" : ms >= 10 ? $"{ms:F1} ms" : $"{ms:F2} ms";

static string X(double ratio) => Math.Round(ratio, 1) >= 10 ? $"{ratio:F0}×" : $"{ratio:F1}×";

record Result(string Engine, double ColdMs, double[] Medians, int[] Pages, double[] SizesKb, double ParallelDocsPerSecond, double PeakMemoryMb)
{
    /// <summary>The median of each number over several runs of one engine; pages and sizes come from the first run.</summary>
    public static Result Median(List<Result> runs) => new(runs[0].Engine, Mid(runs.Select(r => r.ColdMs)),
        Enumerable.Range(0, runs[0].Medians.Length).Select(d => Mid(runs.Select(r => r.Medians[d]))).ToArray(),
        runs[0].Pages, runs[0].SizesKb, Mid(runs.Select(r => r.ParallelDocsPerSecond)), Mid(runs.Select(r => r.PeakMemoryMb)));

    static double Mid(IEnumerable<double> values)
    {
        var v = values.Order().ToArray();
        return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }
}

static class Bench
{
    static readonly string HtmlDir = Path.Combine(AppContext.BaseDirectory, "html");

    /// <summary>
    /// Seconds of warm-up before anything is timed, from PDFPECKER_BENCH_WARMUP (default 3). Tiered JIT needs more
    /// than 3 seconds to settle, so runs meant for publishing use a longer one. Child processes inherit it.
    /// </summary>
    public static double WarmupSeconds =>
        double.TryParse(Environment.GetEnvironmentVariable("PDFPECKER_BENCH_WARMUP"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s >= 0 ? s : 3;

    public static async Task<Result> Run(string name)
    {
        // The generated 30-page report is written next to the samples so every engine can load it from a file.
        var reportPath = Path.Combine(HtmlDir, "large-report.html");
        File.WriteAllText(reportPath, Documents.LargeReport());
        var docs = new[] { "invoice.html", "report.html", "large-report.html" }.Select(f => Path.Combine(HtmlDir, f)).ToArray();

        long t0 = Stopwatch.GetTimestamp();
        await using IEngine engine = await Engines.Create(name);
        await engine.Convert(docs[0]);
        double coldMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);
        var medians = new double[docs.Length];
        var pages = new int[docs.Length];
        var sizes = new double[docs.Length];
        // Warm up on every document (JIT tiering, caches, browser processes) before anything is timed.
        var warm = Stopwatch.StartNew();
        while (warm.Elapsed.TotalSeconds < WarmupSeconds) foreach (var doc in docs) await engine.Convert(doc);

        for (int d = 0; d < docs.Length; d++)
        {
            // Time runs for up to 5 seconds (at least 5, at most 200).
            var pdf = await engine.Convert(docs[d]);
            var times = new List<double>();
            var budget = Stopwatch.StartNew();
            while (times.Count < 5 || (times.Count < 200 && budget.Elapsed.TotalSeconds < 5))
            {
                long s = Stopwatch.GetTimestamp();
                await engine.Convert(docs[d]);
                times.Add(Stopwatch.GetElapsedTime(s).TotalMilliseconds);
            }
            times.Sort();
            medians[d] = times[times.Count / 2];
            pages[d] = PdfPecker.PdfToImage.GetPageCount(pdf);
            sizes[d] = pdf.Length / 1024.0;
            File.WriteAllBytes(Path.Combine(outDir, $"{name}-{Path.GetFileNameWithoutExtension(docs[d])}.pdf"), pdf);
        }

        // Throughput with one conversion per core running at once, on the 5-page feature tour.
        int workers = Environment.ProcessorCount, done = 0;
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            while (sw.Elapsed.TotalSeconds < 5) { await engine.Convert(docs[1]); Interlocked.Increment(ref done); }
        })));
        double perSecond = done / sw.Elapsed.TotalSeconds;

        // Peak memory, measured separately because sampling /proc slows the timed runs down: each document again,
        // then one conversion per core at once for two seconds.
        double peakMb;
        using (var memory = new MemorySampler())
        {
            foreach (var doc in docs) await engine.Convert(doc);
            sw.Restart();
            await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
            {
                foreach (var doc in docs) await engine.Convert(doc);
                while (sw.Elapsed.TotalSeconds < 2) await engine.Convert(docs[1]);
            })));
            peakMb = memory.PeakMb;
        }

        return new Result(name, coldMs, medians, pages, sizes, perSecond, peakMb);
    }

    /// <summary>
    /// Runs one engine in a fresh process of the given build of this program. .NET sizes the garbage collector's
    /// allocation budget from the CPU's L3 cache, so the same build peaks at 90 MB on one cloud VM and 190 MB on
    /// another; a fixed 16 MB budget keeps peak memory comparable between machines (unless DOTNET_GCgen0size is
    /// already set). Returns null when the run fails.
    /// </summary>
    public static Result? RunInChild(string assembly, string engine)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true };
        psi.ArgumentList.Add(assembly);
        psi.ArgumentList.Add(engine);
        if (Environment.GetEnvironmentVariable("DOTNET_GCgen0size") == null) psi.Environment["DOTNET_GCgen0size"] = GcBudget;
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(l => l.StartsWith('{'));
        return p.ExitCode == 0 && line != null ? JsonSerializer.Deserialize<Result>(line) : null;
    }

    const string GcBudget = "0x1000000";

    /// <summary>Describes the machine, which the timings depend on, for the line above every table.</summary>
    public static string MachineLine() =>
        $"PdfPecker {PdfPeckerVersion()}, {Environment.ProcessorCount} cores, {CpuName()}, {L3Cache()} L3, .NET {Environment.Version}, " +
        $"GC gen0 budget {Environment.GetEnvironmentVariable("DOTNET_GCgen0size") ?? GcBudget}, {WarmupSeconds:0.#} s warm-up";

    // The informational version names the NuGet version, or the -dev version and commit of a source build.
    static string PdfPeckerVersion() =>
        typeof(PdfPecker.HtmlToPdf).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is [System.Reflection.AssemblyInformationalVersionAttribute v] ? v.InformationalVersion : "unknown";

    public static string CpuName()
    {
        try { return File.ReadLines("/proc/cpuinfo").First(l => l.StartsWith("model name")).Split(':')[1].Trim(); }
        catch { return "unknown CPU"; }
    }

    static string L3Cache()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/devices/system/cpu/cpu0/cache", "index*"))
                if (File.ReadAllText(Path.Combine(dir, "level")).Trim() == "3") return File.ReadAllText(Path.Combine(dir, "size")).Trim();
        }
        catch { }
        return "unknown";
    }
}

/// <summary>
/// Samples the proportional set size (PSS) of this process and every process it started (browser, renderer and GPU
/// processes, wkhtmltopdf). PSS splits shared pages between the processes that map them, so a browser's processes
/// are not counted several times over. Linux only.
/// </summary>
sealed class MemorySampler : IDisposable
{
    readonly Thread _thread;
    volatile bool _stop;
    long _peakKb;

    public MemorySampler()
    {
        _thread = new Thread(() => { while (!_stop) { _peakKb = Math.Max(_peakKb, TreePssKb()); Thread.Sleep(10); } }) { IsBackground = true };
        _thread.Start();
    }

    public double PeakMb => Math.Max(_peakKb, TreePssKb()) / 1024.0;

    public void Dispose() { _stop = true; _thread.Join(); }

    static long TreePssKb()
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid)) continue;
            try
            {
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                int ppid = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1]);
                if (!children.TryGetValue(ppid, out var list)) children[ppid] = list = new();
                list.Add(pid);
            }
            catch { }
        }
        long total = 0;
        var stack = new Stack<int>([Environment.ProcessId]);
        while (stack.Count > 0)
        {
            int pid = stack.Pop();
            try
            {
                foreach (var line in File.ReadLines($"/proc/{pid}/smaps_rollup"))
                    if (line.StartsWith("Pss:")) { total += long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]); break; }
            }
            catch { }
            if (children.TryGetValue(pid, out var kids)) foreach (var k in kids) stack.Push(k);
        }
        return total;
    }
}
