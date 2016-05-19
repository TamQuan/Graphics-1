using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.VFX;
using UnityEditor.Experimental;
using UnityEditor.Experimental.Graph;
using Object = UnityEngine.Object;

namespace UnityEditor.Experimental
{
    internal class VFXEdDataSource : ScriptableObject, ICanvasDataSource, VFXModelObserver
    {
        private List<CanvasElement> m_Elements = new List<CanvasElement>();

        private Dictionary<VFXContextModel,VFXEdContextNode> m_ContextModelToUI = new Dictionary<VFXContextModel,VFXEdContextNode>();
        private Dictionary<VFXBlockModel, VFXEdProcessingNodeBlock> m_BlockModelToUI = new Dictionary<VFXBlockModel, VFXEdProcessingNodeBlock>();

        public void OnEnable()
        {
        }

        public void OnModelUpdated(VFXElementModel model)
        {
            Type type = model.GetType();
            if (type == typeof(VFXContextModel))
                OnContextUpdated((VFXContextModel)model);
            else if (type == typeof(VFXEdProcessingNodeBlock))
                OnBlockUpdated((VFXEdProcessingNodeBlock)model);
        }

        private void OnContextUpdated(VFXContextModel model)
        {
            VFXEdContextNode contextUI;
            m_ContextModelToUI.TryGetValue(model,out contextUI);
            if (contextUI == null) // Create the context UI
            {
                contextUI = new VFXEdContextNode(model, this);
                m_ContextModelToUI.Add(model, contextUI);
                AddElement(contextUI);
            }
        }

        private void OnBlockUpdated(VFXBlockModel model)
        {
            var ownerModel = model.GetOwner();

            VFXEdProcessingNodeBlock blockUI;
            m_BlockModelToUI.TryGetValue(model, out blockUI);

            if (ownerModel != null && blockUI == null)
            {
                blockUI = new VFXEdProcessingNodeBlock(model, this);
                m_BlockModelToUI.Add(model, blockUI);
                AddElement(blockUI);
            }

            var parentUI = m_ContextModelToUI[ownerModel];
            ownerModel.GetIndex(model);
            
        }

        public VFXEdProcessingNodeBlock GetBlockUI(VFXBlockModel model)
        {
            return m_BlockModelToUI[model];
        }

        public VFXEdContextNode GetContextUI(VFXContextModel model)
        {
            return m_ContextModelToUI[model];
        }
            
        public void OnLinkUpdated(VFXPropertySlot slot)
        {

        }

        public void UndoSnapshot(string Message)
        {
            // TODO : Make RecordObject work (not working, no errors, have to investigate)
            Undo.RecordObject(this, Message);
        }

        public CanvasElement[] FetchElements()
        {
            return m_Elements.ToArray();
        }

        public void CreateContext(Vector2 pos,VFXContextDesc desc)
        {
            VFXContextModel context = new VFXContextModel(desc);
            context.UpdatePosition(pos);

            // Create a tmp system to hold the newly created context
            VFXSystemModel system = new VFXSystemModel();
            system.AddChild(context);
            VFXEditor.AssetModel.AddChild(system);

            CreateContext(context);
        }

        public void CreateContext(VFXContextModel context)
        {
            var contextUI = new VFXEdContextNode(context, this);
            m_ContextModelToUI.Add(context, contextUI);
            AddElement(contextUI);
        }

        public void RemoveContext(VFXContextModel context)
        {
            // First remove all blocks recursively
            for (int i = 0; i < context.GetNbChildren(); ++i)
                RemoveBlock(context.GetChild(i));

            // Then remove all link to slots (data)
            // TODO

            // Finally remove all link to context (flow)
            // TODO

            // Create new system if any
            VFXSystemModel owner = context.GetOwner();
            if (owner != null)
            {
                int nbChildren = owner.GetNbChildren();
                int index = owner.GetIndex(context);

                context.Detach();
                if (index != 0 && index != nbChildren - 1)
                {
                    // if the node is in the middle of a system, we need to create a new system
                    VFXSystemModel newSystem = new VFXSystemModel();
                    while (owner.GetNbChildren() > index)
                        owner.GetChild(index).Attach(newSystem);
                    newSystem.Attach(VFXEditor.AssetModel);
                }
            }

            // RemoveUI
            var contextUI = m_ContextModelToUI[context];
            m_ContextModelToUI.Remove(context);
            contextUI.OnRemove();
            m_Elements.Remove(contextUI);
        }







