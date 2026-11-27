import {BoxGeometry, Float32BufferAttribute, Group, Mesh, MeshStandardMaterial, Scene} from 'three';
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
export async function createStarterVrm(palette:'scout'|'violet'|'amber'='scout'): Promise<File> {
  const colors={scout:['#394361','#6de0d0'],violet:['#44375c','#c6a2ff'],amber:['#4b4038','#ffb968']} as const;
  const selected=colors[palette]??colors.scout;
  const scene = new Scene();
  const unit = new BoxGeometry(1, 1, 1);
  const body = new MeshStandardMaterial({color: selected[0], roughness: .85});
  const highlight = new MeshStandardMaterial({color: selected[1], roughness: .65});
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

  // Actual VRM expression morph targets on a dedicated mouth mesh.
  const mouthGeometry=new BoxGeometry(.085,.012,.012);
  const mouthPositions=mouthGeometry.getAttribute('position');
  const vowelNames=['aa','ih','ou','ee','oh'] as const;
  const mouthScales=[[1.2,5],[1.45,2],[.65,3.2],[1.55,2.7],[.8,4.6]];
  mouthGeometry.morphAttributes.position=mouthScales.map(([sx,sy],index)=>{
    const data=new Float32Array(mouthPositions.count*3);
    for(let i=0;i<mouthPositions.count;i++){
      data[i*3]=mouthPositions.getX(i)*sx;
      data[i*3+1]=mouthPositions.getY(i)*sy;
      data[i*3+2]=mouthPositions.getZ(i);
    }
    const target=new Float32BufferAttribute(data,3);target.name=vowelNames[index];return target;
  });
  const mouth=new Mesh(mouthGeometry,visor);mouth.name='ScoutMouth';mouth.position.set(0,.054,.155);head.add(mouth);
  mouth.updateMorphTargets();
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
        nodeMap: Map<Group | Mesh, number>;
        json: {extensions?: Record<string, unknown>};
      };
      const humanBones = Object.fromEntries(Object.entries(bones).map(([name, object]) => {
        const node = output.nodeMap.get(object);
        if (node === undefined) throw Error('В VRM отсутствует узел ' + name);
        return [name, {node}];
      }));
      output.json.extensions ??= {};
      const mouthNode=output.nodeMap.get(mouth);
      if(mouthNode===undefined)throw Error('В VRM отсутствует узел рта');
      const preset=Object.fromEntries(vowelNames.map((name,index)=>[name,{morphTargetBinds:[{node:mouthNode,index,weight:1}]}]));
      output.json.extensions[extension] = {specVersion: '1.0', meta, humanoid: {humanBones},expressions:{preset}};
      output.extensionsUsed[extension] = true;
    },
  }));

  try {
    const result = await exporter.parseAsync(scene, {binary: true});
    if (!(result instanceof ArrayBuffer)) throw Error('Не удалось создать VRM.');
    return new File([result], 'mysticego-scout.vrm', {type: 'model/gltf-binary'});
  } finally {
    unit.dispose();
    mouthGeometry.dispose();
    body.dispose();
    highlight.dispose();
    visor.dispose();
  }
}
