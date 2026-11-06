// Compares PdfPecker's text extraction (PdfText) with PdfPig's.
//
//   dotnet run -c Release --project src/PdfPecker.TextExtraction.Comparison -p:PdfPeckerSource=../PdfPecker -- <command>
//
//   corpus <corpus folder> <folder>    converts the corpus pages with PdfPecker into <folder>, next to the Chromium PDFs
//                                      and expected texts that corpus/chrome.js writes there, and rotates c01 into c07
//   accuracy <folder> [<result.json>]  extracts every PDF in <folder> that has an expected text (<case>.txt for
//                                      <case>.<producer>.pdf) with every engine, scores words and reading order, and
//                                      writes the texts and the line boxes in output order to <folder>/extracted
//   extract <out folder> <pdf>...      writes each engine's text (and boxes) of PDFs that have no expected text
//   speed <pdf>...                     times every engine on each PDF in this process and counts the bytes it allocates
//
// The scores compare words, not characters: a word is a run of letters between spaces, and every CJK character is a
// word of its own. "Words" is the F1 score of the words found (any order); "Order" is one minus the edit distance
// between the word sequences divided by the longer one, so it is 1 only when every word is there in the right order.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PdfPecker;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

switch (args.FirstOrDefault())
{
    case "corpus": Corpus(args[1], args[2]); break;
    case "accuracy": Accuracy(args[1], args.Length > 2 ? args[2] : Path.Combine(args[1], "accuracy.json")); break;
    case "extract": ExtractOnly(args[1], args[2..]); break;
    case "speed": Speed(args[1..]); break;
    default: Console.Error.WriteLine("Commands: corpus, accuracy, extract, speed (see Program.cs)"); return 2;
}
return 0;

static void Corpus(string corpus, string folder)
{
    Directory.CreateDirectory(folder);
    foreach (var html in Directory.GetFiles(corpus, "c*.html").Order(StringComparer.Ordinal))
    {
        var name = Path.GetFileNameWithoutExtension(html);
        HtmlToPdf.ConvertFile(html, Path.Combine(folder, name + ".pdfpecker.pdf"), new PdfOptions { CreationDate = new DateTime(2026, 1, 1) });
        Console.WriteLine(name);
    }
    // The same two-column page with /Rotate 90: the text is drawn upright on the page and turned when it is shown.
    foreach (var producer in new[] { "chrome", "chrome-tagged", "pdfpecker" })
    {
        var source = Path.Combine(folder, "c01-two-columns." + producer + ".pdf");
        if (File.Exists(source))
            File.WriteAllBytes(Path.Combine(folder, "c07-rotated-page." + producer + ".pdf"), PdfPages.Rotate(File.ReadAllBytes(source), 90));
    }
    var truth = Path.Combine(folder, "c01-two-columns.txt");
    if (File.Exists(truth))
        File.Copy(truth, Path.Combine(folder, "c07-rotated-page.txt"), true);
}