        public void RemoveBlock(VFXBlockModel block)
        {

        }



        public void AddElement(CanvasElement e) {
            m_Elements.Add(e);
        }

        public void DeleteElement(CanvasElement e)
        {
            Canvas2D canvas = e.ParentCanvas();

            // Handle model update when deleting edge here
            var edge = e as FlowEdge;
            if (edge != null)
            {
                VFXEdFlowAnchor anchor = edge.Right;
                var node = anchor.FindParent<VFXEdContextNode>();
                if (node != null)
                {
                    VFXSystemModel owner = node.Model.GetOwner();
                    int index = owner.GetIndex(node.Model);

                    VFXSystemModel newSystem = new VFXSystemModel();
                    while (owner.GetNbChildren() > index)
                        owner.GetChild(index).Attach(newSystem);
                    newSystem.Attach(VFXEditor.AssetModel);
                }
            }

            var propertyEdge = e as VFXUIPropertyEdge;
            if (propertyEdge != null)
            {
                VFXUIPropertyAnchor inputAnchor = propertyEdge.Right;
                ((VFXInputSlot)inputAnchor.Slot).Unlink();
                propertyEdge.Left.Invalidate();
                propertyEdge.Right.Invalidate();
            }

            m_Elements.Remove(e);
            canvas.ReloadData();
            canvas.Repaint();
        }

        public void RemoveConnectedEdges<T, U>(U anchor) 
            where T : Edge<U> 
            where U : CanvasElement, IConnect 
        {
            var edgesToRemove = GetConnectedEdges<T,U>(anchor);

            foreach (var edge in edgesToRemove)
                DeleteElement(edge);
        }

        public List<T> GetConnectedEdges<T, U>(U anchor) 
            where T : Edge<U> 
            where U : CanvasElement, IConnect 
        {
            var edges = new List<T>();
            foreach (CanvasElement element in m_Elements)
            {
                T edge = element as T;
                if (edge != null && (edge.Left == anchor || edge.Right == anchor))
                    edges.Add(edge);
            }
            return edges;
        }

        public void ConnectData(VFXUIPropertyAnchor a, VFXUIPropertyAnchor b)
        {
            // Swap to get a as output and b as input
            if (a.GetDirection() == Direction.Input)
            {
                VFXUIPropertyAnchor tmp = a;
                a = b;
                b = tmp;
            }

            RemoveConnectedEdges<VFXUIPropertyEdge, VFXUIPropertyAnchor>(b);

            // Disconnect connected children anchors and collapse
            b.Owner.DisconnectChildren();
            b.Owner.CollapseChildren(true);    

            ((VFXInputSlot)b.Slot).Link((VFXOutputSlot)a.Slot);
            m_Elements.Add(new VFXUIPropertyEdge(this, a, b));

            a.Invalidate();
            b.Invalidate();
        }

        public bool ConnectFlow(VFXEdFlowAnchor a, VFXEdFlowAnchor b)
        {
            if (a.GetDirection() == Direction.Input)
            {
                VFXEdFlowAnchor tmp = a;
                a = b;
                b = tmp;
            }

            RemoveConnectedEdges<FlowEdge, VFXEdFlowAnchor>(a);
            RemoveConnectedEdges<FlowEdge, VFXEdFlowAnchor>(b);

            VFXEdContextNode context0 = a.FindParent<VFXEdContextNode>();
            VFXEdContextNode context1 = b.FindParent<VFXEdContextNode>();

            if (context0 != null && context1 != null)
            {

                VFXContextModel model0 = context0.Model;
                VFXContextModel model1 = context1.Model;

                if (!VFXSystemModel.ConnectContext(model0, model1))
                    return false;
            }

            m_Elements.Add(new FlowEdge(this, a, b));
            return true;
        }


        /// <summary>
        /// Spawn node is called from context menu, object is expected to be a VFXEdSpawner
        /// </summary>
        /// <param name="o"> param that should be a VFXEdSpawner</param>
        public void SpawnNode(object o)
        {
            VFXEdSpawner spawner = o as VFXEdSpawner;
            if(spawner != null)
            {
                spawner.Spawn();
            }
        }


    }
}

