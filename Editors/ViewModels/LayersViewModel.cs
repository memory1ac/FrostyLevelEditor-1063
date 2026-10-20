using Frosty.Core;
using LevelEditorPlugin.BF1Core.Utils;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Layers;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LevelEditorPlugin.Editors;

public class LayersViewModel : Controls.IDockableItem, INotifyPropertyChanged
{
    public string Header => "Layers";
    public string UniqueId => "UID_LevelEditor_Layers";
    public string Icon => "Images/Layers.png";

    private List<Layers.SceneLayer> m_layers;
    public List<Layers.SceneLayer> Layers
    {
        get => m_layers;
        set
        {
            if (m_layers != value)
            {
                m_layers = value;
                NotifyPropertyChanged();
            }
        }
    }

    private IEditorProvider owner;

    public LayersViewModel(IEditorProvider inOwner)
    {
        owner = inOwner;
        if (owner == null)
        {
            Layers = [];
            App.Logger.LogWarning("LayersViewModel初始化失败：IEditorProvider为Null");
            return;
        }

        ///////////////////

        //Layers.Add(owner.RootLayer);

        //List<Layers.SceneLayer> layers = new List<Layers.SceneLayer>();
        //owner.RootLayer.CollectLayers(layers);

        //owner.RootLayer.SelectionChanged += SelectedLayerChanged;
        //foreach (SceneLayer layer in layers)
        //    layer.SelectionChanged += SelectedLayerChanged;

        ///////////////////

        ReloadLayer();

        ////////////////

        MeshUtil.LayerReloaded += () =>
        {
            ReloadLayer();
        };
    }

    private void SelectedLayerChanged(object sender, Layers.LayerSelectionChangedEventArgs e)
    {
        if (e.Selected)
            owner.SelectLayer(e.Layer);

        App.Logger.Log($"SelectedLayerChanged: {e.Layer.LayerName} {e.Layer.IsSelected}");
    }

    private void ReloadLayer()
    {
        if (owner?.RootLayer == null)
        {
            Layers = [];
            NotifyPropertyChanged("Layers");
            App.Logger.LogWarning("ReloadLayer跳过：RootLayer为Null");
            return;
        }

        SceneLayer rootLayer = owner.RootLayer;
        if (rootLayer.Entity == null)
        {
            Layers = [];
            NotifyPropertyChanged("Layers");
            App.Logger.LogWarning("ReloadLayer跳过：rootLayer.Entity为Null");
            return;
        }

        SceneLayer entityLayer = null;
        if (rootLayer.Entity is SpatialPrefabReferenceObject spatialPrefab)
        {
            entityLayer = spatialPrefab.GetLayer();
        }
        else if (rootLayer.Entity is SubWorldReferenceObject subWorld)
        {
            entityLayer = subWorld.GetLayer();
        }
        if (entityLayer == null)
            return;

        {
            Layers = [entityLayer];
            NotifyPropertyChanged("Layers");
        }

        {
            List<Layers.SceneLayer> layers = [];
            entityLayer.CollectLayers(layers);
            entityLayer.SelectionChanged += SelectedLayerChanged;
            foreach (SceneLayer layer in layers)
                layer.SelectionChanged += SelectedLayerChanged;
        }
    }

    #region -- INotifyPropertyChanged --
    public event PropertyChangedEventHandler PropertyChanged;
    private void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "")
    {
        if (PropertyChanged != null)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    #endregion
}
