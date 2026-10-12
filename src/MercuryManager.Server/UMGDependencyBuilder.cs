namespace MercuryManager.Server;

internal static class UMGDependencyBuilder
{
    public static UMGDocument Expand(UMGDocument document,Func<string,UMGDocument> load)
    {
        var nodes=document.Nodes.ToList();var diagnostics=document.Diagnostics.ToList();
        var revisions=new List<string>{document.Revision};
        var packages=new Dictionary<string,UMGDocument>(StringComparer.OrdinalIgnoreCase);
        void Visit(UMGNode node,int depth,HashSet<string> chain)
        {
            var path=node.ChildWidget?.AssetPath;
            if(path==null||node.ChildWidget?.LocalTreeId!=null)return;
            if(depth>=6||nodes.Count>=6000){diagnostics.Add(new("dependency-limit","warning",node.Id,"Nested widget expansion limit reached."));return;}
            if(chain.Contains(path)){diagnostics.Add(new("dependency-cycle","warning",node.Id,"Cyclic widget package dependency."));return;}
            UMGDocument child;
            try{if(!packages.TryGetValue(path,out child!)){child=load(path);packages[path]=child;revisions.Add(child.Revision);}}
            catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException){diagnostics.Add(new("dependency-unavailable","warning",node.Id,$"Cannot read nested widget {path}: {ex.Message}"));return;}
            var root=child.Roots.FirstOrDefault(r=>r.Id==child.SuggestedRootId)??child.Roots.FirstOrDefault(r=>r.RootNodeId!=null);
            if(root?.RootNodeId==null){diagnostics.Add(new("dependency-root","warning",node.Id,"Nested widget has no usable root."));return;}
            var byId=child.Nodes.ToDictionary(n=>n.Id);var reachable=new HashSet<string>();
            void Collect(string id){if(!reachable.Add(id)||!byId.TryGetValue(id,out var n))return;foreach(var c in n.Children)Collect(c);}
            Collect(root.RootNodeId);
            if(nodes.Count+reachable.Count>6000){diagnostics.Add(new("dependency-limit","warning",node.Id,"Nested widget node budget exceeded."));return;}
            string Scope(string id)=>node.Id+"/"+id;
            var clones=child.Nodes.Where(n=>reachable.Contains(n.Id)).Select(n=>n with {
                Id=Scope(n.Id),TreeId=Scope(n.TreeId),ParentId=n.Id==root.RootNodeId?node.Id:n.ParentId==null?null:Scope(n.ParentId),
                Children=n.Children.Select(Scope).ToList(),ChildWidget=n.ChildWidget==null?null:n.ChildWidget with {LocalTreeId=n.ChildWidget.LocalTreeId==null?null:Scope(n.ChildWidget.LocalTreeId)}
            }).ToList();
            nodes.AddRange(clones);
            var index=nodes.FindIndex(n=>n.Id==node.Id);
            nodes[index]=node with {Children=node.Children.Concat(new[]{Scope(root.RootNodeId)}).ToList(),ChildWidget=new(path,Scope(root.TreeId)),Preview=new("partial","partial",node.Preview.Issues.Where(s=>s!="Class is not supported by the initial static renderer.").ToList())};
            diagnostics.AddRange(child.Diagnostics.Select(d=>d with {NodeId=d.NodeId==null?node.Id:Scope(d.NodeId)}));
            var next=new HashSet<string>(chain,StringComparer.OrdinalIgnoreCase){path};
            foreach(var clone in clones)Visit(clone,depth+1,next);
        }
        foreach(var node in document.Nodes)Visit(node,0,new(StringComparer.OrdinalIgnoreCase){document.AssetPath});
        var revision=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|",revisions))));
        return document with {Nodes=nodes,Diagnostics=diagnostics,Revision=revision};
    }
}
