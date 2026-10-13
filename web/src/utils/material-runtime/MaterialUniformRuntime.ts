export interface UniformNode { type: string; parameterName: string | null; association: number | null; parameterIndex: number | null; values: number[]; metadata: number[]; children: UniformNode[] }
export interface RuntimeWrite { bufferIndex: number; byteOffset: number; width: number; previous: boolean; expression: UniformNode }
export interface UniformProgram { version: number; writes: RuntimeWrite[]; scalars: Record<string, number>; vectors: Record<string, number[]> }
export interface UniformContext { time: number; scalars: Record<string, number>; vectors: Record<string, number[]> }
const f = Math.fround
export function evaluateUniform(n: UniformNode, ctx: UniformContext, depth = 0): number[] {
  if (depth > 64 || (n.association != null && n.association !== 2) || (n.parameterIndex != null && n.parameterIndex !== -1)) throw new Error('Unsupported uniform context')
  const child = (i: number) => evaluateUniform(n.children[i]!, ctx, depth + 1)
  const splat = (v: number) => [v,v,v,v]
  const unary = (fn: (v: number) => number) => child(0).map(v => f(fn(v)))
  const binary = (fn: (a: number,b: number) => number) => { const a=child(0),b=child(1); return a.map((v,i)=>f(fn(v,b[i]!))) }
  let r: number[]
  switch (n.type.replace('FMaterialUniformExpression','')) {
    case 'Constant': r=n.values.slice(); break
    case 'ScalarParameter': r=splat(ctx.scalars[n.parameterName!] ?? n.values[0]!); break
    case 'VectorParameter': r=(ctx.vectors[n.parameterName!] ?? n.values).slice(); break
    case 'Time': case 'RealTime': r=splat(ctx.time); break
    case 'FoldedMath': {
      const op=n.metadata[0];
      if(op===4){const a=child(0),b=child(1),mask=n.metadata[1]!;if(![1,3,7,15].includes(mask))throw new Error('Invalid dot mask');let v=f(a[0]!*b[0]!);for(let i=1;i<4;i++)if(mask>=(1<<i))v=f(v+f(a[i]!*b[i]!));r=splat(v)}
      else r=binary((a,b)=>{switch(op){case 0:return a+b;case 1:return a-b;case 2:return a*b;case 3:return a/(Math.abs(b)<1e-5?f(b>=0?1e-5:-1e-5):b);default:throw new Error('Unknown folded operation')}})
      break
    }
    case 'Fmod': r=binary((a,b)=>{if(Math.abs(b)<=1e-8)return 0;const q=f(a/b);if(!Number.isFinite(q)||q< -2147483648||q>=2147483648)throw new Error('Remainder domain');let product=f(Math.trunc(q)*b);if(Math.abs(product)>Math.abs(a))product=a;return a-product});break
    case 'AppendVector': {const a=child(0),b=child(1),count=n.metadata[0]!;if(count<0||count>4)throw new Error('Append size');r=[0,1,2,3].map(i=>i<count?a[i]!:b[i-count]!);break}
    case 'ComponentSwizzle': {const a=child(0),count=n.metadata[4]!;if(count<1||count>4)throw new Error('Swizzle size');r=[0,0,0,0];for(let i=0;i<count;i++){const index=n.metadata[i]!;if(index<0||index>3)throw new Error('Swizzle channel');r[i]=a[index]!}break}
    case 'TrigMath': case 'Sine': {const op=n.type.endsWith('Sine')?(n.metadata[0]?1:0):n.metadata[0];r=unary(v=>{switch(op){case 0:return Math.sin(v);case 1:return Math.cos(v);case 2:return Math.tan(v);case 3:return Math.asin(Math.max(-1,Math.min(1,v)));case 4:return Math.acos(Math.max(-1,Math.min(1,v)));case 5:return Math.atan(v);default:throw new Error('Trig operation')}});break}
    case 'SquareRoot': r=splat(f(Math.sqrt(Math.max(0,child(0)[0]!))));break
    case 'Length': {const a=child(0);let sum=f(a[0]!*a[0]!);for(let i=1;i<4;i++)sum=f(sum+f(a[i]!*a[i]!));r=splat(f(Math.sqrt(sum)));break}
    case 'Floor':r=unary(Math.floor);break
    case 'Ceil':r=unary(v=>{const c=f(-0.5-f(v+v));if(!Number.isFinite(c)||c<=-2147483648||c>=2147483648)throw new Error('Ceil domain');return -(Math.trunc(c)>>1)});break
    case 'Abs':r=unary(Math.abs);break
    case 'Frac':r=unary(v=>v-Math.floor(v));break
    case 'Min':r=binary(Math.min);break
    case 'Max':r=binary(Math.max);break
    case 'Saturate':r=unary(v=>Math.max(0,Math.min(1,v)));break
    case 'Clamp': {const a=child(0),lo=child(1),hi=child(2);r=a.map((v,i)=>v<lo[i]!?lo[i]!:Math.min(v,hi[i]!));break}
    default:throw new Error(`Unsupported uniform: ${n.type}`)
  }
  r=r.map(f)
  if(r.length!==4||r.some(v=>!Number.isFinite(v)))throw new Error('Invalid uniform result')
  return r
}
