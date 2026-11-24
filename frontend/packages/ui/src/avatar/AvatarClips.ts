import * as THREE from 'three';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {VRMAnimationLoaderPlugin,createVRMAnimationClip} from '@pixiv/three-vrm-animation';
import type {VRM,VRMHumanBoneName} from '@pixiv/three-vrm';

export type ClipAsset={url:string;extension:string};
function localFiles(rootUrl:string){
 const manager=new THREE.LoadingManager();
 manager.setURLModifier(url=>url===rootUrl||url.startsWith('blob:')||url.startsWith('data:')?url:'data:application/octet-stream,');
 return manager;
}
const humanoid:Record<string,VRMHumanBoneName>={
 Hips:'hips',Spine:'spine',Spine1:'chest',Spine2:'upperChest',Neck:'neck',Head:'head',
 LeftShoulder:'leftShoulder',LeftArm:'leftUpperArm',LeftForeArm:'leftLowerArm',LeftHand:'leftHand',
 RightShoulder:'rightShoulder',RightArm:'rightUpperArm',RightForeArm:'rightLowerArm',RightHand:'rightHand',
 LeftUpLeg:'leftUpperLeg',LeftLeg:'leftLowerLeg',LeftFoot:'leftFoot',LeftToeBase:'leftToes',
 RightUpLeg:'rightUpperLeg',RightLeg:'rightLowerLeg',RightFoot:'rightFoot',RightToeBase:'rightToes'
};

export async function avatarClip(asset:ClipAsset,vrm:VRM):Promise<THREE.AnimationClip|null>{
 if(asset.extension==='.vrma'){
  const loader=new GLTFLoader(localFiles(asset.url));loader.register(parser=>new VRMAnimationLoaderPlugin(parser));
  const gltf=await loader.loadAsync(asset.url);
  const animation=gltf.userData.vrmAnimations?.[0];
  return animation?createVRMAnimationClip(animation,vrm):null;
 }
 if(asset.extension==='.fbx'){
  const data=await new FBXLoader(localFiles(asset.url)).loadAsync(asset.url);
  const original=data.animations[0];
  if(!original)return null;
  data.updateMatrixWorld(true);
  const tracks:THREE.KeyframeTrack[]=[];
  const restInverse=new THREE.Quaternion(),parent=new THREE.Quaternion(),pose=new THREE.Quaternion();
  for(const track of original.tracks){
   const [name,property]=track.name.split('.');
   const joint=data.getObjectByName(name);
   const normalized=humanoid[name.replace(/^mixamorig[:_]?/i,'')];
   const target=normalized?vrm.humanoid.getNormalizedBoneNode(normalized):null;
   if(!joint||!target||property!=='quaternion')continue;
   joint.getWorldQuaternion(restInverse).invert();
   if(joint.parent)joint.parent.getWorldQuaternion(parent);else parent.identity();
   const values=new Float32Array(track.values.length);
   for(let frame=0;frame<values.length;frame+=4){
    pose.fromArray(track.values,frame).premultiply(parent).multiply(restInverse);
    if(vrm.meta.metaVersion==='0'){pose.x=-pose.x;pose.z=-pose.z}
    pose.toArray(values,frame);
   }
   tracks.push(new THREE.QuaternionKeyframeTrack(target.name+'.quaternion',track.times,values));
  }
  return tracks.length?new THREE.AnimationClip(original.name,original.duration,tracks):null;
 }
 const gltf=await new GLTFLoader(localFiles(asset.url)).loadAsync(asset.url);
 const root=gltf.animations[0];
 if(!root)return null;
 const tracks=root.tracks.filter(track=>{
  const name=track.name.split('.')[0];
  return vrm.scene.getObjectByName(name)!=null||vrm.humanoid.normalizedHumanBonesRoot.getObjectByName(name)!=null;
 });
 return tracks.length?new THREE.AnimationClip(root.name,root.duration,tracks):null;
}
