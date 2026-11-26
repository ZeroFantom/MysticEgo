export type SamplePoint={at:string;value:number|null|undefined};
export type ChartPoint={at:number;x:number;y:number};
export type ChartGap={from:ChartPoint;to:ChartPoint};
export const isMeasurement=(value:unknown):value is number=>typeof value==='number'&&Number.isFinite(value)&&value>=0;

/** Never turn missing samples into zeroes. Keep the last real point for a labelled gap bridge. */
export function chartGeometry(points:SamplePoint[],start:number,end:number,top:number,width:number,height:number){
  const segments:ChartPoint[][]=[],gaps:ChartGap[]=[];
  const unique=new Map<number,SamplePoint>();
  for(const point of points){const at=Date.parse(point.at);if(Number.isFinite(at)&&at>=start&&at<=end)unique.set(at,point)}
  let previous:ChartPoint|undefined;
  let missing=false;
  for(const [at,point] of [...unique].sort((a,b)=>a[0]-b[0])){
    if(!isMeasurement(point.value)){missing=true;continue}
    const next={at,x:(at-start)/Math.max(1,end-start)*width,y:height-5-(height-10)*point.value/top};
    if(!previous||missing||at-previous.at>15000){
      segments.push([]);
      if(previous)gaps.push({from:previous,to:next});
    }
    segments[segments.length-1].push(next);
    previous=next;missing=false;
  }
  return {segments,gaps};
}

/** Monotone cubic Hermite curves: no overshoot, no false peaks, flat ends join gaps smoothly. */
export function smoothEdges(points:ChartPoint[]){
  const secants=points.slice(1).map((p,i)=>(p.y-points[i].y)/Math.max(Number.EPSILON,p.x-points[i].x));
  const slopes=points.map((_,i)=>{
    if(i===0||i===points.length-1)return 0;
    const a=secants[i-1],b=secants[i];
    if(a===0||b===0||Math.sign(a)!==Math.sign(b))return 0;
    return Math.sign(a)*Math.min(2*Math.abs(a*b)/(Math.abs(a)+Math.abs(b)),3*Math.abs(a),3*Math.abs(b));
  });
  return points.slice(1).map((to,i)=>{
    const from=points[i],dx=(to.x-from.x)/3;
    const lo=Math.min(from.y,to.y),hi=Math.max(from.y,to.y);
    const clamp=(v:number)=>Math.max(lo,Math.min(hi,v));
    return {from,to,command:`C ${from.x+dx} ${clamp(from.y+dx*slopes[i])} ${to.x-dx} ${clamp(to.y-dx*slopes[i+1])} ${to.x} ${to.y}`};
  });
}
export function gapPath({from,to}:ChartGap){
  const dx=(to.x-from.x)/3;
  return `M ${from.x} ${from.y} C ${from.x+dx} ${from.y} ${to.x-dx} ${to.y} ${to.x} ${to.y}`;
}