static void Accuracy(string folder, string resultPath)
{
    var outDir = Path.Combine(folder, "extracted");
    Directory.CreateDirectory(outDir);
    var results = new List<object>();
    Console.WriteLine($"| PDF | {string.Join(" | ", Engines.All.Select(e => e.Name))} |");
    Console.WriteLine($"| --- |{string.Concat(Engines.All.Select(_ => " --- |"))}");
    foreach (var pdf in Directory.GetFiles(folder, "*.pdf").Order(StringComparer.Ordinal))
    {
        var file = Path.GetFileName(pdf);
        var caseName = file[..file.IndexOf('.')];
        var truthPath = Path.Combine(folder, caseName + ".txt");
        if (!File.Exists(truthPath))
            continue;
        var truth = Tokens.Of(File.ReadAllText(truthPath));
        var bytes = File.ReadAllBytes(pdf);
        var cells = new List<string>();
        foreach (var engine in Engines.All)
        {
            var (text, boxes) = Run(engine, bytes);
            File.WriteAllText(Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(pdf)}.{engine.Name}.txt"), text);
            File.WriteAllText(Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(pdf)}.{engine.Name}.boxes.json"), JsonSerializer.Serialize(boxes));
            var found = Tokens.Of(text);
            var words = Tokens.F1(truth, found);
            var wordsNfkc = Tokens.F1(Tokens.Of(File.ReadAllText(truthPath), true), Tokens.Of(text, true));
            var order = Tokens.OrderScore(truth, found);
            cells.Add($"{words:0.00} / {order:0.00}");
            results.Add(new { pdf = file, @case = caseName, producer = file[(caseName.Length + 1)..^4], engine = engine.Name, words, wordsNfkc, order, expected = truth.Count, extracted = found.Count });
        }
        Console.WriteLine($"| {file} | {string.Join(" | ", cells)} |");
    }
    File.WriteAllText(resultPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}

static void ExtractOnly(string outDir, string[] pdfs)
{
    Directory.CreateDirectory(outDir);
    foreach (var pdf in pdfs)
    {
        var bytes = File.ReadAllBytes(pdf);
        foreach (var engine in Engines.All)
        {
            string text;
            List<Box> boxes;
            try { (text, boxes) = Run(engine, bytes); }
            catch (Exception e) { text = "FAILED: " + e.GetType().Name + ": " + e.Message; boxes = []; }
            var stem = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(pdf)}.{engine.Name}");
            File.WriteAllText(stem + ".txt", text);
            File.WriteAllText(stem + ".boxes.json", JsonSerializer.Serialize(boxes));
        }
        Console.WriteLine(Path.GetFileName(pdf));
    }
}

static (string Text, List<Box> Boxes) Run(Engine engine, byte[] pdf)
{
    var boxes = new List<Box>();
    var text = engine.Extract(pdf, boxes);
    return (text, boxes);
}

