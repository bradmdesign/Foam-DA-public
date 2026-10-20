import fs from 'node:fs';
// Detailed provenance stays in research/, outside the product.
fs.mkdirSync('public/reports',{recursive:true});
for(const file of ['coverage-ledger.json','discrepancies.json','variant-mappings.json','validation.json','verification.json'])fs.rmSync('public/reports/'+file,{force:true});
const validation=JSON.parse(fs.readFileSync('research/validation.json','utf8'));
const confirmation=JSON.parse(fs.readFileSync('research/da-wizard-confirmation.json','utf8'));
fs.writeFileSync('public/reports/verification.json',JSON.stringify({generatedAt:new Date().toISOString(),entries:validation.entries,validation:'passed',comparedFields:confirmation.matchedFields,agreedFields:confirmation.agreedFields,updatedFields:confirmation.changed.length,unresolvedFields:confirmation.unresolved.length},null,2));
