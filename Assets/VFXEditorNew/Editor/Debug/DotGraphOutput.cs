using System.Collections.Generic;
using System.Linq;
using UnityEditor.Dot;
using UnityEngine;
using System.Diagnostics;

namespace UnityEditor.VFX
{
    static class DotGraphOutput
    {
        public static void Test()
        {
            DotGraph graph = new DotGraph();
            graph.AddElement(new DotEdge(new DotNode("node 1"), new DotNode("node 2")));
            graph.OutputToDotFile("d:\\testDot.dot");
        }

        private static bool IsHighlightedSlot(VFXSlot slot)
        {
            var owner = slot.owner;
            if (slot.GetExpression() == null)
                return false;

            if ((owner is VFXOperator || owner is VFXParameter) && slot.direction == VFXSlot.Direction.kOutput) // output to operators
                return true;

            var topOwner = slot.GetTopMostParent().owner;
            if (topOwner is VFXBlock && slot.direction == VFXSlot.Direction.kInput && slot.HasLink()) // linked inputs to blocks
                return true;

            return false;
        }

        private static string GetRecursiveName(VFXSlot slot)
        {
            string name = slot.property.name;
            while (slot.GetParent() != null)
            {
                slot = slot.GetParent();
                name = slot.property.name + "." + name;
            }
            return name;
        }

        public static void DebugExpressionGraph(VFXGraph graph,bool reduce = false)
        {
            var objs = new HashSet<UnityEngine.Object>();
            graph.CollectDependencies(objs);

            var mainSlots = new HashSet<VFXSlot>(objs.OfType<VFXSlot>()
                .Where(s => IsHighlightedSlot(s)));//.Select(s => s.GetTopMostParent());

            var mainExpressions = new Dictionary<VFXExpression, List<VFXSlot>>();
            foreach (var slot in mainSlots)
            {
                var expr = slot.GetExpression();
                if (mainExpressions.ContainsKey(expr))
                    mainExpressions[expr].Add(slot);
                else
                {
                    var list = new List<VFXSlot>();
                    list.Add(slot);
                    mainExpressions[expr] = list;
                }
            }

            if (reduce)
            {
                var exprContext = new VFXExpression.Context();
                foreach (var exp in mainExpressions.Keys)
                    exprContext.RegisterExpression(exp);
                exprContext.Compile();

                var reducedExpressions = new Dictionary<VFXExpression, List<VFXSlot>>();
                foreach (var kvp in mainExpressions)
                {
                    var exp = kvp.Key;
                    var slots = kvp.Value;

                    var reduced = exprContext.GetReduced(exp);
                    if (reducedExpressions.ContainsKey(reduced))
                        reducedExpressions[reduced].AddRange(slots);
                    else
                    {
                        var list = new List<VFXSlot>();
                        list.AddRange(slots);
                        reducedExpressions[reduced] = list;
                    }
                }

                mainExpressions = reducedExpressions;
            }

            var expressions = new HashSet<VFXExpression>();

            foreach (var exp in mainExpressions.Keys)
                CollectExpressions(exp, expressions);

            DotGraph dotGraph = new DotGraph();

            var expressionsToDot = new Dictionary<VFXExpression, DotNode>();
            foreach (var exp in expressions)
            {
                var dotNode = new DotNode();

                string name = exp.GetType().Name;
                name += " " + exp.ValueType.ToString();
                string valueStr = GetExpressionValue(exp);
                if (!string.IsNullOrEmpty(valueStr))
                    name += string.Format(" ({0})", valueStr);

                dotNode.attributes[DotAttribute.Shape] = DotShape.Box;
                if (mainExpressions.ContainsKey(exp))
                {
                    string allOwnersStr = string.Empty;
                    bool belongToBlock = false;
                    foreach (var slot in mainExpressions[exp])
                    {
                        var topOwner = slot.GetTopMostParent().owner;
                        allOwnersStr += string.Format("\n{0} - {1}", topOwner.GetType().Name, GetRecursiveName(slot));
                        belongToBlock |= topOwner is VFXBlock;
                    }

                    name += string.Format("{0}", allOwnersStr);

                    dotNode.attributes[DotAttribute.Style] = DotStyle.Filled;
                    dotNode.attributes[DotAttribute.Color] = belongToBlock ? DotColor.Cyan : DotColor.Green;
                }

                dotNode.Label = name;

                expressionsToDot[exp] = dotNode;
                dotGraph.AddElement(dotNode);
            }
          
            foreach (var exp in expressionsToDot)
            {
                var parents = exp.Key.Parents;
                for (int i = 0; i < parents.Length; ++i)
                {
                    var dotEdge = new DotEdge(expressionsToDot[parents[i]], exp.Value);
                    if (parents.Length > 1)
                        dotEdge.attributes[DotAttribute.HeadLabel] = i.ToString();
                    dotGraph.AddElement(dotEdge);
                }
            }

            var basePath = Application.dataPath;
            basePath = basePath.Replace("/Assets", "");
            basePath = basePath.Replace("/", "\\");

            var outputfile = basePath + "\\GraphViz\\output\\expGraph.dot";
            dotGraph.OutputToDotFile(outputfile);

            var proc = new Process();
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.FileName = "C:\\Windows\\system32\\cmd.exe";
            var path = basePath + "\\GraphViz\\Postbuild.bat";
            proc.StartInfo.Arguments = "/c" + path + " \"" + outputfile + "\"";
            proc.EnableRaisingEvents = true;
            proc.Start();
        }

        private static string GetExpressionValue(VFXExpression exp)
        {
            // TODO We should have a way in VFXValue to retrieve an object representing the value
            if (exp is VFXValueFloat) return ((VFXValueFloat)exp).GetContent<float>().ToString();
            if (exp is VFXValueFloat2) return ((VFXValueFloat2)exp).GetContent<Vector2>().ToString();
            if (exp is VFXValueFloat3) return ((VFXValueFloat3)exp).GetContent<Vector3>().ToString();
            if (exp is VFXValueFloat4) return ((VFXValueFloat4)exp).GetContent<Vector4>().ToString();
            if (exp is VFXBuiltInExpression) return ((VFXBuiltInExpression)exp).Operation.ToString();
            if (exp is VFXAttributeExpression) return ((VFXAttributeExpression)exp).attributeName;

            return string.Empty;
        }

        private static void CollectExpressions(VFXExpression exp,HashSet<VFXExpression> expressions)
        {
            if (/*exp != null &&*/ !expressions.Contains(exp))
            {
                expressions.Add(exp);
                foreach (var parent in exp.Parents)
                    //if (parent != null)
                        CollectExpressions(parent, expressions);
            }
        }
    }
}
