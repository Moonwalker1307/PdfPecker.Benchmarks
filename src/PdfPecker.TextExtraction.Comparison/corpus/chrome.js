// Prints every corpus page with Chromium, untagged and tagged, and writes the expected text: <case>.expected.txt when
// there is one, otherwise the page's innerText, because the order of the DOM is the order a person reads these pages in.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const out = process.argv[2];
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage();
  for (const file of fs.readdirSync(__dirname).filter(f => /^c\d\d.*\.html$/.test(f)).sort()) {
    const name = file.replace(/\.html$/, '');
    await page.goto('file://' + path.join(__dirname, file));
    await page.evaluate(() => document.fonts.ready);
    const expected = path.join(__dirname, name + '.expected.txt');
    fs.writeFileSync(path.join(out, name + '.txt'), fs.existsSync(expected) ? fs.readFileSync(expected, 'utf8') : await page.evaluate(() => document.body.innerText));
    await page.pdf({ path: path.join(out, name + '.chrome.pdf'), preferCSSPageSize: true, printBackground: true });
    await page.pdf({ path: path.join(out, name + '.chrome-tagged.pdf'), preferCSSPageSize: true, printBackground: true, tagged: true });
    console.log(name);
  }
  await browser.close();
})();
