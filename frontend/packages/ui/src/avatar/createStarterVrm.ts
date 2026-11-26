import {BoxGeometry, Group, Mesh, MeshStandardMaterial, Scene} from 'three';
import {GLTFExporter} from 'three/addons/exporters/GLTFExporter.js';

const extension = 'VRMC_vrm';
const meta = {
  name: 'MysticEgo Scout',
  version: '1.0',
  authors: ['DarkLie:Zero'],
  licenseUrl: 'https://vrm.dev/licenses/1.0/',
  avatarPermission: 'everyone',
  commercialUsage: 'personalNonProfit',
  creditNotation: 'required',
  allowRedistribution: true,
  modification: 'allowModificationRedistribution',
};

type Dimensions = readonly [number, number, number];

/** Constructs a low-poly VRM template with the humanoid joints used by the avatar player. */
export async function createStarterVrm(): Promise<File> {
  const scene = new Scene();
  const unit = new BoxGeometry(1, 1, 1);
  const body = new MeshStandardMaterial({color: '#394361', roughness: .85});
  const highlight = new MeshStandardMaterial({color: '#6de0d0', roughness: .65});
  const visor = new MeshStandardMaterial({color: '#1e2330', roughness: .4});
  const bones: Record<string, Group> = {};

  function joint(name: string, parent: Scene | Group, position: Dimensions) {
    const item = new Group();
    item.name = name;
    item.position.set(...position);
    parent.add(item);
    bones[name] = item;
    return item;
  }

  function block(parent: Group, size: Dimensions, center: Dimensions, material = body) {
    const mesh = new Mesh(unit, material);
    mesh.scale.set(...size);
    mesh.position.set(...center);
    parent.add(mesh);
  }

  const hips = joint('hips', scene, [0, .83, 0]);
  const spine = joint('spine', hips, [0, .12, 0]);
  const chest = joint('chest', spine, [0, .2, 0]);
  const head = joint('head', chest, [0, .28, 0]);

  block(hips, [.33, .19, .23], [0, .04, 0]);
  block(spine, [.42, .36, .24], [0, .16, 0]);
  block(chest, [.2, .11, .022], [0, .04, .13], highlight);
  block(head, [.3, .3, .28], [0, .12, 0]);
  block(head, [.27, .1, .024], [0, .14, .149], visor);
  block(head, [.05, .04, .025], [-.071, .14, .164], highlight);
  block(head, [.05, .04, .025], [.071, .14, .164], highlight);

  for (const [side, sign] of [['left', 1], ['right', -1]] as const) {
    const arm = joint(side + 'UpperArm', chest, [sign * .26, .08, 0]);
    const forearm = joint(side + 'LowerArm', arm, [sign * .24, 0, 0]);
    const hand = joint(side + 'Hand', forearm, [sign * .22, 0, 0]);
    block(arm, [.24, .14, .16], [sign * .105, 0, 0]);
    block(forearm, [.21, .12, .13], [sign * .1, 0, 0], highlight);
    block(hand, [.11, .11, .13], [sign * .03, 0, 0]);

    const leg = joint(side + 'UpperLeg', hips, [sign * .115, -.07, 0]);
    const shin = joint(side + 'LowerLeg', leg, [0, -.35, 0]);
    const foot = joint(side + 'Foot', shin, [0, -.34, 0]);
    block(leg, [.16, .35, .17], [0, -.16, 0]);
    block(shin, [.15, .35, .16], [0, -.16, 0], highlight);
    block(foot, [.17, .12, .24], [0, -.01, .055]);
  }

  const exporter = new GLTFExporter().register(writer => ({
    afterParse() {
      // GLTFExporter assigns node indices during traversal, so map bones after export parsing.
      const output = writer as typeof writer & {
        nodeMap: Map<Group, number>;
        json: {extensions?: Record<string, unknown>};
      };
      const humanBones = Object.fromEntries(Object.entries(bones).map(([name, object]) => {
        const node = output.nodeMap.get(object);
        if (node === undefined) throw Error('В VRM отсутствует узел ' + name);
        return [name, {node}];
      }));
      output.json.extensions ??= {};
      output.json.extensions[extension] = {specVersion: '1.0', meta, humanoid: {humanBones}};
      output.extensionsUsed[extension] = true;
    },
  }));

  try {
    const result = await exporter.parseAsync(scene, {binary: true});
    if (!(result instanceof ArrayBuffer)) throw Error('Не удалось создать VRM.');
    return new File([result], 'mysticego-scout.vrm', {type: 'model/gltf-binary'});
  } finally {
    unit.dispose();
    body.dispose();
    highlight.dispose();
    visor.dispose();
  }
}
