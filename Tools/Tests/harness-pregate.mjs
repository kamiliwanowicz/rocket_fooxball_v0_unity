import { spawn } from 'node:child_process';

const hookMode = 'PreToolUse';

function hookValue(object, name) {
  return object && typeof object === 'object' ? object[name] : undefined;
}

function hookStrings(object) {
  return ['command', 'cmd', 'script'].flatMap((name) => {
    const value = hookValue(object, name);
    return Array.isArray(value) ? value : [value];
  }).filter((value) => typeof value === 'string' && value.trim().length > 0);
}

function isTarget(event) {
  if (hookValue(event, 'tool_name') !== 'Bash' && hookValue(event, 'tool_name') !== 'PowerShell') return false;
  const input = hookValue(event, 'tool_input') ?? event;
  return [...hookStrings(input), ...hookStrings(event)].some((command) =>
    /Invoke-MovementLabWorkflow\.ps1/i.test(command)
    || /(?:^|[\s"'/\\])Unity(?:\.exe)?(?:$|[\s"'/\\])/i.test(command));
}

let eventText;
try {
  eventText = await new Promise((resolve, reject) => {
    let text = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (chunk) => { text += chunk; });
    process.stdin.on('end', () => resolve(text));
    process.stdin.on('error', reject);
  });
  if (!eventText.trim()) throw new Error(`${hookMode} hook event JSON missing on stdin.`);
  const event = JSON.parse(eventText.trimStart());
  if (!isTarget(event)) {
    console.error(`HOOK ${hookMode} SKIP unrelated event`);
    process.exit(0);
  }
} catch (error) {
  console.error(`HOOK ${hookMode} ERROR: ${error.message}`);
  process.exit(2);
}

const harnessArguments = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'Tools/Tests/Invoke-HarnessTests.ps1', ...'-HookMode PreToolUse'.split(' ')];
const child = spawn('powershell.exe', harnessArguments, {
  stdio: ['pipe', 'inherit', 'inherit']
});
child.stdin.end(eventText);
child.on('error', (error) => {
  console.error(`HOOK ${hookMode} ERROR: ${error.message}`);
  process.exit(2);
});
child.on('close', (code) => process.exit(code ?? 2));
