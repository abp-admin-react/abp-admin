import fs from 'node:fs';
import path from 'node:path';

const cwd = process.cwd();
const dist = fs.existsSync(path.join(cwd, 'dist'))
  ? path.join(cwd, 'dist')
  : path.join(cwd, 'web', 'dist');

if (!fs.existsSync(dist)) {
  console.error(`Production dist not found: ${dist}`);
  process.exit(1);
}

const needles = [
  'customer-errors-demo',
  'CustomerErrorsDemo',
  'pages/framework/customer-errors-demo',
];

function walk(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, acc);
    } else {
      acc.push(full);
    }
  }
  return acc;
}

const files = walk(dist).filter((file) =>
  /\.(js|json|txt|html|css|map)$/i.test(file),
);
const hits = [];
for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  for (const needle of needles) {
    if (text.includes(needle)) {
      hits.push(`${path.relative(dist, file)}: ${needle}`);
    }
  }
}

if (hits.length > 0) {
  console.error('Production build contains client-error demo artifacts:');
  for (const hit of hits) {
    console.error(`  ${hit}`);
  }
  process.exit(1);
}

console.log(`Checked ${files.length} production files under ${dist}; demo markers absent.`);
