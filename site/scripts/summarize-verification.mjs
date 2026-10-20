import fs from 'node:fs';
const read=p=>JSON.parse(fs.readFileSync('research/'+p,'utf8'));
const performance=read('performance.json'),audit=read('export-audit.json'),validation=read('validation.json'),browser=read('browser-checks.json'),contrast=read('contrast.json');
const rows=performance.results.map(r=>`| ${r.name} | ${r.performance} | ${r.accessibility} | ${(r.lcpMs/1000).toFixed(2)} s | ${r.cls.toFixed(4)} | ${(r.scriptTransferBytes/1000).toFixed(1)} KB |`);
const text=`# Local verification

Generated ${new Date().toISOString()}. The local server is http://127.0.0.1:4173/.

## Production measurements

${performance.conditions.tool}; ${performance.conditions.profile}. One fresh-context run per route. These are lab results, not device-independent guarantees.

| Route | Performance | Accessibility | LCP | CLS | JavaScript transfer |
| --- | --- | --- | --- | --- | --- |
${rows.join('\n')}

Targets: mobile performance ≥95, LCP ≤1.5 s, CLS ≤0.05, and initial reference JavaScript ≤200 KB compressed. **The simulated mobile LCP target is not met.** Raw reports and failed audit details remain in [performance.json](performance.json) and the lighthouse folder.

${performance.conditions.offlineSimulation||'Source hosts were blocked during measurement.'} The export audit found ${audit.remoteRuntimeAssets.length} remote runtime assets. This is browser-level offline simulation; the operating system network was not disconnected.

## Functional and content checks

- TypeScript checking passed.
- Twelve calculator tests passed, including source examples, rounding, budget thresholds, large integer values, invalid inputs, and calendar boundaries.
- ${audit.htmlPages.toLocaleString('en-US')} exported HTML pages and ${audit.internalLinksAndAssetsChecked.toLocaleString('en-US')} local references checked: ${audit.broken.length} broken references, ${audit.emptyPages.length} empty pages, ${audit.missingTitle.length} missing titles.
- ${validation.entries.toLocaleString('en-US')} records and ${validation.relations.toLocaleString('en-US')} graph relationships validated. Evidence URLs are accounted for in the coverage ledger. Cordovan Boots has one base record with all twelve required modifier aliases; distinct items remain separate.
- Core text contrast: all ${contrast.results.length} tested token pairs pass 4.5:1; lowest ratio ${contrast.minimum}:1.
- Warm search/filter observations: ${JSON.stringify(browser.responseMeasurements)}. Measurements include local query/filter work and the React commit; this is a small functional sample, not a stress benchmark.

See [browser-checks.json](browser-checks.json) for browser observations, scope, and unverified checks. Screenshots are in the screenshots folder.

## Remaining limits

- Content research is incomplete: ${validation.coverage.sourceBlocked.toLocaleString('en-US')} discovered URLs remain deferred after Vorlof returned HTTP 403. Index-only records disclose missing details. Two Wanderlust links returned 404.
- The class-specific ability-level/AP formula is unavailable because its source script was not cached. The ability-time tool instead accepts cumulative AP totals.
- Source extraction and original editorial rewriting do not constitute independent in-game verification of every fact.
- A static export fixture at 200% root text size was checked at a 390 px viewport: body text was 36 px, the header wrapped, and the document did not overflow horizontally. Native browser zoom and custom browser font settings were not separately verified. The temporary fixture was removed after checking.
- Theme selection is applied by an inline script before body rendering. Persistence was checked across navigation and reload; no high-speed first-paint capture was performed.
`;
fs.writeFileSync('research/VERIFICATION.md',text);console.log('Wrote research/VERIFICATION.md');
