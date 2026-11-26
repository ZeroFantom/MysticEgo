import {Euler, Quaternion} from 'three';

const waveTimes = [0, 0.42, 0.66, 0.88, 1.1, 1.32, 1.58, 2.05];
const joints = {
  hips: 0,
  spine: 1,
  chest: 2,
  rightUpperArm: 3,
  rightLowerArm: 4,
} as const;
const extension = 'VRMC_vrm_animation';

function rotations(frames: readonly [number, number, number][]) {
  const quaternion = new Quaternion();
  const radians = Math.PI / 180;
  return frames.flatMap(([x, y, z]) =>
    quaternion.setFromEuler(new Euler(x * radians, y * radians, z * radians)).toArray());
}

/** Builds a self-contained glTF binary with a humanoid greeting animation. */
export function createWaveVrma(): File {
  const values = [
    waveTimes,
    rotations([[0, 0, 62], [0, -15, -40], [0, -15, -40], [0, -15, -40], [0, -15, -40], [0, -15, -40], [0, -15, 10], [0, 0, 62]]),
    rotations([[0, 0, 0], [0, 0, -57], [0, 0, -25], [0, 0, -60], [0, 0, -25], [0, 0, -58], [0, 0, -20], [0, 0, 0]]),
  ];
  const binary = new ArrayBuffer(values.reduce((size, value) => size + value.length * 4, 0));
  const floats = new DataView(binary);
  let offset = 0;
  const bufferViews = values.map(value => {
    const view = {buffer: 0, byteOffset: offset, byteLength: value.length * 4};
    for (const frame of value) {
      floats.setFloat32(offset, frame, true);
      offset += 4;
    }
    return view;
  });
  const document = {
    asset: {version: '2.0', generator: 'MysticEgo'},
    extensionsUsed: [extension],
    extensions: {
      [extension]: {
        specVersion: '1.0',
        humanoid: {humanBones: Object.fromEntries(Object.entries(joints).map(([name, node]) => [name, {node}]))},
      },
    },
    scene: 0,
    scenes: [{nodes: [joints.hips]}],
    // VRMA stores the source rest pose. The viewer retargets its rotations onto the selected VRM.
    nodes: [
      {name: 'hips', translation: [0, 1, 0], children: [joints.spine]},
      {name: 'spine', translation: [0, .2, 0], children: [joints.chest]},
      {name: 'chest', translation: [0, .24, 0], children: [joints.rightUpperArm]},
      {name: 'rightUpperArm', translation: [-.2, .04, 0], children: [joints.rightLowerArm]},
      {name: 'rightLowerArm', translation: [-.29, 0, 0]},
    ],
    buffers: [{byteLength: binary.byteLength}],
    bufferViews,
    accessors: values.map((_, bufferView) => ({
      bufferView, componentType: 5126, count: waveTimes.length, type: bufferView === 0 ? 'SCALAR' : 'VEC4',
      ...(bufferView === 0 ? {min: [waveTimes[0]], max: [waveTimes.at(-1)]} : {}),
    })),
    animations: [{
      name: 'MysticEgo Greeting',
      samplers: [1, 2].map(output => ({input: 0, output, interpolation: 'LINEAR'})),
      channels: [joints.rightUpperArm, joints.rightLowerArm].map((node, sampler) => ({
        sampler, target: {node, path: 'rotation'},
      })),
    }],
  };

  const encoded = new TextEncoder().encode(JSON.stringify(document));
  const jsonLength = Math.ceil(encoded.byteLength / 4) * 4;
  const jsonChunk = new Uint8Array(jsonLength).fill(0x20);
  jsonChunk.set(encoded);

  const header = new DataView(new ArrayBuffer(20));
  header.setUint32(0, 0x46546c67, true); // glTF binary magic
  header.setUint32(4, 2, true);
  header.setUint32(8, 20 + jsonLength + 8 + binary.byteLength, true);
  header.setUint32(12, jsonLength, true);
  header.setUint32(16, 0x4e4f534a, true); // JSON chunk
  const binaryHeader = new DataView(new ArrayBuffer(8));
  binaryHeader.setUint32(0, binary.byteLength, true);
  binaryHeader.setUint32(4, 0x004e4942, true); // BIN chunk
  return new File([header.buffer, jsonChunk, binaryHeader.buffer, binary], 'mysticego-greeting.vrma', {
    type: 'model/gltf-binary',
  });
}
