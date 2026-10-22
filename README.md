# PdfPecker.Benchmarks

Benchmarks comparing [PdfPecker](https://github.com/Moonwalker1307/PdfPecker) with other .NET HTML-to-PDF converters
(Chromium through PuppeteerSharp and Playwright, Syncfusion, wkhtmltopdf, iText pdfHTML and HtmlRenderer.PdfSharp), and
PdfPecker.Fluent with QuestPDF: speed, memory and output size on the same documents and machine.

They live apart from the library so that its repository, its forks and its CI never pull in a browser or a third-party
package. The results are in PdfPecker's [README](https://github.com/Moonwalker1307/PdfPecker#how-it-compares) and on
[pdfpecker.net](https://pdfpecker.net/docs/performance/). Comparing two versions of PdfPecker with each other is done
in the library repository, with `bench/compare-versions.sh`.

## Projects

- `src/PdfPecker.Comparison` converts an invoice (1 page), a feature tour (5 pages) and a generated report (30 pages)
  with every converter, each in a fresh process, and prints a Markdown table: the first PDF, medians after a warm-up,
  parallel throughput with one conversion per core, peak memory and the pages and size of each PDF.
- `src/PdfPecker.Fluent.Comparison` builds the same invoice with PdfPecker.Fluent and with QuestPDF, call for call, and
  times both in one process after a long warm-up. Its `Examples.cs` and `assets` are copies of
  `samples/PdfPecker.Fluent.Sample` in the PdfPecker repository, and `src/PdfPecker.Comparison/html` holds the invoice
  and report of `samples/PdfPecker.Sample`.

## Running

Both need the .NET 10 SDK and run on Linux (peak memory is read from `/proc`).

```
dotnet run -c Release --project src/PdfPecker.Comparison                       # every converter, a Markdown table
dotnet run -c Release --project src/PdfPecker.Comparison -- pdfpecker          # one converter, its result as JSON
dotnet run -c Release --project src/PdfPecker.Fluent.Comparison -- 200 12 out  # 200 runs, 12 s warm-up, PDFs in ./out
```

The Chromium-based converters use the browser in `CHROMIUM_PATH` (by default `/opt/pw-browsers/chromium`), and
`wkhtmltopdf` must be on the `PATH`. Syncfusion runs as a trial without a licence key. PDFs are written to `out` next
to the program.

## Which PdfPecker

PdfPecker comes from nuget.org, in the version set in `Directory.Build.props`. Another release, or a checkout of the
PdfPecker repository (to measure unreleased code), is chosen on the command line:

```
dotnet run -c Release --project src/PdfPecker.Comparison -p:PdfPeckerVersion=1.17.0
dotnet run -c Release --project src/PdfPecker.Comparison -p:PdfPeckerSource=../PdfPecker
```

PdfPecker.Fluent is published from the next PdfPecker release on; until then the fluent comparison needs
`-p:PdfPeckerSource`.

## Measuring fairly

Cloud machines differ so much in CPU speed and cache size that a number from one machine says little about another,
so compare converters within one run on one machine, and say which machine it was: the first line of the output names
the CPU, its L3 cache, the .NET version and the PdfPecker version. .NET sizes its garbage collector's allocation budget
from the L3 cache, so every converter runs with a fixed 16 MB gen0 budget (`DOTNET_GCgen0size=0x1000000`) unless
`DOTNET_GCgen0size` is already set. Each converter is set up the way its own documentation suggests for converting
many documents; the comments in `Engines.cs` say how.

## License

MIT. The converters measured here come from their own packages, under their own licences; iText pdfHTML is AGPL or
commercial, and Syncfusion is commercial.
