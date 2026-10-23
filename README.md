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
dotnet run -c Release --project src/PdfPecker.Comparison -- --runs 3           # three runs each, medians and ratios
dotnet run -c Release --project src/PdfPecker.Comparison -- pdfpecker          # one converter, its result as JSON
dotnet run -c Release --project src/PdfPecker.Fluent.Comparison -- 200 12 out  # 200 runs, 12 s warm-up, PDFs in ./out
```

The Chromium-based converters use the browser in `CHROMIUM_PATH` (by default `/opt/pw-browsers/chromium`), and
`wkhtmltopdf` must be on the `PATH`. Syncfusion runs as a trial without a licence key. PDFs are written to `out` next
to the program. `--engines pdfpecker,itext` limits the comparison to the converters named, `--json results.json` also
writes every run to a file, and `PDFPECKER_BENCH_WARMUP=30` sets the warm-up to 30 seconds instead of 3 (tiered JIT is
still at work after 3 seconds, so numbers meant for publishing use a longer one). With `--runs`, each converter runs
that many times in a fresh process, one round of every converter after another, and the table shows the medians; a
second table divides each converter's numbers by PdfPecker's.

## Which PdfPecker

PdfPecker comes from nuget.org, in the version set in `Directory.Build.props`. Another release, or a checkout of the
PdfPecker repository (to measure unreleased code), is chosen on the command line:

```
dotnet run -c Release --project src/PdfPecker.Comparison -p:PdfPeckerVersion=1.17.0
dotnet run -c Release --project src/PdfPecker.Comparison -p:PdfPeckerSource=../PdfPecker
```

PdfPecker.Fluent is published with every PdfPecker release from 1.19.0 on, with the same version, so the fluent
comparison takes the same `PdfPeckerVersion`.

## Measuring fairly

Cloud machines differ so much in CPU speed and cache size that a number from one machine says little about another,
so compare converters within one run on one machine, and say which machine it was: the first line of the output names
the CPU, its L3 cache, the .NET version and the PdfPecker version. .NET sizes its garbage collector's allocation budget
from the L3 cache, so every converter runs with a fixed 16 MB gen0 budget (`DOTNET_GCgen0size=0x1000000`) unless
`DOTNET_GCgen0size` is already set. Each converter is set up the way its own documentation suggests for converting
many documents; the comments in `Engines.cs` say how.

## On GitHub Actions

The [Benchmark](.github/workflows/benchmark.yml) workflow runs both comparisons and puts the tables, the CPU, its
caches, the memory and the versions of .NET, wkhtmltopdf and Chromium in the run's summary; the results, every run as
JSON and the PDFs are kept as an artifact for 90 days. Start it under Actions → Benchmark → Run workflow, with a
PdfPecker version (empty for the newest on nuget.org, `main` for unreleased code), the number of runs (3), the
warm-up (30 seconds) and the machine. Once a day it also checks nuget.org and measures a new PdfPecker release once.
It never runs on pull requests. GitHub turns off scheduled workflows in a repository without activity for 60 days;
Actions shows a button to turn it on again.

GitHub's hosted runners are drawn from a pool of different machines, and a run can't choose one, so two runs may well
differ by 20% or more on the same code. Compare times only between runs whose summary names the same CPU, and lean on
the ratios to PdfPecker, which move much less from machine to machine.

### The same machine every run

Only a self-hosted runner gives the same hardware every time: a small dedicated server (not a shared cloud VM, whose
neighbours change the timings too) registered with this repository. Because the repository is public, set it up so
that nobody else's code can reach it:

- Under Settings → Actions → General, require approval for workflow runs from **all outside collaborators**. A pull
  request from a fork runs the workflows as the fork changed them, so one could otherwise ask for the self-hosted
  machine. Before approving such a run, read its changes to `.github/workflows`.
- Give the runner the label `pdfpecker-bench`, run it as an unprivileged user without `sudo`, and keep nothing else
  on the machine: no credentials, SSH keys or other services. A dedicated VM or container that is rebuilt now and
  then (or an `--ephemeral` runner) is safest.
- Install the .NET 10 SDK, PowerShell (`pwsh`), `wkhtmltopdf`, `fonts-liberation` and Chromium's libraries
  (`playwright.ps1 install-deps chromium` once, as root); the workflow then installs nothing with `sudo` there.
- Choose `pdfpecker-bench` when starting the workflow, and set the repository variable `BENCHMARK_RUNNER` to
  `pdfpecker-bench` (Settings → Secrets and variables → Actions → Variables) for the daily release check to use it.
  The workflow runs one benchmark at a time, so two runs never share the machine.
- For steady numbers, turn off CPU frequency scaling (`cpupower frequency-set -g performance`) and leave the machine
  idle otherwise.

## License

MIT. The converters measured here come from their own packages, under their own licences; iText pdfHTML is AGPL or
commercial, and Syncfusion is commercial.
