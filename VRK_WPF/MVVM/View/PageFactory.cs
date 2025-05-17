using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using VRK_WPF.MVVM.View.UserPages;

namespace VRK_WPF.MVVM.View
{
    public static class PageFactory
    {
        private static Dictionary<string, Page> _pageCache = new Dictionary<string, Page>();
        
        public static Page GetPage(string pageName)
        {
            if (_pageCache.TryGetValue(pageName, out Page page))
            {
                return page;
            }
            
            page = CreatePage(pageName);
            
            if (page != null)
            {
                _pageCache[pageName] = page;
            }
            
            return page;
        }
        
        private static Page CreatePage(string pageName)
        {
            return pageName switch
            {
                "Files" => new FilesPage(),
                "Network" => new NetworkPage(),
                "Settings" => new SettingsPage(),
                "Simulation" => new SimulationPage(),
                "Analytics" => new AnalyticsPage(),
                "Documentation" => new DocumentationPage(),
                "About" => new AboutPage(),
                _ => null
            };
        }

        public static void ClearCache()
        {
            _pageCache.Clear();
        }
    }
}