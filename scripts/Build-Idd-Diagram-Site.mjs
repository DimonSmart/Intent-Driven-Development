import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const archifyArgIndex = process.argv.indexOf('--archify');
const archifyPath = path.resolve(root, archifyArgIndex < 0 ? '.archify/archify/bin/archify.mjs' : process.argv[archifyArgIndex + 1]);
const revision = process.env.GITHUB_SHA || execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim();
const names = ['overview', 'routing', 'intent', 'factory', 'quality'];
if (!/^[a-f0-9]{40}$/i.test(revision)) throw new Error('A full Git commit SHA is required');
if (!existsSync(archifyPath)) throw new Error('Missing pinned Archify CLI: ' + archifyPath);

const site = path.join(root, 'site');
const workspace = mkdtempSync(path.join(os.tmpdir(), 'idd-diagrams-'));
mkdirSync(path.join(site, 'views'), { recursive: true });
try {
  const failures = [];
  for (const name of names) {
    const sourcePath = path.join(root, 'docs', 'diagram', 'idd-' + name + '.workflow.json');
    const diagram = JSON.parse(readFileSync(sourcePath, 'utf8'));
    if (diagram.meta.output !== 'site/views/' + name + '.html') throw new Error('Unexpected diagram output: ' + name);
    // Archify workflow schema limits horizontal layout to columns 0..5.
    for (const node of diagram.nodes) {
      if (!Number.isInteger(node.col) || node.col < 0 || node.col > 5) {
        throw new Error('Invalid Archify column in ' + name + ': ' + node.id + ' (' + node.col + '); allowed 0..5');
      }
    }
    for (const phase of diagram.phases ?? []) {
      if (![phase.fromCol, phase.toCol].every(n => Number.isInteger(n) && n >= 0 && n <= 5) || phase.fromCol > phase.toCol) {
        throw new Error('Invalid Archify phase column range in ' + name + ': ' + phase.id);
      }
    }
    const nodes = new Set(diagram.nodes.map(node => node.id));
    if (nodes.size !== diagram.nodes.length) throw new Error('Duplicate diagram nodes: ' + name);
    const placements = new Set();
    for (const node of diagram.nodes) {
      const place = node.lane + '|' + node.col + '|' + (node.yOffset ?? 0);
      if (placements.has(place)) throw new Error('Duplicate diagram placement in ' + name + ': ' + place);
      placements.add(place);
    }
    const connected = new Set();
    for (const edge of diagram.edges) {
      if (!nodes.has(edge.from) || !nodes.has(edge.to)) throw new Error('Dangling diagram edge: ' + name + ': ' + edge.id);
      connected.add(edge.from);
      connected.add(edge.to);
    }
    for (const id of nodes) if (!connected.has(id)) throw new Error('Isolated diagram node: ' + name + ': ' + id);
    diagram.meta.repository.revision = revision;
    const stampedInput = path.join(workspace, name + '.workflow.json');
    writeFileSync(stampedInput, JSON.stringify(diagram, null, 2) + '\n');
    try {
      execFileSync(process.execPath, [
      archifyPath, 'finalize', 'workflow', stampedInput,
      path.join(site, 'views', name + '.html'),
      '--repo-root', root, '--quality', 'standard', '--json'
      ], { cwd: root, stdio: 'inherit' });
    } catch {
      failures.push(name);
    }
  }
  if (failures.length) throw new Error('Archify validation failed for: ' + failures.join(', '));
  copyFileSync(path.join(root, 'docs/diagram/site-shell.html'), path.join(site, 'index.html'));
  for (const name of names) if (!existsSync(path.join(site, 'views', name + '.html'))) throw new Error('Missing generated page: ' + name);
  console.log('Generated five verified interactive diagrams and site/index.html at ' + revision);
} finally {
  rmSync(workspace, { recursive: true, force: true });
}
