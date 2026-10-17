using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace MercuryManager.Server;

internal static class UMGModelBuilder
{
    private static readonly string[] VisualFields=["Size","EffectMaterial","TextureParameter","Phase","PhaseCount","RenderOnInvalidation","RenderOnPhase","bRetainRender","Stretch","StretchDirection","UserSpecifiedScale","IgnoreInheritedScale","ActiveWidgetIndex","Visibility","RenderOpacity","RenderTransform","RenderTransformPivot","Clipping","Brush","Background","BrushColor","ColorAndOpacity","ContentColorAndOpacity","Text","TextAngleStart","TextAngleEnd","TextRadius","TextRadiusInterval","CharacterAngle","bCharacterAngleBasedHorizontal","Font","Justification","AutoWrapText","WrapTextAt","MinDesiredWidth","Padding","HorizontalAlignment","VerticalAlignment","WidthOverride","HeightOverride","bOverride_WidthOverride","bOverride_HeightOverride","MinDesiredHeight","bOverride_MinDesiredWidth","bOverride_MinDesiredHeight","MaxDesiredWidth","MaxDesiredHeight","bOverride_MaxDesiredWidth","bOverride_MaxDesiredHeight","BlendMode","IsVariable","Template","ParticleSystem","EmitterTemplate","Effect"];
    private static readonly string[] SlotFields=["LayoutData","bAutoSize","ZOrder","Padding","HorizontalAlignment","VerticalAlignment","Size","Offset","Anchors","Alignment"];
    private static readonly HashSet<string> Supported=["RichTextBlock","RetainerBox","ScaleBox","WidgetSwitcher","CanvasPanel","Overlay","Border","SizeBox","HorizontalBox","VerticalBox","Image","TextBlock","Spacer"];
    private static List<UMGAnimation> ReadAnimations(UAsset asset)
    {
        var result=new List<UMGAnimation>();
        for(var i=0;i<asset.Exports.Count;i++)
        {
            if(asset.Exports[i] is not NormalExport animation||animation.GetExportClassType().ToString()!="WidgetAnimation")continue;
            int Index(string name)=>animation.Data.OfType<ObjectPropertyData>().FirstOrDefault(p=>p.Name.ToString()==name)?.Value.Index??0;
            var sceneIndex=Index("MovieScene");if(sceneIndex<=0||sceneIndex>asset.Exports.Count||asset.Exports[sceneIndex-1] is not NormalExport scene)continue;
            var bindings=new List<UMGAnimationBinding>();
            var animationBindings=animation.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="AnimationBindings");
            if(animationBindings!=null)foreach(var item in animationBindings.Value.OfType<StructPropertyData>())
            {
                var fields=item.Value;var guid=(fields.FirstOrDefault(p=>p.Name.ToString()=="AnimationGuid") as StructPropertyData)?.Value.OfType<GuidPropertyData>().FirstOrDefault()?.Value.ToString();
                var widget=(fields.FirstOrDefault(p=>p.Name.ToString()=="WidgetName") as NamePropertyData)?.Value.ToString()??string.Empty;
                var slot=(fields.FirstOrDefault(p=>p.Name.ToString()=="SlotWidgetName") as NamePropertyData)?.Value.ToString();
                var root=(fields.FirstOrDefault(p=>p.Name.ToString()=="bIsRootWidget") as BoolPropertyData)?.Value??false;
                if(guid!=null)bindings.Add(new(guid,widget,slot,root));
            }
            var tracks=new List<UMGAnimationTrack>();
            var interval=scene.Data.OfType<FloatPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="FixedFrameInterval")?.Value??0;
            var tick=interval>0?1d/interval:Rate(scene,"TickResolution");var display=Rate(scene,"DisplayRate");
            if(display<=0)display=interval>0?1d/interval:30;
            // MovieScene defaults to 24000 ticks/sec when TickResolution is omitted.
            var channelRate=tick>0?tick:24000d;