static void Speed(string[] pdfs)
{
    Console.WriteLine($"{Environment.ProcessorCount} cores, .NET {Environment.Version}, PdfPecker {typeof(PdfText).Assembly.GetName().Version}, PdfPig {typeof(PdfDocument).Assembly.GetName().Version}");
    Console.WriteLine("| PDF | pages | engine | median ms | MB allocated |");
    Console.WriteLine("| --- | ---: | --- | ---: | ---: |");
    var rows = new List<object>();
    foreach (var pdf in pdfs)
    {
        var bytes = File.ReadAllBytes(pdf);
        var pages = PdfPages.Count(bytes);
        foreach (var engine in Engines.Timed)
        {
            try
            {
                // Warm up for two seconds, then time runs for up to five seconds (at least five runs).
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < 2)
                    engine.Extract(bytes, null);
                GC.Collect();
                var times = new List<double>();
                var allocated = GC.GetTotalAllocatedBytes(true);
                watch.Restart();
                while (times.Count < 5 || (watch.Elapsed.TotalSeconds < 5 && times.Count < 2000))
                {
                    var start = Stopwatch.GetTimestamp();
                    engine.Extract(bytes, null);
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                var perRun = (GC.GetTotalAllocatedBytes(true) - allocated) / (double)times.Count / 1e6;
                times.Sort();
                var median = times[times.Count / 2];
                Console.WriteLine($"| {Path.GetFileName(pdf)} | {pages} | {engine.Name} | {median:0.00} | {perRun:0.0} |");
                rows.Add(new { pdf = Path.GetFileName(pdf), pages, engine = engine.Name, medianMs = median, mbAllocated = perRun, runs = times.Count });
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {Path.GetFileName(pdf)} | {pages} | {engine.Name} | failed: {e.GetType().Name} | |");
            }
        }
    }
    File.WriteAllText("speed.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>A line or run of letters in output order, in points from the top left of the page as displayed.</summary>
sealed record Box(int Page, double X, double Y, double W, double H, double PageWidth, double PageHeight);

sealed record Engine(string Name, Func<byte[], List<Box>?, string> Extract);

static class Engines
{
    static readonly PdfTextOptions Reading = new() { MaxDegreeOfParallelism = 1 };
    static readonly PdfTextOptions TopToBottom = new() { MaxDegreeOfParallelism = 1, Order = PdfTextOrder.TopToBottom };
    static readonly PdfTextOptions Parallel = new();

    public static readonly Engine[] All =
    [
        new("pdfpecker", (pdf, boxes) => PdfPecker(pdf, Reading, boxes)),
        new("pdfpecker-ttb", (pdf, boxes) => PdfPecker(pdf, TopToBottom, boxes)),
        new("pdfpig-text", (pdf, boxes) => PdfPig(pdf, PigMode.PageText, boxes)),
        new("pdfpig-content", (pdf, boxes) => PdfPig(pdf, PigMode.ContentOrder, boxes)),
        new("pdfpig-layout", (pdf, boxes) => PdfPig(pdf, PigMode.Layout, boxes)),
    ];

    public static readonly Engine[] Timed =
    [
        All[0],
        new("pdfpecker-parallel", (pdf, boxes) => PdfPecker(pdf, Parallel, boxes)),
        All[1], All[2], All[3], All[4],
    ];

    static string PdfPecker(byte[] pdf, PdfTextOptions options, List<Box>? boxes)
    {
        var pages = PdfText.ExtractPages(pdf, options);
        if (boxes != null)
        {
            foreach (var page in pages)
                foreach (var line in page.Lines)
                    boxes.Add(new Box(page.PageNumber, line.X, line.Y, line.Width, line.Height, page.Width, page.Height));
        }
        return string.Join("\n\f", pages.Select(p => p.Text));
    }

    enum PigMode { PageText, ContentOrder, Layout }

    static string PdfPig(byte[] pdf, PigMode mode, List<Box>? boxes)
    {
        using var document = PdfDocument.Open(pdf);
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (text.Length > 0)
                text.Append("\n\f");
            switch (mode)
            {
                case PigMode.PageText:
                    text.Append(page.Text);
                    if (boxes != null)
                        AddLetterRuns(page, boxes);
                    break;
                case PigMode.ContentOrder:
                    text.Append(ContentOrderTextExtractor.GetText(page));
                    if (boxes != null)
                        AddLetterRuns(page, boxes);
                    break;
                default:
                    // The layout analysis PdfPig's documentation describes: words by nearest neighbours, blocks by
                    // Docstrum, then the blocks in the order of the unsupervised reading order detector.
                    var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
                    var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                    foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(blocks))
                    {
                        foreach (var line in block.TextLines)
                        {
                            text.Append(line.Text).Append('\n');
                            boxes?.Add(ToBox(page, line.BoundingBox));
                        }
                        text.Append('\n');
                    }
                    break;
            }
        }
        return text.ToString();
    }

    // PdfPig places letters on the page as displayed, with /Rotate applied and the origin at the bottom left, and a
    // rectangle of rotated text has its corners turned with it.
    static Box ToBox(Page page, UglyToad.PdfPig.Core.PdfRectangle r)
    {
        var turned = page.Rotation.Value % 180 != 0;
        var bounds = page.CropBox.Bounds;
        var (left, bottom) = turned ? (bounds.Bottom, bounds.Left) : (bounds.Left, bounds.Bottom);
        var (width, height) = turned ? (bounds.Height, bounds.Width) : (bounds.Width, bounds.Height);
        double[] xs = [r.TopLeft.X, r.TopRight.X, r.BottomLeft.X, r.BottomRight.X];
        double[] ys = [r.TopLeft.Y, r.TopRight.Y, r.BottomLeft.Y, r.BottomRight.Y];
        return new Box(page.Number, xs.Min() - left, bottom + height - ys.Max(), xs.Max() - xs.Min(), ys.Max() - ys.Min(), width, height);
    }

    // PdfPig's page text and content order follow the order the PDF draws its letters; a run ends where the next letter
    // is not to the right of the last one on the same baseline, which shows the order of the runs on the page.
    static void AddLetterRuns(Page page, List<Box> boxes)
    {
        double left = 0, bottom = 0, right = 0, top = 0, baseline = 0, lastRight = 0;
        var open = false;
        foreach (var letter in page.Letters)
        {
            var r = letter.GlyphRectangle;
            if (string.IsNullOrWhiteSpace(letter.Value) || r.Width <= 0)
                continue;
            var size = Math.Max(1, letter.PointSize);
            if (open && (Math.Abs(letter.StartBaseLine.Y - baseline) > size * 0.5 || r.Left < lastRight - size * 0.5 || r.Left > lastRight + size * 3))
            {
                boxes.Add(ToBox(page, new UglyToad.PdfPig.Core.PdfRectangle(left, bottom, right, top)));
                open = false;
            }
            if (!open)
            {
                (left, bottom, right, top, baseline, open) = (r.Left, r.Bottom, r.Right, r.Top, letter.StartBaseLine.Y, true);
            }
            else
            {
                (left, bottom, right, top) = (Math.Min(left, r.Left), Math.Min(bottom, r.Bottom), Math.Max(right, r.Right), Math.Max(top, r.Top));
            }
            lastRight = r.Right;
        }
        if (open)
            boxes.Add(ToBox(page, new UglyToad.PdfPig.Core.PdfRectangle(left, bottom, right, top)));
    }
}

