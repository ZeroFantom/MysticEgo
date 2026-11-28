export type Point3={x:number;y:number;z:number};
const finite=(p:Point3)=>Number.isFinite(p.x)&&Number.isFinite(p.y)&&Number.isFinite(p.z);
/** A short same-height movement hint, not plane identity, collision detection or a navigation mesh. */
export function walkDistance(from:Point3,to:Point3):number|null{
 if(!finite(from)||!finite(to)||Math.abs(to.y-from.y)>.08)return null;
 const distance=Math.hypot(to.x-from.x,to.z-from.z);
 return distance>=.08&&distance<=1?distance:null;
}
export function walkProgress(elapsed:number,duration:number):number{
 if(!Number.isFinite(elapsed)||!Number.isFinite(duration)||duration<=0)return 0;
 return Math.max(0,Math.min(1,elapsed/duration));
}