            var objectBindings=scene.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="ObjectBindings");
            if(objectBindings!=null)foreach(var binding in objectBindings.Value.OfType<StructPropertyData>())
            {
                var bindingGuid=(binding.Value.FirstOrDefault(p=>p.Name.ToString()=="ObjectGuid") as StructPropertyData)?.Value.OfType<GuidPropertyData>().FirstOrDefault()?.Value.ToString();
                var target=bindingGuid;
                var bindingName=binding.Value.OfType<StrPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="BindingName")?.Value.ToString();
                var trackRefs=binding.Value.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Tracks");if(trackRefs==null)continue;
                foreach(var reference in trackRefs.Value.OfType<ObjectPropertyData>())
                {
                    var index=reference.Value.Index;if(index<=0||index>asset.Exports.Count||asset.Exports[index-1] is not NormalExport track)continue;
                    var property=track.Data.OfType<NamePropertyData>().FirstOrDefault(p=>p.Name.ToString()=="PropertyName")?.Value.ToString() ?? track.Data.OfType<NamePropertyData>().FirstOrDefault(p=>p.Name.ToString()=="TrackName")?.Value.ToString();
                    if(property==null&&track.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="BrushPropertyNamePath") is { } brushPath)property=string.Join(".",brushPath.Value.OfType<NamePropertyData>().Select(p=>p.Value.ToString()));
                    var channels=new List<UMGAnimationChannel>();var materialParameters=new List<UMGAnimationMaterialParameter>();
                    var sections=track.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Sections");
                    if(sections!=null)foreach(var sectionRef in sections.Value.OfType<ObjectPropertyData>())
                    {
                        var sectionIndex=sectionRef.Value.Index;if(sectionIndex<=0||sectionIndex>asset.Exports.Count||asset.Exports[sectionIndex-1] is not NormalExport section)continue;
                        foreach(var prop in section.Data)
                        {
                            if((prop.Name.ToString() is "ScalarParameterNamesAndCurves" or "ColorParameterNamesAndCurves" or "VectorParameterNamesAndCurves")&&prop is ArrayPropertyData parameterArray)foreach(var parameter in parameterArray.Value.OfType<StructPropertyData>())
                            {
                                var parameterName=(parameter.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterName") as NamePropertyData)?.Value.ToString();var parameterIndex=parameter.Value.OfType<IntPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Index")?.Value??0;
                                if(parameterName==null)continue;
                                if(prop.Name.ToString()=="ScalarParameterNamesAndCurves"){var scalarCurve=parameter.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterCurve") as StructPropertyData;if(scalarCurve!=null)materialParameters.Add(new(parameterName,parameterIndex,RichCurveChannel(scalarCurve)));}
                                else {var curveStruct=parameter.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterCurve") as StructPropertyData;if(curveStruct!=null)foreach(var component in curveStruct.Value.OfType<StructPropertyData>())if(component.StructType.ToString()=="RichCurve")materialParameters.Add(new($"{parameterName}.{component.Name}",parameterIndex,RichCurveChannel(component)));}
                            }
                            if(prop is MovieSceneFloatChannelPropertyData floatChannel)channels.Add(FloatChannel(ChannelName(prop),floatChannel.Value,channelRate));
                            else if(prop is MovieSceneDoubleChannelPropertyData doubleChannel)channels.Add(DoubleChannel(ChannelName(prop),doubleChannel.Value,channelRate));
                            if(prop is StructPropertyData curve&&curve.StructType.ToString()=="RichCurve")channels.Add(RichCurveChannel(curve));
                            if(prop is StructPropertyData tagged&&tagged.StructType.ToString()=="MovieSceneFloatChannel")channels.Add(TaggedChannel(tagged,channelRate));
                        }
                    }
                    tracks.Add(new(bindingName??target??string.Empty,track.GetExportClassType().ToString(),property,bindingGuid??string.Empty,channels,materialParameters));
                }
            }
            double start=0,end=0;
            var rangeProperty=scene.Data.FirstOrDefault(p=>p.Name.ToString()=="PlaybackRange");double Bound(StructPropertyData? bound)=>bound?.Value.OfType<FloatPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Value")?.Value??0;
            if(rangeProperty is StructPropertyData rangeStruct){var lower=rangeStruct.Value.OfType<StructPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="LowerBound");var upper=rangeStruct.Value.OfType<StructPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="UpperBound");start=Bound(lower);end=Bound(upper);}
            var frameRange=(rangeProperty as StructPropertyData)?.Value.OfType<MovieSceneFrameRangePropertyData>().FirstOrDefault();
            if(frameRange!=null)
            {
                start=frameRange.Value.LowerBound.Value.Value/channelRate;
                end=frameRange.Value.UpperBound.Value.Value/channelRate;
                tick=channelRate;
            }
            var ownerIndex=animation.OuterIndex.Index;
            var chain=new List<int>();var seenOwners=new HashSet<int>();var current=ownerIndex;
            while(current>0&&current<=asset.Exports.Count&&seenOwners.Add(current))
            {chain.Add(current-1);current=asset.Exports[current-1].OuterIndex.Index;}
            chain.Reverse();
            var ownerPath=string.Join(" / ",chain.Where(index=>asset.Exports[index].GetExportClassType().ToString()!="WidgetTree").Select(index=>asset.Exports[index].ObjectName.ToString()));
            var scope=chain.Any(index=>asset.Exports[index].ObjectName.ToString()=="WidgetArchetype")?"archetype":chain.Any(index=>asset.Exports[index].GetExportClassType().ToString()=="WidgetBlueprintGeneratedClass")?"generated":"unknown";
            var ownerTree=asset.Exports.FindIndex(e=>e.GetExportClassType().ToString()=="WidgetTree"&&e.OuterIndex.Index==ownerIndex);
            result.Add(new($"export:{i}",animation.ObjectName.ToString(),start,end,tick,display,bindings,tracks,
                ownerIndex>0?$"export:{ownerIndex-1}":null,ownerPath,ownerTree>=0?$"export:{ownerTree}":null,scope));
        }
        return result;

        static string ChannelName(PropertyData channel)
        {
            var name=channel.Name.ToString();
            return channel.ArrayIndex>0||name is "Translation" or "Scale" or "Shear" or "Rotation"?name+"."+channel.ArrayIndex:name;
        }
        static UMGAnimationChannel TaggedChannel(StructPropertyData channel,double rate)
        {
            var times=channel.Value.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Times")?.Value.OfType<StructPropertyData>().Select(p=>p.Value.OfType<FrameNumberPropertyData>().Single().Value.Value).ToArray()??[];
            var values=channel.Value.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Values")?.Value.OfType<StructPropertyData>().Select(p=>p.Value.OfType<MovieSceneFloatValuePropertyData>().Single().Value).ToArray()??[];
            if(times.Length!=values.Length)throw new InvalidDataException("MovieScene channel times and values differ in length.");
            var name=ChannelName(channel);
            var defaultValue=channel.Value.OfType<FloatPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="DefaultValue")?.Value??0;
            var hasDefault=channel.Value.OfType<BoolPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="bHasDefaultValue")?.Value??false;
            return new(name,"float",defaultValue,hasDefault,times.Zip(values,(time,value)=>new UMGAnimationKey(time/rate,value.Value,value.InterpMode.ToString(),(value.Tangent?.ArriveTangent??0)*rate,(value.Tangent?.LeaveTangent??0)*rate,value.Tangent?.TangentWeightMode.ToString()??"RCTWM_WeightedNone",value.Tangent?.ArriveTangentWeight??0,value.Tangent?.LeaveTangentWeight??0,1d/rate)).ToList());
        }
        static double Rate(NormalExport export,string field)
        {
            var property=export.Data.FirstOrDefault(p=>p.Name.ToString()==field);
            var rate=property?.GetType().GetProperty("Value")?.GetValue(property);
            if(rate is FFrameRate frameRate)return frameRate.Denominator==0?0:(double)frameRate.Numerator/frameRate.Denominator;
            var fields=rate as IEnumerable<PropertyData>;
            var n=fields?.OfType<IntPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Numerator")?.Value??0;
            var d=fields?.OfType<IntPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Denominator")?.Value??1;
            return d==0?0:(double)n/d;
        }
        static UMGAnimationChannel RichCurveChannel(StructPropertyData curve)
        {
            var keys=curve.Value.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Keys")?.Value.OfType<StructPropertyData>().Select(p=>p.Value.OfType<RichCurveKeyPropertyData>().FirstOrDefault()?.Value).Where(k=>k.HasValue).Select(k=>k!.Value).ToArray()??Array.Empty<FRichCurveKey>();
            var defaultValue=curve.Value.OfType<FloatPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="DefaultValue")?.Value??0;
            var arrayIndex=curve.ArrayIndex;
            var curveName=curve.Name.ToString();if(arrayIndex>0||curveName is "Translation" or "Scale" or "Shear" or "Rotation")curveName+="."+arrayIndex;
            return new(curveName,"rich-curve",defaultValue,curve.Value.Any(p=>p.Name.ToString()=="DefaultValue"),keys.Select(k=>new UMGAnimationKey(k.Time,k.Value,k.InterpMode.ToString(),k.ArriveTangent,k.LeaveTangent,k.TangentWeightMode.ToString(),k.ArriveTangentWeight,k.LeaveTangentWeight)).ToList());
        }
        static UMGAnimationChannel FloatChannel(string name,FMovieSceneFloatChannel channel,double rate)=>new(name,"float",channel.DefaultValue,channel.bHasDefaultValue,CheckedChannelPairs(channel.Times,channel.Values,(time,value)=>new UMGAnimationKey(time.Value/rate,value.Value,value.InterpMode.ToString(),(value.Tangent?.ArriveTangent??0)*rate,(value.Tangent?.LeaveTangent??0)*rate,value.Tangent?.TangentWeightMode.ToString()??"RCTWM_WeightedNone",value.Tangent?.ArriveTangentWeight??0,value.Tangent?.LeaveTangentWeight??0,1d/rate)).ToList());
        static UMGAnimationChannel DoubleChannel(string name,FMovieSceneDoubleChannel channel,double rate)=>new(name,"double",channel.DefaultValue,channel.bHasDefaultValue,CheckedChannelPairs(channel.Times,channel.Values,(time,value)=>new UMGAnimationKey(time.Value/rate,value.Value,value.InterpMode.ToString(),(value.Tangent?.ArriveTangent??0)*rate,(value.Tangent?.LeaveTangent??0)*rate,value.Tangent?.TangentWeightMode.ToString()??"RCTWM_WeightedNone",value.Tangent?.ArriveTangentWeight??0,value.Tangent?.LeaveTangentWeight??0,1d/rate)).ToList());
    }
    private static List<TResult> CheckedChannelPairs<TTime,TValue,TResult>(IEnumerable<TTime> times,IEnumerable<TValue> values,Func<TTime,TValue,TResult> project)
    {
        using var time = times.GetEnumerator();
        using var value = values.GetEnumerator();
        var result = new List<TResult>();
        while (true)
        {
            var hasTime = time.MoveNext();
            var hasValue = value.MoveNext();
            if (hasTime != hasValue) throw new InvalidDataException("MovieScene channel times and values differ in length.");
            if (!hasTime) return result;
            result.Add(project(time.Current,value.Current));
        }
    }

    public static UMGDocument Build(UAsset asset,string path,string revision)
    {
        string Id(int i)=>$"export:{i}";
        bool Valid(int i)=>i>0&&i<=asset.Exports.Count;
        var reader=new UMGPropertyReader(asset);var diagnostics=new List<UMGIssue>();var nodes=new List<UMGNode>();var roots=new List<UMGRoot>();
        for(var t=0;t<asset.Exports.Count;t++)
        {
            var tree=asset.Exports[t];if(tree.GetExportClassType().ToString()!="WidgetTree")continue;
            var ownerIndex=tree.OuterIndex.Index;var owner=Valid(ownerIndex)?asset.Exports[ownerIndex-1]:null;
            var kind=owner?.GetExportClassType().ToString()=="WidgetBlueprintGeneratedClass"?"generated":owner?.ObjectName.ToString()=="WidgetArchetype"?"archetype":"nested";
            var rootIndex=UMGPropertyReader.Index(tree as NormalExport,"RootWidget");
            roots.Add(new(Id(t),Id(t),Valid(rootIndex)?Id(rootIndex-1):null,owner?.ObjectName.ToString()??tree.ObjectName.ToString(),kind));
            if(!Valid(rootIndex))diagnostics.Add(new("missing-root","warning",Id(t),"WidgetTree has no resolved RootWidget."));
            for(var i=0;i<asset.Exports.Count;i++)
            {
                var e=asset.Exports[i];var cls=e.GetExportClassType().ToString();
                if(e.OuterIndex.Index!=t+1||cls.EndsWith("Slot",StringComparison.Ordinal))continue;
                var normal=e as NormalExport;var issues=new List<string>();var resources=new List<UMGResource>();
                var visual=reader.Read(normal,VisualFields,resources,issues);
                var sources=visual.Keys.ToDictionary(k=>"visual."+k,_=>"serialized");
                var slotIndex=UMGPropertyReader.Index(normal,"Slot");
                // Resolve both directions; some cooked widgets omit their Slot property.
                if(!Valid(slotIndex))for(var s=0;s<asset.Exports.Count;s++)if(asset.Exports[s].GetExportClassType().ToString().EndsWith("Slot",StringComparison.Ordinal)&&UMGPropertyReader.Index(asset.Exports[s] as NormalExport,"Content")==i+1){slotIndex=s+1;break;}
                UMGSlot? slot=null;string? parentId=null;
                if(Valid(slotIndex))
                {
                    var se=asset.Exports[slotIndex-1];var sn=se as NormalExport;
                    var values=reader.Read(sn,SlotFields,resources,issues);foreach(var key in values.Keys)sources["slot."+key]="serialized";
                    slot=new(slotIndex-1,se.GetExportClassType().ToString(),values);
                    var parent=UMGPropertyReader.Index(sn,"Parent");if(Valid(parent))parentId=Id(parent-1);
                    if(UMGPropertyReader.Index(sn,"Content")!=i+1)issues.Add("Slot.Content does not match widget.");
                }
                var children=new List<string>();
                foreach(var s in UMGPropertyReader.Indices(normal,"Slots"))
                {
                    if(!Valid(s)){issues.Add("Unresolved panel Slot.");continue;}
                    var content=UMGPropertyReader.Index(asset.Exports[s-1] as NormalExport,"Content");
                    if(Valid(content)&&!children.Contains(Id(content-1)))children.Add(Id(content-1));else issues.Add("Unresolved or duplicate Slot.Content.");
                }
                if(children.Count==0){var content=UMGPropertyReader.Index(normal,"Content");if(Valid(content))children.Add(Id(content-1));}
                if(!visual.ContainsKey("Visibility")){visual["Visibility"]="Visible";sources["visual.Visibility"]="engine-default";}
                if(!visual.ContainsKey("RenderOpacity")){visual["RenderOpacity"]=1d;sources["visual.RenderOpacity"]="engine-default";}
                if(!visual.ContainsKey("RenderTransformPivot")){visual["RenderTransformPivot"]=new Dictionary<string,object?>{["X"]=0.5d,["Y"]=0.5d};sources["visual.RenderTransformPivot"]="engine-default";}
                if(!visual.ContainsKey("RenderTransform"))sources["visual.RenderTransform"]="engine-default";
                if(cls=="RichTextBlock")issues.Add("RichTextBlock uses a plain-text fallback; style tables, styled runs and decorators are not rendered.");
                if(cls=="RetainerBox")issues.Add("RetainerBox previews children directly; offscreen compositing, effect material and phase-based refresh are not simulated.");
                if(!Supported.Contains(cls))issues.Add("Class is not supported by the initial static renderer.");
                if(e.Extras is {Length:>0})issues.Add("Opaque custom serialization is not evaluated.");
                if(normal?.Data.Any(p=>p.Name.ToString().Contains("Binding",StringComparison.Ordinal))==true)issues.Add("Runtime bindings are not evaluated.");
                UMGChildWidget? child=null;
                if(Valid(e.ClassIndex.Index))issues.Add("Local custom class defaults are not merged.");
                else if(e.ClassIndex.Index<0){var r=GenericResourceViewer.ResolveReference(e.ClassIndex.Index,asset);if(r["ContentFile"]!=null)child=new(r["ContentFile"]!.ToString(),null);}
                if(resources.Any(r=>r.ClassName is "Material" or "MaterialInstanceConstant"))issues.Add("Brush uses a UI material; shader evaluation is required, not an arbitrary imported texture.");
                if(resources.Any(r=>r.ClassName is "Font" or "FontFace"))issues.Add("Game font has not been loaded; browser font metrics are approximate.");
                if(visual.ContainsKey("Clipping"))issues.Add("Serialized clipping requires renderer support; do not infer full clipping from layout bounds.");
                nodes.Add(new(Id(i),i,Id(t),e.ObjectName.ToString(),cls,parentId,children,slot,visual,sources,resources.Distinct().ToList(),child,new(Supported.Contains(cls)?"partial":"unsupported",Supported.Contains(cls)?"partial":"unsupported",issues)));
            }
        }
        // A cooked WidgetTree directly owned by a widget instance is its local subtree.
        // Match the outer export identity, never a potentially duplicated widget name.
        for(var index=0;index<nodes.Count;index++)
        {
            var node=nodes[index];
            var nested=roots.Where(r=>asset.Exports[int.Parse(r.TreeId[7..])].OuterIndex.Index==node.ExportIndex+1&&r.RootNodeId!=null).ToList();
            if(nested.Count==1)
            {
                var tree=nested[0];
                nodes[index]=node with { ChildWidget=new(node.ChildWidget?.AssetPath,tree.Id), Children=node.Children.Concat(new[]{tree.RootNodeId!}).Distinct().ToList(), Preview=new("partial","partial",node.Preview.Issues.Where(i=>i!="Class is not supported by the initial static renderer.").ToList()) };
                var rootIndex=nodes.FindIndex(n=>n.Id==tree.RootNodeId);
                if(rootIndex>=0)nodes[rootIndex]=nodes[rootIndex] with { ParentId=node.Id };
            }
            else if(nested.Count>1)diagnostics.Add(new("ambiguous-local-tree","warning",node.Id,"Multiple WidgetTrees are owned by this widget instance."));
        }
        var byId=nodes.ToDictionary(n=>n.Id);
        foreach(var n in nodes)
        {
            foreach(var c in n.Children)if(!byId.ContainsKey(c))diagnostics.Add(new("missing-child","warning",n.Id,$"Child {c} is outside the parsed widget set."));
            var seen=new HashSet<string>{n.Id};var parent=n.ParentId;
            while(parent!=null&&byId.TryGetValue(parent,out var p)){if(!seen.Add(parent)){diagnostics.Add(new("parent-cycle","warning",n.Id,"Cyclic parent relationship."));break;}parent=p.ParentId;}
        }
        var animations=ReadAnimations(asset);
        // Prefer a top-level tree with real animated targets; never rebind names across trees.
        var animatedTrees=animations.Where(a=>a.OwnerTreeId!=null&&a.Bindings.Any(b=>nodes.Any(n=>n.TreeId==a.OwnerTreeId&&n.Name==b.WidgetName)))
            .Select(a=>a.OwnerTreeId).ToHashSet();
        var suggested=roots.FirstOrDefault(r=>r.Kind=="archetype"&&r.RootNodeId!=null&&animatedTrees.Contains(r.TreeId))
            ??roots.FirstOrDefault(r=>r.Kind=="generated"&&r.RootNodeId!=null&&animatedTrees.Contains(r.TreeId))
            ??roots.FirstOrDefault(r=>r.Kind=="archetype"&&r.RootNodeId!=null)
            ??roots.FirstOrDefault(r=>r.Kind=="generated"&&r.RootNodeId!=null);
        return new(1,revision,path,roots,suggested?.Id,nodes,diagnostics,Animations:animations);
    }
}
