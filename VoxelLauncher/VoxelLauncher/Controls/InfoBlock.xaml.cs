using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoxelLauncher.Controls
{
    public sealed partial class InfoBlock : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(InfoBlock), new PropertyMetadata(""));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(InfoBlock), new PropertyMetadata(""));

        public static readonly DependencyProperty IsHyperlinkProperty =
            DependencyProperty.Register("IsHyperlink", typeof(bool), typeof(InfoBlock), new PropertyMetadata(false));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public bool IsHyperlink
        {
            get => (bool)GetValue(IsHyperlinkProperty);
            set => SetValue(IsHyperlinkProperty, value);
        }

        public InfoBlock() => this.InitializeComponent();
    }
}