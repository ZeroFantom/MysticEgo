import { mkdir, readFile, writeFile, readdir, lstat, unlink } from 'node:fs/promises';
import { isAbsolute, join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { scenarioSchema } from './scenario.mjs';

const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const MAX = 100;
const LIMIT = 65536;
export class ScenarioLibrary {
  constructor(directory) {
    if (!isAbsolute(directory ?? '')) throw new Error('Configure an absolute private scenario directory');
    this.directory = directory;
  }
  async #prepare() {
    await mkdir(this.directory, {recursive:true,mode:0o700});
    const stat = await lstat(this.directory);
    if (!stat.isDirectory() || stat.isSymbolicLink()) throw new Error('Scenario directory must not be a symlink');
  }
  #path(id) {
    if (!ID.test(id)) throw new Error('Invalid scenario ID');
    return join(this.directory,id+'.json');
  }
  async #read(id) {
    const path = this.#path(id);
    const stat = await lstat(path);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size > LIMIT) throw new Error('Invalid scenario file');
    const item = JSON.parse(await readFile(path,'utf8'));
    if (item.id !== id || typeof item.title !== 'string' || item.title.length > 100 ||
        typeof item.createdAt !== 'string') throw new Error('Invalid scenario metadata');
    return {...item,scenario:scenarioSchema.parse(item.scenario)};
  }
  async list() {
    await this.#prepare();
    const names = (await readdir(this.directory)).filter(name => name.endsWith('.json') && ID.test(name.slice(0,-5)));
    if (names.length > MAX) throw new Error('Scenario library limit exceeded');
    const items = [];
    for (const name of names) {
      const {id,title,createdAt,scenario} = await this.#read(name.slice(0,-5));
      items.push({id,title,createdAt,target:scenario.target,steps:scenario.steps.length});
    }
    return items.sort((a,b)=>b.createdAt.localeCompare(a.createdAt));
  }
  async load(id) {
    await this.#prepare();
    return (await this.#read(id)).scenario;
  }
  // Only the local CLI calls save and remove. No model-controlled path or write endpoint.
  async save(title, scenario) {
    if (typeof title !== 'string' || !title.trim() || title.length > 100) throw new Error('Invalid title');
    if ((await this.list()).length >= MAX) throw new Error('Scenario library full');
    const id = randomUUID();
    const item = { id,title:title.trim(),createdAt:new Date().toISOString(),scenario:scenarioSchema.parse(scenario) };
    await writeFile(this.#path(id),JSON.stringify(item,null,2),{encoding:'utf8',flag:'wx',mode:0o600});
    return id;
  }
  async remove(id) {
    await this.#prepare();
    await this.#read(id);
    await unlink(this.#path(id));
  }
}
