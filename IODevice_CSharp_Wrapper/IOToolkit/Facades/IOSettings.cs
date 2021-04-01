using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IOToolkit.Core;

namespace IOToolkit
{
    public class IOSettings
    {
        public static int SetIOConfigPath(string InFilePath)
        {
            return IONativeWrapper.SetIOConfigPath(InFilePath);
        }
    }
}
