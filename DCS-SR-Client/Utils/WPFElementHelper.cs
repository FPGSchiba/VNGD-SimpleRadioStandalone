using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Vanguard.VCS.Client.Utils
{
    public class WPFElementHelper
    {
        public static IEnumerable<DependencyObject> GetVisuals(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var descendants in GetVisuals(child))
                    yield return descendants;
            }
        }
    }
}