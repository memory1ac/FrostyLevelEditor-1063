using Frosty.Core.Managers;
using FrostySdk.Interfaces;
using System.Windows;

namespace Frosty.Core.Controls
{
    public class FrostyBaseAssetEditor : FrostyAssetEditor
    {
        protected bool firstTimeLoad = true;
        protected UndoManager undoManager;

        public FrostyBaseAssetEditor(ILogger inLogger)
            : base(inLogger)
        {
            undoManager = UndoManager.Create();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            Loaded += BaseAssetEditor_Loaded;
            Unloaded += BaseAssetEditor_Unloaded;
        }

        private void BaseAssetEditor_Loaded(object sender, RoutedEventArgs e)
        {
            UndoManager.SetCurrent(undoManager);

            if (firstTimeLoad)
            {
                Initialize();
                firstTimeLoad = false;
            }

            Reload();
        }

        private void BaseAssetEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            Unload();
        }

        public override void Closed()
        {
            UndoManager.ClearCurrent();
        }

        protected virtual void Initialize()
        {
        }

        protected virtual void Reload()
        {
        }

        protected virtual void Unload()
        {
        }
    }
}
