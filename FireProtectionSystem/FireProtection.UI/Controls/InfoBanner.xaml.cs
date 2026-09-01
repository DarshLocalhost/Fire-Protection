using System.Windows;
using System.Windows.Controls;

namespace FireProtection.UI.Controls
{
    public partial class InfoBanner : UserControl
    {
        public static readonly DependencyProperty InfoContentProperty =
            DependencyProperty.Register(nameof(InfoContent), typeof(object), typeof(InfoBanner),
                new PropertyMetadata(null));

        public static readonly DependencyProperty InfoContentTemplateProperty =
            DependencyProperty.Register(nameof(InfoContentTemplate), typeof(DataTemplate), typeof(InfoBanner),
                new PropertyMetadata(null));

        public object InfoContent
        {
            get => GetValue(InfoContentProperty);
            set => SetValue(InfoContentProperty, value);
        }

        public DataTemplate InfoContentTemplate
        {
            get => (DataTemplate)GetValue(InfoContentTemplateProperty);
            set => SetValue(InfoContentTemplateProperty, value);
        }

        public InfoBanner()
        {
            InitializeComponent();
        }
    }
}
