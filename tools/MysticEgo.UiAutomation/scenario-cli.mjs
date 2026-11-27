import { readFile } from 'node:fs/promises';
import { createInterface } from 'node:readline/promises';
import { ScenarioLibrary } from './scenario-library.mjs';
import { scenarioSchema } from './scenario.mjs';

const config = JSON.parse(await readFile(process.env.MYSTICEGO_UI_CONFIG,'utf8'));
const library = new ScenarioLibrary(config.scenarioDirectory);
const [command, arg, title] = process.argv.slice(2);
if (command === 'list') console.log(JSON.stringify(await library.list(),null,2));
else if (command === 'show') console.log(JSON.stringify(await library.load(arg),null,2));
else if (command === 'save' || command === 'remove') {
  if (!process.stdin.isTTY || !process.stdout.isTTY) throw new Error('Local interactive approval required');
  const scenario = command === 'save' ? scenarioSchema.parse(JSON.parse(await readFile(arg,'utf8'))) : await library.load(arg);
  console.log(JSON.stringify(scenario,null,2));
  const prompt = createInterface({input:process.stdin,output:process.stdout});
  let answer;
  try { answer = await prompt.question(command === 'save' ? 'Save reviewed scenario? Type SAVE: ' : 'Delete this scenario? Type DELETE: '); }
  finally { prompt.close(); }
  if (command === 'save' && answer === 'SAVE') console.log(await library.save(title,scenario));
  else if (command === 'remove' && answer === 'DELETE') await library.remove(arg);
  else throw new Error('Cancelled');
} else throw new Error('Usage: node scenario-cli.mjs list | show ID | save FILE TITLE | remove ID');
