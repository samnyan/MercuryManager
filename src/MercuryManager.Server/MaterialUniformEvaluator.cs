using System.Numerics;

namespace MercuryManager.Server;

// Uniform expressions are CPU-evaluated inputs to a cooked program, not an editor graph.
public abstract record MaterialUniformExpression;
public sealed record UniformConstant(Vector4 Value):MaterialUniformExpression;
public sealed record UniformScalar(string Name,float DefaultValue):MaterialUniformExpression;
public sealed record UniformVector(string Name,Vector4 DefaultValue):MaterialUniformExpression;
public sealed record UniformTime(bool RealTime=false):MaterialUniformExpression;
public sealed record UniformFolded(MaterialUniformExpression Left,MaterialUniformExpression Right,byte Operation,uint ComponentMask=15):MaterialUniformExpression;
public sealed record MaterialUniformContext(IReadOnlyDictionary<string,float> Scalars,IReadOnlyDictionary<string,Vector4> Vectors,float Time,float RealTime);

public static class MaterialUniformEvaluator
{
    public static Vector4 Evaluate(MaterialUniformExpression expression,MaterialUniformContext context)=>Evaluate(expression,context,0);
    public static Vector4 Evaluate(LegacyUniformNode node,MaterialUniformContext context)=>Evaluate(node,context,0);
    private static Vector4 Evaluate(LegacyUniformNode node,MaterialUniformContext context,int depth)
    {
        if(depth>64)throw new InvalidDataException("Uniform expression nesting limit exceeded.");
        if(node.Association is not (null or 2)||node.ParameterIndex is not (null or -1))
            throw new NotSupportedException("Layer-associated parameter lookup is not supported yet.");
        Vector4 Child(int index)=>Evaluate(node.Children[index],context,depth+1);
        Vector4 Color()=>new(node.Values[0],node.Values[1],node.Values[2],node.Values[3]);
        Vector4 result=node.Type switch
        {
            "FMaterialUniformExpressionScalarParameter"=>new Vector4(context.Scalars.TryGetValue(node.ParameterName!,out var scalar)?scalar:node.Values[0]),
            "FMaterialUniformExpressionVectorParameter"=>context.Vectors.TryGetValue(node.ParameterName!,out var vector)?vector:Color(),
            "FMaterialUniformExpressionConstant"=>Color(),
            "FMaterialUniformExpressionFoldedMath"=>Fold(Child(0),Child(1),(byte)node.Metadata[0],(uint)node.Metadata[1]),
            "FMaterialUniformExpressionFmod"=>Remainder(Child(0),Child(1)),
            "FMaterialUniformExpressionAppendVector"=>Append(Child(0),Child(1),node.Metadata[0]),
            "FMaterialUniformExpressionComponentSwizzle"=>Swizzle(Child(0),node.Metadata),
            "FMaterialUniformExpressionTrigMath"=>Trig(Child(0),node.Metadata[0]),
            "FMaterialUniformExpressionTime"=>new Vector4(context.Time),
            "FMaterialUniformExpressionRealTime"=>new Vector4(context.RealTime),
            "FMaterialUniformExpressionSine"=>Trig(Child(0),node.Metadata.Length>0&&node.Metadata[0]!=0?1:0),
            "FMaterialUniformExpressionSquareRoot"=>new Vector4(MathF.Sqrt(MathF.Max(0f,Child(0).X))),
            "FMaterialUniformExpressionLength"=>new Vector4(Child(0).Length()),
            "FMaterialUniformExpressionFloor"=>new Vector4(MathF.Floor(Child(0).X),MathF.Floor(Child(0).Y),MathF.Floor(Child(0).Z),MathF.Floor(Child(0).W)),
            "FMaterialUniformExpressionCeil"=>new Vector4(MathF.Ceiling(Child(0).X),MathF.Ceiling(Child(0).Y),MathF.Ceiling(Child(0).Z),MathF.Ceiling(Child(0).W)),
            "FMaterialUniformExpressionFrac"=>Child(0)-new Vector4(MathF.Floor(Child(0).X),MathF.Floor(Child(0).Y),MathF.Floor(Child(0).Z),MathF.Floor(Child(0).W)),
            "FMaterialUniformExpressionClamp"=>Vector4.Clamp(Child(0),Child(1),Child(2)),
            "FMaterialUniformExpressionMin"=>Vector4.Min(Child(0),Child(1)),
            "FMaterialUniformExpressionMax"=>Vector4.Max(Child(0),Child(1)),
            "FMaterialUniformExpressionSaturate"=>Vector4.Clamp(Child(0),Vector4.Zero,Vector4.One),
            _=>throw new NotSupportedException($"Unsupported uniform evaluation: {node.Type}")
        };
        if(!float.IsFinite(result.X)||!float.IsFinite(result.Y)||!float.IsFinite(result.Z)||!float.IsFinite(result.W))throw new InvalidDataException("Non-finite uniform expression result.");
        return result;
    }
    private static Vector4 Remainder(Vector4 a,Vector4 b)
    {
        float Apply(float value,float divisor)
        {
            if(MathF.Abs(divisor)<=1e-8f)return 0;
            float quotient=value/divisor;
            // Keep the native truncating int32 domain explicit rather than inventing overflow behavior.
            if(!float.IsFinite(quotient)||quotient<int.MinValue||quotient>=2147483648f)
                throw new NotSupportedException("Uniform remainder quotient exceeds int32 range.");
            float product=(int)quotient*divisor;
            if(MathF.Abs(product)>MathF.Abs(value))product=value;
            return value-product;
        }
        return new(Apply(a.X,b.X),Apply(a.Y,b.Y),Apply(a.Z,b.Z),Apply(a.W,b.W));
    }
    private static Vector4 Append(Vector4 a,Vector4 b,int count)
    {
        if(count<0||count>4)throw new InvalidDataException("Invalid append component count.");
        var result=Vector4.Zero;for(int i=0;i<4;i++)result[i]=i<count?a[i]:b[i-count];return result;
    }
    private static Vector4 Swizzle(Vector4 value,int[] metadata)
    {
        int count=metadata[4];if(count<1||count>4)throw new InvalidDataException("Invalid swizzle size.");
        var result=Vector4.Zero;for(int i=0;i<count;i++){if(metadata[i]<0||metadata[i]>3)throw new InvalidDataException("Invalid swizzle channel.");result[i]=value[metadata[i]];}return result;
    }
    private static Vector4 Trig(Vector4 value,int operation)
    {
        float Apply(float v)=>operation switch {0=>MathF.Sin(v),1=>MathF.Cos(v),2=>MathF.Tan(v),3=>MathF.Asin(Math.Clamp(v,-1,1)),4=>MathF.Acos(Math.Clamp(v,-1,1)),5=>MathF.Atan(v),_=>throw new NotSupportedException($"Unsupported trig operation: {operation}")};
        return new(Apply(value.X),Apply(value.Y),Apply(value.Z),Apply(value.W));
    }
    private static Vector4 Evaluate(MaterialUniformExpression expression,MaterialUniformContext context,int depth)
    {
        if(depth>64)throw new InvalidDataException("Uniform expression nesting limit exceeded.");
        Vector4 result=expression switch
        {
            UniformConstant e=>e.Value,
            UniformScalar e=>new Vector4(context.Scalars.TryGetValue(e.Name,out var value)?value:e.DefaultValue),
            UniformVector e=>context.Vectors.TryGetValue(e.Name,out var value)?value:e.DefaultValue,
            UniformTime e=>new Vector4(e.RealTime?context.RealTime:context.Time),
            UniformFolded e=>Fold(Evaluate(e.Left,context,depth+1),Evaluate(e.Right,context,depth+1),e.Operation,e.ComponentMask),
            _=>throw new NotSupportedException($"Unsupported uniform expression: {expression.GetType().Name}")
        };
        if(!float.IsFinite(result.X)||!float.IsFinite(result.Y)||!float.IsFinite(result.Z)||!float.IsFinite(result.W))
            throw new InvalidDataException("Non-finite uniform expression result.");
        return result;
    }
    private static Vector4 Fold(Vector4 a,Vector4 b,byte operation,uint mask)=>operation switch
    {
        0=>a+b,
        1=>a-b,
        2=>a*b,
        // Match engine uniform folding: near-zero divisors retain sign and clamp to epsilon.
        3=>new(a.X/Divisor(b.X),a.Y/Divisor(b.Y),a.Z/Divisor(b.Z),a.W/Divisor(b.W)),
        4 when mask is 1 or 3 or 7 or 15=>new Vector4(a.X*b.X+(mask>=2?a.Y*b.Y:0)+(mask>=4?a.Z*b.Z:0)+(mask>=8?a.W*b.W:0)),
        4=>throw new NotSupportedException("Non-contiguous folded dot component mask."),
        _=>throw new NotSupportedException($"Unknown folded math operation: {operation}")
    };
    private static float Divisor(float value)=>MathF.Abs(value)<1e-5f?(value>=0?1e-5f:-1e-5f):value;
}
