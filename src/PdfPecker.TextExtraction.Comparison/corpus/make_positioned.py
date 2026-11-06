# Writes the corpus pages whose drawing order is not their reading order, as PDFs from report designers and
# typesetters often are: every line is positioned on its own, and the HTML (so the PDF's content stream) lists them
# in another order. The expected text is written next to them as <case>.expected.txt, in the order a person reads.
import random, textwrap

random.seed(14)
left = """The first trams will run in the spring of 2029, if the contractors keep to the schedule that was agreed last month. The route follows the old railway embankment for most of its length, which keeps the cost of buying land low and avoids the narrow streets of the old town. Residents along the embankment have asked for noise barriers and for the trees that line the path to be kept. The planning office says that most of the trees can stay."""
right = """The national government pays forty percent of the construction cost under its programme for public transport in medium sized towns. The rest comes from the municipality and the regional health authority, which wants a faster connection for patients and staff. Ticket prices will match the bus fares, and a single ticket will be valid on both for ninety minutes."""
L = textwrap.wrap(left, 44)
R = textwrap.wrap(right, 44)
head = '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>{t}</title><link rel="stylesheet" href="base.css"><style>.l {{ position: absolute; white-space: nowrap; }}</style></head><body>'
items = []
# Drawn row by row across both columns, as a typesetter that fills lines across the page does, with the footer first
# and the heading last.
items.append((270, 0, "Page 1 of 1", "font-size:9pt"))
for i in range(max(len(L), len(R))):
    if i < len(L): items.append((22 + i * 5.2, 0, L[i], ""))
    if i < len(R): items.append((22 + i * 5.2, 92, R[i], ""))
items.append((0, 0, "Harbour town plans a new tram line", "font: bold 20pt 'Liberation Sans'"))
body = "".join(f'<div class="l" style="top:{y}mm;left:{x}mm;{s}">{t}</div>' for y, x, t, s in items)
open("c13-columns-drawn-across.html", "w").write(head.format(t="Columns drawn across") + '<div style="position:relative;height:275mm">' + body + "</div></body></html>")
open("c13-columns-drawn-across.expected.txt", "w").write("Harbour town plans a new tram line\n" + "\n".join(L) + "\n" + "\n".join(R) + "\nPage 1 of 1\n")

# A delivery note from a report designer: every field is a box of its own, written in a shuffled order.
rows = [
    [("Delivery note", "font: bold 18pt 'Liberation Sans'"), ("No. DN-58213", "font: bold 12pt 'Liberation Sans'")],
    [("Ship to:", "font-weight:bold"), ("Example Customer Ltd.", ""), ("Order:", "font-weight:bold"), ("PO-7781", "")],
    [("Street:", "font-weight:bold"), ("8 Harbour Road", ""), ("Date:", "font-weight:bold"), ("4 November 2026", "")],
    [("City:", "font-weight:bold"), ("Leith EH6 6QF", ""), ("Carrier:", "font-weight:bold"), ("Northern Freight", "")],
    [("Article", "font-weight:bold"), ("Description", "font-weight:bold"), ("Quantity", "font-weight:bold"), ("Weight", "font-weight:bold")],
    [("A-100", ""), ("Office chair", ""), ("4", ""), ("52 kg", "")],
    [("A-215", ""), ("Standing desk", ""), ("2", ""), ("96 kg", "")],
    [("B-007", ""), ("Monitor arm", ""), ("6", ""), ("18 kg", "")],
    [("Received by:", "font-weight:bold"), ("________________", ""), ("Signature:", "font-weight:bold"), ("________________", "")],
]
xs = [0, 35, 95, 130]
boxes = []
for r, row in enumerate(rows):
    y = r * 9 + (6 if r >= 4 else 0) + (8 if r == 8 else 0)
    for c, (t, s) in enumerate(row):
        x = [0, 120][c] if r == 0 else xs[c]
        boxes.append((y, x, t, s))
shuffled = boxes[:]
random.shuffle(shuffled)
body = "".join(f'<div class="l" style="top:{y}mm;left:{x}mm;font-family:\'Liberation Sans\';{s}">{t}</div>' for y, x, t, s in shuffled)
open("c14-report-designer.html", "w").write(head.format(t="Delivery note") + '<div style="position:relative;height:120mm">' + body + "</div></body></html>")
open("c14-report-designer.expected.txt", "w").write("\n".join(" ".join(t for t, _ in row) for row in rows) + "\n")
