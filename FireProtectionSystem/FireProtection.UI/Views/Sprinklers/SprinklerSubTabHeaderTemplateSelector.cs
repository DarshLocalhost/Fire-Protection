using System.Windows;
using System.Windows.Controls;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using FireProtection.UI.ViewModels.Sprinklers.Collision;

namespace FireProtection.UI.Views.Sprinklers
{
    public class SprinklerSubTabHeaderTemplateSelector : DataTemplateSelector
    {
        public DataTemplate CollisionTemplate { get; set; }

        public DataTemplate BruteForceTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is SprinklerCollisionViewModel)
            {
                return CollisionTemplate;
            }

            if (item is SprinklerBruteForceViewModel)
            {
                return BruteForceTemplate;
            }

            return base.SelectTemplate(item, container);
        }
    }
}