static class Tokens
{
    public static List<string> Of(string text, bool compatibility = false)
    {
        text = text.Normalize(compatibility ? NormalizationForm.FormKC : NormalizationForm.FormC).Replace("­", "", StringComparison.Ordinal);
        var tokens = new List<string>();
        var current = new StringBuilder();
        void Flush()
        {
            // Punctuation at either end of a word is left out, so a comma that lands on the other side of an Arabic word
            // or a quote mark does not count against the word itself.
            var word = current.ToString().Trim(['.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '“', '”', '‘', '’', '،', '。', '，', '、', '：', '；', '「', '」', '״', '׳']);
            if (word.Length > 0)
                tokens.Add(word);
            current.Clear();
        }
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                Flush();
            }
            else if (IsCjk(rune.Value))
            {
                Flush();
                current.Append(rune.ToString());
                Flush();
            }
            else
            {
                current.Append(rune.ToString());
            }
        }
        Flush();
        return tokens;
    }

    static bool IsCjk(int c) =>
        c is (>= 0x3040 and <= 0x30FF) or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or (>= 0xF900 and <= 0xFAFF) or (>= 0xFF00 and <= 0xFFEF) or (>= 0x3000 and <= 0x303F);

    public static double F1(List<string> expected, List<string> found)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in expected)
            counts[t] = counts.GetValueOrDefault(t) + 1;
        var matched = 0;
        foreach (var t in found)
        {
            if (counts.GetValueOrDefault(t) > 0)
            {
                counts[t]--;
                matched++;
            }
        }
        if (matched == 0)
            return 0;
        var precision = matched / (double)found.Count;
        var recall = matched / (double)expected.Count;
        return 2 * precision * recall / (precision + recall);
    }

    public static double OrderScore(List<string> expected, List<string> found)
    {
        var n = expected.Count;
        var m = found.Count;
        if (Math.Max(n, m) == 0)
            return 1;
        var previous = new int[m + 1];
        var row = new int[m + 1];
        for (var j = 0; j <= m; j++)
            previous[j] = j;
        for (var i = 1; i <= n; i++)
        {
            row[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = string.Equals(expected[i - 1], found[j - 1], StringComparison.Ordinal) ? 0 : 1;
                row[j] = Math.Min(Math.Min(row[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, row) = (row, previous);
        }
        return 1 - previous[m] / (double)Math.Max(n, m);
    }
}
